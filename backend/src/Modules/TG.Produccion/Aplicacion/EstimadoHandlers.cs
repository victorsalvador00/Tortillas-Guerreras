using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Inventario.Publico;
using TG.Logistica.Publico;
using TG.Produccion.Dominio;
using TG.Produccion.Infraestructura;
using TG.Produccion.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Produccion.Aplicacion;

// ───────────────────────────── Estimado (RN-PRO-11, 12) ─────────────────────────────

/// <summary>Partida de pedido con su ruta: la asignada al pedido o, si no tiene, la ruta activa del cliente.</summary>
internal sealed record PartidaRuta(Guid RutaId, DemandaPartidaInfo Info)
{
    public static async Task<List<PartidaRuta>> ListarAsync(IVentasService ventas, ILogisticaService logistica, DateOnly fecha,
        Guid? canalId, CancellationToken ct)
    {
        var demanda = await ventas.DemandaPorFechaEntregaAsync(fecha, canalId, ct);
        var sinRuta = demanda.Where(d => d.RutaId is null).Select(d => d.ClienteId).Distinct().ToList();
        var rutasClientes = await logistica.RutasDeClientesAsync(sinRuta, ct);
        return demanda
            .Select(d => new PartidaRuta(d.RutaId ?? rutasClientes.GetValueOrDefault(d.ClienteId), d))
            .Where(p => p.RutaId != Guid.Empty)
            .ToList();
    }
}

/// <param name="Encabezado"><c>nombre_corto</c> o, si no tiene, el SKU.</param>
/// <param name="UnidadId">Unidad de venta predeterminada (o la base): en ella van las celdas.</param>
public sealed record ColumnaEstimadoDto(Guid ProductoId, string Encabezado, string Sku, string Producto, Guid UnidadId, string Unidad);

public sealed record CeldaEstimadoDto(Guid ProductoId, decimal Cantidad);

/// <param name="Editable">Canal con <c>pedido_por = Ruta</c> y pedido de la ruta todavía <c>Capturado</c> (o sin pedido).</param>
/// <param name="EsReferencia">Sin pedido para la fecha: las celdas son lo pedido <c>ventas.dias_estimado_referencia</c> días antes.</param>
/// <param name="PedidoIds">Pedidos de la fecha (o de la referencia si <c>EsReferencia</c>).</param>
public sealed record FilaEstimadoDto(Guid RutaId, string RutaCodigo, string RutaNombre, bool Editable, bool EsReferencia,
    IReadOnlyList<CeldaEstimadoDto> Celdas, string? Observaciones, IReadOnlyList<Guid> PedidoIds, int Capturados);

/// <param name="Total">Σ de las filas con pedido, en la unidad de la columna.</param>
/// <param name="Cajas">"N cajas + M paquetes" (RN-CAT-09); nulo si el producto no tiene unidad de traslado.</param>
/// <param name="Real">Lo cargado en envíos despachados de la fecha por las rutas del canal, en la unidad de la columna.</param>
public sealed record TotalEstimadoDto(Guid ProductoId, decimal Total, string? Cajas, decimal Real);

public sealed record EstimadoDto(DateOnly FechaEntrega, Guid CanalId, string Canal, PedidoPor PedidoPor, DateOnly FechaReferencia,
    IReadOnlyList<ColumnaEstimadoDto> Columnas, IReadOnlyList<FilaEstimadoDto> Filas, IReadOnlyList<TotalEstimadoDto> Totales,
    int PedidosCapturados);

public sealed record FilaGuardarEstimadoRequest(Guid RutaId, IReadOnlyList<CeldaEstimadoDto> Celdas);

public sealed record GuardarEstimadoRequest(DateOnly FechaEntrega, Guid CanalId, IReadOnlyList<FilaGuardarEstimadoRequest> Filas);

public sealed class GuardarEstimadoValidator : AbstractValidator<GuardarEstimadoRequest>
{
    public GuardarEstimadoValidator()
    {
        RuleFor(x => x.Filas).NotEmpty();
        RuleForEach(x => x.Filas).ChildRules(f =>
            f.RuleForEach(x => x.Celdas).ChildRules(c =>
                c.RuleFor(x => x.Cantidad).GreaterThanOrEqualTo(0).WithMessage("La cantidad no puede ser negativa.")));
    }
}

public sealed record UsarReferenciaRequest(DateOnly FechaEntrega, Guid CanalId, Guid RutaId);

public sealed record ResultadoEstimadoDto(int Pedidos);

