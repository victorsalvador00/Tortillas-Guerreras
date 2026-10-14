using Microsoft.EntityFrameworkCore;
using TG.Seguridad.Dominio;
using TG.Seguridad.Infraestructura;
using TG.Seguridad.Publico;

namespace TG.Seguridad.Aplicacion;

internal sealed class SeguridadService(SeguridadDbContext db, TimeProvider reloj) : ISeguridadService
{
    public async Task<DispositivoInfo?> ObtenerDispositivoAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Dispositivos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return d is null ? null : AInfo(d);
    }

    public async Task<IReadOnlyList<DispositivoInfo>> DispositivosDeUsuarioAsync(Guid usuarioId, CancellationToken ct) =>
        (await db.Dispositivos.AsNoTracking().Where(d => d.UsuarioId == usuarioId).ToListAsync(ct)).Select(AInfo).ToList();

    public async Task RegistrarContactoAsync(Guid dispositivoId, string? versionApp, int? ultimoConsecutivo,
        int? pendientes, CancellationToken ct)
    {
        var d = await db.Dispositivos.FirstOrDefaultAsync(x => x.Id == dispositivoId, ct);
        if (d is null)
        {
            return;
        }

        d.UltimoSyncEn = reloj.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(versionApp))
        {
            d.VersionApp = versionApp;
        }

        if (ultimoConsecutivo is { } consecutivo && consecutivo > d.UltimoConsecutivo)
        {
            d.UltimoConsecutivo = consecutivo;
        }

        if (pendientes is { } p)
        {
            d.OperacionesPendientes = p;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NombresUsuariosAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => lista.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NombreCompleto, ct);
    }

    public async Task<bool> UsuarioTieneRolAsync(Guid usuarioId, string rol, CancellationToken ct)
    {
        var normalizado = rol.ToUpperInvariant();
        return await db.UserRoles.Where(ur => ur.UserId == usuarioId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.NormalizedName)
            .AnyAsync(n => n == normalizado, ct);
    }

    public async Task<Guid?> UsuarioPorNombreAsync(string usuario, CancellationToken ct)
    {
        var normalizado = usuario.Trim().ToUpperInvariant();
        return await db.Users.AsNoTracking().Where(u => u.NormalizedUserName == normalizado).Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static DispositivoInfo AInfo(Dispositivo d) =>
        new(d.Id, d.Codigo, d.Nombre, d.UsuarioId, d.Activo, d.UltimoConsecutivo, d.OperacionesPendientes, d.UltimoSyncEn);
}
