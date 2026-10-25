using Microsoft.EntityFrameworkCore;
using TG.Compras.Dominio;
using TG.Compras.Infraestructura;
using TG.Compras.Publico;

namespace TG.Compras.Aplicacion;

public sealed class ConsultaComprasHandler(ComprasDbContext db) : IConsultaCompras
{
    public async Task<IReadOnlyList<RecepcionTrazaInfo>> RecepcionesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await (
            from r in db.Recepciones.AsNoTracking()
            where ids.Contains(r.Id)
            join p in db.Proveedores on r.ProveedorId equals p.Id
            join o in db.OrdenesCompra on r.OrdenCompraId equals o.Id into oc
            from o in oc.DefaultIfEmpty()
            select new RecepcionTrazaInfo(r.Id, r.Folio, r.FechaOperativa, r.Estado.ToString(), p.Id, p.Codigo, p.RazonSocial, p.Rfc,
                r.OrdenCompraId, o != null ? o.Folio : null, o != null ? o.Fecha : null, r.FacturaProveedor)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> RecepcionesDeOrdenAsync(Guid ordenCompraId, CancellationToken ct) =>
        await db.Recepciones.Where(r => r.OrdenCompraId == ordenCompraId && r.Estado == EstadoRecepcion.Registrada)
            .Select(r => r.Id).ToListAsync(ct);

    public async Task<OrdenCompraTrazaInfo?> ObtenerOrdenAsync(Guid id, CancellationToken ct) =>
        await Ordenes(db.OrdenesCompra.Where(o => o.Id == id)).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<OrdenCompraTrazaInfo>> BuscarOrdenesAsync(string texto, int limite, CancellationToken ct)
    {
        var patron = $"%{texto.Trim()}%";
        var ordenes = db.OrdenesCompra.Where(o => EF.Functions.ILike(o.Folio, patron)).OrderByDescending(o => o.Folio).Take(limite);
        return await Ordenes(ordenes).ToListAsync(ct);
    }

    private IQueryable<OrdenCompraTrazaInfo> Ordenes(IQueryable<OrdenCompra> ordenes) =>
        from o in ordenes.AsNoTracking()
        join p in db.Proveedores on o.ProveedorId equals p.Id
        select new OrdenCompraTrazaInfo(o.Id, o.Folio, o.Fecha, o.Estado.ToString(), p.RazonSocial);
}
