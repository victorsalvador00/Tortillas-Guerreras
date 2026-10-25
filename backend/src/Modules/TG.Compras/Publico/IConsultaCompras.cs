namespace TG.Compras.Publico;

/// <summary>Recepción con su proveedor y orden de compra, para la trazabilidad de lotes (RN-INV-14).</summary>
public sealed record RecepcionTrazaInfo(
    Guid Id,
    string Folio,
    DateOnly FechaOperativa,
    string Estado,
    Guid ProveedorId,
    string ProveedorCodigo,
    string Proveedor,
    string? Rfc,
    Guid? OrdenCompraId,
    string? OrdenCompraFolio,
    DateOnly? OrdenCompraFecha,
    string? FacturaProveedor);

public sealed record OrdenCompraTrazaInfo(Guid Id, string Folio, DateOnly Fecha, string Estado, string Proveedor);

/// <summary>Lo que otros módulos y el host pueden leer de Compras.</summary>
public interface IConsultaCompras
{
    Task<IReadOnlyList<RecepcionTrazaInfo>> RecepcionesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Recepciones registradas (no canceladas) de una orden de compra.</summary>
    Task<IReadOnlyList<Guid>> RecepcionesDeOrdenAsync(Guid ordenCompraId, CancellationToken ct);

    Task<OrdenCompraTrazaInfo?> ObtenerOrdenAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<OrdenCompraTrazaInfo>> BuscarOrdenesAsync(string texto, int limite, CancellationToken ct);
}
