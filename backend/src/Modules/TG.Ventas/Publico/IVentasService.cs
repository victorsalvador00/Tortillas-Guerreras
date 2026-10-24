using TG.SharedKernel.Resultados;

namespace TG.Ventas.Publico;

/// <summary>Claves de los canales del cliente (v0.2). <c>DET</c> reemplaza a <c>TRAD</c>.</summary>
public static class ClavesCanal
{
    public const string Autoservicio = "AUTO";
    public const string Detalle = "DET";
    public const string Institucional = "INST";
    public const string Cedis = "CEDIS";

    /// <summary>Clave anterior de Detalle; el importador todavía la acepta.</summary>
    public const string DetalleAnterior = "TRAD";

    public static string Normalizar(string clave)
    {
        var c = clave.Trim().ToUpperInvariant();
        return c == DetalleAnterior ? Detalle : c;
    }
}

/// <summary>RN-LOG-19: formato de la hoja de liquidación del canal.</summary>
public enum FormatoLiquidacion
{
    Completo,
    Simplificado,
}

/// <summary>RN-VEN-16: un pedido por cliente o un pedido global de la ruta al genérico.</summary>
public enum PedidoPor
{
    Cliente,
    Ruta,
}

public sealed record CanalInfo(Guid Id, string Clave, string Nombre, FormatoLiquidacion FormatoLiquidacion,
    bool AplicaProductoACobro, PedidoPor PedidoPor, short Orden, bool Activo);

public enum CondicionPago
{
    Contado,
    Credito,
}

public enum FormaPago
{
    Efectivo,
    Transferencia,
    Credito,
}

public enum OrigenVenta
{
    App,
    Web,
}

public enum TipoVenta
{
    VentaRuta,
    EntregaPedido,
}

public enum EstadoVenta
{
    Registrada,
    Cancelada,
}

public enum EstadoPedido
{
    Capturado,
    Confirmado,
    Asignado,
    Entregado,
    EntregadoParcial,
    Cancelado,
}

public enum TipoIncidencia
{
    ExistenciaNegativaRuta,
    GpsFueraDeRango,
    GpsImpreciso,
    SinGps,
    PrecioDistinto,
    UbicacionPorAprobar,
    DiferenciaLiquidacion,

    /// <summary>Venta a crédito de la app que excede el límite o con cliente de contado (RN-VEN-04).</summary>
    CreditoExcedido,

    /// <summary>El total de la app difiere del recalculado por el servidor en más de $0.05 (RN-VEN-02).</summary>
    TotalDistinto,

    /// <summary>Venta de la app de un producto exclusivo de otro cliente o grupo (RN-VEN-14) [SUP-28].</summary>
    ProductoExclusivo,
}

public enum EstadoIncidencia
{
    Abierta,
    Revisada,
}

public enum EstadoCobro
{
    Registrado,
    Cancelado,
}

public sealed record GpsLectura(decimal Latitud, decimal Longitud, decimal? PrecisionM);

/// <param name="PrecioUnitario">Precio que traía la app (sin impuestos). En web se ignora: manda la lista.</param>
public sealed record PartidaVentaComando(Guid ProductoId, Guid UnidadId, decimal Cantidad, decimal? PrecioUnitario = null);

/// <summary>
/// Venta compartida web/app. El llamador resuelve <see cref="AlmacenId"/> (almacén de la ruta o el elegido en web) y
/// <see cref="FechaOperativa"/> (para la app: fecha local de <c>creadaEn</c>, RN-SYN-06).
/// </summary>
/// <param name="VentaId">Id generado por la app; null = el servidor lo genera.</param>
/// <param name="OpId">Llave de idempotencia de la app; un reenvío devuelve la venta original (<c>Duplicada</c>).</param>
/// <param name="Folio">Folio de la app (<c>R01-D03-000123</c>); null en web = <c>VTA-000001</c>.</param>
public sealed record RegistrarVentaComando(
    OrigenVenta Origen,
    Guid ClienteId,
    Guid AlmacenId,
    Guid VendedorId,
    DateOnly FechaOperativa,
    CondicionPago CondicionPago,
    FormaPago FormaPago,
    IReadOnlyList<PartidaVentaComando> Partidas,
    Guid? VentaId = null,
    Guid? OpId = null,
    string? Folio = null,
    Guid? RutaId = null,
    Guid? DispositivoId = null,
    DateTimeOffset? FechaHoraDispositivo = null,
    decimal? TotalApp = null,
    GpsLectura? Gps = null);