public sealed class EstimadoHandler(
    IVentasService ventas,
    ILogisticaService logistica,
    ICatalogosService catalogos,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<EstimadoDto>> ObtenerAsync(DateOnly fechaEntrega, Guid canalId, CancellationToken ct)
    {
        var canal = (await ventas.ListarCanalesAsync(ct)).FirstOrDefault(c => c.Id == canalId);
        if (canal is null)
        {
            return Error.NoEncontrado("PRO-014", "El canal no existe.");
        }

        var rutas = (await logistica.ListarRutasAsync(ct)).Where(r => r.Activo && r.CanalId == canalId).OrderBy(r => r.Codigo).ToList();
        var dias = await catalogos.ObtenerParametroAsync(ClavesParametro.DiasEstimadoReferencia, 7, ct);
        var fechaReferencia = fechaEntrega.AddDays(-dias);
        var actuales = await PartidaRuta.ListarAsync(ventas, logistica, fechaEntrega, canalId, ct);
        var referencia = await PartidaRuta.ListarAsync(ventas, logistica, fechaReferencia, canalId, ct);
        var cargado = (await logistica.CargadoPorFechaAsync(fechaEntrega, ct)).Where(c => rutas.Any(r => r.Id == c.RutaId)).ToList();

        // Columnas: productos con demanda (o en la referencia) y los de las listas activas del canal [SUP-30].
        var productoIds = actuales.Concat(referencia).Where(p => p.Info.Tipo == TipoPartidaPedido.Venta).Select(p => p.Info.ProductoId)
            .Concat(await ventas.ProductosDeCanalAsync(canalId, ct)).Distinct().ToList();
        var productos = await catalogos.ObtenerProductosAsync(productoIds.Concat(actuales.Select(p => p.Info.ProductoId)), ct);
        var columnas = productoIds.Where(id => productos.TryGetValue(id, out var p) && p.EsVendible)
            .Select(id => productos[id]).OrderBy(p => p.Sku)
            .Select(p => { var u = UnidadVenta(p); return new ColumnaEstimadoDto(p.Id, p.NombreCorto ?? p.Sku, p.Sku, p.Nombre, u.Id, u.Clave); })
            .ToList();

        var filas = new List<FilaEstimadoDto>();
        foreach (var ruta in rutas)
        {
            var propias = actuales.Where(p => p.RutaId == ruta.Id).ToList();
            var esReferencia = propias.Count == 0;
            var fuente = esReferencia ? referencia.Where(p => p.RutaId == ruta.Id).ToList() : propias;
            var celdas = columnas
                .Select(c => new CeldaEstimadoDto(c.ProductoId, EnUnidadVenta(productos[c.ProductoId], fuente
                    .Where(p => p.Info.ProductoId == c.ProductoId && p.Info.Tipo == TipoPartidaPedido.Venta).Sum(p => p.Info.CantidadBase))))
                .Where(c => c.Cantidad != 0)
                .ToList();
            var observaciones = fuente.Select(p => p.Info.Tipo == TipoPartidaPedido.CambioFisico
                    ? $"Cambio físico: {p.Info.Cantidad:0.####} {Encabezado(productos, p.Info.ProductoId)}{(p.Info.Notas is null ? "" : $" ({p.Info.Notas})")}"
                    : p.Info.Notas is null ? null : $"{Encabezado(productos, p.Info.ProductoId)}: {p.Info.Notas}")
                .Where(o => o is not null).Distinct().ToList();
            var editable = canal.PedidoPor == PedidoPor.Ruta && propias.All(p => p.Info.Estado == EstadoPedido.Capturado);
            filas.Add(new FilaEstimadoDto(ruta.Id, ruta.Codigo, ruta.Nombre, editable, esReferencia, celdas,
                observaciones.Count > 0 ? string.Join("; ", observaciones) : null,
                fuente.Select(p => p.Info.PedidoId).Distinct().ToList(),
                esReferencia ? 0 : propias.Where(p => p.Info.Estado == EstadoPedido.Capturado).Select(p => p.Info.PedidoId).Distinct().Count()));
        }

        var totales = columnas.Select(c =>
        {
            var producto = productos[c.ProductoId];
            var baseTotal = actuales.Where(p => p.Info.ProductoId == c.ProductoId && p.Info.Tipo == TipoPartidaPedido.Venta).Sum(p => p.Info.CantidadBase);
            return new TotalEstimadoDto(c.ProductoId, EnUnidadVenta(producto, baseTotal), DesgloseCajas.Calcular(producto, baseTotal)?.ToString(),
                EnUnidadVenta(producto, cargado.Where(x => x.ProductoId == c.ProductoId).Sum(x => x.CantidadBase)));
        }).ToList();

        return new EstimadoDto(fechaEntrega, canal.Id, canal.Nombre, canal.PedidoPor, fechaReferencia, columnas, filas, totales,
            filas.Sum(f => f.Capturados));
    }

    /// <summary>RN-PRO-11: las celdas de canales con pedido por ruta escriben en el pedido global de la ruta (RN-VEN-16).</summary>
    public async Task<Result<EstimadoDto>> GuardarAsync(GuardarEstimadoRequest r, CancellationToken ct)
    {
        var canal = (await ventas.ListarCanalesAsync(ct)).FirstOrDefault(c => c.Id == r.CanalId);
        if (canal is null)
        {
            return Error.NoEncontrado("PRO-014", "El canal no existe.");
        }

        if (canal.PedidoPor != PedidoPor.Ruta)
        {
            return Error.Validacion("PRO-015", $"En {canal.Nombre} el pedido es por cliente; captúralo en Pedidos.");
        }

        var rutas = (await logistica.ListarRutasAsync(ct)).ToDictionary(x => x.Id);
        var productos = await catalogos.ObtenerProductosAsync(r.Filas.SelectMany(f => f.Celdas).Select(c => c.ProductoId), ct);
        var guardado = await unitOfWork.EjecutarAsync(async t =>
        {
            foreach (var fila in r.Filas)
            {
                if (!rutas.TryGetValue(fila.RutaId, out var ruta) || ruta.CanalId != r.CanalId)
                {
                    return Result.Falla(Error.Validacion("PRO-016", "Una ruta no existe o no es del canal."));
                }

                var celdas = new List<CeldaPedidoRuta>();
                foreach (var c in fila.Celdas)
                {
                    if (!productos.TryGetValue(c.ProductoId, out var producto))
                    {
                        return Result.Falla(Error.Validacion("PRO-016", "Un producto no existe."));
                    }

                    celdas.Add(new CeldaPedidoRuta(c.ProductoId, UnidadVenta(producto).Id, c.Cantidad));
                }

                var pedido = await ventas.GuardarPedidoRutaAsync(ruta.Id, ruta.Codigo, r.FechaEntrega, celdas, t);
                if (!pedido.EsExito)
                {
                    return Result.Falla(pedido.Error!);
                }
            }

            return Result.Exito();
        }, ct);
        return guardado.EsExito ? await ObtenerAsync(r.FechaEntrega, r.CanalId, ct) : guardado.Error!;
    }

    /// <summary>Confirma los pedidos <c>Capturado</c> del canal y la fecha (RN-VEN-13).</summary>
    public async Task<Result<ResultadoEstimadoDto>> ConfirmarAsync(DateOnly fechaEntrega, Guid canalId, CancellationToken ct)
    {
        var ids = (await PartidaRuta.ListarAsync(ventas, logistica, fechaEntrega, canalId, ct)).Where(p => p.Info.Estado == EstadoPedido.Capturado)
            .Select(p => p.Info.PedidoId).Distinct().ToList();
        var confirmados = await ventas.ConfirmarPedidosAsync(ids, ct);
        return confirmados.EsExito ? new ResultadoEstimadoDto(confirmados.Valor) : confirmados.Error!;
    }

    /// <summary>RN-PRO-12: copia como <c>Capturado</c> los pedidos de la referencia de una ruta sin pedido.</summary>
    public async Task<Result<ResultadoEstimadoDto>> UsarReferenciaAsync(UsarReferenciaRequest r, CancellationToken ct)
    {
        var estimado = await ObtenerAsync(r.FechaEntrega, r.CanalId, ct);
        if (!estimado.EsExito)
        {
            return estimado.Error!;
        }

        var fila = estimado.Valor.Filas.FirstOrDefault(f => f.RutaId == r.RutaId);
        if (fila is null || !fila.EsReferencia || fila.PedidoIds.Count == 0)
        {
            return Error.Validacion("PRO-017", "La ruta ya tiene pedido para la fecha o no hay referencia que copiar.");
        }

        var copiados = await ventas.CopiarPedidosAsync(fila.PedidoIds, r.FechaEntrega, ct);
        return copiados.EsExito ? new ResultadoEstimadoDto(copiados.Valor) : copiados.Error!;
    }

    internal static (Guid Id, string Clave, decimal Factor) UnidadVenta(ProductoInfo p) =>
        p.Conversiones.FirstOrDefault(c => c.PredeterminadaVenta) is { } c ? (c.UnidadId, c.UnidadClave, c.FactorABase) : (p.UnidadBaseId, p.UnidadBaseClave, 1m);

    private static decimal EnUnidadVenta(ProductoInfo p, decimal cantidadBase) => cantidadBase / UnidadVenta(p).Factor;

    private static string Encabezado(IReadOnlyDictionary<Guid, ProductoInfo> productos, Guid id) =>
        productos.TryGetValue(id, out var p) ? p.NombreCorto ?? p.Sku : "?";
}

