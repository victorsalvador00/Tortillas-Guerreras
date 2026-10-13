using System.Globalization;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TG.Seguridad.Dominio;
using TG.Seguridad.Infraestructura;
using TG.SharedKernel.Api;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;

namespace TG.Seguridad.Aplicacion;

public sealed record UsuarioDto(
    Guid Id,
    string Usuario,
    string NombreCompleto,
    string? Email,
    bool Activo,
    IReadOnlyList<string> Roles,
    DateTimeOffset? BloqueadoHasta);

public sealed record GuardarUsuarioRequest(
    string Usuario,
    string NombreCompleto,
    string? Email,
    bool Activo,
    IReadOnlyList<string> Roles,
    string? Contrasena);

public sealed class GuardarUsuarioValidator : AbstractValidator<GuardarUsuarioRequest>
{
    public GuardarUsuarioValidator()
    {
        RuleFor(x => x.Usuario).NotEmpty().MaximumLength(64).Matches("^[a-zA-Z0-9._-]+$")
            .WithMessage("Usa solo letras, números, punto, guion o guion bajo.");
        RuleFor(x => x.NombreCompleto).NotEmpty().WithMessage("Escribe el nombre completo.");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("El correo no es válido.");
        RuleFor(x => x.Contrasena).MinimumLength(8).When(x => !string.IsNullOrEmpty(x.Contrasena))
            .WithMessage("La contraseña debe tener al menos 8 caracteres.");
    }
}

public sealed class ConsultasUsuariosHandler(SeguridadDbContext db, TimeProvider reloj)
{
    public async Task<Paginado<UsuarioDto>> ListarAsync(string? buscar, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var patron = $"%{buscar.Trim()}%";
            consulta = consulta.Where(u => EF.Functions.ILike(u.UserName!, patron) || EF.Functions.ILike(u.NombreCompleto, patron));
        }

        var paginado = await consulta.OrderBy(u => u.NombreCompleto).PaginarAsync(pagina, tamano, ct);
        var roles = await RolesDeAsync(paginado.Items.Select(u => u.Id).ToList(), ct);
        var ahora = reloj.GetUtcNow();
        return paginado.Map(u => ADto(u, roles.GetValueOrDefault(u.Id) ?? [], ahora));
    }

    public async Task<Result<UsuarioDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var usuario = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null)
        {
            return ErroresSeguridad.UsuarioNoEncontrado;
        }

        var roles = await RolesDeAsync([id], ct);
        return ADto(usuario, roles.GetValueOrDefault(id) ?? [], reloj.GetUtcNow());
    }

    private async Task<Dictionary<Guid, List<string>>> RolesDeAsync(List<Guid> ids, CancellationToken ct) =>
        (await db.UserRoles.Where(ur => ids.Contains(ur.UserId))
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(ct))
        .GroupBy(x => x.UserId)
        .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).Order().ToList());

    internal static UsuarioDto ADto(Usuario u, IReadOnlyList<string> roles, DateTimeOffset ahora) =>
        new(u.Id, u.UserName!, u.NombreCompleto, u.Email, u.Activo, roles,
            u.LockoutEnd is { } fin && fin > ahora ? fin : null);
}

public sealed class CrearUsuarioHandler(UserManager<Usuario> usuarios, ConsultasUsuariosHandler consultas)
{
    public async Task<Result<UsuarioDto>> EjecutarAsync(GuardarUsuarioRequest request, CancellationToken ct)
    {
        if (await usuarios.FindByNameAsync(request.Usuario) is not null)
        {
            return ErroresSeguridad.UsuarioDuplicado;
        }

        if (string.IsNullOrEmpty(request.Contrasena))
        {
            return Error.Campo("SEG-012", "contrasena", "Escribe una contraseña inicial.");
        }

        var usuario = new Usuario
        {
            UserName = request.Usuario.Trim(),
            NombreCompleto = request.NombreCompleto.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Activo = request.Activo,
            LockoutEnabled = true,
        };
        var creado = await usuarios.CreateAsync(usuario, request.Contrasena);
        if (!creado.Succeeded)
        {
            return ErroresSeguridad.Identity(creado.Errors.Select(e => e.Description));
        }

        var rolesResultado = await AsignarRolesAsync(usuarios, usuario, request.Roles);
        if (!rolesResultado.EsExito)
        {
            return rolesResultado.Error!;
        }

        return await consultas.ObtenerAsync(usuario.Id, ct);
    }

