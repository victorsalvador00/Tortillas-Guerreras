using TG.SharedKernel.Resultados;

namespace TG.Logistica.Publico;

public enum TipoEnvio
{
    CargaInicial,
    Recarga,
}

public enum EstadoEnvio
{
    Preparado,
    EnRuta,
    Liquidado,
    Cancelado,
}

public enum TipoRetorno
{
    DevolucionCliente,
    RechazoEntrega,
}

public enum CondicionRetorno
{
    Vendible,
    NoVendible,
}

public enum EstadoRetorno
{
    Registrado,
    Cancelado,
}

public enum EstadoLiquidacion
{
    Abierta,
    Cerrada,
}

public enum DestinoSobrante
{
    Reintegrar,
    Merma,
    Reproceso,
}

public sealed record RutaInfo(Guid Id, string Codigo, string Nombre, Guid AlmacenId, Guid? VendedorId, Guid? VehiculoId,
    bool Activo, long VersionSync);

/// <param name="Activo">Falso = la app debe quitarlo (eliminado del alcance).</param>
public sealed record RutaClienteInfo(Guid RutaId, Guid ClienteId, short OrdenVisita, IReadOnlyList<short> DiasVisita,
    bool Activo, long VersionSync);

public sealed record EnvioPartidaInfo(Guid Id, Guid ProductoId, string Lote, decimal CantidadBase, long VersionSync);

public sealed record EnvioInfo(Guid Id, string Folio, DateOnly FechaOperativa, Guid RutaId, TipoEnvio Tipo,
    EstadoEnvio Estado, DateTimeOffset? DespachadoEn, long VersionSync, IReadOnlyList<EnvioPartidaInfo> Partidas);

/// <param name="Lote">Opcional: si falta se usa el lote de la venta original o el más reciente de la camioneta.</param>
/// <param name="Condicion">Opcional: [SUP-13] por defecto, producto terminado perecedero (vida útil ≤ 2 días) = NoVendible.</param>
/// <param name="UnidadId">Opcional: si falta, la cantidad viene en unidad base.</param>
public sealed record PartidaRetornoComando(Guid ProductoId, decimal Cantidad, Guid? UnidadId = null, string? Lote = null,
    CondicionRetorno? Condicion = null, Guid? MotivoMermaId = null);

/// <summary>Retorno compartido web/app (RN-LOG-04/05). Idempotente por <c>OpId</c> (y por <c>RetornoId</c>).</summary>
/// <param name="Folio">Folio de la app (<c>R01-D03-000045</c>); null en web = <c>RET-000001</c>.</param>
public sealed record RegistrarRetornoComando(
    TipoRetorno Tipo,
    Guid RutaId,
    DateOnly FechaOperativa,
    Guid UsuarioId,
    IReadOnlyList<PartidaRetornoComando> Partidas,
    Guid? RetornoId = null,
    Guid? OpId = null,
    string? Folio = null,
    Guid? ClienteId = null,
    Guid? VentaId = null,
    Guid? DispositivoId = null,
    DateTimeOffset? FechaHoraDispositivo = null,
    decimal? Latitud = null,
    decimal? Longitud = null,
    decimal? PrecisionM = null,
    string? Notas = null);

public sealed record RetornoRegistrado(Guid RetornoId, string Folio, bool Duplicado);

public sealed record ResumenLogistica(DateOnly Fecha, int LiquidacionesAbiertasHoy, int LiquidacionesAbiertasAtrasadas,
    int EnviosPreparados, int EnviosEnRuta, int RutasActivas);

/// <summary>Lo que Sincronización y el tablero usan de Logística.</summary>
public interface ILogisticaService
{
    /// <summary>Ruta activa asignada al vendedor (alcance del dispositivo).</summary>
    Task<RutaInfo?> RutaDeVendedorAsync(Guid usuarioId, CancellationToken ct);

    Task<RutaInfo?> ObtenerRutaAsync(Guid id, CancellationToken ct);

    Task<RutaInfo?> ObtenerRutaPorCodigoAsync(string codigo, CancellationToken ct);

    Task<IReadOnlyList<RutaInfo>> ListarRutasAsync(CancellationToken ct);

    /// <summary>Clientes de la ruta con <c>version_sync &gt; desde</c>, incluidos los inactivos (eliminados).</summary>
    Task<IReadOnlyList<RutaClienteInfo>> ClientesDeRutaAsync(Guid rutaId, long desde, CancellationToken ct);

    /// <summary>
    /// Envíos de la ruta en estado <c>EnRuta</c> o <c>Liquidado</c> (la app quita los liquidados) de la fecha operativa y
    /// el día anterior, cambiados después de <paramref name="desdeVersion"/>.
    /// </summary>
    Task<IReadOnlyList<EnvioInfo>> EnviosDeRutaAsync(Guid rutaId, long desdeVersion, DateOnly fechaOperativa, CancellationToken ct);

    Task<bool> LiquidacionCerradaAsync(Guid rutaId, DateOnly fecha, CancellationToken ct);

    Task<Result<RetornoRegistrado>> RegistrarRetornoAsync(RegistrarRetornoComando comando, CancellationToken ct);

    Task<ResumenLogistica> ResumenTableroAsync(DateOnly fecha, CancellationToken ct);
}

/// <summary>
/// RN-LOG-09: operaciones de la app pendientes de sincronizar para la ruta. Logística trae una implementación por
/// defecto (pendientes reportados por los dispositivos del vendedor); Sincronización puede reemplazarla.
/// </summary>
public interface IEstadoSincronizacionRuta
{
    Task<int> OperacionesPendientesAsync(Guid rutaId, CancellationToken ct);
}

public static class ErroresLogistica
{
    public const string VehiculoNoEncontrado = "LOG-001";
    public const string RutaNoEncontrada = "LOG-002";
    public const string CodigoDuplicado = "LOG-003";
    public const string VendedorInvalido = "LOG-004";
    public const string EnvioNoEncontrado = "LOG-005";
    public const string EstadoEnvio = "LOG-006";
    public const string LiquidacionCerrada = "LOG-007";
    public const string RetornoNoEncontrado = "LOG-008";
    public const string FolioDuplicado = "LOG-009";
    public const string MotivoInvalido = "LOG-010";
    public const string LiquidacionNoEncontrada = "LOG-011";
    public const string SinConteo = "LOG-012";
    public const string PendientesSync = "LOG-013";
    public const string AlmacenNoQuedoEnCero = "LOG-014";
    public const string ProductoInvalido = "LOG-015";
    public const string ClienteInvalido = "LOG-016";
}
