using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;

namespace TG.Catalogos.Dominio;

public enum TipoUnidad
{
    Masa,
    Volumen,
    Pieza,
}

public enum TipoProducto
{
    MateriaPrima,
    Empaque,
    Semiterminado,
    Terminado,
    Reventa,
}

public enum TipoParametro
{
    @int,
    @decimal,
    @bool,
    text,
}

public sealed class UnidadMedida : EntidadAuditable
{
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public TipoUnidad Tipo { get; set; }
    public string ClaveSat { get; set; } = string.Empty;
    public short Decimales { get; set; }
    public bool Activo { get; set; } = true;

    /// <summary>RN-CAT-06: las cantidades capturadas respetan los decimales de la unidad.</summary>
    public bool RespetaDecimales(decimal cantidad) => decimal.Round(cantidad, Decimales) == cantidad;
}

public sealed class Producto : EntidadAuditable, IVersionSync, IConcurrencia
{
    public string Sku { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public TipoProducto Tipo { get; set; }
    public Guid UnidadBaseId { get; set; }
    public UnidadMedida? UnidadBase { get; set; }
    public bool EsComprable { get; set; }
    public bool EsProducible { get; set; }
    public bool EsVendible { get; set; }
    public bool ManejaLote { get; set; }
    public int? VidaUtilDias { get; set; }
    public string? ClaveProdServSat { get; set; }
    public string ObjetoImpuestoSat { get; set; } = "02";
    public decimal TasaIva { get; set; }
    public decimal? TasaIeps { get; set; }
    public string? CodigoBarras { get; set; }
    public decimal? StockMinimo { get; set; }
    public bool Activo { get; set; } = true;
    public long VersionSync { get; set; }
    public uint Version { get; set; }
    public List<ProductoUnidad> Conversiones { get; set; } = [];

    /// <summary>RN-CAT-04: banderas propuestas según el tipo (editables).</summary>
    public static (bool Comprable, bool Producible, bool Vendible, bool ManejaLote) BanderasPorTipo(TipoProducto tipo) => tipo switch
    {
        TipoProducto.MateriaPrima => (true, false, false, false),
        TipoProducto.Empaque => (true, false, false, false),
        TipoProducto.Semiterminado => (false, true, false, false),
        // [SUP-02] el producto terminado maneja lote.
        TipoProducto.Terminado => (false, true, true, true),
        TipoProducto.Reventa => (true, false, true, false),
        _ => (false, false, false, false),
    };

    /// <summary>Factor a la unidad base; la unidad base tiene factor 1 implícito (RN-CAT-03).</summary>
    public decimal? FactorDe(Guid unidadId) =>
        unidadId == UnidadBaseId ? 1m : Conversiones.FirstOrDefault(c => c.UnidadId == unidadId)?.FactorABase;
}

public sealed class ProductoUnidad
{
    public Guid ProductoId { get; set; }
    public Guid UnidadId { get; set; }
    public UnidadMedida? Unidad { get; set; }
    public decimal FactorABase { get; set; }
    public bool UsoCompra { get; set; }
    public bool UsoVenta { get; set; }
    public bool UsoProduccion { get; set; }
    public bool PredeterminadaVenta { get; set; }
}

public sealed class MotivoMerma : EntidadAuditable, IVersionSync
{
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool AplicaProduccion { get; set; }
    public bool AplicaAlmacen { get; set; }
    public bool AplicaRuta { get; set; }
    public bool AplicaRetorno { get; set; }
    public bool Activo { get; set; } = true;
    public long VersionSync { get; set; }
}

public sealed class Parametro : IVersionSync
{
    public string Clave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public TipoParametro Tipo { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public long VersionSync { get; set; }
}
