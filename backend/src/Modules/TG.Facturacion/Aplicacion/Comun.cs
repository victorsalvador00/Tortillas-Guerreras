using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Facturacion.Dominio;
using TG.Facturacion.Infraestructura;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Resultados;
using TG.Ventas.Publico;

namespace TG.Facturacion.Aplicacion;

public static class ErroresFacturacion
{
    public static readonly Error VentaFacturada =
        Error.Conflicto("FAC-001", "Alguna de las ventas ya está en una factura vigente o no está registrada.");

    public static Error TicketNoFacturable(string motivo) => new("FAC-002", motivo, TipoError.Negocio);

    public static readonly Error TicketNoEncontrado =
        Error.NoEncontrado("FAC-002", "No encontramos un ticket con ese folio y total. Revisa los datos del ticket.");

    public static readonly Error SinVentasGlobal =
        new("FAC-004", "No hay ventas de contado sin facturar en el periodo.", TipoError.Negocio);

    public static readonly Error EnGlobal =
        new("FAC-005", "El ticket ya se incluyó en la factura global del periodo; ya no se puede autofacturar.", TipoError.Negocio);

    public static Error SinClaveSat(string producto) =>
        new("FAC-006", $"El producto {producto} no tiene clave de producto o servicio del SAT; captúrala en Catálogos.", TipoError.Negocio);

    public static Error DatosReceptor(string mensaje) => Error.Validacion("FAC-007", mensaje);

    public static readonly Error CaptchaInvalido = Error.Campo("FAC-008", "captchaRespuesta", "La respuesta de verificación no es correcta.");

    public static readonly Error FacturaNoEncontrada = Error.NoEncontrado("FAC-009", "La factura no existe.");

    public static Error EstadoInvalido(string mensaje) => Error.Conflicto("FAC-010", mensaje);

    public static readonly Error SinEmisor = new("FAC-011", "Configura los datos del emisor antes de facturar.", TipoError.Negocio);

    public static readonly Error SolicitudNoEncontrada = Error.NoEncontrado("FAC-012", "La solicitud no existe.");

    public static readonly Error ArchivoNoDisponible = Error.NoEncontrado("FAC-013", "El archivo todavía no está disponible.");

    public static Error CancelacionRechazada(string motivo) => new("FAC-014", motivo, TipoError.Negocio);
}

/// <summary>Catálogos del SAT para validar los datos fiscales capturados (RN-FAC-03).</summary>
public static class CatalogosSat
{
    public static readonly IReadOnlySet<string> Regimenes = new HashSet<string>
    {
        "601", "603", "605", "606", "607", "608", "610", "611", "612", "614", "615", "616", "620", "621", "622", "623",
        "624", "625", "626",
    };

    public static readonly IReadOnlySet<string> UsosCfdi = new HashSet<string>
    {
        "G01", "G02", "G03", "I01", "I02", "I03", "I04", "I05", "I06", "I07", "I08", "D01", "D02", "D03", "D04", "D05",
        "D06", "D07", "D08", "D09", "D10", "S01", "CP01", "CN01",
    };

    public static readonly IReadOnlySet<string> MotivosCancelacion = new HashSet<string> { "01", "02", "03", "04" };

    /// <summary>RFC de persona moral (12) o física (13) con formato válido.</summary>
    public static bool RfcValido(string? rfc) =>
        rfc is not null && System.Text.RegularExpressions.Regex.IsMatch(rfc.Trim().ToUpperInvariant(),
            @"^([A-ZÑ&]{3,4})(\d{2})(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])([A-Z\d]{2})([A\d])$");
}

public sealed record ReceptorRequest(string Rfc, string Nombre, string RegimenFiscal, string CodigoPostal, string UsoCfdi);

