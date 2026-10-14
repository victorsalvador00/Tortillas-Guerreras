using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Facturacion.Dominio;
using TG.Facturacion.Infraestructura;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Facturacion.Aplicacion;

public sealed record CaptchaDto(Guid Id, string Pregunta);

public sealed record TicketPortalDto(string Folio, DateOnly Fecha, decimal Total, bool Facturable, string? Motivo, Guid? SolicitudId);

public sealed record SolicitudAutofacturaRequest(
    string Folio,
    decimal Total,
    string Rfc,
    string RazonSocial,
    string RegimenFiscal,
    string CodigoPostal,
    string UsoCfdi,
    string Email,
    Guid CaptchaId,
    int CaptchaRespuesta);

/// <summary>RN-FAC-03: formato de RFC, régimen y uso de CFDI del catálogo, CP de 5 dígitos.</summary>
public sealed class SolicitudAutofacturaValidator : AbstractValidator<SolicitudAutofacturaRequest>
{
    public SolicitudAutofacturaValidator()
    {
        RuleFor(x => x.Folio).NotEmpty().WithMessage("Escribe el folio del ticket.");
        RuleFor(x => x.Total).GreaterThan(0).WithMessage("Escribe el total del ticket.");
        RuleFor(x => x.Rfc).Must(CatalogosSat.RfcValido).WithMessage("El RFC no tiene un formato válido (12 o 13 caracteres).");
        RuleFor(x => x.RazonSocial).NotEmpty().MaximumLength(254).WithMessage("Escribe la razón social como aparece en tu constancia.");
        RuleFor(x => x.RegimenFiscal).Must(r => CatalogosSat.Regimenes.Contains(r)).WithMessage("Elige tu régimen fiscal.");
        RuleFor(x => x.CodigoPostal).Matches(@"^\d{5}$").WithMessage("El código postal tiene 5 dígitos.");
        RuleFor(x => x.UsoCfdi).Must(u => CatalogosSat.UsosCfdi.Contains(u)).WithMessage("Elige el uso del CFDI.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Escribe un correo válido para enviarte la factura.");
    }
}

public sealed record SolicitudFacturaDto(string Serie, string Folio, Guid? UuidFiscal, decimal Total, EstadoFactura Estado);

public sealed record SolicitudDto(Guid Id, string FolioTicket, EstadoSolicitud Estado, string? MotivoRechazo, DateTimeOffset CreadoEn,
    string Rfc, string RazonSocial, string Email, SolicitudFacturaDto? Factura);