    internal static async Task<Result> AsignarRolesAsync(UserManager<Usuario> usuarios, Usuario usuario,
        IReadOnlyList<string> roles)
    {
        var actuales = await usuarios.GetRolesAsync(usuario);
        var quitar = actuales.Except(roles).ToList();
        var agregar = roles.Except(actuales).Distinct().ToList();
        if (quitar.Count > 0)
        {
            await usuarios.RemoveFromRolesAsync(usuario, quitar);
        }

        if (agregar.Count > 0)
        {
            try
            {
                var r = await usuarios.AddToRolesAsync(usuario, agregar);
                if (!r.Succeeded)
                {
                    return Error.Campo("SEG-009", "roles", string.Join(" ", r.Errors.Select(e => e.Description)));
                }
            }
            catch (InvalidOperationException)
            {
                return Error.Campo("SEG-009", "roles", "Uno de los roles no existe.");
            }
        }

        return Result.Exito();
    }
}

public sealed class ActualizarUsuarioHandler(UserManager<Usuario> usuarios, SeguridadDbContext db,
    ConsultasUsuariosHandler consultas, TimeProvider reloj)
{
    public async Task<Result<UsuarioDto>> EjecutarAsync(Guid id, GuardarUsuarioRequest request, CancellationToken ct)
    {
        var usuario = await usuarios.FindByIdAsync(id.ToString());
        if (usuario is null)
        {
            return ErroresSeguridad.UsuarioNoEncontrado;
        }

        if (!string.Equals(usuario.UserName, request.Usuario, StringComparison.OrdinalIgnoreCase) &&
            await usuarios.FindByNameAsync(request.Usuario) is not null)
        {
            return ErroresSeguridad.UsuarioDuplicado;
        }

        usuario.UserName = request.Usuario.Trim();
        usuario.NombreCompleto = request.NombreCompleto.Trim();
        usuario.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var desactivado = usuario.Activo && !request.Activo;
        usuario.Activo = request.Activo;
        var r = await usuarios.UpdateAsync(usuario);
        if (!r.Succeeded)
        {
            return ErroresSeguridad.Identity(r.Errors.Select(e => e.Description));
        }

        if (!string.IsNullOrEmpty(request.Contrasena))
        {
            await usuarios.RemovePasswordAsync(usuario);
            var c = await usuarios.AddPasswordAsync(usuario, request.Contrasena);
            if (!c.Succeeded)
            {
                return ErroresSeguridad.Identity(c.Errors.Select(e => e.Description));
            }
        }

        var roles = await CrearUsuarioHandler.AsignarRolesAsync(usuarios, usuario, request.Roles);
        if (!roles.EsExito)
        {
            return roles.Error!;
        }

        if (desactivado || !string.IsNullOrEmpty(request.Contrasena))
        {
            var ahora = reloj.GetUtcNow();
            await db.RefreshTokens.Where(t => t.UsuarioId == id && t.RevocadoEn == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevocadoEn, ahora), ct);
        }

        return await consultas.ObtenerAsync(id, ct);
    }
}

public sealed class DesbloquearUsuarioHandler(UserManager<Usuario> usuarios)
{
    public async Task<Result> EjecutarAsync(Guid id)
    {
        var usuario = await usuarios.FindByIdAsync(id.ToString());
        if (usuario is null)
        {
            return ErroresSeguridad.UsuarioNoEncontrado;
        }

        await usuarios.SetLockoutEndDateAsync(usuario, null);
        await usuarios.ResetAccessFailedCountAsync(usuario);
        return Result.Exito();
    }
}

public sealed record PermisoDto(string Clave, string Descripcion);

public sealed record RolDto(Guid Id, string Nombre, string? Descripcion, IReadOnlyList<string> Permisos, int Usuarios);

public sealed record CrearRolRequest(string Nombre, string? Descripcion, IReadOnlyList<string> Permisos);

