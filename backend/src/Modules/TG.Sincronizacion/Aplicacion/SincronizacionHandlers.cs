using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TG.Catalogos.Publico;
using TG.Logistica.Publico;
using TG.Seguridad.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;
using TG.Sincronizacion.Dominio;
using TG.Sincronizacion.Infraestructura;
using TG.Ventas.Publico;

namespace TG.Sincronizacion.Aplicacion;

internal sealed record ContextoSync(DispositivoInfo Dispositivo, Guid UsuarioId, RutaInfo? Ruta);

/// <summary>
/// RN-SYN-05: el dispositivo del token debe estar activo y asignado al usuario. RN-SYN-09: el alcance es la ruta del chofer
/// hoy (su carga o, sin carga, su ruta predeterminada).
/// </summary>
public sealed class ContextoDispositivoHandler(IUsuarioActual usuario, ISeguridadService seguridad, ILogisticaService logistica,
    IFechaOperativa fechaOperativa)
{
    internal async Task<Result<ContextoSync>> ResolverAsync(Guid? dispositivoSolicitado, CancellationToken ct)
    {
        var dispositivoId = usuario.DispositivoId;
        if (usuario.Id is not { } usuarioId || dispositivoId is null ||
            (dispositivoSolicitado is { } pedido && pedido != dispositivoId))
        {
            return NoAutorizado();
        }

        var dispositivo = await seguridad.ObtenerDispositivoAsync(dispositivoId.Value, ct);
        if (dispositivo is null || !dispositivo.Activo || dispositivo.UsuarioId != usuarioId)
        {
            return NoAutorizado();
        }

        var ruta = await logistica.RutaDeChoferAsync(usuarioId, fechaOperativa.Hoy(), ct);
        return new ContextoSync(dispositivo, usuarioId, ruta);
    }

    private static Error NoAutorizado() => Error.Prohibido(ErroresSync.DispositivoNoAutorizado,
        "El dispositivo no está activo o no está asignado a tu usuario. Pide que lo aprueben.");
}

/// <summary>GET /sync/dispositivo: el consecutivo de folio al reinstalar la app (RN-SYN-08).</summary>
public sealed class EstadoDispositivoHandler(ContextoDispositivoHandler contexto)
{
    public async Task<Result<EstadoDispositivoServidor>> EjecutarAsync(CancellationToken ct)
    {
        var r = await contexto.ResolverAsync(null, ct);
        if (!r.EsExito)
        {
            return r.Error!;
        }

        var (d, _, ruta) = r.Valor;
        return new EstadoDispositivoServidor(d.Id, d.Codigo ?? string.Empty, d.Activo, ruta?.Id, ruta?.Codigo, d.UltimoConsecutivo);
    }
}

