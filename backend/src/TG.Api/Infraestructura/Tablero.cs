using System.Data;
using Dapper;
using Hangfire;
using Microsoft.AspNetCore.Http.HttpResults;
using TG.Facturacion.Aplicacion;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;

namespace TG.Api.Infraestructura;

// Tablero y reportes (07-web, T-7.1): consultas de solo lectura entre esquemas con Dapper, en el host (no escriben
// ni dependen de internos de los módulos).

public sealed record ProduccionDiaDto(string Sku, string Producto, string Unidad, decimal Planeado, decimal Producido, int Ordenes,
    int Terminadas);

public sealed record MermaPeriodoDto(decimal CostoMerma, decimal CostoProducido, decimal? Porcentaje);

public sealed record VentasGrupoDto(string Grupo, int Ventas, decimal Total);

public sealed record BajoMinimoDto(Guid ProductoId, string Sku, string Producto, string Unidad, decimal Minimo, decimal Existencia);

public sealed record ConteoTipoDto(string Tipo, int Total);

public sealed record LiquidacionPendienteDto(Guid Id, string Folio, string Ruta, DateOnly FechaOperativa);

public sealed record AjusteRecienteDto(Guid DocumentoId, string Folio, DateTimeOffset FechaHora, decimal CostoTotal, string? Notas);

public sealed record TableroDto(
    DateOnly Fecha,
    IReadOnlyList<ProduccionDiaDto> Produccion,
    MermaPeriodoDto MermaDia,
    MermaPeriodoDto MermaMes,
    IReadOnlyList<VentasGrupoDto> VentasPorRuta,
    IReadOnlyList<VentasGrupoDto> VentasPorCanal,
    decimal VentasTotalDia,
    IReadOnlyList<BajoMinimoDto> BajoMinimo,
    IReadOnlyList<ConteoTipoDto> IncidenciasAbiertas,
    IReadOnlyList<LiquidacionPendienteDto> LiquidacionesPendientes,
    IReadOnlyList<AjusteRecienteDto> AjustesDelMes);

public sealed record MermaMotivoDto(string Motivo, string Tipo, decimal Cantidad, decimal Costo, int Movimientos);

public sealed record MermaProductoDto(string Sku, string Producto, string Unidad, decimal Cantidad, decimal Costo);

public sealed record ReporteMermasDto(DateOnly Desde, DateOnly Hasta, IReadOnlyList<MermaMotivoDto> PorMotivo,
    IReadOnlyList<MermaProductoDto> PorProducto, MermaPeriodoDto Total);

public sealed record VentasProductoDto(string Sku, string Producto, string Unidad, decimal Cantidad, decimal Importe);

public sealed record VentasDiaDto(DateOnly Fecha, int Ventas, decimal Total);

public sealed record ReporteVentasDto(DateOnly Desde, DateOnly Hasta, decimal Total, IReadOnlyList<VentasGrupoDto> PorRuta,
    IReadOnlyList<VentasGrupoDto> PorCanal, IReadOnlyList<VentasProductoDto> PorProducto, IReadOnlyList<VentasDiaDto> PorDia);

public sealed record VisitaClienteDto(string Ruta, Guid ClienteId, string Cliente, int OrdenVisita, bool Visitado, decimal Vendido);

public sealed record ContinuidadRutaDto(string Ruta, int Programados, int Visitados, decimal Porcentaje);

public sealed record ReporteContinuidadDto(DateOnly Fecha, IReadOnlyList<ContinuidadRutaDto> Rutas, IReadOnlyList<VisitaClienteDto> Clientes);

internal sealed class ConsultasTablero(IUnitOfWork unitOfWork)
{
    static ConsultasTablero() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    private IDbConnection Conexion => unitOfWork.Conexion;

    private static DateTime D(DateOnly f) => f.ToDateTime(TimeOnly.MinValue);

    private sealed class FilaProduccion
    {
        public string Sku { get; init; } = "";
        public string Producto { get; init; } = "";
        public string Unidad { get; init; } = "";
        public decimal Planeado { get; init; }
        public decimal Producido { get; init; }
        public long Ordenes { get; init; }
        public long Terminadas { get; init; }
    }

