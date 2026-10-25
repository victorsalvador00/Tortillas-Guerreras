using Microsoft.EntityFrameworkCore;
using TG.Ventas.Infraestructura;
using TG.Ventas.Publico;

namespace TG.Ventas.Aplicacion;

public sealed class ConsultaVentasHandler(VentasDbContext db) : IConsultaVentas
{
    public async Task<IReadOnlyList<VentaTrazaInfo>> VentasAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await (
            from v in db.Ventas.AsNoTracking()
            where ids.Contains(v.Id)
            join c in db.Clientes on v.ClienteId equals c.Id
            select new VentaTrazaInfo(v.Id, v.Folio, v.FechaOperativa, v.Estado.ToString(), c.Id, c.Codigo, c.Nombre, v.RutaId)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ClienteTrazaInfo>> ClientesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Clientes.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new ClienteTrazaInfo(c.Id, c.Codigo, c.Nombre)).ToListAsync(ct);
    }
}
