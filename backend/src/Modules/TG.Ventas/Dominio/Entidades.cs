using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;
using TG.Ventas.Publico;

namespace TG.Ventas.Dominio;

public sealed class Canal : EntidadAuditable
{
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool RequiereFacturaDefault { get; set; }
    public FormatoLiquidacion FormatoLiquidacion { get; set; }

    /// <summary>RN-LOG-24: en este canal la devolución fuera de tolerancia se cobra al chofer.</summary>
    public bool AplicaProductoACobro { get; set; }
    public PedidoPor PedidoPor { get; set; }

    /// <summary>Orden de las pestañas del estimado.</summary>
    public short Orden { get; set; }
    public bool Activo { get; set; } = true;
}

[Bitacora]
public sealed class ListaPrecio : EntidadAuditable, IVersionSync
{
    public string Nombre { get; set; } = string.Empty;
    public Guid? CanalId { get; set; }
    public DateOnly VigenteDesde { get; set; }
    public bool Activo { get; set; } = true;
    public long VersionSync { get; set; }
    public List<ListaPrecioPartida> Partidas { get; set; } = [];
}

/// <summary>Precio <b>sin impuestos</b> [SUP-07].</summary>
[Bitacora(Documento = nameof(ListaPrecioId))]
public sealed class ListaPrecioPartida : IVersionSync
{
    public Guid ListaPrecioId { get; set; }
    public Guid ProductoId { get; set; }
    public Guid UnidadId { get; set; }
    public decimal PrecioUnitario { get; set; }
    public long VersionSync { get; set; }
}

/// <summary>Corporativo (Walmart, Chedraui, SANBORNS…): agrupa clientes para exclusividades y lista heredada.</summary>
[Bitacora]
public sealed class GrupoCliente : EntidadAuditable, IVersionSync
{
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public Guid CanalId { get; set; }

    /// <summary>Lista que hereda un cliente nuevo del grupo si no se indica otra (RN-VEN-12).</summary>
    public Guid? ListaPrecioId { get; set; }

    public bool Activo { get; set; } = true;
    public long VersionSync { get; set; }
}

/// <summary>RN-VEN-12: precio especial <b>sin impuestos</b> que gana sobre la lista desde <c>VigenteDesde</c>.</summary>
[Bitacora(Documento = nameof(ClienteId))]
public sealed class PrecioCliente : IVersionSync
{
    public Guid ClienteId { get; set; }
    public Guid ProductoId { get; set; }
    public Guid UnidadId { get; set; }
    public decimal PrecioUnitario { get; set; }
    public DateOnly VigenteDesde { get; set; }
    public bool Activo { get; set; } = true;
    public long VersionSync { get; set; }
}

/// <summary>RN-VEN-14: el producto solo se vende al cliente o a los clientes del grupo (uno de los dos).</summary>
[Bitacora(Documento = nameof(ProductoId))]
public sealed class ProductoExclusivo : IVersionSync
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid ProductoId { get; set; }
    public Guid? ClienteId { get; set; }
    public Guid? GrupoClienteId { get; set; }
    public bool Activo { get; set; } = true;
    public long VersionSync { get; set; }
}

[Bitacora]
public sealed class Cliente : EntidadAuditable, IVersionSync, IConcurrencia
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public Guid CanalId { get; set; }
    public Guid? GrupoClienteId { get; set; }
    public Guid ListaPrecioId { get; set; }
    public CondicionPago CondicionPago { get; set; }
    public int DiasCredito { get; set; }
    public decimal? LimiteCredito { get; set; }
    public bool RequiereFactura { get; set; }
    public string? Rfc { get; set; }
    public string? RazonSocial { get; set; }
    public string? RegimenFiscal { get; set; }
    public string? CodigoPostalFiscal { get; set; }
    public string? UsoCfdi { get; set; }
    public string? EmailFacturacion { get; set; }
    public string? Telefono { get; set; }
    public string? Calle { get; set; }
    public string? Colonia { get; set; }
    public string? Municipio { get; set; }
    public string? Estado { get; set; }
    public string? CodigoPostal { get; set; }
    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    public bool UbicacionAprobada { get; set; }

    /// <summary>Ubicación capturada en ruta pendiente de aprobar (RN-VEN-06).</summary>
    public decimal? LatitudPropuesta { get; set; }

    public decimal? LongitudPropuesta { get; set; }
    public bool EsGenerico { get; set; }

    /// <summary>Código de la ruta a la que pertenece el cliente genérico (RN-VEN-10).</summary>
    public string? RutaGenerico { get; set; }

    public bool Activo { get; set; } = true;
    public long VersionSync { get; set; }
    public uint Version { get; set; }

    public string? Direccion => string.Join(", ", new[] { Calle, Colonia, Municipio, Estado, CodigoPostal }
        .Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } d ? d : null;
}