    private sealed class FilaGrupo
    {
        public string Grupo { get; init; } = "";
        public long Ventas { get; init; }
        public decimal Total { get; init; }
    }

    private sealed class FilaMerma
    {
        public decimal Merma { get; init; }
        public decimal Producido { get; init; }
    }

    public async Task<TableroDto> TableroAsync(DateOnly fecha, CancellationToken ct)
    {
        var inicioMes = new DateOnly(fecha.Year, fecha.Month, 1);
        var p = new { f = D(fecha), mes = D(inicioMes) };

        var produccion = await Conexion.QueryAsync<FilaProduccion>(new CommandDefinition("""
            SELECT pr.sku, pr.nombre AS producto, u.clave AS unidad, SUM(o.cantidad_planeada) AS planeado,
                   SUM(COALESCE(o.cantidad_producida, 0)) AS producido, COUNT(*) AS ordenes,
                   COUNT(*) FILTER (WHERE o.estado = 'Terminada') AS terminadas
            FROM produccion.ordenes_produccion o
            JOIN catalogos.productos pr ON pr.id = o.producto_id
            JOIN catalogos.unidades_medida u ON u.id = pr.unidad_base_id
            WHERE o.fecha_operativa = @f::date AND o.estado <> 'Cancelada'
            GROUP BY pr.sku, pr.nombre, u.clave ORDER BY pr.sku
            """, p, cancellationToken: ct));

        var porRuta = await Grupos("""
            SELECT COALESCE(r.codigo, 'Mostrador') AS grupo, COUNT(*) AS ventas, SUM(v.total) AS total
            FROM ventas.ventas v LEFT JOIN logistica.rutas r ON r.id = v.ruta_id
            WHERE v.fecha_operativa = @f::date AND v.estado = 'Registrada' GROUP BY 1 ORDER BY 1
            """, p, ct);
        var porCanal = await Grupos("""
            SELECT ca.nombre AS grupo, COUNT(*) AS ventas, SUM(v.total) AS total
            FROM ventas.ventas v JOIN ventas.clientes c ON c.id = v.cliente_id JOIN ventas.canales ca ON ca.id = c.canal_id
            WHERE v.fecha_operativa = @f::date AND v.estado = 'Registrada' GROUP BY 1 ORDER BY 1
            """, p, ct);

        var bajoMinimo = await Conexion.QueryAsync<BajoMinimoDto>(new CommandDefinition("""
            SELECT pr.id AS productoid, pr.sku, pr.nombre AS producto, u.clave AS unidad, pr.stock_minimo AS minimo,
                   COALESCE(SUM(e.cantidad), 0) AS existencia
            FROM catalogos.productos pr
            JOIN catalogos.unidades_medida u ON u.id = pr.unidad_base_id
            LEFT JOIN inventario.existencias e ON e.producto_id = pr.id
                 AND e.almacen_id IN (SELECT id FROM inventario.almacenes WHERE tipo <> 'Ruta')
            WHERE pr.activo AND pr.stock_minimo IS NOT NULL
            GROUP BY pr.id, pr.sku, pr.nombre, u.clave, pr.stock_minimo
            HAVING COALESCE(SUM(e.cantidad), 0) < pr.stock_minimo
            ORDER BY pr.sku
            """, cancellationToken: ct));

        var incidencias = await Conexion.QueryAsync<(string Tipo, long Total)>(new CommandDefinition(
            "SELECT tipo, COUNT(*) FROM ventas.incidencias WHERE estado = 'Abierta' GROUP BY tipo ORDER BY 2 DESC",
            cancellationToken: ct));

        var liquidaciones = await Conexion.QueryAsync<(Guid Id, string Folio, string Ruta, DateTime Fecha)>(new CommandDefinition("""
            SELECT l.id, l.folio, r.codigo, l.fecha_operativa FROM logistica.liquidaciones l
            JOIN logistica.rutas r ON r.id = l.ruta_id WHERE l.estado = 'Abierta' ORDER BY l.fecha_operativa, r.codigo
            """, cancellationToken: ct));

        var ajustes = await Conexion.QueryAsync<(Guid DocumentoId, string Folio, DateTime FechaHora, decimal CostoTotal, string? Notas)>(
            new CommandDefinition("""
                SELECT documento_id, MIN(documento_folio), MIN(fecha_hora), SUM(costo_total), MIN(notas)
                FROM inventario.movimientos WHERE documento_tipo = 'Ajuste' AND fecha_operativa BETWEEN @mes::date AND @f::date
                GROUP BY documento_id ORDER BY MIN(fecha_hora) DESC LIMIT 10
                """, p, cancellationToken: ct));

        return new TableroDto(
            fecha,
            produccion.Select(x => new ProduccionDiaDto(x.Sku, x.Producto, x.Unidad, x.Planeado, x.Producido, (int)x.Ordenes, (int)x.Terminadas)).ToList(),
            await MermaAsync(fecha, fecha, ct),
            await MermaAsync(inicioMes, fecha, ct),
            porRuta,
            porCanal,
            porRuta.Sum(x => x.Total),
            bajoMinimo.ToList(),
            incidencias.Select(x => new ConteoTipoDto(x.Tipo, (int)x.Total)).ToList(),
            liquidaciones.Select(x => new LiquidacionPendienteDto(x.Id, x.Folio, x.Ruta, DateOnly.FromDateTime(x.Fecha))).ToList(),
            ajustes.Select(x => new AjusteRecienteDto(x.DocumentoId, x.Folio, new DateTimeOffset(DateTime.SpecifyKind(x.FechaHora, DateTimeKind.Utc)),
                x.CostoTotal, x.Notas)).ToList());
    }