public sealed record PartidaEntregaComando(Guid PedidoPartidaId, decimal Cantidad);

/// <summary>Entrega de pedido (RN-VEN-08): crea una venta <c>EntregaPedido</c> con los precios del pedido.</summary>
public sealed record EntregarPedidoComando(
    OrigenVenta Origen,
    Guid PedidoId,
    Guid AlmacenId,
    Guid VendedorId,
    DateOnly FechaOperativa,
    FormaPago FormaPago,
    IReadOnlyList<PartidaEntregaComando> Partidas,
    Guid? VentaId = null,
    Guid? OpId = null,
    string? Folio = null,
    Guid? RutaId = null,
    Guid? DispositivoId = null,
    DateTimeOffset? FechaHoraDispositivo = null,
    decimal? TotalApp = null,
    GpsLectura? Gps = null);

public sealed record VentaRegistrada(
    Guid VentaId,
    string Folio,
    decimal Subtotal,
    decimal Ieps,
    decimal Iva,
    decimal Total,
    bool RequiereRevision,
    IReadOnlyList<TipoIncidencia> Incidencias,
    bool Duplicada);

/// <param name="VentaIds">Ventas a las que se aplica; vacío = las más antiguas con saldo.</param>
public sealed record RegistrarCobroComando(
    Guid ClienteId,
    DateOnly FechaOperativa,
    FormaPago FormaPago,
    decimal Importe,
    Guid? CobroId = null,
    Guid? OpId = null,
    Guid? RutaId = null,
    Guid? DispositivoId = null,
    IReadOnlyList<Guid>? VentaIds = null);

public sealed record CobroRegistrado(Guid CobroId, string Folio, decimal Importe, decimal SaldoCliente, bool Duplicado);

public sealed record ProponerUbicacionComando(Guid ClienteId, GpsLectura Gps, Guid? DispositivoId = null);

public sealed record ClienteInfo(
    Guid Id,
    string Codigo,
    string Nombre,
    Guid CanalId,
    string CanalClave,
    Guid? GrupoClienteId,
    Guid ListaPrecioId,
    CondicionPago CondicionPago,
    int DiasCredito,
    decimal? LimiteCredito,
    bool RequiereFactura,
    string? Rfc,
    string? RazonSocial,
    string? RegimenFiscal,
    string? CodigoPostalFiscal,
    string? UsoCfdi,
    string? EmailFacturacion,
    string? Telefono,
    string? Direccion,
    decimal? Latitud,
    decimal? Longitud,
    bool UbicacionAprobada,
    bool EsGenerico,
    bool Activo,
    long VersionSync);

public sealed record VentaPartidaInfo(
    Guid Id,
    Guid ProductoId,
    Guid UnidadId,
    decimal FactorABase,
    decimal Cantidad,
    decimal CantidadBase,
    string Lote,
    decimal PrecioUnitario,
    decimal TasaIva,
    decimal? TasaIeps,
    decimal Importe,
    decimal Iva,
    decimal Ieps);

public sealed record VentaInfo(
    Guid Id,
    string Folio,
    OrigenVenta Origen,
    TipoVenta Tipo,
    Guid ClienteId,
    Guid? RutaId,
    Guid AlmacenId,
    Guid VendedorId,
    DateOnly FechaOperativa,
    DateTimeOffset FechaHoraServidor,
    CondicionPago CondicionPago,
    FormaPago FormaPago,
    decimal Subtotal,
    decimal Iva,
    decimal Ieps,
    decimal Total,
    EstadoVenta Estado,
    Guid? FacturaId,
    IReadOnlyList<VentaPartidaInfo> Partidas);

public sealed record VendidoProductoLote(Guid ProductoId, string Lote, decimal CantidadBase);

/// <summary>Lo que la liquidación necesita del día de una ruta (RN-LOG-06 y RN-LOG-08).</summary>
public sealed record ResumenRutaDia(
    Guid RutaId,
    DateOnly Fecha,
    IReadOnlyList<VendidoProductoLote> Vendido,
    decimal VentasContadoEfectivo,
    decimal CobrosEfectivo,
    decimal EfectivoEsperado,
    int Ventas,
    decimal TotalVendido);

