using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TG.Seguridad.Dominio;
using TG.Seguridad.Infraestructura;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;

namespace TG.Seguridad.Aplicacion;

public sealed record LoginRequest(string Usuario, string Contrasena, Guid? DispositivoId);

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Usuario).NotEmpty().WithMessage("Escribe tu usuario.");
        RuleFor(x => x.Contrasena).NotEmpty().WithMessage("Escribe tu contraseña.");
    }
}

/// <summary>Inicio de sesión con bloqueo tras 5 intentos fallidos por 15 minutos (RN-SEG-05).</summary>
public sealed class LoginHandler(UserManager<Usuario> usuarios, SeguridadDbContext db, ServicioTokens tokens,
    TimeProvider reloj)
{
    public async Task<Result<SesionDto>> EjecutarAsync(LoginRequest request, CancellationToken ct)
    {
        var usuario = await usuarios.FindByNameAsync(request.Usuario.Trim());
        if (usuario is null)
        {
            return ErroresSeguridad.CredencialesInvalidas;
        }

        if (await usuarios.IsLockedOutAsync(usuario))
        {
            return ErroresSeguridad.Bloqueado(usuario.LockoutEnd ?? reloj.GetUtcNow());
        }

        if (!await usuarios.CheckPasswordAsync(usuario, request.Contrasena))
        {
            await usuarios.AccessFailedAsync(usuario);
            return await usuarios.IsLockedOutAsync(usuario)
                ? ErroresSeguridad.Bloqueado(usuario.LockoutEnd ?? reloj.GetUtcNow())
                : ErroresSeguridad.CredencialesInvalidas;
        }

        if (!usuario.Activo)
        {
            return ErroresSeguridad.UsuarioInactivo;
        }

        await usuarios.ResetAccessFailedCountAsync(usuario);

        Dispositivo? dispositivo = null;
        if (request.DispositivoId is { } dispositivoId)
        {
            dispositivo = await db.Dispositivos.FirstOrDefaultAsync(d => d.Id == dispositivoId, ct);
            if (dispositivo is null || !dispositivo.Activo || dispositivo.UsuarioId != usuario.Id)
            {
                return ErroresSeguridad.DispositivoInactivo;
            }
        }

        return await tokens.EmitirAsync(usuario, dispositivo, ct);
    }
}

public sealed record RefreshRequest(string RefreshToken);

/// <summary>
/// Refresh rotativo (RN-SEG-01): cada uso emite uno nuevo y revoca el anterior; reusar uno revocado revoca toda la
/// cadena. Un dispositivo inactivo no puede refrescar (RN-SEG-02).
/// </summary>
public sealed class RefreshHandler(SeguridadDbContext db, UserManager<Usuario> usuarios, ServicioTokens tokens,
    TimeProvider reloj)
{
    public async Task<Result<SesionDto>> EjecutarAsync(RefreshRequest request, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var hash = ServicioTokens.Hash(request.RefreshToken ?? string.Empty);
        var actual = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct);
        if (actual is null)
        {
            return ErroresSeguridad.RefreshInvalido;
        }

        if (actual.RevocadoEn is not null)
        {
            await RevocarCadenaAsync(actual, ahora, ct);
            return ErroresSeguridad.RefreshReutilizado;
        }

        if (actual.ExpiraEn <= ahora)
        {
            return ErroresSeguridad.RefreshInvalido;
        }

        var usuario = await usuarios.FindByIdAsync(actual.UsuarioId.ToString());
        if (usuario is null || !usuario.Activo)
        {
            return ErroresSeguridad.UsuarioInactivo;
        }

        Dispositivo? dispositivo = null;
        if (actual.DispositivoId is { } dispositivoId)
        {
            dispositivo = await db.Dispositivos.FirstOrDefaultAsync(d => d.Id == dispositivoId, ct);
            if (dispositivo is null || !dispositivo.Activo || dispositivo.UsuarioId != usuario.Id)
            {
                actual.RevocadoEn = ahora;
                await db.SaveChangesAsync(ct);
                return ErroresSeguridad.DispositivoInactivo;
            }
        }

        var sesion = await tokens.EmitirAsync(usuario, dispositivo, ct);
        var nuevo = await db.RefreshTokens.SingleAsync(r => r.TokenHash == ServicioTokens.Hash(sesion.RefreshToken), ct);
        actual.RevocadoEn = ahora;
        actual.ReemplazadoPor = nuevo.Id;
        await db.SaveChangesAsync(ct);
        return sesion;
    }

    /// <summary>Revoca los tokens que descienden del reutilizado (posible robo del token).</summary>
    private async Task RevocarCadenaAsync(RefreshToken reutilizado, DateTimeOffset ahora, CancellationToken ct)
    {
        var siguienteId = reutilizado.ReemplazadoPor;
        var vistos = new HashSet<Guid>();
        while (siguienteId is { } id && vistos.Add(id))
        {
            var siguiente = await db.RefreshTokens.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (siguiente is null)
            {
                break;
            }

            siguiente.RevocadoEn ??= ahora;
            siguienteId = siguiente.ReemplazadoPor;
        }

        await db.SaveChangesAsync(ct);
    }
}