public sealed record PermisosRolRequest(IReadOnlyList<string> Permisos);

public sealed class CrearRolValidator : AbstractValidator<CrearRolRequest>
{
    public CrearRolValidator() => RuleFor(x => x.Nombre).NotEmpty().MaximumLength(64);
}

public sealed class RolesHandler(SeguridadDbContext db)
{
    public async Task<IReadOnlyList<PermisoDto>> PermisosAsync(CancellationToken ct) =>
        await db.Permisos.AsNoTracking().OrderBy(p => p.Clave).Select(p => new PermisoDto(p.Clave, p.Descripcion)).ToListAsync(ct);

    public async Task<IReadOnlyList<RolDto>> ListarAsync(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permisos).ThenInclude(rp => rp.Permiso)
            .OrderBy(r => r.Name).ToListAsync(ct);
        var conteo = await db.UserRoles.GroupBy(ur => ur.RoleId).Select(g => new { g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        return roles.Select(r => ADto(r, conteo.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<Result<RolDto>> CrearAsync(CrearRolRequest request, CancellationToken ct)
    {
        var normalizado = request.Nombre.Trim().ToUpperInvariant();
        if (await db.Roles.AnyAsync(r => r.NormalizedName == normalizado, ct))
        {
            return ErroresSeguridad.RolDuplicado;
        }

        var rol = new Rol(request.Nombre.Trim()) { Descripcion = request.Descripcion };
        db.Roles.Add(rol);
        var asignado = await AsignarAsync(rol, request.Permisos, ct);
        if (!asignado.EsExito)
        {
            return asignado.Error!;
        }

        await db.SaveChangesAsync(ct);
        return ADto(rol, 0);
    }

    public async Task<Result<RolDto>> ActualizarPermisosAsync(Guid id, PermisosRolRequest request, CancellationToken ct)
    {
        var rol = await db.Roles.Include(r => r.Permisos).ThenInclude(rp => rp.Permiso).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rol is null)
        {
            return ErroresSeguridad.RolNoEncontrado;
        }

        var asignado = await AsignarAsync(rol, request.Permisos, ct);
        if (!asignado.EsExito)
        {
            return asignado.Error!;
        }

        await db.SaveChangesAsync(ct);
        var usuarios = await db.UserRoles.CountAsync(ur => ur.RoleId == id, ct);
        return ADto(rol, usuarios);
    }

    private async Task<Result> AsignarAsync(Rol rol, IReadOnlyList<string> claves, CancellationToken ct)
    {
        var permisos = await db.Permisos.Where(p => claves.Contains(p.Clave)).ToListAsync(ct);
        var desconocido = claves.FirstOrDefault(c => permisos.All(p => p.Clave != c));
        if (desconocido is not null)
        {
            return ErroresSeguridad.PermisoDesconocido(desconocido);
        }

        rol.Permisos.RemoveAll(rp => permisos.All(p => p.Id != rp.PermisoId));
        foreach (var permiso in permisos.Where(p => rol.Permisos.All(rp => rp.PermisoId != p.Id)))
        {
            rol.Permisos.Add(new RolPermiso { RolId = rol.Id, PermisoId = permiso.Id, Permiso = permiso });
        }

        return Result.Exito();
    }

    private static RolDto ADto(Rol r, int usuarios) =>
        new(r.Id, r.Name!, r.Descripcion, r.Permisos.Select(p => p.Permiso!.Clave).Order().ToList(), usuarios);
}

public sealed record DispositivoDto(
    Guid Id,
    string? Codigo,
    string Nombre,
    Guid? UsuarioId,
    string? UsuarioNombre,
    PlataformaDispositivo Plataforma,
    string? VersionApp,
    DateTimeOffset? UltimoSyncEn,
    int UltimoConsecutivo,
    int OperacionesPendientes,
    bool Activo,
    string Estado);

public sealed record SolicitarDispositivoRequest(string Nombre, PlataformaDispositivo Plataforma, string? VersionApp);

public sealed record AprobarDispositivoRequest(Guid? UsuarioId);

public sealed class SolicitarDispositivoValidator : AbstractValidator<SolicitarDispositivoRequest>
{
    public SolicitarDispositivoValidator() => RuleFor(x => x.Nombre).NotEmpty().MaximumLength(100);
}

/// <summary>Registro y aprobación de dispositivos de ruta (RN-SEG-02, RN-SEG-03).</summary>
public sealed class DispositivosHandler(SeguridadDbContext db, IUsuarioActual actual, TimeProvider reloj)
{
    public async Task<IReadOnlyList<DispositivoDto>> ListarAsync(CancellationToken ct)
    {
        var dispositivos = await db.Dispositivos.AsNoTracking().OrderBy(d => d.Codigo == null ? 0 : 1)
            .ThenBy(d => d.Codigo).ToListAsync(ct);
        var ids = dispositivos.Where(d => d.UsuarioId != null).Select(d => d.UsuarioId!.Value).Distinct().ToList();
        var nombres = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NombreCompleto, ct);
        return dispositivos.Select(d => ADto(d, nombres)).ToList();
    }

    public async Task<Result<DispositivoDto>> SolicitarAsync(SolicitarDispositivoRequest request, CancellationToken ct)
    {
        var dispositivo = new Dispositivo
        {
            Nombre = request.Nombre.Trim(),
            Plataforma = request.Plataforma,
            VersionApp = request.VersionApp,
            SolicitadoPor = actual.Id,
            UsuarioId = actual.Id,
            Activo = false,
        };
        db.Dispositivos.Add(dispositivo);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(dispositivo.Id, ct);
    }

    public async Task<Result<DispositivoDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var dispositivo = await db.Dispositivos.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dispositivo is null)
        {
            return ErroresSeguridad.DispositivoNoEncontrado;
        }

        var nombres = await db.Users.Where(u => u.Id == dispositivo.UsuarioId).ToDictionaryAsync(u => u.Id, u => u.NombreCompleto, ct);
        return ADto(dispositivo, nombres);
    }