public sealed record ListaPrecioPartidaInfo(Guid ListaPrecioId, Guid ProductoId, Guid UnidadId, decimal PrecioUnitario,
    long VersionSync);

public sealed record ListaPrecioInfo(Guid Id, string Nombre, Guid? CanalId, bool Activo, long VersionSync);

public sealed record PedidoPartidaInfo(Guid Id, Guid ProductoId, Guid UnidadId, decimal FactorABase, decimal Cantidad,
    decimal CantidadBase, decimal PrecioUnitario, decimal TasaIva, decimal? TasaIeps, decimal Importe,
    decimal CantidadEntregadaBase);

public sealed record PedidoInfo(
    Guid Id,
    string Folio,
    Guid ClienteId,
    DateOnly Fecha,
    DateOnly? FechaEntrega,
    EstadoPedido Estado,
    Guid? RutaId,
    Guid? EnvioId,
    string? OrdenCompraCliente,
    decimal Subtotal,
    decimal Iva,
    decimal Ieps,
    decimal Total,
    long VersionSync,
    IReadOnlyList<PedidoPartidaInfo> Partidas);

/// <summary>Casos de uso y consultas de Ventas para Logística, Facturación y Sincronización.</summary>
public interface IVentasService
{
    /// <summary>RN-VEN-01 a 06 y 09. Idempotente por <c>OpId</c>.</summary>
    Task<Result<VentaRegistrada>> RegistrarVentaAsync(RegistrarVentaComando comando, CancellationToken ct);

    /// <summary>RN-VEN-08.</summary>
    Task<Result<VentaRegistrada>> EntregarPedidoAsync(EntregarPedidoComando comando, CancellationToken ct);

    /// <summary>[SUP-08] Idempotente por <c>OpId</c>.</summary>
    Task<Result<CobroRegistrado>> RegistrarCobroAsync(RegistrarCobroComando comando, CancellationToken ct);

    /// <summary>RN-VEN-06: deja la ubicación propuesta e incidencia <c>UbicacionPorAprobar</c>.</summary>
    Task<Result<IReadOnlyList<TipoIncidencia>>> ProponerUbicacionClienteAsync(ProponerUbicacionComando comando,
        CancellationToken ct);

    /// <summary>
    /// RN-VEN-10 / RN-LOG-31: "Público en general &lt;ruta&gt;" del canal de la ruta, con la lista de precios de ese canal
    /// (idempotente por código de ruta; si ya existe, lo alinea al canal).
    /// </summary>
    Task<Result<ClienteInfo>> CrearClienteGenericoAsync(string rutaCodigo, Guid canalId, CancellationToken ct);

    /// <summary>Canales ordenados por <c>orden</c>.</summary>
    Task<IReadOnlyList<CanalInfo>> ListarCanalesAsync(CancellationToken ct);

