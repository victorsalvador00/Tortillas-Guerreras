namespace TG.Ventas.Publico;

/// <summary>Venta con su cliente para la trazabilidad de lotes (RN-INV-14).</summary>
public sealed record VentaTrazaInfo(
    Guid Id,
    string Folio,
    DateOnly FechaOperativa,
    string Estado,
    Guid ClienteId,
    string ClienteCodigo,
    string Cliente,
    Guid? RutaId);

public sealed record ClienteTrazaInfo(Guid Id, string Codigo, string Nombre);

public interface IConsultaVentas
{
    Task<IReadOnlyList<VentaTrazaInfo>> VentasAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<IReadOnlyList<ClienteTrazaInfo>> ClientesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