// ───────────────────────────── Plan sugerido (RN-PRO-13) ─────────────────────────────

public enum AccionPlan
{
    Producir,
    Comprar,
}

/// <param name="Demanda">Σ partidas (Venta + CambioFisico) en unidad base, incluida la referencia si se pidió.</param>
/// <param name="DemandaEstimada">Parte de la demanda que viene de la referencia (rutas sin pedido, RN-PRO-12).</param>
/// <param name="CambiosPendientes">Parte de la demanda por cambios pendientes de los clientes que se visitan ese día (RN-LOG-16).</param>
/// <param name="ParaOrdenes">Parte de la demanda que consumen las órdenes abiertas que lo usan como componente (RN-PRO-13b).</param>
/// <param name="EnOrdenes">Órdenes Planeada/Liberada/EnProceso con fecha operativa hasta la fecha de entrega.</param>
public sealed record PlanSugeridoFilaDto(Guid ProductoId, string Sku, string Producto, string Unidad, short Decimales, AccionPlan Accion,
    decimal Demanda, decimal DemandaEstimada, decimal CambiosPendientes, decimal ParaOrdenes, decimal Cargado, decimal Disponible, decimal EnOrdenes, decimal Faltante, bool TieneReceta);

public sealed record PlanSugeridoDto(DateOnly FechaEntrega, bool IncluyeReferencia, IReadOnlyList<PlanSugeridoFilaDto> Filas);

