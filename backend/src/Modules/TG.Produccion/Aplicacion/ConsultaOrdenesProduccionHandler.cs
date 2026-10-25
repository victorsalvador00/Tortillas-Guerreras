using Microsoft.EntityFrameworkCore;
using TG.Produccion.Infraestructura;
using TG.Produccion.Publico;

namespace TG.Produccion.Aplicacion;

public sealed class ConsultaOrdenesProduccionHandler(ProduccionDbContext db) : IConsultaOrdenesProduccion
{
    public async Task<IReadOnlyList<OrdenTrazaInfo>> OrdenesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await (
            from o in db.Ordenes.AsNoTracking()
            where ids.Contains(o.Id)
            join r in db.Recetas on o.RecetaId equals r.Id into rs
            from r in rs.DefaultIfEmpty()
            join l in db.Lineas on o.LineaId equals l.Id into ls
            from l in ls.DefaultIfEmpty()
            select new OrdenTrazaInfo(o.Id, o.Folio, o.FechaOperativa, o.Turno.ToString(), o.Estado.ToString(), o.ProductoId, o.Lote,
                o.CantidadPlaneada, o.CantidadProducida, l != null ? l.Nombre : null, r != null ? r.Nombre : null,
                r != null ? r.NumeroVersion : null, o.TerminadaEn)).ToListAsync(ct);
    }
}