    /// <summary>RN-SEG-03: al aprobar se asigna el siguiente código <c>D01</c>, <c>D02</c>… (prefijo de folios).</summary>
    public async Task<Result<DispositivoDto>> AprobarAsync(Guid id, AprobarDispositivoRequest request, CancellationToken ct)
    {
        var dispositivo = await db.Dispositivos.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dispositivo is null)
        {
            return ErroresSeguridad.DispositivoNoEncontrado;
        }

        if (request.UsuarioId is { } usuarioId)
        {
            if (!await db.Users.AnyAsync(u => u.Id == usuarioId, ct))
            {
                return ErroresSeguridad.UsuarioNoEncontrado;
            }

            dispositivo.UsuarioId = usuarioId;
        }

        if (dispositivo.Codigo is null)
        {
            var codigos = await db.Dispositivos.Where(d => d.Codigo != null).Select(d => d.Codigo!).ToListAsync(ct);
            var maximo = codigos.Select(c => int.TryParse(c.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
                .DefaultIfEmpty(0).Max();
            dispositivo.Codigo = string.Create(CultureInfo.InvariantCulture, $"D{maximo + 1:D2}");
        }

        dispositivo.Activo = true;
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    public async Task<Result<DispositivoDto>> DesactivarAsync(Guid id, CancellationToken ct)
    {
        var dispositivo = await db.Dispositivos.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dispositivo is null)
        {
            return ErroresSeguridad.DispositivoNoEncontrado;
        }

        dispositivo.Activo = false;
        var ahora = reloj.GetUtcNow();
        await db.RefreshTokens.Where(t => t.DispositivoId == id && t.RevocadoEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevocadoEn, ahora), ct);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    private static DispositivoDto ADto(Dispositivo d, Dictionary<Guid, string> nombres) =>
        new(d.Id, d.Codigo, d.Nombre, d.UsuarioId,
            d.UsuarioId is { } u ? nombres.GetValueOrDefault(u) : null,
            d.Plataforma, d.VersionApp, d.UltimoSyncEn, d.UltimoConsecutivo, d.OperacionesPendientes, d.Activo,
            d.Codigo is null ? "PorAprobar" : d.Activo ? "Activo" : "Inactivo");
}
