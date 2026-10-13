using TG.SharedKernel.Resultados;

namespace TG.Catalogos.Publico;

public enum UsoUnidad
{
    Compra,
    Venta,
    Produccion,
}

public sealed record ConversionInfo(Guid UnidadId, string UnidadClave, string UnidadNombre, string ClaveSat, short Decimales,
    decimal FactorABase, bool UsoCompra, bool UsoVenta, bool UsoProduccion, bool PredeterminadaVenta);

public sealed record ProductoInfo(
    Guid Id,
    string Sku,
    string Nombre,
    string Tipo,
    Guid UnidadBaseId,
    string UnidadBaseClave,
    string UnidadBaseClaveSat,
    short UnidadBaseDecimales,
    bool EsComprable,
    bool EsProducible,
    bool EsVendible,
    bool ManejaLote,
    int? VidaUtilDias,
    string? ClaveProdServSat,
    string ObjetoImpuestoSat,
    decimal TasaIva,
    decimal? TasaIeps,
    decimal? StockMinimo,
    bool Activo,
    long VersionSync,
    IReadOnlyList<ConversionInfo> Conversiones);

public sealed record UnidadInfo(Guid Id, string Clave, string Nombre, string ClaveSat, short Decimales);

public sealed record MotivoMermaInfo(Guid Id, string Clave, string Nombre, bool AplicaProduccion, bool AplicaAlmacen,
    bool AplicaRuta, bool AplicaRetorno, bool Activo, long VersionSync);

public sealed record ParametroInfo(string Clave, string Valor, string Tipo, long VersionSync);

/// <summary>Resultado de convertir una cantidad capturada a la unidad base del producto.</summary>
public sealed record CantidadBase(Guid ProductoId, Guid UnidadId, decimal Cantidad, decimal FactorABase, decimal Base);

/// <summary>Lo que otros módulos pueden usar de Catálogos.</summary>
public interface ICatalogosService
{
    Task<ProductoInfo?> ObtenerProductoAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, ProductoInfo>> ObtenerProductosAsync(IEnumerable<Guid> ids, CancellationToken ct);

    /// <summary>Ids de productos cuyo SKU o nombre contiene el texto (para filtros de otros módulos).</summary>
    Task<IReadOnlyList<Guid>> BuscarProductosAsync(string texto, CancellationToken ct);

    Task<IReadOnlyList<ProductoInfo>> ListarProductosAsync(bool? vendibles, bool soloActivos, CancellationToken ct);

    /// <summary>
    /// RN-CAT-03 y RN-CAT-06: la unidad debe tener conversión a la base (con el uso indicado) y la cantidad respetar
    /// los decimales de la unidad. Devuelve la cantidad en unidad base.
    /// </summary>
    Task<Result<CantidadBase>> ConvertirABaseAsync(Guid productoId, Guid unidadId, decimal cantidad, UsoUnidad? uso,
        CancellationToken ct);

    Task<UnidadInfo?> ObtenerUnidadAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, UnidadInfo>> ObtenerUnidadesAsync(CancellationToken ct);

    Task<MotivoMermaInfo?> ObtenerMotivoAsync(Guid id, CancellationToken ct);

    Task<MotivoMermaInfo?> ObtenerMotivoPorClaveAsync(string clave, CancellationToken ct);

    Task<IReadOnlyList<MotivoMermaInfo>> ListarMotivosAsync(CancellationToken ct);

    /// <summary>Parámetro de negocio (<c>catalogos.parametros</c>) con caché en memoria.</summary>
    Task<T> ObtenerParametroAsync<T>(string clave, T predeterminado, CancellationToken ct);

    Task<IReadOnlyList<ParametroInfo>> ListarParametrosAsync(CancellationToken ct);
}

/// <summary>
/// Lo implementa Inventario (Catálogos no depende de nadie): RN-CAT-01, 02 y 05 necesitan saber si un producto tiene
/// movimientos o existencia.
/// </summary>
public interface IConsultaInventarioProducto
{
    Task<bool> TieneMovimientosAsync(Guid productoId, CancellationToken ct);

    Task<decimal> ExistenciaTotalAsync(Guid productoId, CancellationToken ct);
}

public static class ClavesParametro
{
    public const string ZonaHoraria = "zona_horaria";
    public const string GpsUmbralDistancia = "gps.umbral_distancia_m";
    public const string GpsPrecisionMaxima = "gps.precision_maxima_m";
    public const string RutaPermiteNegativo = "ruta.permite_existencia_negativa";
    public const string RutaDiasRetencion = "ruta.dias_retencion_local";
    public const string AutofacturaLimite = "autofactura.limite";
    public const string FacturaGlobalPeriodicidad = "factura_global.periodicidad";
    public const string ProduccionConsumoSinExistencia = "produccion.permitir_consumo_sin_existencia";
    public const string ProduccionUmbralVariacion = "produccion.umbral_variacion_pct";
    public const string ComprasTolerancia = "compras.tolerancia_pct";
    public const string MetodoCosteo = "inventario.metodo_costeo";
}

public static class ClavesMotivoMerma
{
    public const string DevolucionNoVendible = "DEV_NO_VENDIBLE";
    public const string FaltanteRuta = "FALTANTE_RUTA";
    public const string Reproceso = "REPROCESO";
    public const string Otro = "OTRO";
}
