using System.Text.Json;

namespace TG.Sincronizacion.Aplicacion;

// Contrato de 05-sincronizacion-offline.md; mismas formas que frontend/packages/shared/src/sync.ts (camelCase).

public sealed record ConversionSync(Guid UnidadId, string UnidadClave, decimal FactorABase, int Decimales, bool UsoVenta,
    bool PredeterminadaVenta);

public sealed record ProductoSync(Guid Id, string Sku, string Nombre, string Tipo, Guid UnidadBaseId, string UnidadBaseClave,
    int UnidadBaseDecimales, bool ManejaLote, decimal TasaIva, decimal? TasaIeps, bool Activo, long VersionSync,
    IReadOnlyList<ConversionSync> Conversiones);

public sealed record ClienteSync(Guid Id, string Codigo, string Nombre, string CanalClave, Guid ListaPrecioId,
    string CondicionPago, int DiasCredito, decimal? LimiteCredito, bool RequiereFactura, string? Rfc, string? RazonSocial,
    string? Telefono, string? Direccion, decimal? Latitud, decimal? Longitud, bool UbicacionAprobada, bool EsGenerico,
    bool Activo, long VersionSync, Guid? GrupoClienteId = null);

/// <summary>RN-VEN-12: gana sobre la lista desde <c>VigenteDesde</c>. <c>Activo = false</c>: la app lo borra.</summary>
public sealed record PrecioClienteSync(Guid ClienteId, Guid ProductoId, Guid UnidadId, decimal PrecioUnitario, DateOnly VigenteDesde,
    bool Activo, long VersionSync);

/// <summary>RN-VEN-14: el producto solo se ofrece al cliente o a los clientes del grupo. Los inactivos van en eliminados.</summary>
public sealed record ProductoExclusivoSync(Guid Id, Guid ProductoId, Guid? ClienteId, Guid? GrupoClienteId, long VersionSync);

public sealed record RutaClienteSync(Guid RutaId, Guid ClienteId, int OrdenVisita, IReadOnlyList<int> DiasVisita, bool Activo,
    long VersionSync);

public sealed record PrecioSync(Guid ListaPrecioId, Guid ProductoId, Guid UnidadId, decimal PrecioUnitario, long VersionSync);

public sealed record EnvioPartidaSync(Guid Id, Guid ProductoId, string Lote, decimal CantidadBase);

public sealed record EnvioSync(Guid Id, string Folio, DateOnly FechaOperativa, string Tipo, string Estado,
    DateTimeOffset? DespachadoEn, long VersionSync, IReadOnlyList<EnvioPartidaSync> Partidas);

/// <param name="Tipo">RN-VEN-17: <c>Venta</c> o <c>CambioFisico</c> (precio 0; se entrega con un retorno).</param>
public sealed record PedidoPartidaSync(Guid Id, Guid ProductoId, Guid UnidadId, decimal Cantidad, decimal CantidadBase,
    decimal PrecioUnitario, decimal TasaIva, decimal? TasaIeps, decimal CantidadEntregadaBase, string Tipo = "Venta");

/// <summary>Pedido por entregar; <c>EnvioId</c>/<c>EnvioFolio</c>: la carga en la que va (RN-LOG-11), nulos si no va en ninguna.</summary>
public sealed record PedidoSync(Guid Id, string Folio, Guid ClienteId, DateOnly? FechaEntrega, string Estado, decimal Total,
    Guid? EnvioId, string? EnvioFolio, long VersionSync, IReadOnlyList<PedidoPartidaSync> Partidas, bool EsRuta = false);

public sealed record SaldoClienteSync(Guid ClienteId, decimal Saldo);

public sealed record MotivoMermaSync(Guid Id, string Clave, string Nombre, bool AplicaRuta, bool AplicaRetorno, bool Activo,
    long VersionSync);

public sealed record ParametroSync(string Clave, string Valor, long VersionSync);

public sealed record CambiosSync(
    IReadOnlyList<ProductoSync> Productos,
    IReadOnlyList<ClienteSync> Clientes,
    IReadOnlyList<RutaClienteSync> RutasClientes,
    IReadOnlyList<PrecioSync> Precios,
    IReadOnlyList<EnvioSync> Envios,
    IReadOnlyList<PedidoSync> Pedidos,
    IReadOnlyList<SaldoClienteSync> SaldosClientes,
    IReadOnlyList<MotivoMermaSync> MotivosMerma,
    IReadOnlyList<ParametroSync> Parametros,
    IReadOnlyDictionary<string, IReadOnlyList<Guid>> Eliminados,
    IReadOnlyList<PrecioClienteSync>? PreciosCliente = null,
    IReadOnlyList<ProductoExclusivoSync>? ProductosExclusivos = null);

public sealed record RutaSync(Guid Id, string Codigo, string Nombre, Guid AlmacenId);

