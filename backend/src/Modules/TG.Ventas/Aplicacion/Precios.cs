using Microsoft.EntityFrameworkCore;
using TG.Ventas.Dominio;
using TG.Ventas.Infraestructura;

namespace TG.Ventas.Aplicacion;

/// <summary>Precio vigente del cliente (RN-VEN-01/12) y productos exclusivos (RN-VEN-14).</summary>
internal static class Precios
{
    /// <summary>RN-VEN-12: precio especial activo con <c>vigente_desde ≤ fecha</c>; si no hay, el de la lista del cliente.</summary>
    public static async Task<decimal?> VigenteAsync(this VentasDbContext db, Cliente cliente, Guid productoId, Guid unidadId,
        DateOnly fecha, CancellationToken ct)
    {
        var especial = await db.PreciosClientes.AsNoTracking()
            .Where(p => p.ClienteId == cliente.Id && p.ProductoId == productoId && p.UnidadId == unidadId && p.Activo
                && p.VigenteDesde <= fecha)
            .Select(p => (decimal?)p.PrecioUnitario)
            .FirstOrDefaultAsync(ct);
        return especial ?? await db.ListasPrecioPartidas.AsNoTracking()
            .Where(x => x.ListaPrecioId == cliente.ListaPrecioId && x.ProductoId == productoId && x.UnidadId == unidadId)
            .Select(x => (decimal?)x.PrecioUnitario)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// RN-VEN-14: de los productos pedidos, los que tienen exclusividad activa que no incluye al cliente ni a su grupo.
    /// </summary>
    public static async Task<IReadOnlySet<Guid>> ExclusivosAjenosAsync(this VentasDbContext db, Cliente cliente,
        IEnumerable<Guid> productoIds, CancellationToken ct)
    {
        var ids = productoIds.Distinct().ToList();
        var registros = await db.ProductosExclusivos.AsNoTracking()
            .Where(x => x.Activo && ids.Contains(x.ProductoId))
            .Select(x => new { x.ProductoId, x.ClienteId, x.GrupoClienteId })
            .ToListAsync(ct);
        return registros.GroupBy(r => r.ProductoId)
            .Where(g => !g.Any(r => r.ClienteId == cliente.Id || (cliente.GrupoClienteId is { } grupo && r.GrupoClienteId == grupo)))
            .Select(g => g.Key)
            .ToHashSet();
    }
}
