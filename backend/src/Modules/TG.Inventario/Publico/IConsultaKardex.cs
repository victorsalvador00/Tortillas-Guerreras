namespace TG.Inventario.Publico;

/// <summary>Renglón del kardex con su documento origen, para la trazabilidad de lotes (RN-INV-14).</summary>
public sealed record MovimientoKardex(
    Guid Id,
    DateTimeOffset FechaHora,
    DateOnly FechaOperativa,
    TipoMovimiento Tipo,
    Guid AlmacenId,
    Guid ProductoId,
    string Lote,
    decimal Cantidad,
    DocumentoInventario DocumentoTipo,
    Guid DocumentoId,
    string DocumentoFolio,
    DateOnly? FechaCaducidad);

public sealed record LoteKardex(Guid ProductoId, string Lote, DateOnly? FechaCaducidad);

public sealed record DocumentoKardex(DocumentoInventario Tipo, Guid Id, string Folio);

/// <summary>Consultas de solo lectura del kardex para la trazabilidad (RN-INV-14 a RN-INV-17).</summary>
public interface IConsultaKardex
{
    /// <summary>Movimientos de los lotes indicados (producto + lote no vacío) en todos los almacenes.</summary>
    Task<IReadOnlyList<MovimientoKardex>> MovimientosDeLotesAsync(IReadOnlyCollection<(Guid ProductoId, string Lote)> lotes,
        CancellationToken ct);

    /// <summary>Todos los movimientos de los documentos indicados.</summary>
    Task<IReadOnlyList<MovimientoKardex>> MovimientosDeDocumentosAsync(IReadOnlyCollection<(DocumentoInventario Tipo, Guid Id)> documentos,
        CancellationToken ct);

    /// <summary>Lotes cuyo texto contiene <paramref name="texto"/> (sin distinguir mayúsculas).</summary>
    Task<IReadOnlyList<LoteKardex>> BuscarLotesAsync(string texto, int limite, CancellationToken ct);

    /// <summary>Documentos que movieron inventario cuyo folio contiene <paramref name="texto"/>.</summary>
    Task<IReadOnlyList<DocumentoKardex>> BuscarDocumentosAsync(string texto, int limite, CancellationToken ct);
}