public sealed class ReceptorValidator : AbstractValidator<ReceptorRequest>
{
    public ReceptorValidator()
    {
        RuleFor(x => x.Rfc).Must(CatalogosSat.RfcValido).WithMessage("El RFC no tiene un formato válido (12 o 13 caracteres).");
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Escribe la razón social tal como aparece en la constancia fiscal.");
        RuleFor(x => x.RegimenFiscal).Must(r => CatalogosSat.Regimenes.Contains(r)).WithMessage("Elige un régimen fiscal del catálogo del SAT.");
        RuleFor(x => x.CodigoPostal).Matches(@"^\d{5}$").WithMessage("El código postal tiene 5 dígitos.");
        RuleFor(x => x.UsoCfdi).Must(u => CatalogosSat.UsosCfdi.Contains(u)).WithMessage("Elige un uso de CFDI del catálogo del SAT.");
    }
}

/// <summary>Arma la factura (conceptos, totales, folio y outbox) a partir de ventas.</summary>
public sealed class ConstructorFacturas(FacturacionDbContext db, ICatalogosService catalogos, TimeProvider reloj)
{
    public async Task<Emisor?> EmisorAsync(CancellationToken ct) => await db.Emisores.AsNoTracking().FirstOrDefaultAsync(ct);

    /// <summary>Factura de ingreso: un concepto por partida de venta (RN-FAC-06).</summary>
    public async Task<Result<Factura>> IngresoAsync(Emisor emisor, IReadOnlyList<VentaInfo> ventas, ReceptorRequest receptor,
        Guid? clienteId, string? email, DateOnly fechaOperativa, CancellationToken ct)
    {
        var productos = await catalogos.ObtenerProductosAsync(ventas.SelectMany(v => v.Partidas).Select(p => p.ProductoId), ct);
        var unidades = await catalogos.ObtenerUnidadesAsync(ct);
        var factura = Nueva(TipoFactura.Ingreso, emisor.SerieFacturas, receptor, clienteId, email, fechaOperativa);
        var credito = ventas.Any(v => v.CondicionPago == CondicionPago.Credito);
        var transferencia = ventas.All(v => v.FormaPago == FormaPago.Transferencia);
        (factura.MetodoPagoSat, factura.FormaPagoSat) = ReglasFacturacion.MetodoYForma(credito, transferencia);

        short orden = 0;
        foreach (var venta in ventas)
        {
            foreach (var p in venta.Partidas)
            {
                var producto = productos.GetValueOrDefault(p.ProductoId);
                if (producto?.ClaveProdServSat is not { Length: > 0 } clave)
                {
                    return ErroresFacturacion.SinClaveSat(producto?.Nombre ?? p.ProductoId.ToString());
                }

                factura.Conceptos.Add(new FacturaConcepto
                {
                    FacturaId = factura.Id,
                    VentaId = venta.Id,
                    ProductoId = p.ProductoId,
                    NoIdentificacion = producto.Sku,
                    ClaveProdServ = clave,
                    ClaveUnidad = unidades.GetValueOrDefault(p.UnidadId)?.ClaveSat ?? producto.UnidadBaseClaveSat,
                    Cantidad = p.Cantidad,
                    Descripcion = producto.Nombre,
                    ValorUnitario = p.PrecioUnitario,
                    Importe = p.Importe,
                    ObjetoImp = producto.ObjetoImpuestoSat,
                    TasaIva = p.TasaIva,
                    TasaIeps = p.TasaIeps,
                    Iva = p.Iva,
                    Ieps = p.Ieps,
                    Orden = orden++,
                });
            }

            factura.Ventas.Add(new FacturaVenta { FacturaId = factura.Id, VentaId = venta.Id, VentaFolio = venta.Folio });
        }

        Totalizar(factura);
        factura.Folio = await SiguienteFolioAsync("ingreso", ct);
        return factura;
    }