    /// <summary>Merma a costo: mermas (producción, almacén, ruta) entre el costo de lo producido en el periodo.</summary>
    public async Task<MermaPeriodoDto> MermaAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var fila = await Conexion.QuerySingleAsync<FilaMerma>(new CommandDefinition("""
            SELECT COALESCE(SUM(-costo_total) FILTER (WHERE tipo = 'Merma'), 0) AS merma,
                   COALESCE(SUM(costo_total) FILTER (WHERE tipo = 'EntradaProduccion'), 0) AS producido
            FROM inventario.movimientos WHERE fecha_operativa BETWEEN @d::date AND @h::date
            """, new { d = D(desde), h = D(hasta) }, cancellationToken: ct));
        return new MermaPeriodoDto(fila.Merma, fila.Producido, fila.Producido > 0 ? Math.Round(fila.Merma / fila.Producido, 4) : null);
    }

    public async Task<ReporteMermasDto> MermasAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var p = new { d = D(desde), h = D(hasta) };
        var porMotivo = await Conexion.QueryAsync<(string Motivo, string Tipo, decimal Cantidad, decimal Costo, long Movimientos)>(
            new CommandDefinition("""
                SELECT COALESCE(mm.nombre, 'Sin motivo'), m.tipo, SUM(-m.cantidad), SUM(-m.costo_total), COUNT(*)
                FROM inventario.movimientos m LEFT JOIN catalogos.motivos_merma mm ON mm.id = m.motivo_merma_id
                WHERE m.fecha_operativa BETWEEN @d::date AND @h::date
                  AND (m.tipo = 'Merma' OR (m.tipo = 'AjusteSalida' AND m.motivo_merma_id IS NOT NULL))
                GROUP BY 1, 2 ORDER BY 4 DESC
                """, p, cancellationToken: ct));
        var porProducto = await Conexion.QueryAsync<MermaProductoDto>(new CommandDefinition("""
            SELECT pr.sku, pr.nombre AS producto, u.clave AS unidad, SUM(-m.cantidad) AS cantidad, SUM(-m.costo_total) AS costo
            FROM inventario.movimientos m JOIN catalogos.productos pr ON pr.id = m.producto_id
            JOIN catalogos.unidades_medida u ON u.id = pr.unidad_base_id
            WHERE m.fecha_operativa BETWEEN @d::date AND @h::date AND m.tipo = 'Merma'
            GROUP BY pr.sku, pr.nombre, u.clave ORDER BY 5 DESC
            """, p, cancellationToken: ct));
        return new ReporteMermasDto(desde, hasta,
            porMotivo.Select(x => new MermaMotivoDto(x.Motivo, x.Tipo, x.Cantidad, x.Costo, (int)x.Movimientos)).ToList(),
            porProducto.ToList(), await MermaAsync(desde, hasta, ct));
    }

    public async Task<ReporteVentasDto> VentasAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var p = new { d = D(desde), h = D(hasta) };
        const string filtro = "v.fecha_operativa BETWEEN @d::date AND @h::date AND v.estado = 'Registrada'";
        var porRuta = await Grupos($"""
            SELECT COALESCE(r.codigo, 'Mostrador') AS grupo, COUNT(*) AS ventas, SUM(v.total) AS total
            FROM ventas.ventas v LEFT JOIN logistica.rutas r ON r.id = v.ruta_id WHERE {filtro} GROUP BY 1 ORDER BY 3 DESC
            """, p, ct);
        var porCanal = await Grupos($"""
            SELECT ca.nombre AS grupo, COUNT(*) AS ventas, SUM(v.total) AS total
            FROM ventas.ventas v JOIN ventas.clientes c ON c.id = v.cliente_id JOIN ventas.canales ca ON ca.id = c.canal_id
            WHERE {filtro} GROUP BY 1 ORDER BY 3 DESC
            """, p, ct);
        var porProducto = await Conexion.QueryAsync<VentasProductoDto>(new CommandDefinition($"""
            SELECT pr.sku, pr.nombre AS producto, u.clave AS unidad, SUM(vp.cantidad_base) AS cantidad, SUM(vp.importe) AS importe
            FROM ventas.ventas v JOIN ventas.ventas_partidas vp ON vp.venta_id = v.id
            JOIN catalogos.productos pr ON pr.id = vp.producto_id JOIN catalogos.unidades_medida u ON u.id = pr.unidad_base_id
            WHERE {filtro} GROUP BY pr.sku, pr.nombre, u.clave ORDER BY 5 DESC
            """, p, cancellationToken: ct));
        var porDia = await Conexion.QueryAsync<(DateTime Fecha, long Ventas, decimal Total)>(new CommandDefinition($"""
            SELECT v.fecha_operativa, COUNT(*), SUM(v.total) FROM ventas.ventas v WHERE {filtro} GROUP BY 1 ORDER BY 1
            """, p, cancellationToken: ct));
        return new ReporteVentasDto(desde, hasta, porRuta.Sum(x => x.Total), porRuta, porCanal, porProducto.ToList(),
            porDia.Select(x => new VentasDiaDto(DateOnly.FromDateTime(x.Fecha), (int)x.Ventas, x.Total)).ToList());
    }

    /// <summary>Continuidad de visitas: clientes con visita programada ese día (dias_visita) contra los que tuvieron venta.</summary>
    public async Task<ReporteContinuidadDto> ContinuidadAsync(DateOnly fecha, Guid? rutaId, CancellationToken ct)
    {
        var dia = (short)(fecha.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)fecha.DayOfWeek);
        var filas = await Conexion.QueryAsync<(string Ruta, Guid ClienteId, string Cliente, short Orden, decimal Vendido, long Ventas)>(
            new CommandDefinition("""
                SELECT r.codigo, c.id, c.nombre, rc.orden_visita,
                       COALESCE(SUM(v.total), 0), COUNT(v.id)
                FROM logistica.rutas_clientes rc
                JOIN logistica.rutas r ON r.id = rc.ruta_id
                JOIN ventas.clientes c ON c.id = rc.cliente_id
                LEFT JOIN ventas.ventas v ON v.cliente_id = c.id AND v.ruta_id = r.id
                     AND v.fecha_operativa = @f::date AND v.estado = 'Registrada'
                WHERE rc.activo AND r.activo AND NOT c.es_generico AND @dia = ANY(rc.dias_visita)
                  AND (@ruta::uuid IS NULL OR r.id = @ruta::uuid)
                GROUP BY r.codigo, c.id, c.nombre, rc.orden_visita
                ORDER BY r.codigo, rc.orden_visita
                """, new { f = D(fecha), dia, ruta = rutaId }, cancellationToken: ct));
        var clientes = filas.Select(x => new VisitaClienteDto(x.Ruta, x.ClienteId, x.Cliente, x.Orden, x.Ventas > 0, x.Vendido)).ToList();
        var rutas = clientes.GroupBy(c => c.Ruta).Select(g =>
        {
            var visitados = g.Count(c => c.Visitado);
            return new ContinuidadRutaDto(g.Key, g.Count(), visitados, g.Any() ? Math.Round((decimal)visitados / g.Count(), 4) : 0);
        }).ToList();
        return new ReporteContinuidadDto(fecha, rutas, clientes);
    }

    private async Task<IReadOnlyList<VentasGrupoDto>> Grupos(string sql, object p, CancellationToken ct) =>
        (await Conexion.QueryAsync<FilaGrupo>(new CommandDefinition(sql, p, cancellationToken: ct)))
        .Select(x => new VentasGrupoDto(x.Grupo, (int)x.Ventas, x.Total)).ToList();
}

