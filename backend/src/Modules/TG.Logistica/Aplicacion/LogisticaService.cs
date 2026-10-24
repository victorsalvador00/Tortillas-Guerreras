using Microsoft.EntityFrameworkCore;
using TG.Logistica.Dominio;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.Seguridad.Publico;
using TG.SharedKernel.Resultados;

namespace TG.Logistica.Aplicacion;

internal sealed class LogisticaService(LogisticaDbContext db, ServicioRetornos retornos) : ILogisticaService
{
    public async Task<IReadOnlyList<CargadoInfo>> CargadoPorFechaAsync(DateOnly fecha, CancellationToken ct) =>
        await db.Envios.AsNoTracking()
            .Where(e => e.FechaOperativa == fecha && (e.Estado == EstadoEnvio.EnRuta || e.Estado == EstadoEnvio.Liquidado))
            .SelectMany(e => e.Partidas.Select(p => new { e.RutaId, p.ProductoId, p.CantidadBase }))
            .GroupBy(x => new { x.RutaId, x.ProductoId })
            .Select(g => new CargadoInfo(g.Key.RutaId, g.Key.ProductoId, g.Sum(x => x.CantidadBase)))
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, Guid>> RutasDeClientesAsync(IReadOnlyCollection<Guid> clienteIds, CancellationToken ct) =>
        clienteIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : (await db.RutasClientes.AsNoTracking().Where(rc => rc.Activo && clienteIds.Contains(rc.ClienteId))
                    .Select(rc => new { rc.ClienteId, rc.RutaId }).ToListAsync(ct))
                .GroupBy(x => x.ClienteId).ToDictionary(g => g.Key, g => g.Min(x => x.RutaId));

    public async Task<IReadOnlyList<CambioPendienteInfo>> CambiosPendientesAsync(IReadOnlyCollection<Guid> clienteIds, long desde,
        CancellationToken ct) =>
        clienteIds.Count == 0
            ? []
            : await db.CambiosPendientes.AsNoTracking().Where(x => clienteIds.Contains(x.ClienteId) && x.VersionSync > desde)
                .OrderBy(x => x.VersionSync)
                .Select(x => new CambioPendienteInfo(x.Id, x.ClienteId, x.RutaId, x.ProductoId, x.CantidadBase, x.CantidadSaldadaBase,
                    x.FechaOperativa, x.Estado, x.VersionSync))
                .ToListAsync(ct);

