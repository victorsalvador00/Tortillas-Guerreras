using TG.SharedKernel.Resultados;

namespace TG.Inventario.Publico;

public enum TipoMovimiento
{
    EntradaCompra,
    DevolucionProveedor,
    EntradaProduccion,
    SalidaConsumo,
    Merma,
    TraspasoSalida,
    TraspasoEntrada,
    Venta,
    RetornoCliente,
    AjusteEntrada,
    AjusteSalida,
}

public enum DocumentoInventario
{
    Recepcion,
    OrdenProduccion,
    Envio,
    Venta,
    Retorno,
    Liquidacion,
    Ajuste,
    Traspaso,
    ConteoFisico,
}

public enum TipoAlmacen
{
    MateriaPrima,
    ProductoTerminado,
    General,
    Ruta,
}

/// <summary>Una línea a registrar. <see cref="Cantidad"/> lleva signo: + entrada, − salida, en unidad base.</summary>
/// <param name="Lote">Para productos con lote: obligatorio en entradas; en salidas, <c>null</c> = FEFO (RN-INV-07).</param>
/// <param name="CostoUnitario">Entradas con costo recalculan el promedio (RN-INV-05); <c>null</c> usa el promedio vigente.</param>
public sealed record LineaMovimiento(
    TipoMovimiento Tipo,
    Guid AlmacenId,
    Guid ProductoId,
    string? Lote,
    decimal Cantidad,
    decimal? CostoUnitario = null,
    Guid? MotivoMermaId = null,
    DateOnly? FechaCaducidad = null,
    string? Notas = null);

public sealed record RegistrarMovimientosRequest(
    DocumentoInventario DocumentoTipo,
    Guid DocumentoId,
    string DocumentoFolio,
    DateOnly FechaOperativa,
    Guid UsuarioId,
    IReadOnlyList<LineaMovimiento> Lineas,
    Guid? DispositivoId = null,
    string? Notas = null);

/// <param name="QuedoNegativo">La existencia resultante quedó negativa (solo posible en almacenes de ruta, RN-INV-03).</param>
public sealed record MovimientoRegistrado(
    Guid Id,
    TipoMovimiento Tipo,
    Guid AlmacenId,
    Guid ProductoId,
    string Lote,
    decimal Cantidad,
    decimal CostoUnitario,
    decimal CostoTotal,
    decimal ExistenciaResultante,
    bool QuedoNegativo,
    DateOnly? FechaCaducidad);

public sealed record AlmacenInfo(Guid Id, string Codigo, string Nombre, TipoAlmacen Tipo, Guid? RutaId, bool PermiteNegativo, bool Activo);

public sealed record ExistenciaInfo(Guid AlmacenId, Guid ProductoId, string Lote, decimal Cantidad, DateOnly? FechaCaducidad);

/// <summary>
/// Único punto de escritura de existencias (regla 3). Todo cambio entra por el kardex, en la transacción de la
/// unidad de trabajo, con bloqueo de fila (RN-INV-04).
/// </summary>
public interface IInventarioService
{
    Task<Result<IReadOnlyList<MovimientoRegistrado>>> RegistrarMovimientosAsync(RegistrarMovimientosRequest request,
        CancellationToken ct);

    Task<decimal> ObtenerExistenciaAsync(Guid almacenId, Guid productoId, string? lote, CancellationToken ct);

    Task<decimal> ObtenerCostoPromedioAsync(Guid productoId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, decimal>> ObtenerCostosPromedioAsync(IEnumerable<Guid> productoIds, CancellationToken ct);

    Task<IReadOnlyList<ExistenciaInfo>> ExistenciasDeAlmacenAsync(Guid almacenId, CancellationToken ct);

    Task<AlmacenInfo?> ObtenerAlmacenAsync(Guid id, CancellationToken ct);

    Task<AlmacenInfo?> ObtenerAlmacenPorCodigoAsync(string codigo, CancellationToken ct);

    Task<AlmacenInfo?> AlmacenDeRutaAsync(Guid rutaId, CancellationToken ct);

    Task<IReadOnlyList<AlmacenInfo>> ListarAlmacenesAsync(CancellationToken ct);

    /// <summary>RN-LOG-01: crea el almacén tipo <c>Ruta</c> (con negativos permitidos) de una ruta nueva.</summary>
    Task<Result<AlmacenInfo>> CrearAlmacenRutaAsync(Guid rutaId, string codigo, string nombre, CancellationToken ct);

    Task<IReadOnlyList<MovimientoRegistrado>> MovimientosDeDocumentoAsync(DocumentoInventario tipo, Guid documentoId,
        CancellationToken ct);
}

public static class ErroresInventario
{
    public const string ExistenciaInsuficiente = "INV-001";
    public const string SignoInvalido = "INV-002";
    public const string LoteRequerido = "INV-003";
    public const string AlmacenNoEncontrado = "INV-004";
    public const string ProductoNoEncontrado = "INV-005";
    public const string AlmacenInactivo = "INV-006";
}