public static class TableroEndpoints
{
    private static ProblemHttpResult? SinPermiso(IUsuarioActual usuario, params string[] permisos) =>
        permisos.Any(usuario.TienePermiso)
            ? null
            : Error.Prohibido("GEN-403", "No tienes permiso para consultar este reporte.").AProblema();

    public static IEndpointRouteBuilder MapTableroEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/tablero", (DateOnly? fecha, ConsultasTablero consultas, IFechaOperativa hoy, CancellationToken ct) =>
                consultas.TableroAsync(fecha ?? hoy.Hoy(), ct))
            .RequireAuthorization(Permisos.Tablero.Ver).WithTags("Tablero").WithName("obtenerTablero");

        var reportes = api.MapGroup("/reportes").WithTags("Reportes").RequireAuthorization();
        reportes.MapGet("/mermas", async Task<Results<Ok<ReporteMermasDto>, ProblemHttpResult>> (DateOnly? desde, DateOnly? hasta,
                ConsultasTablero consultas, IFechaOperativa hoy, IUsuarioActual usuario, CancellationToken ct) =>
            SinPermiso(usuario, Permisos.Produccion.Ver, Permisos.Inventario.Ver, Permisos.Tablero.Ver) is { } prohibido
                ? prohibido
                : TypedResults.Ok(await consultas.MermasAsync(desde ?? hoy.Hoy().AddDays(-30), hasta ?? hoy.Hoy(), ct)))
            .WithName("reporteMermas");
        reportes.MapGet("/ventas", async Task<Results<Ok<ReporteVentasDto>, ProblemHttpResult>> (DateOnly? desde, DateOnly? hasta,
                ConsultasTablero consultas, IFechaOperativa hoy, IUsuarioActual usuario, CancellationToken ct) =>
            SinPermiso(usuario, Permisos.Ventas.Ver, Permisos.Tablero.Ver) is { } prohibido
                ? prohibido
                : TypedResults.Ok(await consultas.VentasAsync(desde ?? hoy.Hoy().AddDays(-30), hasta ?? hoy.Hoy(), ct)))
            .WithName("reporteVentas");
        reportes.MapGet("/continuidad", async Task<Results<Ok<ReporteContinuidadDto>, ProblemHttpResult>> (DateOnly? fecha, Guid? rutaId,
                ConsultasTablero consultas, IFechaOperativa hoy, IUsuarioActual usuario, CancellationToken ct) =>
            SinPermiso(usuario, Permisos.Ventas.Ver, Permisos.Logistica.Ver) is { } prohibido
                ? prohibido
                : TypedResults.Ok(await consultas.ContinuidadAsync(fecha ?? hoy.Hoy(), rutaId, ct)))
            .WithName("reporteContinuidad");
        return api;
    }

    public static WebApplication ProgramarTrabajosRecurrentes(this WebApplication app)
    {
        // Outbox de facturación: timbrado con reintentos (04-modulos/facturacion.md).
        RecurringJob.AddOrUpdate<ProcesadorOutboxFacturacion>(
            "facturacion-outbox", p => p.ProcesarPendientesAsync(CancellationToken.None), Cron.Minutely());
        return app;
    }
}
