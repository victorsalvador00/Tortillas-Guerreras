namespace TG.SharedKernel.Dominio;

/// <summary>Entidad con columnas de auditoría; las llena <c>AuditoriaInterceptor</c>.</summary>
public interface IAuditable
{
    DateTimeOffset CreadoEn { get; set; }
    Guid CreadoPor { get; set; }
    DateTimeOffset? ActualizadoEn { get; set; }
    Guid? ActualizadoPor { get; set; }
}

/// <summary>Entidad que baja a la app; <c>VersionSyncInterceptor</c> le asigna <c>sync.version_seq</c> al guardar.</summary>
public interface IVersionSync
{
    long VersionSync { get; set; }
}

/// <summary>Base de entidades editables con auditoría.</summary>
public abstract class EntidadAuditable : IAuditable
{
    public Guid Id { get; set; } = Dominio.Id.Nuevo();
    public DateTimeOffset CreadoEn { get; set; }
    public Guid CreadoPor { get; set; }
    public DateTimeOffset? ActualizadoEn { get; set; }
    public Guid? ActualizadoPor { get; set; }
}
