using Microsoft.AspNetCore.Identity;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;

namespace TG.Seguridad.Dominio;

[Bitacora(Ignorar = [nameof(SecurityStamp), nameof(ConcurrencyStamp), nameof(AccessFailedCount), nameof(LockoutEnd), nameof(NormalizedUserName), nameof(NormalizedEmail)], Ocultar = [nameof(PasswordHash)])]
public sealed class Usuario : IdentityUser<Guid>, IAuditable
{
    public Usuario()
    {
        Id = SharedKernel.Dominio.Id.Nuevo();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string NombreCompleto { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
    public Guid CreadoPor { get; set; }
    public DateTimeOffset? ActualizadoEn { get; set; }
    public Guid? ActualizadoPor { get; set; }
}

public sealed class Rol : IdentityRole<Guid>
{
    public Rol()
    {
        Id = SharedKernel.Dominio.Id.Nuevo();
    }

    public Rol(string nombre) : this()
    {
        Name = nombre;
        NormalizedName = nombre.ToUpperInvariant();
    }

    public string? Descripcion { get; set; }
    public List<RolPermiso> Permisos { get; set; } = [];
}

public sealed class Permiso
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public string Clave { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
}

public sealed class RolPermiso
{
    public Guid RolId { get; set; }
    public Guid PermisoId { get; set; }
    public Permiso? Permiso { get; set; }
}

public enum PlataformaDispositivo
{
    android,
    ios,
}

/// <summary>Teléfono de ruta. Sin <see cref="Codigo"/> está por aprobar (RN-SEG-03); inactivo no sincroniza (RN-SEG-02).</summary>
[Bitacora(Ignorar = [nameof(VersionApp), nameof(UltimoSyncEn), nameof(UltimoConsecutivo), nameof(OperacionesPendientes)])]
public sealed class Dispositivo : EntidadAuditable
{
    public string? Codigo { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public Guid? UsuarioId { get; set; }
    public PlataformaDispositivo Plataforma { get; set; } = PlataformaDispositivo.android;
    public string? VersionApp { get; set; }
    public DateTimeOffset? UltimoSyncEn { get; set; }
    public int UltimoConsecutivo { get; set; }
    public int OperacionesPendientes { get; set; }
    public bool Activo { get; set; }
    public Guid? SolicitadoPor { get; set; }

    public bool PorAprobar => Codigo is null;
}

public sealed class RefreshToken
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid UsuarioId { get; set; }
    public Guid? DispositivoId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ExpiraEn { get; set; }
    public DateTimeOffset? RevocadoEn { get; set; }
    public Guid? ReemplazadoPor { get; set; }

    public bool Vigente(DateTimeOffset ahora) => RevocadoEn is null && ExpiraEn > ahora;
}
