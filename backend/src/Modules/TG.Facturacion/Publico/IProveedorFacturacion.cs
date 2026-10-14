namespace TG.Facturacion.Publico;

public sealed record EmisorParaTimbrar(string Rfc, string RazonSocial, string RegimenFiscal, string CodigoPostal);

public sealed record ReceptorParaTimbrar(string Rfc, string Nombre, string RegimenFiscal, string CodigoPostal, string UsoCfdi);

public sealed record ConceptoParaTimbrar(
    string ClaveProdServ,
    string? NoIdentificacion,
    decimal Cantidad,
    string ClaveUnidad,
    string Descripcion,
    decimal ValorUnitario,
    decimal Importe,
    string ObjetoImp,
    decimal TasaIva,
    decimal Iva,
    decimal? TasaIeps,
    decimal Ieps);

/// <summary>Datos de un CFDI 4.0 listo para timbrar (D-06: lo consume el proveedor real o el simulado).</summary>
public sealed record FacturaParaTimbrar(
    Guid FacturaId,
    string Tipo,
    string Serie,
    string Folio,
    DateTimeOffset Fecha,
    EmisorParaTimbrar Emisor,
    ReceptorParaTimbrar Receptor,
    string MetodoPago,
    string FormaPago,
    string? Periodicidad,
    string? Meses,
    string? Anio,
    decimal Subtotal,
    decimal Iva,
    decimal Ieps,
    decimal Total,
    IReadOnlyList<ConceptoParaTimbrar> Conceptos);

public sealed record ResultadoTimbrado(bool Exito, Guid? UuidFiscal, byte[]? Xml, byte[]? Pdf, DateTimeOffset? FechaTimbrado,
    string? Error);

public sealed record ResultadoCancelacion(bool Exito, string? Error);

/// <summary>
/// Integración de timbrado. <b>D-06 pendiente</b>: mientras se decide entre el agente CONTPAQi o una API en la nube,
/// solo existe <c>ProveedorFacturacionSimulado</c>.
/// </summary>
public interface IProveedorFacturacion
{
    Task<ResultadoTimbrado> TimbrarAsync(FacturaParaTimbrar factura, CancellationToken ct);

    Task<ResultadoCancelacion> CancelarAsync(Guid uuidFiscal, string motivo, Guid? sustituye, CancellationToken ct);
}
