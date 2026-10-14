using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TG.Seguridad.Dominio;
using TG.Seguridad.Infraestructura;
using TG.SharedKernel.Seguridad;

namespace TG.Seguridad.Aplicacion;

public sealed class OpcionesJwt
{
    public string Issuer { get; set; } = "tortillas-guerreras";
    public string Audience { get; set; } = "tortillas-guerreras";
    public string Key { get; set; } = string.Empty;
    public int MinutosAcceso { get; set; } = 15;
    public int DiasRefresh { get; set; } = 30;

    /// <summary>
    /// Segundos en que un refresh recién rotado todavía se acepta (dos pestañas o una respuesta perdida). Pasado este
    /// tiempo, reusarlo revoca la cadena (RN-SEG-01).
    /// </summary>
    public int SegundosGraciaRefresh { get; set; } = 30;
}

public sealed record DispositivoSesionDto(Guid Id, string? Codigo, string Nombre, bool Activo);

public sealed record PerfilDto(
    Guid Id,
    string Usuario,
    string NombreCompleto,
    string? Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permisos,
    DispositivoSesionDto? Dispositivo);

public sealed record SesionDto(
    string AccessToken,
    DateTimeOffset ExpiraEn,
    string RefreshToken,
    DateTimeOffset RefreshExpiraEn,
    PerfilDto Usuario);

/// <summary>Emite el access token (15 min) y el refresh token rotativo (30 días) de RN-SEG-01.</summary>
public sealed class ServicioTokens(SeguridadDbContext db, IOptions<OpcionesJwt> opciones, TimeProvider reloj)
{
    private readonly OpcionesJwt _opciones = opciones.Value;

    public async Task<PerfilDto> ObtenerPerfilAsync(Usuario usuario, Dispositivo? dispositivo, CancellationToken ct)
    {
        var roles = await db.UserRoles.Where(ur => ur.UserId == usuario.Id)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
            .OrderBy(n => n)
            .ToListAsync(ct);
        var permisos = await db.UserRoles.Where(ur => ur.UserId == usuario.Id)
            .Join(db.RolesPermisos, ur => ur.RoleId, rp => rp.RolId, (ur, rp) => rp.Permiso!.Clave)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);
        return new PerfilDto(usuario.Id, usuario.UserName!, usuario.NombreCompleto, usuario.Email, roles, permisos,
            dispositivo is null ? null : new DispositivoSesionDto(dispositivo.Id, dispositivo.Codigo, dispositivo.Nombre, dispositivo.Activo));
    }

    public async Task<SesionDto> EmitirAsync(Usuario usuario, Dispositivo? dispositivo, CancellationToken ct)
    {
        var perfil = await ObtenerPerfilAsync(usuario, dispositivo, ct);
        var ahora = reloj.GetUtcNow();
        var expira = ahora.AddMinutes(_opciones.MinutosAcceso);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new("usuario", usuario.UserName!),
            new("nombre", usuario.NombreCompleto),
        };
        claims.AddRange(perfil.Roles.Select(r => new Claim("rol", r)));
        claims.AddRange(perfil.Permisos.Select(p => new Claim(Permisos.Claim, p)));
        if (dispositivo is not null)
        {
            claims.Add(new Claim(UsuarioActualHttp.ClaimDispositivo, dispositivo.Id.ToString()));
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _opciones.Issuer,
            Audience = _opciones.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = ahora.UtcDateTime,
            NotBefore = ahora.UtcDateTime,
            Expires = expira.UtcDateTime,
            SigningCredentials = new SigningCredentials(ClaveFirma(_opciones), SecurityAlgorithms.HmacSha256),
        });

        var (refresh, entidad) = NuevoRefresh(usuario.Id, dispositivo?.Id, ahora);
        db.RefreshTokens.Add(entidad);
        await db.SaveChangesAsync(ct);
        return new SesionDto(token, expira, refresh, entidad.ExpiraEn, perfil);
    }

    public (string Token, RefreshToken Entidad) NuevoRefresh(Guid usuarioId, Guid? dispositivoId, DateTimeOffset ahora)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        return (token, new RefreshToken
        {
            UsuarioId = usuarioId,
            DispositivoId = dispositivoId,
            TokenHash = Hash(token),
            CreadoEn = ahora,
            ExpiraEn = ahora.AddDays(_opciones.DiasRefresh),
        });
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static SymmetricSecurityKey ClaveFirma(OpcionesJwt opciones)
    {
        if (Encoding.UTF8.GetByteCount(opciones.Key) < 32)
        {
            throw new InvalidOperationException("Jwt:Key debe tener al menos 32 bytes.");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opciones.Key));
    }
}