/// <summary>Bajada incremental por alcance de la ruta del vendedor (05, "Bajada").</summary>
public sealed class PullHandler(
    ContextoDispositivoHandler contexto,
    ICatalogosService catalogos,
    ILogisticaService logistica,
    IConsultasSyncVentas ventas,
    IFechaOperativa fechaOperativa,
    TimeProvider reloj)
{
    private static readonly string[] PrefijosParametros = ["gps.", "ruta."];

    /// <param name="rutaId">Ruta que la app tiene guardada; si ya no es la del alcance, <c>rutaCambiada</c> (RN-SYN-09).</param>
    public async Task<Result<RespuestaPull>> EjecutarAsync(long? desde, Guid? rutaId, CancellationToken ct)
    {
        var r = await contexto.ResolverAsync(null, ct);
        if (!r.EsExito)
        {
            return r.Error!;
        }

        var cursor = Math.Max(0, desde ?? 0);
        var rutaCambiada = rutaId is not null && rutaId != r.Valor.Ruta?.Id;
        if (rutaCambiada && cursor > 0)
        {
            var actual = r.Valor.Ruta;
            return new RespuestaPull(0, false, reloj.GetUtcNow(),
                actual is null ? null : new RutaSync(actual.Id, actual.Codigo, actual.Nombre, actual.AlmacenId),
                new CambiosSync([], [], [], [], [], [], [], [], [], new Dictionary<string, IReadOnlyList<Guid>>(), [], []), true);
        }

        var maximo = cursor;
        void Version(long v) => maximo = Math.Max(maximo, v);
        var eliminados = new Dictionary<string, List<Guid>>();
        void Eliminar(string coleccion, Guid id)
        {
            if (!eliminados.TryGetValue(coleccion, out var lista))
            {
                eliminados[coleccion] = lista = [];
            }

            lista.Add(id);
        }

        // Productos vendibles (con conversiones y unidades).
        var productos = new List<ProductoSync>();
        foreach (var p in (await catalogos.ListarProductosAsync(true, false, ct)).Where(p => p.VersionSync > cursor))
        {
            Version(p.VersionSync);
            if (!p.Activo)
            {
                Eliminar("productos", p.Id);
                continue;
            }

            productos.Add(new ProductoSync(p.Id, p.Sku, p.Nombre, p.Tipo, p.UnidadBaseId, p.UnidadBaseClave, p.UnidadBaseDecimales,
                p.ManejaLote, p.TasaIva, p.TasaIeps, p.Activo, p.VersionSync,
                p.Conversiones.Where(c => c.UsoVenta)
                    .Select(c => new ConversionSync(c.UnidadId, c.UnidadClave, c.FactorABase, c.Decimales, c.UsoVenta, c.PredeterminadaVenta))
                    .ToList()));
        }

        // Motivos y parámetros de ruta.
        var motivos = new List<MotivoMermaSync>();
        foreach (var m in (await catalogos.ListarMotivosAsync(ct)).Where(m => (m.AplicaRuta || m.AplicaRetorno) && m.VersionSync > cursor))
        {
            Version(m.VersionSync);
            if (!m.Activo)
            {
                Eliminar("motivosMerma", m.Id);
                continue;
            }

            motivos.Add(new MotivoMermaSync(m.Id, m.Clave, m.Nombre, m.AplicaRuta, m.AplicaRetorno, m.Activo, m.VersionSync));
        }

        var parametros = (await catalogos.ListarParametrosAsync(ct))
            .Where(p => PrefijosParametros.Any(x => p.Clave.StartsWith(x, StringComparison.Ordinal)) && p.VersionSync > cursor)
            .Select(p =>
            {
                Version(p.VersionSync);
                return new ParametroSync(p.Clave, p.Valor, p.VersionSync);
            })
            .ToList();

        var ruta = r.Valor.Ruta;
        var clientes = new List<ClienteSync>();
        var rutasClientes = new List<RutaClienteSync>();
        var precios = new List<PrecioSync>();
        var envios = new List<EnvioSync>();
        var pedidos = new List<PedidoSync>();
        var saldos = new List<SaldoClienteSync>();
        var preciosCliente = new List<PrecioClienteSync>();

        // RN-VEN-14: todos los registros (también de clientes fuera de la ruta) para ocultar el producto a los demás.
        var exclusivos = new List<ProductoExclusivoSync>();
        foreach (var x in await ventas.ProductosExclusivosAsync(cursor, ct))
        {
            Version(x.VersionSync);
            if (!x.Activo)
            {
                Eliminar("productosExclusivos", x.Id);
                continue;
            }

            exclusivos.Add(new ProductoExclusivoSync(x.Id, x.ProductoId, x.ClienteId, x.GrupoClienteId, x.VersionSync));
        }

        if (ruta is not null)
        {
            var asignaciones = await logistica.ClientesDeRutaAsync(ruta.Id, 0, ct);
            var nuevos = new HashSet<Guid>();
            foreach (var a in asignaciones.Where(a => a.VersionSync > cursor))
            {
                Version(a.VersionSync);
                if (!a.Activo)
                {
                    Eliminar("rutasClientes", a.ClienteId);
                    continue;
                }

                nuevos.Add(a.ClienteId);
                rutasClientes.Add(new RutaClienteSync(a.RutaId, a.ClienteId, a.OrdenVisita, a.DiasVisita.Select(d => (int)d).ToList(),
                    a.Activo, a.VersionSync));
            }

            var activos = asignaciones.Where(a => a.Activo).Select(a => a.ClienteId).ToList();
            var todos = await ventas.ClientesAsync(activos, 0, ct);
            // Un cliente recién asignado baja completo aunque no haya cambiado.
            foreach (var c in todos.Where(c => c.VersionSync > cursor || nuevos.Contains(c.Id)))
            {
                Version(c.VersionSync);
                if (!c.Activo)
                {
                    Eliminar("clientes", c.Id);
                    continue;
                }

                clientes.Add(new ClienteSync(c.Id, c.Codigo, c.Nombre, c.CanalClave, c.ListaPrecioId, c.CondicionPago.ToString(),
                    c.DiasCredito, c.LimiteCredito, c.RequiereFactura, c.Rfc, c.RazonSocial, c.Telefono, c.Direccion, c.Latitud,
                    c.Longitud, c.UbicacionAprobada, c.EsGenerico, c.Activo, c.VersionSync, c.GrupoClienteId));
            }

            var listas = todos.Select(c => c.ListaPrecioId).Distinct().ToList();
            var listasNuevas = todos.Where(c => nuevos.Contains(c.Id)).Select(c => c.ListaPrecioId).Distinct().ToList();
            var partidas = (await ventas.PartidasListasAsync(listas, cursor, ct))
                .Concat(listasNuevas.Count > 0 ? await ventas.PartidasListasAsync(listasNuevas, 0, ct) : [])
                .DistinctBy(p => (p.ListaPrecioId, p.ProductoId, p.UnidadId));
            foreach (var p in partidas)
            {
                Version(p.VersionSync);
                precios.Add(new PrecioSync(p.ListaPrecioId, p.ProductoId, p.UnidadId, p.PrecioUnitario, p.VersionSync));
            }

            // RN-VEN-12: precios especiales; los de un cliente recién asignado bajan completos.
            var especiales = (await ventas.PreciosClientesAsync(activos, cursor, ct))
                .Concat(nuevos.Count > 0 ? await ventas.PreciosClientesAsync(nuevos, 0, ct) : [])
                .DistinctBy(p => (p.ClienteId, p.ProductoId, p.UnidadId));
            foreach (var p in especiales)
            {
                Version(p.VersionSync);
                preciosCliente.Add(new PrecioClienteSync(p.ClienteId, p.ProductoId, p.UnidadId, p.PrecioUnitario, p.VigenteDesde,
                    p.Activo, p.VersionSync));
            }

            foreach (var e in await logistica.EnviosDeRutaAsync(ruta.Id, cursor, fechaOperativa.Hoy(), ct))
            {
                Version(e.VersionSync);
                envios.Add(new EnvioSync(e.Id, e.Folio, e.FechaOperativa, e.Tipo.ToString(), e.Estado.ToString(), e.DespachadoEn,
                    e.VersionSync, e.Partidas.Select(x => new EnvioPartidaSync(x.Id, x.ProductoId, x.Lote, x.CantidadBase)).ToList()));
            }

            var pedidosRuta = await ventas.PedidosDeRutaAsync(ruta.Id, cursor, ct);
            var foliosCargas = await logistica.FoliosEnviosAsync(
                pedidosRuta.Where(p => p.EnvioId is not null).Select(p => p.EnvioId!.Value).Distinct().ToList(), ct);
            foreach (var p in pedidosRuta)
            {
                Version(p.VersionSync);
                if (p.Estado is EstadoPedido.Entregado or EstadoPedido.Cancelado)
                {
                    Eliminar("pedidos", p.Id);
                    continue;
                }

                pedidos.Add(new PedidoSync(p.Id, p.Folio, p.ClienteId, p.FechaEntrega, p.Estado.ToString(), p.Total, p.EnvioId,
                    p.EnvioId is { } envioId ? foliosCargas.GetValueOrDefault(envioId) : null, p.VersionSync,
                    p.Partidas.Select(x => new PedidoPartidaSync(x.Id, x.ProductoId, x.UnidadId, x.Cantidad, x.CantidadBase,
                        x.PrecioUnitario, x.TasaIva, x.TasaIeps, x.CantidadEntregadaBase, x.Tipo.ToString())).ToList(), p.EsRuta));
            }

            // Saldos de crédito: siempre completos [SUP-08].
            var saldosServidor = await ventas.SaldosClientesAsync(activos, ct);
            saldos.AddRange(saldosServidor.Select(s => new SaldoClienteSync(s.Key, s.Value)));
        }

        var cambios = new CambiosSync(productos, clientes, rutasClientes, precios, envios, pedidos, saldos, motivos, parametros,
            eliminados.ToDictionary(e => e.Key, e => (IReadOnlyList<Guid>)e.Value), preciosCliente, exclusivos);
        return new RespuestaPull(maximo, false, reloj.GetUtcNow(),
            ruta is null ? null : new RutaSync(ruta.Id, ruta.Codigo, ruta.Nombre, ruta.AlmacenId), cambios, rutaCambiada);
    }
}