    public async Task<IReadOnlyList<PendienteDelDiaInfo>> CambiosPendientesDelDiaAsync(DateOnly fecha, CancellationToken ct)
    {
        // 1 = lunes … 7 = domingo.
        var dia = (short)(fecha.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)fecha.DayOfWeek);
        var visitas = await (from rc in db.RutasClientes.AsNoTracking()
                             join r in db.Rutas.AsNoTracking() on rc.RutaId equals r.Id
                             where rc.Activo && r.Activo && rc.DiasVisita.Contains(dia)
                             select new { rc.RutaId, rc.ClienteId }).ToListAsync(ct);
        var clientes = visitas.Select(v => v.ClienteId).Distinct().ToList();
        var pendientes = await db.CambiosPendientes.AsNoTracking()
            .Where(x => clientes.Contains(x.ClienteId) && x.Estado == EstadoCambioPendiente.Pendiente).ToListAsync(ct);
        // Un cliente en varias rutas el mismo día: lo lleva la de menor id (igual que RutasDeClientesAsync).
        var rutaDe = visitas.GroupBy(v => v.ClienteId).ToDictionary(g => g.Key, g => g.Min(v => v.RutaId));
        return pendientes.Select(x => new PendienteDelDiaInfo(rutaDe[x.ClienteId], x.ClienteId, x.ProductoId, x.CantidadBase - x.CantidadSaldadaBase))
            .Where(x => x.RestanteBase > 0).ToList();
    }

    public async Task<RutaInfo?> RutaDeChoferAsync(Guid usuarioId, DateOnly fecha, CancellationToken ct)
    {
        var rutaId = await RutaDeCargaAsync(db, usuarioId, fecha, ct);
        var ruta = rutaId is { } id
            ? await db.Rutas.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct)
            : await db.Rutas.AsNoTracking().Where(r => r.VendedorId == usuarioId && r.Activo).OrderBy(r => r.Codigo).FirstOrDefaultAsync(ct);
        return AInfo(ruta);
    }

    /// <summary>RN-SYN-09: la carga del chofer en la fecha (o la de ayer que sigue en ruta) define su ruta.</summary>
    internal static async Task<Guid?> RutaDeCargaAsync(LogisticaDbContext db, Guid choferId, DateOnly fecha, CancellationToken ct)
    {
        var ayer = fecha.AddDays(-1);
        var cargas = await db.Envios.AsNoTracking()
            .Where(e => e.ChoferId == choferId && e.Estado != EstadoEnvio.Cancelado
                && (e.FechaOperativa == fecha || (e.FechaOperativa == ayer && e.Estado == EstadoEnvio.EnRuta)))
            .Select(e => new { e.RutaId, e.FechaOperativa, e.Estado, e.CreadoEn })
            .ToListAsync(ct);
        return cargas
            .OrderBy(e => e.FechaOperativa == fecha ? 0 : 1)
            .ThenBy(e => e.Estado is EstadoEnvio.Preparado or EstadoEnvio.EnRuta ? 0 : 1)
            .ThenByDescending(e => e.CreadoEn)
            .Select(e => (Guid?)e.RutaId)
            .FirstOrDefault();
    }

    public async Task<RutaInfo?> ObtenerRutaAsync(Guid id, CancellationToken ct) =>
        AInfo(await db.Rutas.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct));

    public async Task<RutaInfo?> ObtenerRutaPorCodigoAsync(string codigo, CancellationToken ct) =>
        AInfo(await db.Rutas.AsNoTracking().FirstOrDefaultAsync(r => r.Codigo == codigo, ct));

    public async Task<IReadOnlyList<RutaInfo>> ListarRutasAsync(CancellationToken ct) =>
        (await db.Rutas.AsNoTracking().OrderBy(r => r.Codigo).ToListAsync(ct)).Select(r => AInfo(r)!).ToList();

    public async Task<IReadOnlyList<RutaClienteInfo>> ClientesDeRutaAsync(Guid rutaId, long desde, CancellationToken ct) =>
        await db.RutasClientes.AsNoTracking().Where(rc => rc.RutaId == rutaId && rc.VersionSync > desde)
            .OrderBy(rc => rc.VersionSync)
            .Select(rc => new RutaClienteInfo(rc.RutaId, rc.ClienteId, rc.OrdenVisita, rc.DiasVisita, rc.Activo, rc.VersionSync))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EnvioInfo>> EnviosDeRutaAsync(Guid rutaId, long desdeVersion, DateOnly fechaOperativa, CancellationToken ct)
    {
        var desdeFecha = fechaOperativa.AddDays(-1);
        var envios = await db.Envios.AsNoTracking().Include(e => e.Partidas)
            .Where(e => e.RutaId == rutaId && e.FechaOperativa >= desdeFecha && e.FechaOperativa <= fechaOperativa
                && (e.Estado == EstadoEnvio.EnRuta || e.Estado == EstadoEnvio.Liquidado)
                && (e.VersionSync > desdeVersion || e.Partidas.Any(p => p.VersionSync > desdeVersion)))
            .OrderBy(e => e.VersionSync)
            .ToListAsync(ct);
        return envios.Select(e => new EnvioInfo(e.Id, e.Folio, e.FechaOperativa, e.RutaId, e.Tipo, e.Estado, e.DespachadoEn, e.VersionSync,
            e.Partidas.Select(p => new EnvioPartidaInfo(p.Id, p.ProductoId, p.Lote, p.CantidadBase, p.VersionSync)).ToList())).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> FoliosEnviosAsync(IReadOnlyCollection<Guid> envioIds, CancellationToken ct) =>
        envioIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Envios.AsNoTracking().Where(e => envioIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Folio, ct);

    public Task<bool> LiquidacionCerradaAsync(Guid rutaId, DateOnly fecha, CancellationToken ct) =>
        db.Liquidaciones.AnyAsync(l => l.RutaId == rutaId && l.FechaOperativa == fecha && l.Estado != EstadoLiquidacion.Abierta, ct);

    public Task<Result<RetornoRegistrado>> RegistrarRetornoAsync(RegistrarRetornoComando comando, CancellationToken ct) =>
        retornos.RegistrarAsync(comando, ct);

    public async Task<ResumenLogistica> ResumenTableroAsync(DateOnly fecha, CancellationToken ct)
    {
        var abiertasHoy = await db.Liquidaciones.CountAsync(l => l.Estado != EstadoLiquidacion.Cerrada && l.FechaOperativa == fecha, ct);
        var atrasadas = await db.Liquidaciones.CountAsync(l => l.Estado != EstadoLiquidacion.Cerrada && l.FechaOperativa < fecha, ct);
        var preparados = await db.Envios.CountAsync(e => e.Estado == EstadoEnvio.Preparado && e.FechaOperativa == fecha, ct);
        var enRuta = await db.Envios.CountAsync(e => e.Estado == EstadoEnvio.EnRuta, ct);
        var rutas = await db.Rutas.CountAsync(r => r.Activo, ct);
        return new ResumenLogistica(fecha, abiertasHoy, atrasadas, preparados, enRuta, rutas);
    }

    private static RutaInfo? AInfo(Ruta? r) =>
        r is null ? null : new RutaInfo(r.Id, r.Codigo, r.Nombre, r.AlmacenId, r.CanalId, r.VendedorId, r.VehiculoId, r.Activo, r.VersionSync);
}

/// <summary>
/// RN-LOG-09 por defecto: suma de operaciones pendientes que reportaron en su último contacto los dispositivos del
/// chofer de la ruta en esa fecha (el de sus cargas, RN-LOG-13; si no hubo cargas, el predeterminado). El módulo de
/// Sincronización puede registrar otra implementación.
/// </summary>
internal sealed class EstadoSincronizacionRutaPredeterminado(LogisticaDbContext db, ISeguridadService seguridad) : IEstadoSincronizacionRuta
{
    public async Task<int> OperacionesPendientesAsync(Guid rutaId, DateOnly fecha, CancellationToken ct)
    {
        var choferes = await db.Envios.AsNoTracking()
            .Where(e => e.RutaId == rutaId && e.FechaOperativa == fecha && e.Estado != EstadoEnvio.Cancelado && e.ChoferId != null)
            .Select(e => e.ChoferId!.Value).Distinct().ToListAsync(ct);
        if (choferes.Count == 0)
        {
            var predeterminado = await db.Rutas.AsNoTracking().Where(r => r.Id == rutaId).Select(r => r.VendedorId).FirstOrDefaultAsync(ct);
            if (predeterminado is { } v)
            {
                choferes.Add(v);
            }
        }

        var total = 0;
        foreach (var chofer in choferes)
        {
            var dispositivos = await seguridad.DispositivosDeUsuarioAsync(chofer, ct);
            total += dispositivos.Where(d => d.Activo).Sum(d => d.OperacionesPendientes);
        }

        return total;
    }
}
