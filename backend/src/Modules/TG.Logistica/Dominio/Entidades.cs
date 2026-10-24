using TG.Logistica.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;

namespace TG.Logistica.Dominio;

public sealed class Vehiculo : EntidadAuditable
{
    public string Placas { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal? CapacidadKg { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Ruta de reparto con su almacén tipo <c>Ruta</c> (RN-LOG-01) y su canal (RN-LOG-31).</summary>
[Bitacora]
public sealed class Ruta : EntidadAuditable, IVersionSync, IConcurrencia
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public Guid AlmacenId { get; set; }

    /// <summary>Canal de Ventas (otro esquema, sin llave foránea).</summary>
    public Guid CanalId { get; set; }

    /// <summary>Chofer predeterminado: se propone al crear una carga (RN-LOG-12).</summary>
    public Guid? VendedorId { get; set; }

    /// <summary>Vehículo predeterminado: se propone al crear una carga (RN-LOG-12).</summary>
    public Guid? VehiculoId { get; set; }
    public bool Activo { get; set; } = true;
    public long VersionSync { get; set; }
    public uint Version { get; set; }
}

[Bitacora(Documento = nameof(RutaId))]
public sealed class RutaCliente : IVersionSync
{
    public Guid RutaId { get; set; }
    public Guid ClienteId { get; set; }
    public short OrdenVisita { get; set; }

    /// <summary>1 = lunes … 7 = domingo.</summary>
    public short[] DiasVisita { get; set; } = [];

    public bool Activo { get; set; } = true;
    public long VersionSync { get; set; }
}

[Bitacora]
public sealed class Envio : EntidadAuditable, IVersionSync, IConcurrencia
{
    public string Folio { get; set; } = string.Empty;
    public DateOnly FechaOperativa { get; set; }
    public Guid RutaId { get; set; }

    /// <summary>RN-LOG-12. Nulo solo en cargas de v0.1 cuya ruta no tenía vendedor.</summary>
    public Guid? ChoferId { get; set; }

    public Guid? VehiculoId { get; set; }
    public TipoEnvio Tipo { get; set; }
    public Guid AlmacenOrigenId { get; set; }
    public Guid AlmacenDestinoId { get; set; }
    public EstadoEnvio Estado { get; set; } = EstadoEnvio.Preparado;
    public DateTimeOffset? DespachadoEn { get; set; }
    public Guid? LiquidacionId { get; set; }
    public string? Notas { get; set; }
    public long VersionSync { get; set; }
    public uint Version { get; set; }
    public List<EnvioPartida> Partidas { get; set; } = [];
}

/// <summary>En <c>Preparado</c> el lote vacío significa "el que toque por FEFO"; al despachar queda el lote real.</summary>
[Bitacora(Documento = nameof(EnvioId))]
public sealed class EnvioPartida : IVersionSync
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid EnvioId { get; set; }
    public Guid ProductoId { get; set; }
    public string Lote { get; set; } = string.Empty;
    public decimal CantidadBase { get; set; }
    public long VersionSync { get; set; }
}

[Bitacora]
public sealed class Retorno : EntidadAuditable
{
    public string Folio { get; set; } = string.Empty;
    public Guid? OpId { get; set; }
    public TipoRetorno Tipo { get; set; }
    public Guid RutaId { get; set; }
    public Guid? ClienteId { get; set; }
    public Guid? VentaId { get; set; }
    public DateOnly FechaOperativa { get; set; }
    public DateTimeOffset? FechaHoraDispositivo { get; set; }
    public Guid? DispositivoId { get; set; }
    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    public decimal? PrecisionM { get; set; }
    public EstadoRetorno Estado { get; set; } = EstadoRetorno.Registrado;
    public string? Notas { get; set; }
    public List<RetornoPartida> Partidas { get; set; } = [];
}

[Bitacora(Documento = nameof(RetornoId))]
public sealed class RetornoPartida
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid RetornoId { get; set; }
    public Guid ProductoId { get; set; }
    public string Lote { get; set; } = string.Empty;
    public decimal CantidadBase { get; set; }
    public CondicionRetorno Condicion { get; set; }
    public Guid? MotivoMermaId { get; set; }
}

[Bitacora]
public sealed class Liquidacion : EntidadAuditable, IConcurrencia
{
    public string Folio { get; set; } = string.Empty;
    public Guid RutaId { get; set; }
    public DateOnly FechaOperativa { get; set; }

    /// <summary>RN-LOG-13: el chofer de las cargas de la ruta y fecha.</summary>
    public Guid? ChoferId { get; set; }

    public EstadoLiquidacion Estado { get; set; } = EstadoLiquidacion.Abierta;
    public decimal EfectivoEsperado { get; set; }
    public decimal? EfectivoEntregado { get; set; }
    public decimal? DiferenciaEfectivo { get; set; }
    public Guid? CerradaPor { get; set; }
    public DateTimeOffset? CerradaEn { get; set; }
    public bool CerradaForzada { get; set; }
    public string? Notas { get; set; }
    public uint Version { get; set; }
    public List<LiquidacionPartida> Partidas { get; set; } = [];
}

[Bitacora(Documento = nameof(LiquidacionId))]
public sealed class LiquidacionPartida
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid LiquidacionId { get; set; }
    public Guid ProductoId { get; set; }
    public string Lote { get; set; } = string.Empty;
    public decimal Cargado { get; set; }
    public decimal Vendido { get; set; }
    public decimal DevueltoClientesVendible { get; set; }
    public decimal DevueltoClientesNoVendible { get; set; }
    public decimal SobranteEsperado { get; set; }
    public decimal? SobranteContado { get; set; }
    public decimal? Diferencia { get; set; }
    public DestinoSobrante DestinoSobrante { get; set; } = DestinoSobrante.Reintegrar;
    public Guid? MotivoMermaId { get; set; }
}

public static class ReglasLogistica
{
    /// <summary>RN-LOG-06: lo no vendible ya salió como merma.</summary>
    public static decimal SobranteEsperado(decimal cargado, decimal vendido, decimal devueltoVendible) =>
        cargado - vendido + devueltoVendible;

    public static decimal Diferencia(decimal contado, decimal esperado) => contado - esperado;

    /// <summary>RN-LOG-08.</summary>
    public static decimal DiferenciaEfectivo(decimal entregado, decimal esperado) => Dinero.Redondear(entregado - esperado);

    /// <summary>
    /// [SUP-13] Condición por defecto de una devolución: el producto terminado perecedero (vida útil ≤ 2 días, como
    /// la tortilla y la masa) no se puede volver a vender.
    /// </summary>
    public static CondicionRetorno CondicionPredeterminada(string tipoProducto, int? vidaUtilDias) =>
        tipoProducto == "Terminado" && vidaUtilDias is <= 2 ? CondicionRetorno.NoVendible : CondicionRetorno.Vendible;

    public static bool DiasValidos(IEnumerable<short> dias) => dias.All(d => d is >= 1 and <= 7);
}
