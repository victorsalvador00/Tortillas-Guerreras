namespace TG.Logistica.Publico;

/// <summary>Carga a ruta para la trazabilidad de lotes (RN-INV-14).</summary>
public sealed record EnvioTrazaInfo(
    Guid Id,
    string Folio,
    DateOnly FechaOperativa,
    string Tipo,
    string Estado,
    string RutaCodigo,
    string RutaNombre,
    string? Chofer,
    string? Vehiculo,
    DateTimeOffset? DespachadoEn);

public sealed record RetornoTrazaInfo(Guid Id, string Folio, DateOnly FechaOperativa, string Tipo, string RutaCodigo, Guid? ClienteId, Guid? VentaId);

public sealed record LiquidacionTrazaInfo(Guid Id, string Folio, DateOnly FechaOperativa, string Estado, string RutaCodigo, string? Chofer);

public interface IConsultaEnvios
{
    Task<IReadOnlyList<EnvioTrazaInfo>> EnviosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<IReadOnlyList<RetornoTrazaInfo>> RetornosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<IReadOnlyList<LiquidacionTrazaInfo>> LiquidacionesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
