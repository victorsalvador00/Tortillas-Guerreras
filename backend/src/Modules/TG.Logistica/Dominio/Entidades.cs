using TG.Logistica.Publico;
using TG.Ventas.Publico;
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
    public SentidoRetorno Sentido { get; set; } = SentidoRetorno.Retirado;
    public Guid? CambioPendienteId { get; set; }
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

    /// <summary>RN-LOG-19: copiado del canal de la ruta al abrirse.</summary>
    public FormatoLiquidacion Formato { get; set; } = FormatoLiquidacion.Completo;

    public decimal EfectivoEsperado { get; set; }
    public decimal? EfectivoEntregado { get; set; }
    public decimal? DiferenciaEfectivo { get; set; }
    public decimal? TotalVenta { get; set; }
    public decimal? TotalCredito { get; set; }
    public decimal? TotalCobros { get; set; }
    public decimal? TotalTransferencias { get; set; }
    public decimal? TotalCheques { get; set; }
    public decimal TotalGastos { get; set; }
    public decimal DiferenciaPrecios { get; set; }
    public Guid? ProductoConfirmadoPor { get; set; }
    public DateTimeOffset? ProductoConfirmadoEn { get; set; }
    public Guid? RevisadaPor { get; set; }
    public DateTimeOffset? RevisadaEn { get; set; }
    public Guid? CerradaPor { get; set; }
    public DateTimeOffset? CerradaEn { get; set; }
    public bool CerradaForzada { get; set; }
    public string? Notas { get; set; }
    public uint Version { get; set; }
    public List<LiquidacionPartida> Partidas { get; set; } = [];
    public List<LiquidacionGasto> Gastos { get; set; } = [];
}

/// <summary>RN-LOG-25: cargo a la cuenta del chofer; positivo = se le cobra.</summary>
[Bitacora]
public sealed class CargoChofer : EntidadAuditable, IConcurrencia
{
    public string Folio { get; set; } = string.Empty;
    public Guid ChoferId { get; set; }
    public TipoCargoChofer Tipo { get; set; }
    public Guid? LiquidacionId { get; set; }
    public Guid? ProductoId { get; set; }
    public decimal? CantidadBase { get; set; }
    public decimal? PrecioUnitario { get; set; }
    public decimal Importe { get; set; }
    public DateOnly FechaOperativa { get; set; }
    public EstadoCargoChofer Estado { get; set; } = EstadoCargoChofer.Pendiente;
    public Guid? CorteId { get; set; }
    public string? Notas { get; set; }
    public Guid? CanceladoPor { get; set; }
    public DateTimeOffset? CanceladoEn { get; set; }
    public string? MotivoCancelacion { get; set; }
    public uint Version { get; set; }
}

/// <summary>RN-LOG-28: corte semanal del chofer.</summary>
[Bitacora]
public sealed class CorteChofer : EntidadAuditable, IConcurrencia
{
    public string Folio { get; set; } = string.Empty;
    public Guid ChoferId { get; set; }
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    public decimal TotalVenta { get; set; }
    public decimal TotalCargos { get; set; }
    public EstadoCorteChofer Estado { get; set; } = EstadoCorteChofer.Abierto;
    public Guid? CerradoPor { get; set; }
    public DateTimeOffset? CerradoEn { get; set; }
    public uint Version { get; set; }
}

/// <summary>Gasto de ruta (diésel, casetas, maniobras…); los autorizados se restan del efectivo esperado (RN-LOG-08).</summary>
public sealed class LiquidacionGasto
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid LiquidacionId { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public decimal Importe { get; set; }
    public string? Comprobante { get; set; }
    public bool Autorizado { get; set; }
    public Guid CapturadoPor { get; set; }
    public DateTimeOffset CapturadoEn { get; set; }
}

[Bitacora(Documento = nameof(LiquidacionId))]
public sealed class LiquidacionPartida
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid LiquidacionId { get; set; }
    public Guid ProductoId { get; set; }
    public string Lote { get; set; } = string.Empty;
    public decimal Cargado { get; set; }

    /// <summary>RN-LOG-06 (v0.2): <c>cargado = cargado_inicial + recargado</c>.</summary>
    public decimal CargadoInicial { get; set; }

    public decimal Recargado { get; set; }
    public decimal Vendido { get; set; }

    /// <summary>Columna "Total $": importe con impuestos de lo vendido del producto, repartido entre lotes.</summary>
    public decimal ImporteVenta { get; set; }

    /// <summary>RN-LOG-24: capturada por almacén; <c>null</c> = aún no capturada (se usa la sugerida).</summary>
    public decimal? CantidadACobro { get; set; }

    public decimal? PrecioACobro { get; set; }

    /// <summary>RN-LOG-24: sugerencia por antigüedad del lote (calculada, no se guarda).</summary>
    public decimal CantidadACobroSugerida { get; set; }

    /// <summary>La partida admite producto a cobro (formato, canal, producto y devuelto no vendible).</summary>
    public bool AplicaACobro { get; set; }
    public decimal DevueltoClientesVendible { get; set; }
    public decimal DevueltoClientesNoVendible { get; set; }

    /// <summary>RN-LOG-18: salidas <c>SalidaCambio</c> del día.</summary>
    public decimal CambiosEntregados { get; set; }

    public decimal SobranteEsperado { get; set; }
    public decimal? SobranteContado { get; set; }
    public decimal? Diferencia { get; set; }
    public DestinoSobrante DestinoSobrante { get; set; } = DestinoSobrante.Reintegrar;
    public Guid? MotivoMermaId { get; set; }
}

