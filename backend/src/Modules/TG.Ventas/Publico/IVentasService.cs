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

    /// <summary>v0.2: solo en ventas de contado; no suma al efectivo de la liquidación (RN-LOG-08).</summary>
    Cheque,
}

public enum OrigenVenta
{
    App,
    Web,
}

/// <summary>RN-VEN-17.</summary>
public enum TipoPartidaPedido
{
    Venta,
    CambioFisico,
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
public sealed record PartidaPedidoComando(Guid ProductoId, Guid UnidadId, decimal Cantidad, TipoPartidaPedido Tipo, string? Notas);

/// <summary>
/// RN-SYN-10 / RN-VEN-18: pedido capturado en la app. El id lo genera la app; los precios los pone el servidor.
/// <paramref name="FechaCaptura"/> es la fecha operativa de <c>creadaEn</c> (RN-VEN-15).
/// </summary>
public sealed record CapturarPedidoComando(Guid PedidoId, Guid OpId, Guid ClienteId, DateOnly FechaEntrega, DateOnly FechaCaptura,
    string? OrdenCompraCliente, string? Notas, IReadOnlyList<PartidaPedidoComando> Partidas, Guid CapturadoPor, Guid? DispositivoId,
    Guid RutaId);

public sealed record PedidoCapturado(Guid PedidoId, string Folio, bool Duplicado);

/// <summary>Partida de un pedido no cancelado para el estimado y el plan sugerido (RN-PRO-11/13).</summary>
public sealed record DemandaPartidaInfo(Guid PedidoId, string Folio, Guid ClienteId, Guid CanalId, Guid? RutaId, bool EsRuta,
    EstadoPedido Estado, Guid ProductoId, Guid UnidadId, decimal Cantidad, decimal CantidadBase, TipoPartidaPedido Tipo, string? Notas);

/// <summary>Partida pendiente de entregar de un pedido sin carga (RN-LOG-14), en unidad base.</summary>
public sealed record PartidaParaCargaInfo(Guid ProductoId, TipoPartidaPedido Tipo, decimal PendienteBase);

/// <summary>Pedido <c>Confirmado</c>/<c>Asignado</c> sin carga para las cargas sugeridas (RN-LOG-14).</summary>
public sealed record PedidoParaCargaInfo(Guid PedidoId, string Folio, Guid ClienteId, string Cliente, Guid? RutaId, DateOnly FechaEntrega,
    IReadOnlyList<PartidaParaCargaInfo> Partidas);

/// <summary>Celda del estimado: cantidad en la unidad indicada; 0 quita la partida.</summary>
public sealed record CeldaPedidoRuta(Guid ProductoId, Guid UnidadId, decimal Cantidad);

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
    decimal TotalVendido,
    decimal TotalCredito = 0,
    decimal CobrosTotal = 0,
    decimal VentasTransferencia = 0,
    decimal CobrosTransferencia = 0,
    decimal VentasCheque = 0,
    decimal DiferenciaPrecios = 0,
    IReadOnlyList<ImporteProductoDia>? ImportePorProducto = null,
    IReadOnlyList<CreditoDelDia>? Creditos = null);

/// <summary>Lo vendido por un vendedor en un día y producto; importe con impuestos.</summary>
public sealed record VentaVendedorDia(Guid VendedorId, DateOnly Fecha, Guid ProductoId, decimal CantidadBase, decimal Importe);

/// <summary>Importe con impuestos vendido de un producto en el día (columna "Total $" de la liquidación).</summary>
public sealed record ImporteProductoDia(Guid ProductoId, decimal Importe);

/// <summary>Venta a crédito del día (hoja de liquidación, RN-LOG-22).</summary>
public sealed record CreditoDelDia(Guid VentaId, string Folio, Guid ClienteId, string Cliente, decimal Total);

public sealed record ListaPrecioPartidaInfo(Guid ListaPrecioId, Guid ProductoId, Guid UnidadId, decimal PrecioUnitario,
    long VersionSync);

public sealed record ListaPrecioInfo(Guid Id, string Nombre, Guid? CanalId, bool Activo, long VersionSync);

