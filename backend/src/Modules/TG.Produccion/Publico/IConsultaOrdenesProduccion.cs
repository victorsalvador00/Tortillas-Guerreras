namespace TG.Produccion.Publico;

/// <summary>Orden de producción para la trazabilidad de lotes (RN-INV-14).</summary>
public sealed record OrdenTrazaInfo(
    Guid Id,
    string Folio,
    DateOnly FechaOperativa,
    string Turno,
    string Estado,
    Guid ProductoId,
    string Lote,
    decimal CantidadPlaneada,
    decimal? CantidadProducida,
    string? Linea,
    string? Receta,
    int? RecetaVersion,
    DateTimeOffset? TerminadaEn);

public interface IConsultaOrdenesProduccion
{
    Task<IReadOnlyList<OrdenTrazaInfo>> OrdenesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
