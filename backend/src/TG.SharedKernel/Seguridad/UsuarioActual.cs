using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace TG.SharedKernel.Seguridad;

/// <summary>Usuario de la solicitud en curso (o del proceso en segundo plano).</summary>
public interface IUsuarioActual
{
    Guid? Id { get; }

    string? Nombre { get; }

    Guid? DispositivoId { get; }

    bool TienePermiso(string permiso);
}

public sealed class UsuarioActualHttp(IHttpContextAccessor accesor) : IUsuarioActual
{
    public const string ClaimDispositivo = "dispositivo";

    private ClaimsPrincipal? Principal => accesor.HttpContext?.User;

    public Guid? Id => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub"), out var id)
        ? id
        : null;

    public string? Nombre => Principal?.FindFirstValue("nombre") ?? Principal?.Identity?.Name;

    public Guid? DispositivoId => Guid.TryParse(Principal?.FindFirstValue(ClaimDispositivo), out var id) ? id : null;

    public bool TienePermiso(string permiso) => Principal?.HasClaim(Permisos.Claim, permiso) ?? false;
}