/// <summary>
/// Subida idempotente (05, "Subida"): las operaciones se aplican en orden de <c>secuencia</c>, cada una en su propia
/// transacción (RN-SYN-01) y con el <c>op_id</c> registrado en esa misma transacción (RN-SYN-03).
/// </summary>
public sealed class PushHandler(
    ContextoDispositivoHandler contexto,
    IServiceScopeFactory scopes,
    ISeguridadService seguridad,
    TimeProvider reloj,
    ILogger<PushHandler> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Aplicacion(IReadOnlyDictionary<string, Guid> Ids, IReadOnlyList<string> Incidencias, bool Duplicada);

    public async Task<Result<RespuestaPush>> EjecutarAsync(SolicitudPush solicitud, CancellationToken ct)
    {
        var r = await contexto.ResolverAsync(solicitud.DispositivoId, ct);
        if (!r.EsExito)
        {
            return r.Error!;
        }

        if (solicitud.Operaciones.Count > ErroresSync.MaximoOperacionesLote)
        {
            return Error.Validacion(ErroresSync.LoteDemasiadoGrande,
                $"Máximo {ErroresSync.MaximoOperacionesLote} operaciones por lote; parte el envío.");
        }

        var ctx = r.Valor;
        var resultados = new List<ResultadoOperacion>();
        int? pendientes = null;
        var consecutivo = 0;

        // PostgreSQL guarda timestamptz en UTC; la fecha operativa se calcula antes con la zona de México.
        foreach (var op in solicitud.Operaciones.OrderBy(o => o.Secuencia).Select(o => o with { CreadaEn = o.CreadaEn.ToUniversalTime() }))
        {
            ResultadoOperacion resultado;
            try
            {
                resultado = await ProcesarAsync(op, solicitud.LoteId, ctx, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Error inesperado: se detiene el lote; las operaciones sin resultado se reintentan (RN-SYN-04).
                log.LogError(ex, "Error al aplicar la operación {OpId} ({Tipo})", op.OpId, op.Tipo);
                break;
            }

            resultados.Add(resultado);
            consecutivo = Math.Max(consecutivo, ConsecutivoDe(op));
            if (op.Tipo == TiposOperacion.ReportarEstado && Leer<ReportarEstadoDatos>(op) is { } estado)
            {
                pendientes = estado.Pendientes;
                consecutivo = Math.Max(consecutivo, estado.UltimoConsecutivo);
            }
        }

        await seguridad.RegistrarContactoAsync(ctx.Dispositivo.Id, solicitud.VersionApp, consecutivo > 0 ? consecutivo : null, pendientes, ct);
        await RegistrarLoteAsync(solicitud, ctx, resultados, ct);
        return new RespuestaPush(resultados, 0);
    }

    private async Task<ResultadoOperacion> ProcesarAsync(OperacionSync op, Guid loteId, ContextoSync ctx, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<SincronizacionDbContext>();

        // RN-SYN-02: si ya se recibió, se responde con el resultado guardado.
        var existente = await db.Operaciones.AsNoTracking().FirstOrDefaultAsync(o => o.OpId == op.OpId, ct);
        if (existente is not null)
        {
            return existente.Estado == EstadoOperacion.Rechazada
                ? new ResultadoOperacion(op.OpId, "Rechazada", Codigo: existente.Codigo, Mensaje: existente.Error)
                : Duplicada(op.OpId, existente.Resultado);
        }

        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var aplicada = await unitOfWork.EjecutarAsync(async c =>
        {
            var efecto = await AplicarAsync(sp, op, ctx, c);
            if (!efecto.EsExito)
            {
                return Result<ResultadoOperacion>.Falla(efecto.Error!);
            }

            var resultadoJson = JsonSerializer.Serialize(new { idsServidor = efecto.Valor.Ids, incidencias = efecto.Valor.Incidencias }, Json);
            db.Operaciones.Add(Nueva(op, loteId, ctx, EstadoOperacion.Aplicada, resultadoJson, null, null));
            await db.SaveChangesAsync(c);
            return Result<ResultadoOperacion>.Exito(new ResultadoOperacion(op.OpId, efecto.Valor.Duplicada ? "Duplicada" : "Aplicada",
                efecto.Valor.Ids, efecto.Valor.Incidencias));
        }, ct);

        if (aplicada.EsExito)
        {
            return aplicada.Valor;
        }

        // Rechazo de negocio: queda registrado (visible en la app y en el panel) y no se reintenta (RN-SYN-04).
        var error = aplicada.Error!;
        db.ChangeTracker.Clear();
        db.Operaciones.Add(Nueva(op, loteId, ctx, EstadoOperacion.Rechazada, null, error.Codigo, error.Mensaje));
        await db.SaveChangesAsync(ct);
        return new ResultadoOperacion(op.OpId, "Rechazada", Codigo: error.Codigo, Mensaje: error.Mensaje);
    }

    private async Task<Result<Aplicacion>> AplicarAsync(IServiceProvider sp, OperacionSync op, ContextoSync ctx, CancellationToken ct)
    {
        var fecha = sp.GetRequiredService<IFechaOperativa>().De(op.CreadaEn);
        var ventas = sp.GetRequiredService<IVentasService>();
        var logistica = sp.GetRequiredService<ILogisticaService>();
        var dispositivo = ctx.Dispositivo.Id;

        // RN-SYN-09: la ruta de la operación es la del chofer en la fecha operativa de la operación, no la de hoy.
        var rutaOperacion = await logistica.RutaDeChoferAsync(ctx.UsuarioId, fecha, ct);

        try
        {
            switch (op.Tipo)
            {
                case TiposOperacion.RegistrarVenta:
                {
                    if (rutaOperacion is not { } ruta)
                    {
                        return SinRuta();
                    }

                    var d = Leer<RegistrarVentaDatos>(op) ?? throw new JsonException();
                    var venta = await ventas.RegistrarVentaAsync(new RegistrarVentaComando(
                        OrigenVenta.App, d.ClienteId, ruta.AlmacenId, ctx.UsuarioId, fecha, Enum.Parse<CondicionPago>(d.CondicionPago),
                        Enum.Parse<FormaPago>(d.FormaPago),
                        d.Partidas.Select(p => new PartidaVentaComando(p.ProductoId, p.UnidadId, p.Cantidad, p.PrecioUnitario)).ToList(),
                        d.VentaId, op.OpId, d.Folio, ruta.Id, dispositivo, op.CreadaEn, d.TotalApp, Gps(d.Gps)), ct);
                    if (!venta.EsExito)
                    {
                        return venta.Error!;
                    }

                    var incidencias = venta.Valor.Incidencias.Select(i => i.ToString()).ToList();
                    await IncidenciaSiLiquidadaAsync(sp, ruta.Id, fecha, "Venta", venta.Valor.VentaId, incidencias, ct);
                    return new Aplicacion(new Dictionary<string, Guid> { ["ventaId"] = venta.Valor.VentaId }, incidencias, venta.Valor.Duplicada);
                }

                case TiposOperacion.EntregarPedido:
                {
                    if (rutaOperacion is not { } ruta)
                    {
                        return SinRuta();
                    }

                    var d = Leer<EntregarPedidoDatos>(op) ?? throw new JsonException();
                    var entrega = await ventas.EntregarPedidoAsync(new EntregarPedidoComando(
                        OrigenVenta.App, d.PedidoId, ruta.AlmacenId, ctx.UsuarioId, fecha, Enum.Parse<FormaPago>(d.FormaPago),
                        // La app manda también las partidas no entregadas (cantidad 0).
                        d.Partidas.Where(p => p.Cantidad > 0).Select(p => new PartidaEntregaComando(p.PedidoPartidaId, p.Cantidad)).ToList(),
                        d.VentaId, op.OpId, d.Folio, ruta.Id, dispositivo, op.CreadaEn, d.TotalApp, Gps(d.Gps)), ct);
                    if (!entrega.EsExito)
                    {
                        return entrega.Error!;
                    }

                    var incidencias = entrega.Valor.Incidencias.Select(i => i.ToString()).ToList();
                    await IncidenciaSiLiquidadaAsync(sp, ruta.Id, fecha, "Venta", entrega.Valor.VentaId, incidencias, ct);
                    return new Aplicacion(new Dictionary<string, Guid> { ["ventaId"] = entrega.Valor.VentaId }, incidencias, entrega.Valor.Duplicada);
                }

                case TiposOperacion.RegistrarRetorno:
                {
                    if (rutaOperacion is not { } ruta)
                    {
                        return SinRuta();
                    }

                    var d = Leer<RegistrarRetornoDatos>(op) ?? throw new JsonException();
                    var retorno = await logistica.RegistrarRetornoAsync(new RegistrarRetornoComando(
                        Enum.Parse<TipoRetorno>(d.Tipo), ruta.Id, fecha, ctx.UsuarioId,
                        d.Partidas.Select(p => new PartidaRetornoComando(p.ProductoId, p.Cantidad, null, null,
                            Enum.Parse<CondicionRetorno>(p.Condicion), p.MotivoMermaId)).ToList(),
                        d.RetornoId, op.OpId, d.Folio, d.ClienteId, d.VentaId, dispositivo, op.CreadaEn,
                        d.Gps?.Lat, d.Gps?.Lng, d.Gps?.PrecisionM,
                        d.PedidoId is { } pedido ? $"Pedido {pedido}" : null), ct);
                    return retorno.EsExito
                        ? new Aplicacion(new Dictionary<string, Guid> { ["retornoId"] = retorno.Valor.RetornoId }, [], retorno.Valor.Duplicado)
                        : retorno.Error!;
                }

                case TiposOperacion.RegistrarCobro:
                {
                    var d = Leer<RegistrarCobroDatos>(op) ?? throw new JsonException();
                    var cobro = await ventas.RegistrarCobroAsync(new RegistrarCobroComando(
                        d.ClienteId, fecha, Enum.Parse<FormaPago>(d.FormaPago), d.Importe, d.CobroId, op.OpId, rutaOperacion?.Id,
                        dispositivo, d.VentaIds), ct);
                    return cobro.EsExito
                        ? new Aplicacion(new Dictionary<string, Guid> { ["cobroId"] = cobro.Valor.CobroId }, [], cobro.Valor.Duplicado)
                        : cobro.Error!;
                }

                case TiposOperacion.ProponerUbicacionCliente:
                {
                    var d = Leer<ProponerUbicacionDatos>(op) ?? throw new JsonException();
                    var propuesta = await ventas.ProponerUbicacionClienteAsync(
                        new ProponerUbicacionComando(d.ClienteId, Gps(d.Gps)!, dispositivo), ct);
                    return propuesta.EsExito
                        ? new Aplicacion(new Dictionary<string, Guid>(), propuesta.Valor.Select(i => i.ToString()).ToList(), false)
                        : propuesta.Error!;
                }

                case TiposOperacion.ReportarEstado:
                    return new Aplicacion(new Dictionary<string, Guid>(), [], false);

                case TiposOperacion.CapturarPedido:
                {
                    // RN-SYN-10: permiso y cliente de la ruta se validan aquí; Ventas no consulta rutas.
                    if (!sp.GetRequiredService<IUsuarioActual>().TienePermiso(Permisos.Ventas.PedidosApp))
                    {
                        return Error.Prohibido(ErroresSync.SinPermisoPedidos, "Tu usuario no tiene permiso para capturar pedidos desde la app.");
                    }

                    if (rutaOperacion is not { } ruta)
                    {
                        return SinRuta();
                    }

                    var d = Leer<CapturarPedidoDatos>(op) ?? throw new JsonException();
                    var clientesRuta = await logistica.ClientesDeRutaAsync(ruta.Id, 0, ct);
                    if (!clientesRuta.Any(c => c.ClienteId == d.ClienteId && c.Activo))
                    {
                        return Error.Validacion(ErroresSync.ClienteFueraDeRuta, "El cliente no pertenece a la ruta de esta operación.");
                    }

                    var pedido = await ventas.CapturarPedidoAsync(new CapturarPedidoComando(
                        d.PedidoId, op.OpId, d.ClienteId, d.FechaEntrega, fecha, d.OrdenCompraCliente, d.Notas,
                        d.Partidas.Select(p => new PartidaPedidoComando(p.ProductoId, p.UnidadId, p.Cantidad,
                            Enum.Parse<TipoPartidaPedido>(p.Tipo), p.Notas)).ToList(),
                        ctx.UsuarioId, dispositivo, ruta.Id), ct);
                    return pedido.EsExito
                        ? new Aplicacion(new Dictionary<string, Guid> { ["pedidoId"] = pedido.Valor.PedidoId }, [], pedido.Valor.Duplicado)
                        : pedido.Error!;
                }

                default:
                    return Error.Validacion(ErroresSync.TipoDesconocido, $"Tipo de operación desconocido: {op.Tipo}.");
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException { Source: "System.Text.Json" })
        {
            return Error.Validacion(ErroresSync.DatosInvalidos, "Los datos de la operación no son válidos.");
        }
    }

    /// <summary>RN-SYN-06: si la liquidación de ese día ya cerró, se aplica igual y se crea incidencia.</summary>
    private static async Task IncidenciaSiLiquidadaAsync(IServiceProvider sp, Guid rutaId, DateOnly fecha, string entidad, Guid id,
        List<string> incidencias, CancellationToken ct)
    {
        if (!await sp.GetRequiredService<ILogisticaService>().LiquidacionCerradaAsync(rutaId, fecha, ct))
        {
            return;
        }

        await sp.GetRequiredService<IIncidenciasService>().CrearAsync(TipoIncidencia.DiferenciaLiquidacion, entidad, id,
            new { motivo = "Operación recibida después de cerrar la liquidación", fechaOperativa = fecha }, ct);
        incidencias.Add(nameof(TipoIncidencia.DiferenciaLiquidacion));
    }

    private static T? Leer<T>(OperacionSync op) where T : class
    {
        try
        {
            return op.Datos.Deserialize<T>(Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ConsecutivoDe(OperacionSync op) =>
        op.Datos.ValueKind == JsonValueKind.Object && op.Datos.TryGetProperty("consecutivo", out var c) && c.TryGetInt32(out var n) ? n : 0;

    private static GpsLectura? Gps(GpsDatos? g) => g is null ? null : new GpsLectura(g.Lat, g.Lng, g.PrecisionM);

    private static Error SinRuta() =>
        Error.Validacion(ErroresSync.SinRuta,
            "No tenías carga ni ruta asignada en la fecha de esta operación; pide a logística que te asignen una.");

    private Operacion Nueva(OperacionSync op, Guid loteId, ContextoSync ctx, EstadoOperacion estado, string? resultado,
        string? codigo, string? error) => new()
    {
        OpId = op.OpId,
        DispositivoId = ctx.Dispositivo.Id,
        LoteId = loteId,
        Tipo = op.Tipo,
        SecuenciaDispositivo = op.Secuencia,
        CreadaEn = op.CreadaEn,
        RecibidaEn = reloj.GetUtcNow(),
        Estado = estado,
        Resultado = resultado,
        Codigo = codigo,
        Error = error,
    };

    private static ResultadoOperacion Duplicada(Guid opId, string? resultado)
    {
        Dictionary<string, Guid>? ids = null;
        List<string>? incidencias = null;
        if (resultado is not null)
        {
            using var json = JsonDocument.Parse(resultado);
            if (json.RootElement.TryGetProperty("idsServidor", out var i))
            {
                ids = i.Deserialize<Dictionary<string, Guid>>(Json);
            }

            if (json.RootElement.TryGetProperty("incidencias", out var inc))
            {
                incidencias = inc.Deserialize<List<string>>(Json);
            }
        }

        return new ResultadoOperacion(opId, "Duplicada", ids, incidencias);
    }

    private async Task RegistrarLoteAsync(SolicitudPush solicitud, ContextoSync ctx, List<ResultadoOperacion> resultados,
        CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SincronizacionDbContext>();
        if (await db.Lotes.AnyAsync(l => l.Id == solicitud.LoteId, ct))
        {
            return;
        }

        db.Lotes.Add(new LoteSync
        {
            Id = solicitud.LoteId,
            DispositivoId = ctx.Dispositivo.Id,
            UsuarioId = ctx.UsuarioId,
            RecibidoEn = reloj.GetUtcNow(),
            TotalOperaciones = solicitud.Operaciones.Count,
            Aplicadas = resultados.Count(x => x.Estado == "Aplicada"),
            Rechazadas = resultados.Count(x => x.Estado == "Rechazada"),
            Duplicadas = resultados.Count(x => x.Estado == "Duplicada"),
            VersionApp = solicitud.VersionApp,
        });
        await db.SaveChangesAsync(ct);
    }
}

public sealed record OperacionRecibidaDto(Guid OpId, Guid DispositivoId, string Tipo, long Secuencia, DateTimeOffset CreadaEn,
    DateTimeOffset RecibidaEn, string Estado, string? Codigo, string? Error);

/// <summary>Panel: operaciones recibidas por dispositivo (rechazadas con su motivo, 05 principio 4).</summary>
public sealed class ConsultaOperacionesHandler(SincronizacionDbContext db)
{
    public async Task<IReadOnlyList<OperacionRecibidaDto>> ListarAsync(Guid? dispositivoId, bool? soloRechazadas, CancellationToken ct)
    {
        var consulta = db.Operaciones.AsNoTracking();
        if (dispositivoId is not null)
        {
            consulta = consulta.Where(o => o.DispositivoId == dispositivoId);
        }

        if (soloRechazadas == true)
        {
            consulta = consulta.Where(o => o.Estado == EstadoOperacion.Rechazada);
        }

        return await consulta.OrderByDescending(o => o.RecibidaEn).Take(500)
            .Select(o => new OperacionRecibidaDto(o.OpId, o.DispositivoId, o.Tipo, o.SecuenciaDispositivo, o.CreadaEn, o.RecibidaEn,
                o.Estado.ToString(), o.Codigo, o.Error))
            .ToListAsync(ct);
    }

    internal static string Formatear(long v) => v.ToString(CultureInfo.InvariantCulture);
}
