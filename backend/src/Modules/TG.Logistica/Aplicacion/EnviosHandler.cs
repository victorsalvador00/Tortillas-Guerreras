using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Inventario.Publico;
using TG.Logistica.Dominio;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.Seguridad.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Logistica.Aplicacion;

public sealed record EnvioPartidaRequest(Guid ProductoId, decimal Cantidad, Guid? UnidadId, string? Lote);

/// <param name="ChoferId">RN-LOG-12: si falta, el chofer predeterminado de la ruta (al editar, el que ya tiene).</param>
/// <param name="VehiculoId">Si falta, el vehículo predeterminado de la ruta (al editar, el que ya tiene).</param>
public sealed record GuardarEnvioRequest(
    Guid RutaId,
    TipoEnvio Tipo,
    DateOnly? FechaOperativa,
    Guid? AlmacenOrigenId,
    string? Notas,
    IReadOnlyList<EnvioPartidaRequest> Partidas,
    IReadOnlyList<Guid>? PedidoIds,
    Guid? ChoferId = null,
    Guid? VehiculoId = null);

public sealed class GuardarEnvioValidator : AbstractValidator<GuardarEnvioRequest>
{
    public GuardarEnvioValidator()
    {
        RuleFor(x => x.RutaId).NotEmpty().WithMessage("Elige la ruta.");
        RuleFor(x => x.Partidas).NotEmpty().WithMessage("Agrega al menos un producto a la carga.");
        RuleForEach(x => x.Partidas).ChildRules(p =>
        {
            p.RuleFor(x => x.ProductoId).NotEmpty();
            p.RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad debe ser mayor que cero.");
        });
    }
}

public sealed record EnvioPartidaDto(Guid Id, Guid ProductoId, string Sku, string Producto, string Unidad, string Lote, decimal CantidadBase);

public sealed record EnvioDto(
    Guid Id,
    string Folio,
    DateOnly FechaOperativa,
    Guid RutaId,
    string RutaCodigo,
    Guid? ChoferId,
    string? ChoferNombre,
    Guid? VehiculoId,
    string? VehiculoPlacas,
    TipoEnvio Tipo,
    Guid AlmacenOrigenId,
    string AlmacenOrigen,
    Guid AlmacenDestinoId,
    string AlmacenDestino,
    EstadoEnvio Estado,
    DateTimeOffset? DespachadoEn,
    Guid? LiquidacionId,
    string? Notas,
    DateTimeOffset CreadoEn,
    IReadOnlyList<EnvioPartidaDto> Partidas);