    Task<ClienteInfo?> ClienteGenericoDeRutaAsync(string rutaCodigo, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, ClienteInfo>> ObtenerClientesAsync(IEnumerable<Guid> ids, CancellationToken ct);

    /// <summary>Clientes activos ordenados por código (semilla y selectores de Logística).</summary>
    Task<IReadOnlyList<ClienteInfo>> ListarClientesAsync(bool incluirGenericos, CancellationToken ct);

    Task<VentaInfo?> ObtenerVentaAsync(Guid id, CancellationToken ct);

    Task<VentaInfo?> ObtenerVentaPorFolioAsync(string folio, CancellationToken ct);

    Task<IReadOnlyList<VentaInfo>> ObtenerVentasAsync(IEnumerable<Guid> ids, CancellationToken ct);

    Task<ResumenRutaDia> ResumenRutaDiaAsync(Guid rutaId, DateOnly fecha, CancellationToken ct);

    /// <summary>RN-FAC-04: ventas <c>Registrada</c>, de contado, sin factura, del periodo.</summary>
    Task<IReadOnlyList<VentaInfo>> VentasParaFacturaGlobalAsync(DateOnly desde, DateOnly hasta, CancellationToken ct);

    /// <summary>RN-FAC-01: falla con VEN-010 si alguna venta ya tiene factura vigente o no está registrada.</summary>
    Task<Result> AsignarFacturaAsync(IReadOnlyCollection<Guid> ventaIds, Guid facturaId, CancellationToken ct);

    /// <summary>Quita la factura de sus ventas (factura cancelada).</summary>
    Task LiberarFacturaAsync(Guid facturaId, CancellationToken ct);

    /// <summary>
    /// RN-LOG-11: liga pedidos `Confirmado`/`Asignado` de la misma ruta a un envío (carga) de Logística. Falla con VEN-018
    /// si alguno es de otra ruta o ya va en otra carga.
    /// </summary>
    Task<Result> VincularPedidosEnvioAsync(IReadOnlyCollection<Guid> pedidoIds, Guid envioId, Guid rutaId, CancellationToken ct);

    /// <summary>RN-LOG-11: la carga se canceló; sus pedidos pendientes quedan asignados a la ruta, sin carga.</summary>
    Task DesvincularPedidosEnvioAsync(Guid envioId, CancellationToken ct);
}

/// <summary>Para que otros módulos registren incidencias (p. ej. <c>DiferenciaLiquidacion</c>).</summary>
public interface IIncidenciasService
{
    Task<Guid> CrearAsync(TipoIncidencia tipo, string entidadTipo, Guid entidadId, object? detalle, CancellationToken ct);
}

/// <summary>Consultas para la bajada (pull) de la app: cambios con <c>version_sync &gt; desde</c>.</summary>
/// <param name="Activo">Falso = la app lo borra.</param>
public sealed record PrecioClienteInfo(Guid ClienteId, Guid ProductoId, Guid UnidadId, decimal PrecioUnitario, DateOnly VigenteDesde,
    bool Activo, long VersionSync);

public sealed record ProductoExclusivoInfo(Guid Id, Guid ProductoId, Guid? ClienteId, Guid? GrupoClienteId, bool Activo, long VersionSync);

public interface IConsultasSyncVentas
{
    /// <summary>RN-VEN-12: precios especiales (activos e inactivos) de los clientes, cambiados después de <paramref name="desde"/>.</summary>
    Task<IReadOnlyList<PrecioClienteInfo>> PreciosClientesAsync(IReadOnlyCollection<Guid> clienteIds, long desde, CancellationToken ct);

    /// <summary>
    /// RN-VEN-14: todos los registros de exclusividad cambiados después de <paramref name="desde"/>; la app necesita también
    /// los de clientes ajenos a la ruta para ocultar el producto a los demás.
    /// </summary>
    Task<IReadOnlyList<ProductoExclusivoInfo>> ProductosExclusivosAsync(long desde, CancellationToken ct);

    Task<IReadOnlyList<ClienteInfo>> ClientesAsync(IReadOnlyCollection<Guid> ids, long desde, CancellationToken ct);

    Task<IReadOnlyList<ListaPrecioInfo>> ListasPrecioAsync(IReadOnlyCollection<Guid> listaIds, long desde, CancellationToken ct);

    Task<IReadOnlyList<ListaPrecioPartidaInfo>> PartidasListasAsync(IReadOnlyCollection<Guid> listaIds, long desde,
        CancellationToken ct);

    /// <summary>Pedidos de la ruta cambiados después de <paramref name="desde"/> (cualquier estado: la app quita los cerrados).</summary>
    Task<IReadOnlyList<PedidoInfo>> PedidosDeRutaAsync(Guid rutaId, long desde, CancellationToken ct);

    /// <summary>Saldo de crédito por cliente [SUP-08].</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> SaldosClientesAsync(IReadOnlyCollection<Guid> clienteIds, CancellationToken ct);
}

public static class ErroresVentas
{
    public const string SinPrecio = "VEN-001";
    public const string ClienteNoEncontrado = "VEN-002";
    public const string ProductoNoVendible = "VEN-003";
    public const string Credito = "VEN-004";
    public const string FormaPagoInvalida = "VEN-005";
    public const string VentaNoEncontrada = "VEN-006";
    public const string VentaFacturada = "VEN-007";
    public const string FueraDeDia = "VEN-008";
    public const string FolioDuplicado = "VEN-009";
    public const string FacturaVigente = "VEN-010";
    public const string PedidoNoEncontrado = "VEN-011";
    public const string EstadoPedido = "VEN-012";
    public const string EntregaExcedida = "VEN-013";
    public const string RutaSinAlmacen = "VEN-014";
    public const string Duplicado = "VEN-015";
    public const string NoEncontrado = "VEN-016";
    public const string VentaCancelada = "VEN-017";
    public const string PedidoOtraCarga = "VEN-018";
    public const string ProductoExclusivo = "VEN-019";
}
