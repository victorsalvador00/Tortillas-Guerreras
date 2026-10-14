using Microsoft.EntityFrameworkCore;
using TG.Logistica.Dominio;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.Seguridad.Publico;
using TG.SharedKernel.Resultados;

namespace TG.Logistica.Aplicacion;

internal sealed class LogisticaService(LogisticaDbContext db, ServicioRetornos retornos) : ILogisticaService
{
    public async Task<RutaInfo?> RutaDeVendedorAsync(Guid usuarioId, CancellationToken ct) =>
        AInfo(await db.Rutas.AsNoTracking().Where(r => r.VendedorId == usuarioId && r.Activo).OrderBy(r => r.Codigo).FirstOrDefaultAsync(ct));

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
        db.Liquidaciones.AnyAsync(l => l.RutaId == rutaId && l.FechaOperativa == fecha && l.Estado == EstadoLiquidacion.Cerrada, ct);

    public Task<Result<RetornoRegistrado>> RegistrarRetornoAsync(RegistrarRetornoComando comando, CancellationToken ct) =>
        retornos.RegistrarAsync(comando, ct);

    public async Task<ResumenLogistica> ResumenTableroAsync(DateOnly fecha, CancellationToken ct)
    {
        var abiertasHoy = await db.Liquidaciones.CountAsync(l => l.Estado == EstadoLiquidacion.Abierta && l.FechaOperativa == fecha, ct);
        var atrasadas = await db.Liquidaciones.CountAsync(l => l.Estado == EstadoLiquidacion.Abierta && l.FechaOperativa < fecha, ct);
        var preparados = await db.Envios.CountAsync(e => e.Estado == EstadoEnvio.Preparado && e.FechaOperativa == fecha, ct);
        var enRuta = await db.Envios.CountAsync(e => e.Estado == EstadoEnvio.EnRuta, ct);
        var rutas = await db.Rutas.CountAsync(r => r.Activo, ct);
        return new ResumenLogistica(fecha, abiertasHoy, atrasadas, preparados, enRuta, rutas);
    }

    private static RutaInfo? AInfo(Ruta? r) =>
        r is null ? null : new RutaInfo(r.Id, r.Codigo, r.Nombre, r.AlmacenId, r.VendedorId, r.VehiculoId, r.Activo, r.VersionSync);
}

/// <summary>
/// RN-LOG-09 por defecto: suma de operaciones pendientes que reportaron en su último contacto los dispositivos del
/// vendedor de la ruta. El módulo de Sincronización puede registrar otra implementación.
/// </summary>
internal sealed class EstadoSincronizacionRutaPredeterminado(LogisticaDbContext db, ISeguridadService seguridad) : IEstadoSincronizacionRuta
{
    public async Task<int> OperacionesPendientesAsync(Guid rutaId, CancellationToken ct)
    {
        var vendedor = await db.Rutas.AsNoTracking().Where(r => r.Id == rutaId).Select(r => r.VendedorId).FirstOrDefaultAsync(ct);
        if (vendedor is null)
        {
            return 0;
        }

        var dispositivos = await seguridad.DispositivosDeUsuarioAsync(vendedor.Value, ct);
        return dispositivos.Where(d => d.Activo).Sum(d => d.OperacionesPendientes);
    }
}