/// <summary>Cargas de camioneta: Preparado → EnRuta (despacho con traspaso PT → ruta) → Liquidado; o Cancelado.</summary>
public sealed class EnviosHandler(
    LogisticaDbContext db,
    IInventarioService inventario,
    ICatalogosService catalogos,
    IVentasService ventas,
    ISeguridadService seguridad,
    IGeneradorFolios folios,
    IFechaOperativa fechaOperativa,
    IUsuarioActual usuario,
    IUnitOfWork unitOfWork,
    TimeProvider reloj)
{
    public async Task<Paginado<EnvioDto>> ListarAsync(DateOnly? fecha, Guid? rutaId, EstadoEnvio? estado, int? pagina, int? tamano,
        CancellationToken ct)
    {
        var consulta = db.Envios.AsNoTracking().Include(e => e.Partidas).AsQueryable();
        if (fecha is not null)
        {
            consulta = consulta.Where(e => e.FechaOperativa == fecha);
        }

        if (rutaId is not null)
        {
            consulta = consulta.Where(e => e.RutaId == rutaId);
        }

        if (estado is not null)
        {
            consulta = consulta.Where(e => e.Estado == estado);
        }

        var paginado = await consulta.OrderByDescending(e => e.FechaOperativa).ThenByDescending(e => e.Folio).PaginarAsync(pagina, tamano, ct);
        var mapeados = await MapearAsync(paginado.Items.ToList(), ct);
        return new Paginado<EnvioDto>(mapeados, paginado.Total, paginado.Pagina, paginado.Tamano);
    }

    public async Task<Result<EnvioDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var envio = await db.Envios.AsNoTracking().Include(e => e.Partidas).FirstOrDefaultAsync(e => e.Id == id, ct);
        return envio is null ? NoEncontrado() : (await MapearAsync([envio], ct))[0];
    }

    public async Task<Result<EnvioDto>> CrearAsync(GuardarEnvioRequest r, CancellationToken ct)
    {
        var ruta = await db.Rutas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.RutaId, ct);
        if (ruta is null || !ruta.Activo)
        {
            return Error.Campo(ErroresLogistica.RutaNoEncontrada, "rutaId", "La ruta no existe o está inactiva.");
        }

        var origen = await ResolverOrigenAsync(r.AlmacenOrigenId, ct);
        if (!origen.EsExito)
        {
            return origen.Error!;
        }

        var partidas = await ConvertirPartidasAsync(r.Partidas, ct);
        if (!partidas.EsExito)
        {
            return partidas.Error!;
        }

        var fecha = r.FechaOperativa ?? fechaOperativa.Hoy();
        var asignacion = await ResolverChoferAsync(ruta, fecha, r.ChoferId ?? ruta.VendedorId, r.VehiculoId ?? ruta.VehiculoId, null, ct);
        if (!asignacion.EsExito)
        {
            return asignacion.Error!;
        }

        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var envio = new Envio
            {
                Folio = await folios.SiguienteAsync(LogisticaDbContext.Esquema, "ENV", c),
                FechaOperativa = fecha,
                RutaId = ruta.Id,
                ChoferId = asignacion.Valor.ChoferId,
                VehiculoId = asignacion.Valor.VehiculoId,
                Tipo = r.Tipo,
                AlmacenOrigenId = origen.Valor.Id,
                AlmacenDestinoId = ruta.AlmacenId,
                Notas = Limpiar(r.Notas),
            };
            envio.Partidas = partidas.Valor.Select(p => new EnvioPartida
            {
                EnvioId = envio.Id, ProductoId = p.ProductoId, Lote = p.Lote, CantidadBase = p.Cantidad,
            }).ToList();
            db.Envios.Add(envio);
            await AlinearChoferJornadaAsync(envio, c);
            await db.SaveChangesAsync(c);
            if (r.PedidoIds is { Count: > 0 })
            {
                var vinculo = await ventas.VincularPedidosEnvioAsync(r.PedidoIds, envio.Id, envio.RutaId, c);
                if (!vinculo.EsExito)
                {
                    return Result<Guid>.Falla(vinculo.Error!);
                }
            }

            return Result<Guid>.Exito(envio.Id);
        }, ct);
        return resultado.EsExito ? await ObtenerAsync(resultado.Valor, ct) : resultado.Error!;
    }

    public async Task<Result<EnvioDto>> ActualizarAsync(Guid id, GuardarEnvioRequest r, CancellationToken ct)
    {
        var envio = await db.Envios.Include(e => e.Partidas).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (envio is null)
        {
            return NoEncontrado();
        }

        if (envio.Estado != EstadoEnvio.Preparado)
        {
            return Error.Conflicto(ErroresLogistica.EstadoEnvio, "Solo se edita una carga en estado Preparado.");
        }

        if (r.RutaId != envio.RutaId)
        {
            return Error.Campo(ErroresLogistica.RutaNoEncontrada, "rutaId", "La ruta de una carga no se cambia; cancélala y crea otra.");
        }

        var origen = await ResolverOrigenAsync(r.AlmacenOrigenId ?? envio.AlmacenOrigenId, ct);
        if (!origen.EsExito)
        {
            return origen.Error!;
        }

        var partidas = await ConvertirPartidasAsync(r.Partidas, ct);
        if (!partidas.EsExito)
        {
            return partidas.Error!;
        }

        var ruta = await db.Rutas.AsNoTracking().FirstAsync(x => x.Id == envio.RutaId, ct);
        var fecha = r.FechaOperativa ?? envio.FechaOperativa;
        var asignacion = await ResolverChoferAsync(ruta, fecha, r.ChoferId ?? envio.ChoferId ?? ruta.VendedorId,
            r.VehiculoId ?? envio.VehiculoId ?? ruta.VehiculoId, envio.Id, ct);
        if (!asignacion.EsExito)
        {
            return asignacion.Error!;
        }

        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            envio.Tipo = r.Tipo;
            envio.FechaOperativa = fecha;
            envio.ChoferId = asignacion.Valor.ChoferId;
            envio.VehiculoId = asignacion.Valor.VehiculoId;
            await AlinearChoferJornadaAsync(envio, c);
            envio.AlmacenOrigenId = origen.Valor.Id;
            envio.Notas = Limpiar(r.Notas);
            db.RemoveRange(envio.Partidas);
            envio.Partidas = partidas.Valor.Select(p => new EnvioPartida
            {
                EnvioId = envio.Id, ProductoId = p.ProductoId, Lote = p.Lote, CantidadBase = p.Cantidad,
            }).ToList();
            foreach (var p in envio.Partidas)
            {
                db.Entry(p).State = EntityState.Added;
            }

            await db.SaveChangesAsync(c);
            if (r.PedidoIds is { Count: > 0 })
            {
                var vinculo = await ventas.VincularPedidosEnvioAsync(r.PedidoIds, envio.Id, envio.RutaId, c);
                if (!vinculo.EsExito)
                {
                    return Result.Falla(vinculo.Error!);
                }
            }

            return Result.Exito();
        }, ct);
        return resultado.EsExito ? await ObtenerAsync(id, ct) : resultado.Error!;
    }

    /// <summary>
    /// RN-LOG-02: traspaso del almacén origen (FEFO, existencia suficiente) al de la ruta con los mismos lotes y costo
    /// (RN-INV-06). RN-LOG-03: la primera carga del día abre la liquidación de la ruta y fecha.
    /// </summary>
    public async Task<Result<EnvioDto>> DespacharAsync(Guid id, CancellationToken ct)
    {
        var envio = await db.Envios.Include(e => e.Partidas).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (envio is null)
        {
            return NoEncontrado();
        }

        if (envio.Estado != EstadoEnvio.Preparado)
        {
            return Error.Conflicto(ErroresLogistica.EstadoEnvio, "Solo se despacha una carga en estado Preparado.");
        }

        // Cargas de v0.1 sin chofer y cambios desde que se preparó: se valida igual que al guardar (RN-LOG-12/13).
        var ruta = await db.Rutas.AsNoTracking().FirstAsync(x => x.Id == envio.RutaId, ct);
        var asignacion = await ResolverChoferAsync(ruta, envio.FechaOperativa, envio.ChoferId ?? ruta.VendedorId, envio.VehiculoId,
            envio.Id, ct);
        if (!asignacion.EsExito)
        {
            return asignacion.Error!;
        }

        envio.ChoferId = asignacion.Valor.ChoferId;

        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var liquidacion = await db.Liquidaciones.FirstOrDefaultAsync(l => l.RutaId == envio.RutaId && l.FechaOperativa == envio.FechaOperativa, c);
            if (liquidacion is not null && liquidacion.Estado != EstadoLiquidacion.Abierta)
            {
                return Result.Falla(Error.Conflicto(ErroresLogistica.LiquidacionCerrada,
                    "La liquidación de la ruta para ese día ya está cerrada; programa la carga para otro día."));
            }

            var salida = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.Envio, envio.Id, envio.Folio, envio.FechaOperativa, usuario.Id ?? Guid.Empty,
                envio.Partidas.Select(p => new LineaMovimiento(TipoMovimiento.TraspasoSalida, envio.AlmacenOrigenId, p.ProductoId,
                    p.Lote.Length == 0 ? null : p.Lote, -p.CantidadBase)).ToList(), Notas: $"Carga {envio.Folio}"), c);
            if (!salida.EsExito)
            {
                return Result.Falla(salida.Error!);
            }

            var entrada = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.Envio, envio.Id, envio.Folio, envio.FechaOperativa, usuario.Id ?? Guid.Empty,
                salida.Valor.Select(m => new LineaMovimiento(TipoMovimiento.TraspasoEntrada, envio.AlmacenDestinoId, m.ProductoId,
                    m.Lote, -m.Cantidad, m.CostoUnitario, FechaCaducidad: m.FechaCaducidad)).ToList(), Notas: $"Carga {envio.Folio}"), c);
            if (!entrada.EsExito)
            {
                return Result.Falla(entrada.Error!);
            }

            // Las partidas quedan con el lote real que salió (lo que baja a la app).
            db.RemoveRange(envio.Partidas);
            envio.Partidas = salida.Valor.GroupBy(m => new { m.ProductoId, m.Lote })
                .Select(g => new EnvioPartida { EnvioId = envio.Id, ProductoId = g.Key.ProductoId, Lote = g.Key.Lote, CantidadBase = -g.Sum(m => m.Cantidad) })
                .ToList();
            foreach (var p in envio.Partidas)
            {
                db.Entry(p).State = EntityState.Added;
            }

            if (liquidacion is null)
            {
                // RN-LOG-19: el formato se copia del canal de la ruta al abrirse.
                var canalId = await db.Rutas.Where(r => r.Id == envio.RutaId).Select(r => r.CanalId).FirstAsync(c);
                var canal = (await ventas.ListarCanalesAsync(c)).FirstOrDefault(x => x.Id == canalId);
                liquidacion = new Liquidacion
                {
                    Formato = canal?.FormatoLiquidacion ?? FormatoLiquidacion.Completo,
                    Folio = await folios.SiguienteAsync(LogisticaDbContext.Esquema, "LIQ", c),
                    RutaId = envio.RutaId,
                    FechaOperativa = envio.FechaOperativa,
                    ChoferId = envio.ChoferId,
                };
                db.Liquidaciones.Add(liquidacion);
            }
            else
            {
                // RN-LOG-13: la liquidación lleva el chofer de las cargas de la jornada.
                liquidacion.ChoferId ??= envio.ChoferId;
            }

            envio.Estado = EstadoEnvio.EnRuta;
            envio.DespachadoEn = reloj.GetUtcNow();
            envio.LiquidacionId = liquidacion.Id;
            await db.SaveChangesAsync(c);
            return Result.Exito();
        }, ct);
        return resultado.EsExito ? await ObtenerAsync(id, ct) : resultado.Error!;
    }

    public async Task<Result<EnvioDto>> CancelarAsync(Guid id, CancellationToken ct)
    {
        var envio = await db.Envios.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (envio is null)
        {
            return NoEncontrado();
        }

        if (envio.Estado != EstadoEnvio.Preparado)
        {
            return Error.Conflicto(ErroresLogistica.EstadoEnvio, "Solo se cancela una carga en estado Preparado.");
        }

        await unitOfWork.EjecutarAsync(async c =>
        {
            envio.Estado = EstadoEnvio.Cancelado;
            await db.SaveChangesAsync(c);
            await ventas.DesvincularPedidosEnvioAsync(envio.Id, c);
            return Result.Exito();
        }, ct);
        return await ObtenerAsync(id, ct);
    }

    private async Task<Result<AlmacenInfo>> ResolverOrigenAsync(Guid? almacenId, CancellationToken ct)
    {
        var almacen = almacenId is { } id
            ? await inventario.ObtenerAlmacenAsync(id, ct)
            : await inventario.ObtenerAlmacenPorCodigoAsync("PT", ct);
        if (almacen is null || !almacen.Activo)
        {
            return Error.Campo(ErroresInventario.AlmacenNoEncontrado, "almacenOrigenId", "El almacén de origen no existe o está inactivo.");
        }

        if (almacen.Tipo == TipoAlmacen.Ruta)
        {
            return Error.Campo(ErroresInventario.AlmacenNoEncontrado, "almacenOrigenId", "El origen de una carga no puede ser otra ruta.");
        }

        return almacen;
    }

    private sealed record Asignacion(Guid ChoferId, Guid? VehiculoId);

    /// <summary>
    /// RN-LOG-12: chofer activo con rol Vendedor y vehículo activo. RN-LOG-13: la jornada (ruta y fecha) tiene un solo
    /// chofer —no se cambia si ya hay cargas despachadas o una liquidación con otro— y [SUP-24] el chofer no lleva otra
    /// ruta ese día.
    /// </summary>
    private async Task<Result<Asignacion>> ResolverChoferAsync(Ruta ruta, DateOnly fecha, Guid? choferId, Guid? vehiculoId, Guid? envioId,
        CancellationToken ct)
    {
        if (choferId is not { } chofer)
        {
            return Error.Campo(ErroresLogistica.ChoferInvalido, "choferId", "Elige el chofer de la carga; la ruta no tiene chofer predeterminado.");
        }

        var vendedores = await seguridad.UsuariosConRolAsync(RutasHandler.RolVendedor, ct);
        if (!vendedores.Any(u => u.Id == chofer && u.Activo))
        {
            return Error.Campo(ErroresLogistica.ChoferInvalido, "choferId", "El chofer debe ser un usuario activo con el rol Vendedor.");
        }

        if (vehiculoId is { } vehiculo && !await db.Vehiculos.AnyAsync(v => v.Id == vehiculo && v.Activo, ct))
        {
            return Error.Campo(ErroresLogistica.VehiculoNoEncontrado, "vehiculoId", "El vehículo no existe o está inactivo.");
        }

        var otroChofer = await db.Envios.AsNoTracking().AnyAsync(e => e.RutaId == ruta.Id && e.FechaOperativa == fecha && e.Id != envioId
            && (e.Estado == EstadoEnvio.EnRuta || e.Estado == EstadoEnvio.Liquidado) && e.ChoferId != null && e.ChoferId != chofer, ct)
            || await db.Liquidaciones.AsNoTracking().AnyAsync(l => l.RutaId == ruta.Id && l.FechaOperativa == fecha
                && l.ChoferId != null && l.ChoferId != chofer, ct);
        if (otroChofer)
        {
            return Error.Campo(ErroresLogistica.ChoferDistintoJornada, "choferId",
                "La ruta ya salió ese día con otro chofer; todas las cargas de la jornada llevan el mismo. Liquida o cancela lo anterior.");
        }

        if (await catalogos.ObtenerParametroAsync(ClavesParametro.ChoferUnaRutaPorDia, true, ct))
        {
            var otraRuta = await db.Envios.AsNoTracking()
                .Where(e => e.ChoferId == chofer && e.FechaOperativa == fecha && e.RutaId != ruta.Id
                    && (e.Estado == EstadoEnvio.Preparado || e.Estado == EstadoEnvio.EnRuta))
                .Join(db.Rutas, e => e.RutaId, x => x.Id, (e, x) => x.Codigo)
                .FirstOrDefaultAsync(ct);
            if (otraRuta is not null)
            {
                return Error.Campo(ErroresLogistica.ChoferEnOtraRuta, "choferId",
                    $"El chofer ya tiene una carga en la ruta {otraRuta} ese día; liquídala o cancélala primero.");
            }
        }

        return new Asignacion(chofer, vehiculoId);
    }

    /// <summary>RN-LOG-13: las demás cargas en <c>Preparado</c> de la jornada toman el chofer de esta.</summary>
    private async Task AlinearChoferJornadaAsync(Envio envio, CancellationToken ct)
    {
        var hermanas = await db.Envios.Where(e => e.RutaId == envio.RutaId && e.FechaOperativa == envio.FechaOperativa && e.Id != envio.Id
            && e.Estado == EstadoEnvio.Preparado && e.ChoferId != envio.ChoferId).ToListAsync(ct);
        foreach (var h in hermanas)
        {
            h.ChoferId = envio.ChoferId;
        }
    }

    private sealed record PartidaBase(Guid ProductoId, string Lote, decimal Cantidad);

    private async Task<Result<List<PartidaBase>>> ConvertirPartidasAsync(IReadOnlyList<EnvioPartidaRequest> partidas, CancellationToken ct)
    {
        var productos = await catalogos.ObtenerProductosAsync(partidas.Select(p => p.ProductoId), ct);
        var resultado = new List<PartidaBase>();
        foreach (var p in partidas)
        {
            if (!productos.TryGetValue(p.ProductoId, out var producto) || !producto.Activo || !producto.EsVendible)
            {
                return Error.Campo(ErroresLogistica.ProductoInvalido, "partidas", "Uno de los productos no existe, no es vendible o está inactivo.");
            }

            var cantidad = p.Cantidad;
            if (p.UnidadId is { } unidad)
            {
                var conversion = await catalogos.ConvertirABaseAsync(p.ProductoId, unidad, p.Cantidad, UsoUnidad.Venta, ct);
                if (!conversion.EsExito)
                {
                    return conversion.Error!;
                }

                cantidad = conversion.Valor.Base;
            }

            var lote = producto.ManejaLote ? (p.Lote ?? string.Empty).Trim().ToUpperInvariant() : string.Empty;
            resultado.Add(new PartidaBase(p.ProductoId, lote, cantidad));
        }

        return resultado;
    }

    private async Task<IReadOnlyList<EnvioDto>> MapearAsync(List<Envio> envios, CancellationToken ct)
    {
        var productos = await catalogos.ObtenerProductosAsync(envios.SelectMany(e => e.Partidas).Select(p => p.ProductoId), ct);
        var almacenes = (await inventario.ListarAlmacenesAsync(ct)).ToDictionary(a => a.Id, a => a.Codigo);
        var rutas = await db.Rutas.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Codigo, ct);
        var choferes = await seguridad.NombresUsuariosAsync(envios.Where(e => e.ChoferId != null).Select(e => e.ChoferId!.Value), ct);
        var vehiculos = await db.Vehiculos.AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.Placas, ct);
        return envios.Select(e => new EnvioDto(e.Id, e.Folio, e.FechaOperativa, e.RutaId, rutas.GetValueOrDefault(e.RutaId) ?? "?",
            e.ChoferId, e.ChoferId is { } ch ? choferes.GetValueOrDefault(ch) : null, e.VehiculoId,
            e.VehiculoId is { } v ? vehiculos.GetValueOrDefault(v) : null, e.Tipo,
            e.AlmacenOrigenId, almacenes.GetValueOrDefault(e.AlmacenOrigenId) ?? "?", e.AlmacenDestinoId,
            almacenes.GetValueOrDefault(e.AlmacenDestinoId) ?? "?", e.Estado, e.DespachadoEn, e.LiquidacionId, e.Notas, e.CreadoEn,
            e.Partidas.OrderBy(p => productos.GetValueOrDefault(p.ProductoId)?.Sku).ThenBy(p => p.Lote).Select(p =>
            {
                var producto = productos.GetValueOrDefault(p.ProductoId);
                return new EnvioPartidaDto(p.Id, p.ProductoId, producto?.Sku ?? "?", producto?.Nombre ?? "?",
                    producto?.UnidadBaseClave ?? string.Empty, p.Lote, p.CantidadBase);
            }).ToList())).ToList();
    }

    private static Error NoEncontrado() => Error.NoEncontrado(ErroresLogistica.EnvioNoEncontrado, "La carga no existe.");

    private static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