    /// <summary>
    /// RN-FAC-04: un concepto por ticket (clave 01010101, unidad ACT, cantidad 1). Si un ticket mezcla tasas se parte en
    /// un concepto por tasa con el mismo <c>NoIdentificacion</c> (el folio del ticket).
    /// </summary>
    public async Task<Factura> GlobalAsync(Emisor emisor, IReadOnlyList<VentaInfo> ventas, DateOnly hasta, string periodicidad,
        DateOnly fechaOperativa, CancellationToken ct)
    {
        var receptor = new ReceptorRequest(ReglasFacturacion.RfcPublicoGeneral, ReglasFacturacion.NombrePublicoGeneral, "616",
            emisor.CodigoPostalExpedicion, "S01");
        var factura = Nueva(TipoFactura.Global, emisor.SerieGlobal, receptor, null, null, fechaOperativa);
        factura.MetodoPagoSat = "PUE";
        factura.FormaPagoSat = ventas.All(v => v.FormaPago == FormaPago.Transferencia) ? "03" : "01";
        factura.Periodicidad = ReglasFacturacion.ClavePeriodicidad(periodicidad);
        factura.Meses = hasta.Month.ToString("00", CultureInfo.InvariantCulture);
        factura.Anio = hasta.Year.ToString(CultureInfo.InvariantCulture);

        short orden = 0;
        foreach (var venta in ventas.OrderBy(v => v.FechaOperativa).ThenBy(v => v.Folio))
        {
            foreach (var grupo in venta.Partidas.GroupBy(p => (p.TasaIva, p.TasaIeps)))
            {
                var importe = grupo.Sum(p => p.Importe);
                factura.Conceptos.Add(new FacturaConcepto
                {
                    FacturaId = factura.Id,
                    VentaId = venta.Id,
                    NoIdentificacion = venta.Folio,
                    ClaveProdServ = "01010101",
                    ClaveUnidad = "ACT",
                    Cantidad = 1,
                    Descripcion = "Venta",
                    ValorUnitario = importe,
                    Importe = importe,
                    ObjetoImp = "02",
                    TasaIva = grupo.Key.TasaIva,
                    TasaIeps = grupo.Key.TasaIeps,
                    Iva = grupo.Sum(p => p.Iva),
                    Ieps = grupo.Sum(p => p.Ieps),
                    Orden = orden++,
                });
            }

            factura.Ventas.Add(new FacturaVenta { FacturaId = factura.Id, VentaId = venta.Id, VentaFolio = venta.Folio });
        }

        Totalizar(factura);
        factura.Folio = await SiguienteFolioAsync("global", ct);
        return factura;
    }

    /// <summary>Agrega la factura y su mensaje <c>TimbrarFactura</c> al outbox (misma transacción).</summary>
    public void Encolar(Factura factura)
    {
        var ahora = reloj.GetUtcNow();
        db.Facturas.Add(factura);
        db.Outbox.Add(new MensajeOutbox
        {
            Tipo = TipoOutbox.TimbrarFactura,
            FacturaId = factura.Id,
            Payload = $"{{\"facturaId\":\"{factura.Id}\"}}",
            CreadoEn = ahora,
            SiguienteIntentoEn = ahora,
        });
    }

    private static Factura Nueva(TipoFactura tipo, string serie, ReceptorRequest receptor, Guid? clienteId, string? email,
        DateOnly fechaOperativa) => new()
        {
            Tipo = tipo,
            Serie = serie,
            ClienteId = clienteId,
            ReceptorRfc = receptor.Rfc.Trim().ToUpperInvariant(),
            ReceptorNombre = receptor.Nombre.Trim().ToUpperInvariant(),
            ReceptorRegimen = receptor.RegimenFiscal,
            ReceptorCp = receptor.CodigoPostal,
            UsoCfdi = receptor.UsoCfdi,
            EmailReceptor = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            FechaOperativa = fechaOperativa,
            Proveedor = ProveedorFacturacionSimulado.Nombre,
        };

    private static void Totalizar(Factura f)
    {
        f.Subtotal = Dinero.Redondear(f.Conceptos.Sum(c => c.Importe));
        f.Iva = Dinero.Redondear(f.Conceptos.Sum(c => c.Iva));
        f.Ieps = Dinero.Redondear(f.Conceptos.Sum(c => c.Ieps));
        f.Total = f.Subtotal + f.Iva + f.Ieps;
    }

    private async Task<string> SiguienteFolioAsync(string tipo, CancellationToken ct)
    {
        var sql = tipo == "global"
            ? "SELECT nextval('facturacion.folio_global_seq') AS \"Value\""
            : "SELECT nextval('facturacion.folio_ingreso_seq') AS \"Value\"";
        var valor = await db.Database.SqlQueryRaw<long>(sql).SingleAsync(ct);
        return valor.ToString(CultureInfo.InvariantCulture);
    }
}
