using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Produccion.Dominio;
using TG.Produccion.Infraestructura;
using TG.Produccion.Publico;

namespace TG.Produccion.Aplicacion;

public sealed record VariacionComponenteDto(Guid ProductoId, string Sku, string Producto, string Unidad,
    decimal TeoricoPlaneado, decimal TeoricoAjustado, decimal Real, decimal Diferencia, decimal? VariacionPct, bool FueraDeUmbral);

public sealed record VariacionOrdenDto(Guid OrdenId, string Folio, DateOnly FechaOperativa, Turno Turno, Guid ProductoId,
    string Sku, string Producto, decimal CantidadPlaneada, decimal CantidadProducida, decimal MermaTotal,
    decimal? CostoUnitario, bool FueraDeUmbral, IReadOnlyList<VariacionComponenteDto> Componentes);

public sealed record ReporteVariacionDto(DateOnly Desde, DateOnly Hasta, decimal Umbral, IReadOnlyList<VariacionOrdenDto> Ordenes);

/// <summary>
/// RN-PRO-08: variación teórico vs real por orden terminada y componente. El teórico se ajusta a la cantidad producida
/// (<c>teórico planeado × producida / planeada</c>) para comparar contra lo que realmente se hizo.
/// </summary>
public sealed class ReportesProduccionHandler(ProduccionDbContext db, ICatalogosService catalogos) : IConsultaProduccion
{
    public async Task<ReporteVariacionDto> VariacionAsync(DateOnly desde, DateOnly hasta, Guid? productoId, CancellationToken ct)
    {
        var umbral = await catalogos.ObtenerParametroAsync(ClavesParametro.ProduccionUmbralVariacion, 0.05m, ct);
        var consulta = db.Ordenes.AsNoTracking().Include(o => o.Consumos).Include(o => o.Mermas)
            .Where(o => o.Estado == EstadoOrden.Terminada && o.FechaOperativa >= desde && o.FechaOperativa <= hasta);
        if (productoId is not null)
        {
            consulta = consulta.Where(o => o.ProductoId == productoId);
        }

        var ordenes = await consulta.OrderBy(o => o.FechaOperativa).ThenBy(o => o.Folio).ToListAsync(ct);
        var productos = await catalogos.ObtenerProductosAsync(
            ordenes.Select(o => o.ProductoId).Concat(ordenes.SelectMany(o => o.Consumos).Select(c => c.ProductoId)), ct);

        var resultado = ordenes.Select(o =>
        {
            var producida = o.CantidadProducida ?? 0;
            var componentes = o.Consumos.Select(c =>
            {
                var ajustado = ReglasProduccion.ConsumoReal(null, c.CantidadTeoricaBase, o.CantidadPlaneada, producida);
                var real = c.CantidadRealBase ?? ajustado;
                var variacion = ReglasProduccion.Variacion(ajustado, real);
                var p = productos.GetValueOrDefault(c.ProductoId);
                return new VariacionComponenteDto(c.ProductoId, p?.Sku ?? "?", p?.Nombre ?? "?", p?.UnidadBaseClave ?? string.Empty,
                    c.CantidadTeoricaBase, ajustado, real, real - ajustado, variacion,
                    ReglasProduccion.FueraDeUmbral(variacion, umbral));
            }).ToList();
            var producto = productos.GetValueOrDefault(o.ProductoId);
            return new VariacionOrdenDto(o.Id, o.Folio, o.FechaOperativa, o.Turno, o.ProductoId, producto?.Sku ?? "?",
                producto?.Nombre ?? "?", o.CantidadPlaneada, producida, o.Mermas.Sum(m => m.CantidadBase), o.CostoUnitario,
                componentes.Any(c => c.FueraDeUmbral), componentes);
        }).ToList();

        return new ReporteVariacionDto(desde, hasta, umbral, resultado);
    }

    public async Task<ResumenProduccion> ResumenAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var ordenes = await db.Ordenes.AsNoTracking().Include(o => o.Mermas)
            .Where(o => o.FechaOperativa >= desde && o.FechaOperativa <= hasta && o.Estado != EstadoOrden.Cancelada)
            .ToListAsync(ct);
        var porProducto = ordenes.GroupBy(o => o.ProductoId)
            .Select(g => new ResumenProductoProduccion(
                g.Key,
                g.Sum(o => o.CantidadPlaneada),
                g.Sum(o => o.CantidadProducida ?? 0),
                g.SelectMany(o => o.Mermas).Where(m => m.ProductoId == g.Key).Sum(m => m.CantidadBase)))
            .ToList();
        var terminadas = ordenes.Where(o => o.Estado == EstadoOrden.Terminada).ToList();
        var producido = terminadas.Sum(o => o.CantidadProducida ?? 0);
        var merma = terminadas.SelectMany(o => o.Mermas).Sum(m => m.CantidadBase);
        return new ResumenProduccion(desde, hasta, ordenes.Count, terminadas.Count, producido, merma,
            producido + merma == 0 ? 0 : Math.Round(merma / (producido + merma), 6), porProducto);
    }
}