[Bitacora]
public sealed class Pedido : EntidadAuditable, IVersionSync, IConcurrencia
{
    public string Folio { get; set; } = string.Empty;
    public Guid ClienteId { get; set; }
    public DateOnly Fecha { get; set; }
    public DateOnly? FechaEntrega { get; set; }
    public EstadoPedido Estado { get; set; } = EstadoPedido.Capturado;
    public Guid? EnvioId { get; set; }
    public Guid? RutaId { get; set; }
    public string? OrdenCompraCliente { get; set; }
    public string? Notas { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Ieps { get; set; }
    public decimal Total { get; set; }
    public OrigenVenta Origen { get; set; } = OrigenVenta.Web;

    /// <summary>Idempotencia de <c>CapturarPedido</c> (RN-SYN-10).</summary>
    public Guid? OpId { get; set; }

    public Guid CapturadoPor { get; set; }
    public Guid? DispositivoId { get; set; }
    public Guid? ConfirmadoPor { get; set; }
    public DateTimeOffset? ConfirmadoEn { get; set; }

    /// <summary>RN-VEN-16: pedido global de la ruta al cliente genérico (canal con <c>pedido_por = Ruta</c>).</summary>
    public bool EsRuta { get; set; }

    public long VersionSync { get; set; }
    public uint Version { get; set; }
    public List<PedidoPartida> Partidas { get; set; } = [];
}

[Bitacora(Documento = nameof(PedidoId))]
public sealed class PedidoPartida
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid PedidoId { get; set; }
    public Guid ProductoId { get; set; }
    public Guid UnidadId { get; set; }
    public decimal FactorABase { get; set; }
    public decimal Cantidad { get; set; }
    public decimal CantidadBase { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal TasaIva { get; set; }
    public decimal? TasaIeps { get; set; }
    public decimal Importe { get; set; }
    public decimal CantidadEntregadaBase { get; set; }

    /// <summary>RN-VEN-17: las de cambio físico llevan precio 0, no suman al total ni al estado del pedido.</summary>
    public TipoPartidaPedido Tipo { get; set; } = TipoPartidaPedido.Venta;

    /// <summary>RN-VEN-18: lo que capturó el vendedor en la app antes de validarse en oficina.</summary>
    public decimal? CantidadSolicitada { get; set; }

    public string? Notas { get; set; }
}

public sealed class Venta : EntidadAuditable
{
    public Guid? OpId { get; set; }
    public string Folio { get; set; } = string.Empty;
    public OrigenVenta Origen { get; set; }
    public TipoVenta Tipo { get; set; }
    public Guid? PedidoId { get; set; }
    public Guid ClienteId { get; set; }
    public Guid? RutaId { get; set; }
    public Guid AlmacenId { get; set; }
    public Guid? DispositivoId { get; set; }
    public Guid VendedorId { get; set; }
    public DateTimeOffset? FechaHoraDispositivo { get; set; }
    public DateTimeOffset FechaHoraServidor { get; set; }
    public DateOnly FechaOperativa { get; set; }
    public CondicionPago CondicionPago { get; set; }
    public FormaPago FormaPago { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Ieps { get; set; }
    public decimal Total { get; set; }
    public decimal? TotalApp { get; set; }
    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }
    public decimal? PrecisionM { get; set; }
    public decimal? DistanciaClienteM { get; set; }
    public bool RequiereRevision { get; set; }
    public EstadoVenta Estado { get; set; } = EstadoVenta.Registrada;
    public string? MotivoCancelacion { get; set; }
    public DateTimeOffset? CanceladaEn { get; set; }
    public Guid? CanceladaPor { get; set; }
    public Guid? FacturaId { get; set; }
    public List<VentaPartida> Partidas { get; set; } = [];
}

public sealed class VentaPartida
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid VentaId { get; set; }
    public Guid? PedidoPartidaId { get; set; }
    public Guid ProductoId { get; set; }
    public Guid UnidadId { get; set; }
    public decimal FactorABase { get; set; }
    public decimal Cantidad { get; set; }
    public decimal CantidadBase { get; set; }