public sealed record PartidaCrearOrdenRequest(Guid ProductoId, decimal Cantidad);

public sealed record CrearOrdenesRequest(DateOnly? FechaOperativa, Turno Turno, Guid? LineaId, IReadOnlyList<PartidaCrearOrdenRequest> Partidas);

public sealed class CrearOrdenesValidator : AbstractValidator<CrearOrdenesRequest>
{
    public CrearOrdenesValidator()
    {
        RuleFor(x => x.Partidas).NotEmpty().WithMessage("Elige al menos un producto.");
        RuleForEach(x => x.Partidas).ChildRules(p => p.RuleFor(x => x.Cantidad).GreaterThan(0));
    }
}

public sealed record OrdenCreadaDto(Guid ProductoId, Guid OrdenId, string Folio, decimal Cantidad);

public sealed record OrdenOmitidaDto(Guid ProductoId, string Codigo, string Mensaje);

public sealed record CrearOrdenesDto(IReadOnlyList<OrdenCreadaDto> Creadas, IReadOnlyList<OrdenOmitidaDto> Omitidas);

public sealed class PlanSugeridoHandler(
    ProduccionDbContext db,
    IVentasService ventas,
    ILogisticaService logistica,
    ICatalogosService catalogos,
    IInventarioService inventario,
    OrdenesHandler ordenes) : IPendientesProduccion
{
    private static readonly EstadoOrden[] Abiertas = [EstadoOrden.Planeada, EstadoOrden.Liberada, EstadoOrden.EnProceso];

    public async Task<PlanSugeridoDto> ObtenerAsync(DateOnly fechaEntrega, bool incluirReferencia, CancellationToken ct)
    {
        // La demanda cuenta todos los pedidos, tengan ruta o no; la ruta solo importa para la referencia.
        var demanda = (await ventas.DemandaPorFechaEntregaAsync(fechaEntrega, null, ct)).GroupBy(p => p.ProductoId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.CantidadBase));

        // RN-PRO-12: las rutas activas sin pedido aportan lo de la referencia, marcado como estimado.
        var estimada = new Dictionary<Guid, decimal>();
        if (incluirReferencia)
        {
            var dias = await catalogos.ObtenerParametroAsync(ClavesParametro.DiasEstimadoReferencia, 7, ct);
            var conPedido = (await PartidaRuta.ListarAsync(ventas, logistica, fechaEntrega, null, ct)).Select(p => p.RutaId).ToHashSet();
            var rutasActivas = (await logistica.ListarRutasAsync(ct)).Where(r => r.Activo).Select(r => r.Id).ToHashSet();
            foreach (var p in await PartidaRuta.ListarAsync(ventas, logistica, fechaEntrega.AddDays(-dias), null, ct))
            {
                if (rutasActivas.Contains(p.RutaId) && !conPedido.Contains(p.RutaId))
                {
                    estimada[p.Info.ProductoId] = estimada.GetValueOrDefault(p.Info.ProductoId) + p.Info.CantidadBase;
                }
            }
        }

        var cargado = (await logistica.CargadoPorFechaAsync(fechaEntrega, ct)).GroupBy(c => c.ProductoId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.CantidadBase));
        var pendientes = (await logistica.CambiosPendientesDelDiaAsync(fechaEntrega, ct)).GroupBy(p => p.ProductoId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.RestanteBase));
        var paraOrdenes = await ConsumosDeOrdenesAbiertasAsync(fechaEntrega, ct);
        var productoIds = demanda.Keys.Concat(estimada.Keys).Concat(pendientes.Keys).Concat(paraOrdenes.Select(c => c.ProductoId)).Distinct().ToList();

        // Lo vendible se surte de PT; un componente, además, del almacén de donde lo consumen sus órdenes (RN-PRO-11).
        var pt = await inventario.ObtenerAlmacenPorCodigoAsync("PT", ct);
        var disponible = new Dictionary<Guid, decimal>();
        var almacenes = paraOrdenes.Select(c => (c.AlmacenId, Componente: (Guid?)c.ProductoId));
        if (pt is not null)
        {
            almacenes = almacenes.Prepend((pt.Id, null));
        }

        foreach (var almacen in almacenes.GroupBy(a => a.AlmacenId))
        {
            // PT cuenta para todo; otro almacén, solo para los componentes que de ahí se consumen.
            var componentes = almacen.Any(a => a.Componente is null) ? null : almacen.Select(a => a.Componente!.Value).ToHashSet();
            foreach (var e in await inventario.ExistenciasDeAlmacenAsync(almacen.Key, ct))
            {
                if (e.Cantidad > 0 && (e.FechaCaducidad is null || e.FechaCaducidad >= fechaEntrega)
                    && (componentes is null || componentes.Contains(e.ProductoId)))
                {
                    disponible[e.ProductoId] = disponible.GetValueOrDefault(e.ProductoId) + e.Cantidad;
                }
            }
        }

        var componenteDe = paraOrdenes.GroupBy(c => c.ProductoId).ToDictionary(g => g.Key, g => g.Sum(c => c.CantidadBase));
        var enOrdenes = (await db.Ordenes.AsNoTracking()
                .Where(o => productoIds.Contains(o.ProductoId) && Abiertas.Contains(o.Estado) && o.FechaOperativa <= fechaEntrega)
                .Select(o => new { o.ProductoId, o.CantidadPlaneada }).ToListAsync(ct))
            .GroupBy(o => o.ProductoId).ToDictionary(g => g.Key, g => g.Sum(o => o.CantidadPlaneada));
        var conReceta = (await db.Recetas.AsNoTracking()
            .Where(r => productoIds.Contains(r.ProductoId) && r.Estado == EstadoReceta.Vigente).Select(r => r.ProductoId).ToListAsync(ct)).ToHashSet();
        var productos = await catalogos.ObtenerProductosAsync(productoIds, ct);

        var filas = new List<PlanSugeridoFilaDto>();
        foreach (var id in productoIds)
        {
            if (!productos.TryGetValue(id, out var p) || (!p.EsProducible && !p.EsComprable))
            {
                continue;
            }

            // RN-PRO-13b: un componente que se compra (materia prima) va por el requerimiento de compras, no por el plan.
            var consumo = p.EsProducible ? componenteDe.GetValueOrDefault(id) : 0m;
            var total = demanda.GetValueOrDefault(id) + estimada.GetValueOrDefault(id) + pendientes.GetValueOrDefault(id) + consumo;
            if (consumo == 0 && !demanda.ContainsKey(id) && !estimada.ContainsKey(id) && !pendientes.ContainsKey(id))
            {
                continue;
            }

            var neta = total - cargado.GetValueOrDefault(id);
            var producir = p.EsProducible;
            var ordenesAbiertas = producir ? enOrdenes.GetValueOrDefault(id) : 0m;
            var faltante = Math.Max(0, neta - disponible.GetValueOrDefault(id) - ordenesAbiertas);
            filas.Add(new PlanSugeridoFilaDto(id, p.Sku, p.Nombre, p.UnidadBaseClave, p.UnidadBaseDecimales,
                producir ? AccionPlan.Producir : AccionPlan.Comprar, total, estimada.GetValueOrDefault(id), pendientes.GetValueOrDefault(id),
                consumo, cargado.GetValueOrDefault(id),
                disponible.GetValueOrDefault(id), ordenesAbiertas, faltante, conReceta.Contains(id)));
        }

        return new PlanSugeridoDto(fechaEntrega, incluirReferencia,
            filas.OrderBy(f => f.Accion).ThenByDescending(f => f.Faltante).ThenBy(f => f.Sku).ToList());
    }

    public async Task<int> PlanPorProgramarAsync(DateOnly fechaEntrega, CancellationToken ct) =>
        (await ObtenerAsync(fechaEntrega, true, ct)).Filas.Count(f => f.Accion == AccionPlan.Producir && f.Faltante > 0);

    private sealed record ConsumoPendiente(Guid ProductoId, Guid AlmacenId, decimal CantidadBase);

    /// <summary>
    /// RN-PRO-13b: lo que consumirán las órdenes abiertas hasta la fecha. Una orden liberada ya tiene sus consumos (teórico y almacén);
    /// una planeada los toma de su receta. Es un solo nivel: el componente de un componente aparece hasta que exista la orden de este.
    /// </summary>
    private async Task<List<ConsumoPendiente>> ConsumosDeOrdenesAbiertasAsync(DateOnly fechaEntrega, CancellationToken ct)
    {
        var abiertas = await db.Ordenes.AsNoTracking().Include(o => o.Consumos)
            .Where(o => Abiertas.Contains(o.Estado) && o.FechaOperativa <= fechaEntrega).ToListAsync(ct);
        var recetaIds = abiertas.Where(o => o.Consumos.Count == 0).Select(o => o.RecetaId).Distinct().ToList();
        var recetas = await db.Recetas.AsNoTracking().Include(r => r.Componentes).Where(r => recetaIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);

        var consumos = new List<ConsumoPendiente>();
        foreach (var orden in abiertas)
        {
            if (orden.Consumos.Count > 0)
            {
                consumos.AddRange(orden.Consumos.Select(c => new ConsumoPendiente(c.ProductoId, c.AlmacenId, c.CantidadTeoricaBase)));
            }
            else if (recetas.TryGetValue(orden.RecetaId, out var receta))
            {
                consumos.AddRange(receta.Componentes.Select(c => new ConsumoPendiente(c.ProductoId, c.AlmacenId ?? orden.AlmacenConsumoId,
                    ReglasProduccion.ConsumoTeorico(c.CantidadBase, orden.CantidadPlaneada, receta.RendimientoCantidad))));
            }
        }

        return consumos.Where(c => c.CantidadBase > 0).ToList();
    }

    /// <summary>Una orden por producto (receta vigente); sin receta vigente se omite con PRO-008.</summary>
    public async Task<CrearOrdenesDto> CrearOrdenesAsync(CrearOrdenesRequest r, CancellationToken ct)
    {
        var productos = await catalogos.ObtenerProductosAsync(r.Partidas.Select(p => p.ProductoId), ct);
        var creadas = new List<OrdenCreadaDto>();
        var omitidas = new List<OrdenOmitidaDto>();
        foreach (var partida in r.Partidas)
        {
            // La cantidad se redondea hacia arriba a los decimales de la unidad base (CAT-006).
            var decimales = productos.TryGetValue(partida.ProductoId, out var p) ? p.UnidadBaseDecimales : (short)4;
            var escala = (decimal)Math.Pow(10, decimales);
            var cantidad = Math.Ceiling(partida.Cantidad * escala) / escala;
            var orden = await ordenes.PlanearAsync(new PlanearOrdenRequest(partida.ProductoId, null, r.FechaOperativa, r.Turno, r.LineaId,
                cantidad, null, null, "Plan sugerido"), ct);
            if (orden.EsExito)
            {
                creadas.Add(new OrdenCreadaDto(partida.ProductoId, orden.Valor.Id, orden.Valor.Folio, cantidad));
            }
            else
            {
                omitidas.Add(new OrdenOmitidaDto(partida.ProductoId, orden.Error!.Codigo, orden.Error.Mensaje));
            }
        }

        return new CrearOrdenesDto(creadas, omitidas);
    }
}
