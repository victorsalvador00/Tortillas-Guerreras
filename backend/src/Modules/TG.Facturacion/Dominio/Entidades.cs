using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;

namespace TG.Facturacion.Dominio;

public enum TipoFactura
{
    Ingreso,
    Global,
    NotaCredito,
}

public enum EstadoFactura
{
    Pendiente,
    EnviadaProveedor,
    Timbrada,
    Error,
    Cancelada,
}

public enum EstadoSolicitud
{
    Recibida,
    Rechazada,
    EnProceso,
    Timbrada,
    Error,
}

public enum TipoOutbox
{
    TimbrarFactura,
    CancelarFactura,
}

public enum EstadoOutbox
{
    Pendiente,
    EnProceso,
    Procesado,
    Error,
}

/// <summary>Datos fiscales del emisor (una sola fila).</summary>
public sealed class Emisor : EntidadAuditable
{
    public string Rfc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string RegimenFiscal { get; set; } = string.Empty;
    public string CodigoPostalExpedicion { get; set; } = string.Empty;
    public string SerieFacturas { get; set; } = "F";
    public string SerieGlobal { get; set; } = "G";
    public string SerieNotasCredito { get; set; } = "NC";

    /// <summary>La semilla carga un emisor de ejemplo; se debe reemplazar por el real antes de facturar en producción.</summary>
    public bool EsEjemplo { get; set; }
}

public sealed class Factura : EntidadAuditable, IConcurrencia
{
    public TipoFactura Tipo { get; set; }
    public string Serie { get; set; } = string.Empty;
    public string Folio { get; set; } = string.Empty;
    public Guid? UuidFiscal { get; set; }
    public Guid? ClienteId { get; set; }
    public string ReceptorRfc { get; set; } = string.Empty;
    public string ReceptorNombre { get; set; } = string.Empty;
    public string ReceptorRegimen { get; set; } = string.Empty;
    public string ReceptorCp { get; set; } = string.Empty;
    public string UsoCfdi { get; set; } = string.Empty;
    public string? EmailReceptor { get; set; }
    public string MetodoPagoSat { get; set; } = "PUE";
    public string FormaPagoSat { get; set; } = "01";
    public string? Periodicidad { get; set; }
    public string? Meses { get; set; }
    public string? Anio { get; set; }
    public DateOnly FechaOperativa { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Ieps { get; set; }
    public decimal Total { get; set; }
    public EstadoFactura Estado { get; set; } = EstadoFactura.Pendiente;
    public string Proveedor { get; set; } = "Simulado";
    public string? XmlUrl { get; set; }
    public string? PdfUrl { get; set; }
    public string? ErrorDetalle { get; set; }
    public int Intentos { get; set; }
    public DateTimeOffset? TimbradaEn { get; set; }
    public string? MotivoCancelacion { get; set; }
    public DateTimeOffset? CanceladaEn { get; set; }
    public uint Version { get; set; }
    public List<FacturaConcepto> Conceptos { get; set; } = [];
    public List<FacturaVenta> Ventas { get; set; } = [];

    public bool Vigente => Estado != EstadoFactura.Cancelada;
}

public sealed class FacturaConcepto
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid FacturaId { get; set; }
    public Guid? VentaId { get; set; }
    public Guid? ProductoId { get; set; }
    public string? NoIdentificacion { get; set; }
    public string ClaveProdServ { get; set; } = string.Empty;
    public string ClaveUnidad { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal ValorUnitario { get; set; }
    public decimal Importe { get; set; }
    public string ObjetoImp { get; set; } = "02";
    public decimal TasaIva { get; set; }
    public decimal? TasaIeps { get; set; }
    public decimal Iva { get; set; }
    public decimal Ieps { get; set; }
    public short Orden { get; set; }
}

/// <summary>Ventas incluidas en la factura (RN-FAC-01: una venta en máximo una factura vigente).</summary>
public sealed class FacturaVenta
{
    public Guid FacturaId { get; set; }
    public Guid VentaId { get; set; }
    public string VentaFolio { get; set; } = string.Empty;
}

public sealed class SolicitudAutofactura : EntidadAuditable
{
    public string FolioTicket { get; set; } = string.Empty;
    public Guid? VentaId { get; set; }
    public string Rfc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string RegimenFiscal { get; set; } = string.Empty;
    public string CodigoPostal { get; set; } = string.Empty;
    public string UsoCfdi { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? IpOrigen { get; set; }
    public EstadoSolicitud Estado { get; set; } = EstadoSolicitud.Recibida;
    public string? MotivoRechazo { get; set; }
    public Guid? FacturaId { get; set; }
}

public sealed class MensajeOutbox
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public TipoOutbox Tipo { get; set; }
    public string Payload { get; set; } = "{}";
    public Guid FacturaId { get; set; }
    public EstadoOutbox Estado { get; set; } = EstadoOutbox.Pendiente;
    public int Intentos { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? SiguienteIntentoEn { get; set; }
    public DateTimeOffset? ProcesadoEn { get; set; }
    public string? Error { get; set; }
}

/// <summary>Reglas puras de facturación.</summary>
public static class ReglasFacturacion
{
    public const int MaximoIntentos = 10;
    public const string RfcPublicoGeneral = "XAXX010101000";
    public const string NombrePublicoGeneral = "PUBLICO EN GENERAL";

    private static readonly int[] EsperasMinutos = [1, 5, 15, 60];

    /// <summary>Reintentos del outbox: 1, 5, 15 y 60 minutos (de ahí en adelante cada 60), máximo 10.</summary>
    public static DateTimeOffset? SiguienteIntento(int intentos, DateTimeOffset ahora) =>
        intentos >= MaximoIntentos
            ? null
            : ahora.AddMinutes(EsperasMinutos[Math.Min(Math.Max(intentos, 1), EsperasMinutos.Length) - 1]);

    /// <summary>
    /// RN-FAC-02 [SUP-11]: <c>fin_de_mes</c> = hasta el último día del mes de la venta; un número = días naturales
    /// después de la venta.
    /// </summary>
    public static bool AutofacturaVigente(DateOnly fechaVenta, DateOnly hoy, string limite)
    {
        if (hoy < fechaVenta)
        {
            return true;
        }

        if (int.TryParse(limite, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var dias))
        {
            return hoy <= fechaVenta.AddDays(dias);
        }

        var finDeMes = new DateOnly(fechaVenta.Year, fechaVenta.Month, DateTime.DaysInMonth(fechaVenta.Year, fechaVenta.Month));
        return hoy <= finDeMes;
    }

    /// <summary>RN-FAC-07: contado → PUE; crédito → PPD con forma "99".</summary>
    public static (string Metodo, string Forma) MetodoYForma(bool credito, bool transferencia) =>
        credito ? ("PPD", "99") : ("PUE", transferencia ? "03" : "01");

    /// <summary>Periodicidad SAT de la factura global: 01 diaria, 02 semanal, 03 quincenal, 04 mensual, 05 bimestral.</summary>
    public static string ClavePeriodicidad(string periodicidad) => periodicidad.ToLowerInvariant() switch
    {
        "diaria" => "01",
        "semanal" => "02",
        "quincenal" => "03",
        "bimestral" => "05",
        _ => "04",
    };
}