    /// <summary>Lote(s) que salieron por FEFO; varios se separan con coma.</summary>
    public string Lote { get; set; } = string.Empty;

    public decimal PrecioUnitario { get; set; }

    /// <summary>RN-VEN-12: precio vigente del cliente (especial o lista) al aplicar la venta; nulo si no tenía.</summary>
    public decimal? PrecioReferencia { get; set; }

    public decimal TasaIva { get; set; }
    public decimal? TasaIeps { get; set; }
    public decimal Importe { get; set; }
    public decimal Iva { get; set; }
    public decimal Ieps { get; set; }
}

public sealed class Cobro : EntidadAuditable
{
    public Guid? OpId { get; set; }
    public string Folio { get; set; } = string.Empty;
    public Guid ClienteId { get; set; }
    public Guid? RutaId { get; set; }
    public Guid? DispositivoId { get; set; }
    public DateOnly FechaOperativa { get; set; }
    public FormaPago FormaPago { get; set; }
    public decimal Importe { get; set; }
    public EstadoCobro Estado { get; set; } = EstadoCobro.Registrado;
    public List<CobroAplicacion> Aplicaciones { get; set; } = [];
}

public sealed class CobroAplicacion
{
    public Guid CobroId { get; set; }
    public Guid VentaId { get; set; }
    public decimal Importe { get; set; }
}

public sealed class Incidencia : EntidadAuditable
{
    public TipoIncidencia Tipo { get; set; }
    public string EntidadTipo { get; set; } = string.Empty;
    public Guid EntidadId { get; set; }

    /// <summary>JSON (<c>jsonb</c>).</summary>
    public string? Detalle { get; set; }

    public EstadoIncidencia Estado { get; set; } = EstadoIncidencia.Abierta;
    public Guid? RevisadaPor { get; set; }
    public DateTimeOffset? RevisadaEn { get; set; }
    public string? NotasRevision { get; set; }
}

/// <summary>RN-VEN-02: importes por partida redondeados a 2 decimales, AwayFromZero.</summary>
public static class CalculoImportes
{
    public static (decimal Importe, decimal Ieps, decimal Iva) Partida(decimal cantidad, decimal precio, decimal tasaIva,
        decimal? tasaIeps)
    {
        var importe = Dinero.Redondear(cantidad * precio);
        var ieps = Dinero.Redondear(importe * (tasaIeps ?? 0m));
        var iva = Dinero.Redondear((importe + ieps) * tasaIva);
        return (importe, ieps, iva);
    }

    public const decimal ToleranciaTotalApp = 0.05m;

    public static bool TotalDifiere(decimal totalServidor, decimal? totalApp) =>
        totalApp is { } app && Math.Abs(app - totalServidor) > ToleranciaTotalApp;
}

public static class Geo
{
    private const double RadioTierraM = 6_371_000d;

    /// <summary>Distancia haversine en metros.</summary>
    public static decimal DistanciaMetros(decimal lat1, decimal lng1, decimal lat2, decimal lng2)
    {
        static double Rad(decimal g) => (double)g * Math.PI / 180d;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + (Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2));
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return Math.Round((decimal)(RadioTierraM * c), 2, MidpointRounding.AwayFromZero);
    }
}
