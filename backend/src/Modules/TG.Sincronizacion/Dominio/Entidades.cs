namespace TG.Sincronizacion.Dominio;

public enum EstadoOperacion
{
    Aplicada,
    Rechazada,
}

/// <summary>
/// Operación recibida de la app. El <c>op_id</c> se inserta en la misma transacción que el efecto de negocio
/// (RN-SYN-03); un reenvío responde con el resultado guardado (RN-SYN-02).
/// </summary>
public sealed class Operacion
{
    public Guid OpId { get; set; }
    public Guid DispositivoId { get; set; }
    public Guid LoteId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public long SecuenciaDispositivo { get; set; }
    public DateTimeOffset CreadaEn { get; set; }
    public DateTimeOffset RecibidaEn { get; set; }
    public EstadoOperacion Estado { get; set; }

    /// <summary>JSON con <c>idsServidor</c> e <c>incidencias</c>.</summary>
    public string? Resultado { get; set; }

    public string? Codigo { get; set; }
    public string? Error { get; set; }
}

public sealed class LoteSync
{
    public Guid Id { get; set; }
    public Guid DispositivoId { get; set; }
    public Guid UsuarioId { get; set; }
    public DateTimeOffset RecibidoEn { get; set; }
    public int TotalOperaciones { get; set; }
    public int Aplicadas { get; set; }
    public int Rechazadas { get; set; }
    public int Duplicadas { get; set; }
    public string? VersionApp { get; set; }
}