public sealed class LogoutHandler(SeguridadDbContext db, TimeProvider reloj)
{
    public async Task<Result> EjecutarAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = ServicioTokens.Hash(request.RefreshToken ?? string.Empty);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct);
        if (token is { RevocadoEn: null })
        {
            token.RevocadoEn = reloj.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return Result.Exito();
    }
}

public sealed class PerfilHandler(UserManager<Usuario> usuarios, SeguridadDbContext db, ServicioTokens tokens,
    IUsuarioActual actual)
{
    public async Task<Result<PerfilDto>> EjecutarAsync(CancellationToken ct)
    {
        var usuario = actual.Id is { } id ? await usuarios.FindByIdAsync(id.ToString()) : null;
        if (usuario is null)
        {
            return ErroresSeguridad.UsuarioNoEncontrado;
        }

        var dispositivo = actual.DispositivoId is { } dispositivoId
            ? await db.Dispositivos.FirstOrDefaultAsync(d => d.Id == dispositivoId, ct)
            : null;
        return await tokens.ObtenerPerfilAsync(usuario, dispositivo, ct);
    }
}

public sealed record CambiarContrasenaRequest(string ContrasenaActual, string ContrasenaNueva);

public sealed class CambiarContrasenaValidator : AbstractValidator<CambiarContrasenaRequest>
{
    public CambiarContrasenaValidator()
    {
        RuleFor(x => x.ContrasenaActual).NotEmpty().WithMessage("Escribe tu contraseña actual.");
        RuleFor(x => x.ContrasenaNueva).NotEmpty().MinimumLength(8)
            .WithMessage("La contraseña nueva debe tener al menos 8 caracteres.");
    }
}

public sealed class CambiarContrasenaHandler(UserManager<Usuario> usuarios, SeguridadDbContext db, IUsuarioActual actual,
    TimeProvider reloj)
{
    public async Task<Result> EjecutarAsync(CambiarContrasenaRequest request, CancellationToken ct)
    {
        var usuario = actual.Id is { } id ? await usuarios.FindByIdAsync(id.ToString()) : null;
        if (usuario is null)
        {
            return ErroresSeguridad.UsuarioNoEncontrado;
        }

        if (!await usuarios.CheckPasswordAsync(usuario, request.ContrasenaActual))
        {
            return ErroresSeguridad.ContrasenaActualIncorrecta;
        }

        var resultado = await usuarios.ChangePasswordAsync(usuario, request.ContrasenaActual, request.ContrasenaNueva);
        if (!resultado.Succeeded)
        {
            return ErroresSeguridad.Identity(resultado.Errors.Select(e => e.Description));
        }

        // Cambiar la contraseña cierra las demás sesiones.
        var ahora = reloj.GetUtcNow();
        await db.RefreshTokens.Where(r => r.UsuarioId == usuario.Id && r.RevocadoEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevocadoEn, ahora), ct);
        return Result.Exito();
    }
}
