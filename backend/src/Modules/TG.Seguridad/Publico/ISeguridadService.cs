namespace TG.Seguridad.Publico;

public sealed record UsuarioResumen(Guid Id, string Usuario, string NombreCompleto, bool Activo);

public sealed record DispositivoInfo(Guid Id, string? Codigo, string Nombre, Guid? UsuarioId, bool Activo,
    int UltimoConsecutivo, int OperacionesPendientes, DateTimeOffset? UltimoSyncEn);

/// <summary>Lo que otros módulos (Sincronizacion, Logistica) pueden consultar de Seguridad.</summary>
public interface ISeguridadService
{
    Task<DispositivoInfo?> ObtenerDispositivoAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<DispositivoInfo>> DispositivosDeUsuarioAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>Guarda el último contacto del dispositivo (versión, consecutivo de folio y pendientes reportados).</summary>
    Task RegistrarContactoAsync(Guid dispositivoId, string? versionApp, int? ultimoConsecutivo, int? pendientes,
        CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> NombresUsuariosAsync(IEnumerable<Guid> ids, CancellationToken ct);

    Task<bool> UsuarioTieneRolAsync(Guid usuarioId, string rol, CancellationToken ct);

    /// <summary>Id del usuario por su nombre de usuario (semillas y asignaciones).</summary>
    Task<Guid?> UsuarioPorNombreAsync(string usuario, CancellationToken ct);

    /// <summary>Usuarios activos con un rol (p. ej. vendedores para asignar a rutas).</summary>
    Task<IReadOnlyList<UsuarioResumen>> UsuariosConRolAsync(string rol, CancellationToken ct);
}
