using Microsoft.EntityFrameworkCore;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.Seguridad.Publico;

namespace TG.Logistica.Aplicacion;

public sealed class ConsultaEnviosHandler(LogisticaDbContext db, ISeguridadService seguridad) : IConsultaEnvios
{
    public async Task<IReadOnlyList<EnvioTrazaInfo>> EnviosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var filas = await (
            from e in db.Envios.AsNoTracking()
            where ids.Contains(e.Id)
            join r in db.Rutas on e.RutaId equals r.Id
            join v in db.Vehiculos on e.VehiculoId equals v.Id into vs
            from v in vs.DefaultIfEmpty()
            select new { e, r.Codigo, r.Nombre, Vehiculo = v != null ? v.Placas + " · " + v.Descripcion : null }).ToListAsync(ct);
        var choferes = await seguridad.NombresUsuariosAsync(filas.Where(f => f.e.ChoferId != null).Select(f => f.e.ChoferId!.Value), ct);
        return filas.Select(f => new EnvioTrazaInfo(f.e.Id, f.e.Folio, f.e.FechaOperativa, f.e.Tipo.ToString(), f.e.Estado.ToString(), f.Codigo,
            f.Nombre, f.e.ChoferId is { } c ? choferes.GetValueOrDefault(c) : null, f.Vehiculo, f.e.DespachadoEn)).ToList();
    }

    public async Task<IReadOnlyList<RetornoTrazaInfo>> RetornosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await (
            from x in db.Retornos.AsNoTracking()
            where ids.Contains(x.Id)
            join r in db.Rutas on x.RutaId equals r.Id
            select new RetornoTrazaInfo(x.Id, x.Folio, x.FechaOperativa, x.Tipo.ToString(), r.Codigo, x.ClienteId, x.VentaId)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LiquidacionTrazaInfo>> LiquidacionesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var filas = await (
            from l in db.Liquidaciones.AsNoTracking()
            where ids.Contains(l.Id)
            join r in db.Rutas on l.RutaId equals r.Id
            select new { l, r.Codigo }).ToListAsync(ct);
        var choferes = await seguridad.NombresUsuariosAsync(filas.Where(f => f.l.ChoferId != null).Select(f => f.l.ChoferId!.Value), ct);
        return filas.Select(f => new LiquidacionTrazaInfo(f.l.Id, f.l.Folio, f.l.FechaOperativa, f.l.Estado.ToString(), f.Codigo,
            f.l.ChoferId is { } c ? choferes.GetValueOrDefault(c) : null)).ToList();
    }
}