/// <summary>RN-LOG-16/17: cambio que se le debe a un cliente; se salda con entregas posteriores.</summary>
public sealed class CambioPendiente : EntidadAuditable, IVersionSync, IConcurrencia
{
    public Guid ClienteId { get; set; }
    public Guid RutaId { get; set; }
    public Guid ProductoId { get; set; }
    public decimal CantidadBase { get; set; }
    public decimal CantidadSaldadaBase { get; set; }
    public Guid RetornoOrigenId { get; set; }
    public DateOnly FechaOperativa { get; set; }
    public EstadoCambioPendiente Estado { get; set; } = EstadoCambioPendiente.Pendiente;
    public Guid? CanceladoPor { get; set; }
    public DateTimeOffset? CanceladoEn { get; set; }
    public string? MotivoCancelacion { get; set; }
    public long VersionSync { get; set; }
    public uint Version { get; set; }
}

public static class ReglasLogistica
{
    /// <summary>RN-LOG-06/18: lo no vendible ya salió como merma; los cambios entregados salieron de la camioneta.</summary>
    public static decimal SobranteEsperado(decimal cargado, decimal vendido, decimal devueltoVendible, decimal cambiosEntregados = 0) =>
        cargado - vendido - cambiosEntregados + devueltoVendible;

    /// <summary>
    /// RN-LOG-16: lo retirado que no se entregó (sin contar lo que saldó pendientes anteriores), limitado a lo retirado con
    /// "dejar pendiente".
    /// </summary>
    public static decimal NuevoPendiente(decimal retirado, decimal retiradoDejarPendiente, decimal entregadoNormal) =>
        Math.Max(0, Math.Min(retirado - entregadoNormal, retiradoDejarPendiente));

    public static decimal Diferencia(decimal contado, decimal esperado) => contado - esperado;

    /// <summary>RN-LOG-08 (v0.2): ventas de contado en efectivo + cobros en efectivo − gastos autorizados.</summary>
    public static decimal EfectivoEsperado(decimal ventasContadoEfectivo, decimal cobrosEfectivo, decimal gastosAutorizados) =>
        Dinero.Redondear(ventasContadoEfectivo + cobrosEfectivo - gastosAutorizados);

    /// <summary>RN-LOG-20: siguiente etapa válida para cerrar el dinero.</summary>
    public static bool PuedeCerrar(EstadoLiquidacion estado, bool requiereRevision) =>
        estado == EstadoLiquidacion.Revisada || (estado == EstadoLiquidacion.ProductoConfirmado && !requiereRevision);

    /// <summary>RN-LOG-25: <c>importe = round(cantidad × precio, 2)</c>.</summary>
    public static decimal ImporteCargo(decimal cantidad, decimal precio) => Dinero.Redondear(cantidad * precio);

    /// <summary>
    /// RN-LOG-24: días del lote a la fecha de operación. Lote <c>AAMMDD…</c>; si no trae fecha,
    /// <c>fecha_caducidad − vida_util_dias</c>; <c>null</c> si no se puede saber.
    /// </summary>
    public static int? AntiguedadLote(string lote, DateOnly? fechaCaducidad, int? vidaUtilDias, DateOnly fechaOperacion)
    {
        if (lote.Length >= 6 && lote[..6].All(char.IsAsciiDigit)
            && DateOnly.TryParseExact(lote[..6], "yyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var fechaLote))
        {
            return fechaOperacion.DayNumber - fechaLote.DayNumber;
        }

        return fechaCaducidad is { } caducidad && vidaUtilDias is { } vida
            ? fechaOperacion.DayNumber - caducidad.AddDays(-vida).DayNumber
            : null;
    }

    /// <summary>RN-LOG-24: se propone todo lo devuelto no vendible del lote si su antigüedad supera la tolerancia.</summary>
    public static decimal ACobroSugerido(decimal devueltoNoVendible, int? antiguedad, int tolerancia) =>
        antiguedad is { } dias && dias > tolerancia ? devueltoNoVendible : 0;

    /// <summary>RN-LOG-28: inicio de la semana del corte (<c>diaInicio</c> 1 = lunes … 7 = domingo).</summary>
    public static DateOnly InicioSemana(DateOnly fecha, int diaInicio)
    {
        var dia = fecha.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)fecha.DayOfWeek;
        return fecha.AddDays(-((dia - Math.Clamp(diaInicio, 1, 7) + 7) % 7));
    }

    /// <summary>Reparte un importe entre lotes en proporción a lo vendido; el redondeo va al último.</summary>
    public static IReadOnlyList<decimal> Repartir(decimal importe, IReadOnlyList<decimal> pesos)
    {
        var total = pesos.Sum();
        if (pesos.Count == 0)
        {
            return [];
        }

        var partes = pesos.Select(p => total == 0 ? 0 : Dinero.Redondear(importe * p / total)).ToList();
        partes[^1] += Dinero.Redondear(importe - partes.Sum());
        return partes;
    }

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