public sealed record PedidoPartidaInfo(Guid Id, Guid ProductoId, Guid UnidadId, decimal FactorABase, decimal Cantidad,
    decimal CantidadBase, decimal PrecioUnitario, decimal TasaIva, decimal? TasaIeps, decimal Importe,
    decimal CantidadEntregadaBase, TipoPartidaPedido Tipo = TipoPartidaPedido.Venta);

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
    IReadOnlyList<PedidoPartidaInfo> Partidas,
    bool EsRuta = false);

/// <summary>Casos de uso y consultas de Ventas para Logística, Facturación y Sincronización.</summary>
public interface IVentasService
{
    /// <summary>RN-VEN-01 a 06 y 09. Idempotente por <c>OpId</c>.</summary>
    Task<Result<VentaRegistrada>> RegistrarVentaAsync(RegistrarVentaComando comando, CancellationToken ct);

    /// <summary>RN-VEN-08.</summary>
    Task<Result<VentaRegistrada>> EntregarPedidoAsync(EntregarPedidoComando comando, CancellationToken ct);

    /// <summary>Productos con precio en las listas activas del canal (columnas del estimado, [SUP-30]).</summary>
    Task<IReadOnlyList<Guid>> ProductosDeCanalAsync(Guid canalId, CancellationToken ct);

    /// <summary>RN-PRO-11/13: partidas de los pedidos no cancelados con esa fecha de entrega (del canal, si se indica).</summary>
    Task<IReadOnlyList<DemandaPartidaInfo>> DemandaPorFechaEntregaAsync(DateOnly fechaEntrega, Guid? canalId, CancellationToken ct);

    /// <summary>
    /// RN-VEN-16 / RN-PRO-11: crea o corrige el pedido global de la ruta (al genérico <c>GEN-ruta</c>) con las celdas del
    /// estimado. Solo pedidos <c>Capturado</c> (VEN-012). Las partidas de otros productos y las de cambio físico se conservan.
    /// </summary>
    Task<Result<Guid?>> GuardarPedidoRutaAsync(Guid rutaId, string rutaCodigo, DateOnly fechaEntrega, IReadOnlyList<CeldaPedidoRuta> celdas,
        CancellationToken ct);

    /// <summary>
    /// RN-LOG-11/14: pedidos <c>Confirmado</c>/<c>Asignado</c> sin carga con entrega (<c>fecha_entrega</c>, o <c>fecha</c>)
    /// hasta <paramref name="hasta"/>, con lo pendiente de entregar de cada partida (incluye <c>CambioFisico</c>).
    /// </summary>
    Task<IReadOnlyList<PedidoParaCargaInfo>> PedidosParaCargaAsync(DateOnly hasta, CancellationToken ct);

    /// <summary>RN-VEN-13: confirma los pedidos <c>Capturado</c> indicados (los demás se ignoran). Todo o nada.</summary>
    Task<Result<int>> ConfirmarPedidosAsync(IReadOnlyCollection<Guid> pedidoIds, CancellationToken ct);

    /// <summary>RN-PRO-12 "Usar referencia": copia los pedidos como <c>Capturado</c> con otra fecha de entrega. Todo o nada.</summary>
    Task<Result<int>> CopiarPedidosAsync(IReadOnlyCollection<Guid> pedidoIds, DateOnly fechaEntrega, CancellationToken ct);

    /// <summary>RN-SYN-10, RN-VEN-15 a 18. Idempotente por <c>OpId</c> (y por el id del pedido).</summary>
    Task<Result<PedidoCapturado>> CapturarPedidoAsync(CapturarPedidoComando comando, CancellationToken ct);

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

    /// <summary>Precio vigente sin impuestos del cliente por unidad base (RN-VEN-12); falta la llave si no tiene precio.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> PreciosBaseClienteAsync(Guid clienteId, IReadOnlyCollection<Guid> productoIds, DateOnly fecha,
        CancellationToken ct);

    /// <summary>Ventas registradas por vendedor, día y producto (corte semanal, RN-LOG-28).</summary>
    Task<IReadOnlyList<VentaVendedorDia>> VentasDeVendedoresAsync(DateOnly desde, DateOnly hasta, IReadOnlyCollection<Guid>? vendedorIds,
        CancellationToken ct);

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
    public const string PedidoRutaDuplicado = "VEN-020";
    public const string FechaEntregaPasada = "VEN-021";
}