/// <summary>Portal público de autofactura (RN-FAC-02, 03, 05 y 08).</summary>
public sealed class PortalHandler(
    FacturacionDbContext db,
    ConstructorFacturas constructor,
    IVentasService ventas,
    ICatalogosService catalogos,
    IUnitOfWork unitOfWork,
    IFechaOperativa fechaOperativa,
    CaptchaPortal captcha,
    IAlmacenArchivos archivos)
{
    public CaptchaDto NuevoCaptcha()
    {
        var (id, pregunta) = captcha.Nuevo();
        return new CaptchaDto(id, pregunta);
    }

    /// <summary>Verifica folio y total juntos para no exponer datos de tickets ajenos.</summary>
    public async Task<Result<TicketPortalDto>> ConsultarTicketAsync(string folio, decimal total, CancellationToken ct)
    {
        var venta = await BuscarAsync(folio, total, ct);
        if (venta is null)
        {
            return ErroresFacturacion.TicketNoEncontrado;
        }

        var motivo = await MotivoNoFacturableAsync(venta, ct);
        var solicitud = await db.Solicitudes.AsNoTracking()
            .Where(s => s.VentaId == venta.Id && s.FacturaId != null && s.FacturaId == venta.FacturaId)
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        return new TicketPortalDto(venta.Folio, venta.FechaOperativa, venta.Total, motivo is null, motivo?.Mensaje, solicitud);
    }

    public async Task<Result<SolicitudDto>> SolicitarAsync(SolicitudAutofacturaRequest r, string? ip, CancellationToken ct)
    {
        if (!captcha.Validar(r.CaptchaId, r.CaptchaRespuesta))
        {
            return ErroresFacturacion.CaptchaInvalido;
        }

        var solicitud = new SolicitudAutofactura
        {
            FolioTicket = r.Folio.Trim().ToUpperInvariant(),
            Rfc = r.Rfc.Trim().ToUpperInvariant(),
            RazonSocial = r.RazonSocial.Trim().ToUpperInvariant(),
            RegimenFiscal = r.RegimenFiscal,
            CodigoPostal = r.CodigoPostal,
            UsoCfdi = r.UsoCfdi,
            Email = r.Email.Trim(),
            IpOrigen = ip,
        };

        var venta = await BuscarAsync(r.Folio, r.Total, ct);
        var rechazo = venta is null ? ErroresFacturacion.TicketNoEncontrado : await MotivoNoFacturableAsync(venta, ct);
        if (rechazo is not null)
        {
            solicitud.VentaId = venta?.Id;
            solicitud.Estado = EstadoSolicitud.Rechazada;
            solicitud.MotivoRechazo = rechazo.Mensaje;
            db.Solicitudes.Add(solicitud);
            await db.SaveChangesAsync(ct);
            return rechazo;
        }

        var emisor = await constructor.EmisorAsync(ct);
        if (emisor is null)
        {
            return ErroresFacturacion.SinEmisor;
        }

        solicitud.VentaId = venta!.Id;
        var receptor = new ReceptorRequest(solicitud.Rfc, solicitud.RazonSocial, solicitud.RegimenFiscal, solicitud.CodigoPostal, solicitud.UsoCfdi);
        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var factura = await constructor.IngresoAsync(emisor, [venta], receptor, venta.ClienteId, solicitud.Email, fechaOperativa.Hoy(), c);
            if (!factura.EsExito)
            {
                return Result<Guid>.Falla(factura.Error!);
            }

            constructor.Encolar(factura.Valor);
            solicitud.FacturaId = factura.Valor.Id;
            solicitud.Estado = EstadoSolicitud.EnProceso;
            db.Solicitudes.Add(solicitud);
            await db.SaveChangesAsync(c);
            var asignada = await ventas.AsignarFacturaAsync([venta.Id], factura.Valor.Id, c);
            return asignada.EsExito ? Result<Guid>.Exito(solicitud.Id) : Result<Guid>.Falla(ErroresFacturacion.VentaFacturada);
        }, ct);

        return resultado.EsExito ? await ObtenerAsync(resultado.Valor, ct) : resultado.Error!;
    }

    public async Task<Result<SolicitudDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Solicitudes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null)
        {
            return ErroresFacturacion.SolicitudNoEncontrada;
        }

        var f = s.FacturaId is null ? null : await db.Facturas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == s.FacturaId, ct);
        return ADto(s, f);
    }

    public async Task<Result<ArchivoDescarga>> ArchivoAsync(Guid id, bool xml, CancellationToken ct)
    {
        var s = await db.Solicitudes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s?.FacturaId is null)
        {
            return ErroresFacturacion.SolicitudNoEncontrada;
        }

        var f = await db.Facturas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == s.FacturaId, ct);
        if (f is not { Estado: EstadoFactura.Timbrada })
        {
            return ErroresFacturacion.ArchivoNoDisponible;
        }

        var llave = xml ? f.XmlUrl : f.PdfUrl;
        var contenido = llave is null ? null : await archivos.LeerAsync(llave, ct);
        return contenido is null
            ? ErroresFacturacion.ArchivoNoDisponible
            : new ArchivoDescarga(contenido, xml ? "application/xml" : "application/pdf", $"{f.Serie}{f.Folio}_{f.ReceptorRfc}.{(xml ? "xml" : "pdf")}");
    }

    public async Task<Paginado<SolicitudDto>> ListarAsync(EstadoSolicitud? estado, string? buscar, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Solicitudes.AsNoTracking();
        if (estado is not null) consulta = consulta.Where(s => s.Estado == estado);
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var patron = $"%{buscar.Trim()}%";
            consulta = consulta.Where(s => EF.Functions.ILike(s.FolioTicket, patron) || EF.Functions.ILike(s.Rfc, patron));
        }

        var paginado = await consulta.OrderByDescending(s => s.CreadoEn).PaginarAsync(pagina, tamano, ct);
        var ids = paginado.Items.Where(s => s.FacturaId != null).Select(s => s.FacturaId!.Value).ToList();
        var facturas = await db.Facturas.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        return paginado.Map(s => ADto(s, s.FacturaId is { } fid ? facturas.GetValueOrDefault(fid) : null));
    }

    private async Task<VentaInfo?> BuscarAsync(string folio, decimal total, CancellationToken ct)
    {
        var venta = await ventas.ObtenerVentaPorFolioAsync(folio.Trim().ToUpperInvariant(), ct);
        return venta is not null && Math.Abs(venta.Total - total) < 0.005m ? venta : null;
    }

    /// <summary>RN-FAC-02 y RN-FAC-05: devuelve el motivo por el que el ticket no se puede autofacturar (o null).</summary>
    private async Task<Error?> MotivoNoFacturableAsync(VentaInfo venta, CancellationToken ct)
    {
        if (venta.Estado != EstadoVenta.Registrada)
        {
            return ErroresFacturacion.TicketNoFacturable("El ticket está cancelado.");
        }

        if (venta.FacturaId is { } facturaId)
        {
            var tipo = await db.Facturas.AsNoTracking().Where(f => f.Id == facturaId).Select(f => (TipoFactura?)f.Tipo).FirstOrDefaultAsync(ct);
            return tipo == TipoFactura.Global ? ErroresFacturacion.EnGlobal : ErroresFacturacion.TicketNoFacturable("El ticket ya fue facturado.");
        }

        if (venta.CondicionPago != CondicionPago.Contado)
        {
            return ErroresFacturacion.TicketNoFacturable("Las ventas a crédito se facturan desde la empresa; comunícate con nosotros.");
        }

        var limite = await catalogos.ObtenerParametroAsync(ClavesParametro.AutofacturaLimite, "fin_de_mes", ct);
        return ReglasFacturacion.AutofacturaVigente(venta.FechaOperativa, fechaOperativa.Hoy(), limite)
            ? null
            : ErroresFacturacion.TicketNoFacturable("El plazo para facturar este ticket ya venció.");
    }

    private static SolicitudDto ADto(SolicitudAutofactura s, Factura? f) =>
        new(s.Id, s.FolioTicket, s.Estado, s.MotivoRechazo, s.CreadoEn, s.Rfc, s.RazonSocial, s.Email,
            f is null ? null : new SolicitudFacturaDto(f.Serie, f.Folio, f.UuidFiscal, f.Total, f.Estado));
}