/// <param name="RutaCambiada">
/// RN-SYN-09: la ruta del alcance ya no es la que la app tiene (<c>rutaId</c>). Con <c>desde &gt; 0</c> no trae cambios y
/// el cursor vuelve a 0: la app borra las colecciones por ruta y baja todo de nuevo, conservando su cola y su consecutivo.
/// </param>
public sealed record RespuestaPull(long Cursor, bool HayMas, DateTimeOffset FechaServidor, RutaSync? Ruta, CambiosSync Cambios,
    bool RutaCambiada = false);

public sealed record EstadoDispositivoServidor(Guid DispositivoId, string Codigo, bool Activo, Guid? RutaId, string? RutaCodigo,
    int UltimoConsecutivo);

public sealed record OperacionSync(Guid OpId, long Secuencia, string Tipo, DateTimeOffset CreadaEn, JsonElement Datos);

public sealed record SolicitudPush(Guid DispositivoId, Guid LoteId, string? VersionApp, IReadOnlyList<OperacionSync> Operaciones);

public sealed record ResultadoOperacion(Guid OpId, string Estado, IReadOnlyDictionary<string, Guid>? IdsServidor = null,
    IReadOnlyList<string>? Incidencias = null, string? Codigo = null, string? Mensaje = null);

public sealed record RespuestaPush(IReadOnlyList<ResultadoOperacion> Resultados, int PendientesServidor);

// Datos de cada tipo de operación.

public sealed record GpsDatos(decimal Lat, decimal Lng, decimal? PrecisionM, DateTimeOffset? TomadoEn);

public sealed record PartidaVentaDatos(Guid ProductoId, Guid UnidadId, decimal Cantidad, decimal PrecioUnitario);

public sealed record RegistrarVentaDatos(Guid VentaId, string Folio, int Consecutivo, Guid ClienteId, string CondicionPago,
    string FormaPago, IReadOnlyList<PartidaVentaDatos> Partidas, decimal TotalApp, GpsDatos? Gps);

public sealed record PartidaEntregaDatos(Guid PedidoPartidaId, decimal Cantidad);

public sealed record EntregarPedidoDatos(Guid VentaId, string Folio, int Consecutivo, Guid PedidoId, string FormaPago,
    IReadOnlyList<PartidaEntregaDatos> Partidas, decimal TotalApp, GpsDatos? Gps);

public sealed record PartidaRetornoDatos(Guid ProductoId, decimal Cantidad, string Condicion, Guid? MotivoMermaId);

public sealed record RegistrarRetornoDatos(Guid RetornoId, string Folio, int Consecutivo, string Tipo, Guid? ClienteId,
    Guid? VentaId, Guid? PedidoId, IReadOnlyList<PartidaRetornoDatos> Partidas, GpsDatos? Gps);

public sealed record RegistrarCobroDatos(Guid CobroId, Guid ClienteId, decimal Importe, string FormaPago, IReadOnlyList<Guid>? VentaIds);

public sealed record PartidaPedidoDatos(Guid ProductoId, Guid UnidadId, decimal Cantidad, string Tipo, string? Notas);

/// <param name="EsRuta">Informativo: el servidor decide si es pedido de ruta (cliente genérico de un canal por ruta, RN-VEN-16).</param>
public sealed record CapturarPedidoDatos(Guid PedidoId, Guid ClienteId, DateOnly FechaEntrega, string? OrdenCompraCliente, bool EsRuta,
    IReadOnlyList<PartidaPedidoDatos> Partidas, string? Notas, GpsDatos? Gps);

public sealed record ProponerUbicacionDatos(Guid ClienteId, GpsDatos Gps);

public sealed record ReportarEstadoDatos(int Pendientes, decimal? Bateria, string? VersionApp, int UltimoConsecutivo);

public static class TiposOperacion
{
    public const string RegistrarVenta = "RegistrarVenta";
    public const string EntregarPedido = "EntregarPedido";
    public const string RegistrarRetorno = "RegistrarRetorno";
    public const string RegistrarCobro = "RegistrarCobro";
    public const string ProponerUbicacionCliente = "ProponerUbicacionCliente";
    public const string ReportarEstado = "ReportarEstado";
    public const string CapturarPedido = "CapturarPedido";
}

public static class ErroresSync
{
    public const string DispositivoNoAutorizado = "SYN-001";
    /// <summary>RN-SYN-09: el chofer no tiene carga ni ruta predeterminada en la fecha de la operación (v0.1: SYN-002).</summary>
    public const string SinRuta = "SYN-006";
    public const string LoteDemasiadoGrande = "SYN-003";
    public const string TipoDesconocido = "SYN-004";
    public const string DatosInvalidos = "SYN-005";
    /// <summary>RN-SYN-10: el cliente no pertenece a la ruta de la operación.</summary>
    public const string ClienteFueraDeRuta = "SYN-007";
    /// <summary>RN-SYN-10: falta el permiso <c>ventas.pedidos_app</c>.</summary>
    public const string SinPermisoPedidos = "SYN-008";

    /// <summary>RN-SYN-07: la app parte lotes más grandes.</summary>
    public const int MaximoOperacionesLote = 200;
}
