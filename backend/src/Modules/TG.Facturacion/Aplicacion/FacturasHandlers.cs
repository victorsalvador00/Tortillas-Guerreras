using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Facturacion.Dominio;
using TG.Facturacion.Infraestructura;
using TG.Facturacion.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Facturacion.Aplicacion;

public sealed record ConceptoDto(Guid Id, Guid? VentaId, string? NoIdentificacion, string ClaveProdServ, string ClaveUnidad,
    decimal Cantidad, string Descripcion, decimal ValorUnitario, decimal Importe, decimal TasaIva, decimal? TasaIeps, decimal Iva,
    decimal Ieps);

public sealed record FacturaVentaDto(Guid VentaId, string Folio);

public sealed record FacturaResumenDto(Guid Id, TipoFactura Tipo, string Serie, string Folio, Guid? UuidFiscal, Guid? ClienteId,
    string ReceptorRfc, string ReceptorNombre, DateOnly FechaOperativa, DateTimeOffset CreadoEn, decimal Subtotal, decimal Iva,
    decimal Ieps, decimal Total, EstadoFactura Estado, string MetodoPagoSat, string FormaPagoSat, int Intentos,
    string? ErrorDetalle, DateTimeOffset? TimbradaEn);

public sealed record FacturaDto(Guid Id, TipoFactura Tipo, string Serie, string Folio, Guid? UuidFiscal, Guid? ClienteId,
    string ReceptorRfc, string ReceptorNombre, string ReceptorRegimen, string ReceptorCp, string UsoCfdi, string? EmailReceptor,
    string MetodoPagoSat, string FormaPagoSat, string? Periodicidad, string? Meses, string? Anio, DateOnly FechaOperativa,
    DateTimeOffset CreadoEn, decimal Subtotal, decimal Iva, decimal Ieps, decimal Total, EstadoFactura Estado, string Proveedor,
    bool TieneXml, bool TienePdf, string? ErrorDetalle, int Intentos, DateTimeOffset? TimbradaEn, string? MotivoCancelacion,
    DateTimeOffset? CanceladaEn, IReadOnlyList<ConceptoDto> Conceptos, IReadOnlyList<FacturaVentaDto> Ventas);

public sealed record CrearFacturaRequest(IReadOnlyList<Guid> VentaIds, ReceptorRequest? Receptor, string? Email);

public sealed class CrearFacturaValidator : AbstractValidator<CrearFacturaRequest>
{
    public CrearFacturaValidator()
    {
        RuleFor(x => x.VentaIds).NotEmpty().WithMessage("Elige al menos una venta.");
        RuleFor(x => x.Receptor!).SetValidator(new ReceptorValidator()).When(x => x.Receptor is not null);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("El correo no es válido.");
    }
}

public sealed record CancelarFacturaRequest(string Motivo, Guid? Sustituye);

public sealed class CancelarFacturaValidator : AbstractValidator<CancelarFacturaRequest>
{
    public CancelarFacturaValidator() =>
        RuleFor(x => x.Motivo).Must(m => CatalogosSat.MotivosCancelacion.Contains(m))
            .WithMessage("Elige el motivo de cancelación del SAT (01 a 04).");
}

public sealed record ArchivoDescarga(byte[] Contenido, string TipoContenido, string Nombre);

public sealed class ConsultasFacturasHandler(FacturacionDbContext db, IAlmacenArchivos archivos)
{
    public async Task<Paginado<FacturaResumenDto>> ListarAsync(EstadoFactura? estado, TipoFactura? tipo, DateOnly? desde, DateOnly? hasta,
        Guid? clienteId, string? buscar, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Facturas.AsNoTracking();
        if (estado is not null) consulta = consulta.Where(f => f.Estado == estado);
        if (tipo is not null) consulta = consulta.Where(f => f.Tipo == tipo);
        if (desde is not null) consulta = consulta.Where(f => f.FechaOperativa >= desde);
        if (hasta is not null) consulta = consulta.Where(f => f.FechaOperativa <= hasta);
        if (clienteId is not null) consulta = consulta.Where(f => f.ClienteId == clienteId);
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var patron = $"%{buscar.Trim()}%";
            consulta = consulta.Where(f => EF.Functions.ILike(f.ReceptorRfc, patron) || EF.Functions.ILike(f.ReceptorNombre, patron)
                || EF.Functions.ILike(f.Folio, patron) || f.Ventas.Any(v => EF.Functions.ILike(v.VentaFolio, patron)));
        }

        var paginado = await consulta.OrderByDescending(f => f.CreadoEn).PaginarAsync(pagina, tamano, ct);
        return paginado.Map(f => new FacturaResumenDto(f.Id, f.Tipo, f.Serie, f.Folio, f.UuidFiscal, f.ClienteId, f.ReceptorRfc,
            f.ReceptorNombre, f.FechaOperativa, f.CreadoEn, f.Subtotal, f.Iva, f.Ieps, f.Total, f.Estado, f.MetodoPagoSat,
            f.FormaPagoSat, f.Intentos, f.ErrorDetalle, f.TimbradaEn));
    }

    public async Task<Result<FacturaDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var f = await db.Facturas.AsNoTracking().Include(x => x.Conceptos).Include(x => x.Ventas).FirstOrDefaultAsync(x => x.Id == id, ct);
        return f is null ? ErroresFacturacion.FacturaNoEncontrada : ADto(f);
    }

    public async Task<Result<ArchivoDescarga>> ArchivoAsync(Guid id, bool xml, CancellationToken ct)
    {
        var f = await db.Facturas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null)
        {
            return ErroresFacturacion.FacturaNoEncontrada;
        }

        var llave = xml ? f.XmlUrl : f.PdfUrl;
        var contenido = llave is null ? null : await archivos.LeerAsync(llave, ct);
        if (contenido is null)
        {
            return ErroresFacturacion.ArchivoNoDisponible;
        }

        var nombre = $"{f.Serie}{f.Folio}_{f.ReceptorRfc}.{(xml ? "xml" : "pdf")}";
        return new ArchivoDescarga(contenido, xml ? "application/xml" : "application/pdf", nombre);
    }

    internal static FacturaDto ADto(Factura f) => new(f.Id, f.Tipo, f.Serie, f.Folio, f.UuidFiscal, f.ClienteId, f.ReceptorRfc,
        f.ReceptorNombre, f.ReceptorRegimen, f.ReceptorCp, f.UsoCfdi, f.EmailReceptor, f.MetodoPagoSat, f.FormaPagoSat,
        f.Periodicidad, f.Meses, f.Anio, f.FechaOperativa, f.CreadoEn, f.Subtotal, f.Iva, f.Ieps, f.Total, f.Estado, f.Proveedor,
        f.XmlUrl is not null, f.PdfUrl is not null, f.ErrorDetalle, f.Intentos, f.TimbradaEn, f.MotivoCancelacion, f.CanceladaEn,
        f.Conceptos.OrderBy(c => c.Orden).Select(c => new ConceptoDto(c.Id, c.VentaId, c.NoIdentificacion, c.ClaveProdServ, c.ClaveUnidad,
            c.Cantidad, c.Descripcion, c.ValorUnitario, c.Importe, c.TasaIva, c.TasaIeps, c.Iva, c.Ieps)).ToList(),
        f.Ventas.OrderBy(v => v.VentaFolio).Select(v => new FacturaVentaDto(v.VentaId, v.VentaFolio)).ToList());
}

/// <summary>Factura de una o varias ventas del mismo cliente desde la web (institucional / autoservicio).</summary>
public sealed class CrearFacturaHandler(
    FacturacionDbContext db,
    ConstructorFacturas constructor,
    IVentasService ventas,
    IUnitOfWork unitOfWork,
    IFechaOperativa fechaOperativa,
    ConsultasFacturasHandler consultas)
{
    public async Task<Result<FacturaDto>> EjecutarAsync(CrearFacturaRequest r, CancellationToken ct)
    {
        var emisor = await constructor.EmisorAsync(ct);
        if (emisor is null)
        {
            return ErroresFacturacion.SinEmisor;
        }

        var lista = await ventas.ObtenerVentasAsync(r.VentaIds.Distinct(), ct);
        if (lista.Count != r.VentaIds.Distinct().Count())
        {
            return Error.NoEncontrado("FAC-009", "Alguna de las ventas no existe.");
        }

        if (lista.Any(v => v.Estado != EstadoVenta.Registrada || v.FacturaId is not null))
        {
            return ErroresFacturacion.VentaFacturada;
        }

        if (lista.Select(v => v.ClienteId).Distinct().Count() > 1)
        {
            return ErroresFacturacion.DatosReceptor("Las ventas deben ser del mismo cliente.");
        }

        var clienteId = lista[0].ClienteId;
        var cliente = (await ventas.ObtenerClientesAsync([clienteId], ct)).GetValueOrDefault(clienteId);
        var receptor = r.Receptor;
        if (receptor is null)
        {
            if (cliente is null || string.IsNullOrWhiteSpace(cliente.Rfc) || string.IsNullOrWhiteSpace(cliente.RazonSocial)
                || string.IsNullOrWhiteSpace(cliente.RegimenFiscal) || string.IsNullOrWhiteSpace(cliente.CodigoPostalFiscal)
                || string.IsNullOrWhiteSpace(cliente.UsoCfdi))
            {
                return ErroresFacturacion.DatosReceptor("El cliente no tiene completos sus datos fiscales (RFC, razón social, régimen, CP y uso de CFDI).");
            }

            receptor = new ReceptorRequest(cliente.Rfc, cliente.RazonSocial, cliente.RegimenFiscal, cliente.CodigoPostalFiscal, cliente.UsoCfdi);
        }

        var email = string.IsNullOrWhiteSpace(r.Email) ? cliente?.EmailFacturacion : r.Email;
        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var factura = await constructor.IngresoAsync(emisor, lista, receptor, clienteId, email, fechaOperativa.Hoy(), c);
            if (!factura.EsExito)
            {
                return Result<Guid>.Falla(factura.Error!);
            }

            constructor.Encolar(factura.Valor);
            await db.SaveChangesAsync(c);
            var asignada = await ventas.AsignarFacturaAsync(lista.Select(v => v.Id).ToList(), factura.Valor.Id, c);
            return asignada.EsExito ? Result<Guid>.Exito(factura.Valor.Id) : Result<Guid>.Falla(ErroresFacturacion.VentaFacturada);
        }, ct);

        return resultado.EsExito ? await consultas.ObtenerAsync(resultado.Valor, ct) : resultado.Error!;
    }
}

public sealed class AccionesFacturaHandler(
    FacturacionDbContext db,
    IProveedorFacturacion proveedor,
    IVentasService ventas,
    IUnitOfWork unitOfWork,
    TimeProvider reloj,
    ConsultasFacturasHandler consultas)
{
    /// <summary>Vuelve a encolar una factura en <c>Error</c>.</summary>
    public async Task<Result<FacturaDto>> ReintentarAsync(Guid id, CancellationToken ct)
    {
        var factura = await db.Facturas.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (factura is null)
        {
            return ErroresFacturacion.FacturaNoEncontrada;
        }

        if (factura.Estado != EstadoFactura.Error)
        {
            return ErroresFacturacion.EstadoInvalido("Solo se reintentan facturas con error de timbrado.");
        }

        var ahora = reloj.GetUtcNow();
        var mensaje = await db.Outbox.Where(o => o.FacturaId == id && o.Tipo == TipoOutbox.TimbrarFactura)
            .OrderByDescending(o => o.CreadoEn).FirstOrDefaultAsync(ct);
        if (mensaje is null)
        {
            db.Outbox.Add(new MensajeOutbox { Tipo = TipoOutbox.TimbrarFactura, FacturaId = id, CreadoEn = ahora, SiguienteIntentoEn = ahora });
        }
        else
        {
            mensaje.Estado = EstadoOutbox.Pendiente;
            mensaje.Intentos = 0;
            mensaje.SiguienteIntentoEn = ahora;
            mensaje.Error = null;
        }

        factura.Estado = EstadoFactura.Pendiente;
        factura.ErrorDetalle = null;
        await db.SaveChangesAsync(ct);
        return await consultas.ObtenerAsync(id, ct);
    }

    /// <summary>[SUP-15] Cancelación desde el sistema con motivo SAT; libera las ventas para volver a facturarlas.</summary>
    public async Task<Result<FacturaDto>> CancelarAsync(Guid id, CancelarFacturaRequest r, CancellationToken ct)
    {
        var factura = await db.Facturas.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (factura is null)
        {
            return ErroresFacturacion.FacturaNoEncontrada;
        }

        if (factura.Estado == EstadoFactura.Cancelada)
        {
            return ErroresFacturacion.EstadoInvalido("La factura ya está cancelada.");
        }

        if (factura is { Estado: EstadoFactura.Timbrada, UuidFiscal: { } uuid })
        {
            var cancelacion = await proveedor.CancelarAsync(uuid, r.Motivo, r.Sustituye, ct);
            if (!cancelacion.Exito)
            {
                return ErroresFacturacion.CancelacionRechazada(cancelacion.Error ?? "El proveedor rechazó la cancelación.");
            }
        }

        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            factura.Estado = EstadoFactura.Cancelada;
            factura.MotivoCancelacion = r.Motivo;
            factura.CanceladaEn = reloj.GetUtcNow();
            await db.Outbox.Where(o => o.FacturaId == id && o.Estado != EstadoOutbox.Procesado)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Estado, EstadoOutbox.Procesado)
                    .SetProperty(o => o.SiguienteIntentoEn, (DateTimeOffset?)null)
                    .SetProperty(o => o.Error, "Factura cancelada"), c);
            await db.SaveChangesAsync(c);
            await ventas.LiberarFacturaAsync(id, c);
            return Result.Exito();
        }, ct);

        return resultado.EsExito ? await consultas.ObtenerAsync(id, ct) : resultado.Error!;
    }
}

public sealed record EmisorDto(string Rfc, string RazonSocial, string RegimenFiscal, string CodigoPostalExpedicion, string SerieFacturas,
    string SerieGlobal, string SerieNotasCredito, bool EsEjemplo);

public sealed record GuardarEmisorRequest(string Rfc, string RazonSocial, string RegimenFiscal, string CodigoPostalExpedicion,
    string SerieFacturas, string SerieGlobal, string SerieNotasCredito);

public sealed class GuardarEmisorValidator : AbstractValidator<GuardarEmisorRequest>
{
    public GuardarEmisorValidator()
    {
        RuleFor(x => x.Rfc).Must(CatalogosSat.RfcValido).WithMessage("El RFC no tiene un formato válido.");
        RuleFor(x => x.RazonSocial).NotEmpty();
        RuleFor(x => x.RegimenFiscal).Must(r => CatalogosSat.Regimenes.Contains(r)).WithMessage("Elige un régimen del catálogo del SAT.");
        RuleFor(x => x.CodigoPostalExpedicion).Matches(@"^\d{5}$").WithMessage("El código postal tiene 5 dígitos.");
        RuleFor(x => x.SerieFacturas).NotEmpty().MaximumLength(10);
        RuleFor(x => x.SerieGlobal).NotEmpty().MaximumLength(10);
        RuleFor(x => x.SerieNotasCredito).NotEmpty().MaximumLength(10);
    }
}

public sealed class EmisorHandler(FacturacionDbContext db)
{
    public async Task<Result<EmisorDto>> ObtenerAsync(CancellationToken ct)
    {
        var e = await db.Emisores.AsNoTracking().FirstOrDefaultAsync(ct);
        return e is null ? ErroresFacturacion.SinEmisor : ADto(e);
    }

    public async Task<Result<EmisorDto>> GuardarAsync(GuardarEmisorRequest r, CancellationToken ct)
    {
        var e = await db.Emisores.FirstOrDefaultAsync(ct);
        if (e is null)
        {
            e = new Emisor();
            db.Emisores.Add(e);
        }

        e.Rfc = r.Rfc.Trim().ToUpperInvariant();
        e.RazonSocial = r.RazonSocial.Trim().ToUpperInvariant();
        e.RegimenFiscal = r.RegimenFiscal;
        e.CodigoPostalExpedicion = r.CodigoPostalExpedicion;
        e.SerieFacturas = r.SerieFacturas.Trim().ToUpperInvariant();
        e.SerieGlobal = r.SerieGlobal.Trim().ToUpperInvariant();
        e.SerieNotasCredito = r.SerieNotasCredito.Trim().ToUpperInvariant();
        e.EsEjemplo = false;
        await db.SaveChangesAsync(ct);
        return ADto(e);
    }

    private static EmisorDto ADto(Emisor e) => new(e.Rfc, e.RazonSocial, e.RegimenFiscal, e.CodigoPostalExpedicion, e.SerieFacturas,
        e.SerieGlobal, e.SerieNotasCredito, e.EsEjemplo);
}

public sealed record VentaGlobalDto(Guid VentaId, string Folio, DateOnly FechaOperativa, FormaPago FormaPago, decimal Subtotal,
    decimal Iva, decimal Ieps, decimal Total);

public sealed record PrevisualizacionGlobalDto(DateOnly Desde, DateOnly Hasta, string Periodicidad, string Meses, string Anio, int Ventas,
    decimal Subtotal, decimal Iva, decimal Ieps, decimal Total, IReadOnlyList<VentaGlobalDto> Detalle);

public sealed record CrearFacturaGlobalRequest(DateOnly Desde, DateOnly Hasta);

public sealed class CrearFacturaGlobalValidator : AbstractValidator<CrearFacturaGlobalRequest>
{
    public CrearFacturaGlobalValidator() =>
        RuleFor(x => x.Hasta).GreaterThanOrEqualTo(x => x.Desde).WithMessage("La fecha final debe ser igual o posterior a la inicial.");
}

/// <summary>RN-FAC-04: factura global con las ventas de contado no facturadas del periodo.</summary>
public sealed class FacturaGlobalHandler(
    FacturacionDbContext db,
    ConstructorFacturas constructor,
    IVentasService ventas,
    ICatalogosService catalogos,
    IUnitOfWork unitOfWork,
    IFechaOperativa fechaOperativa,
    ConsultasFacturasHandler consultas)
{
    public async Task<PrevisualizacionGlobalDto> PrevisualizarAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var lista = await ventas.VentasParaFacturaGlobalAsync(desde, hasta, ct);
        var periodicidad = await catalogos.ObtenerParametroAsync(ClavesParametro.FacturaGlobalPeriodicidad, "Mensual", ct);
        var detalle = lista.OrderBy(v => v.FechaOperativa).ThenBy(v => v.Folio)
            .Select(v => new VentaGlobalDto(v.Id, v.Folio, v.FechaOperativa, v.FormaPago, v.Subtotal, v.Iva, v.Ieps, v.Total)).ToList();
        return new PrevisualizacionGlobalDto(desde, hasta, ReglasFacturacion.ClavePeriodicidad(periodicidad),
            hasta.Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
            hasta.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), detalle.Count,
            detalle.Sum(v => v.Subtotal), detalle.Sum(v => v.Iva), detalle.Sum(v => v.Ieps), detalle.Sum(v => v.Total), detalle);
    }

    public async Task<Result<FacturaDto>> CrearAsync(CrearFacturaGlobalRequest r, CancellationToken ct)
    {
        var emisor = await constructor.EmisorAsync(ct);
        if (emisor is null)
        {
            return ErroresFacturacion.SinEmisor;
        }

        var lista = await ventas.VentasParaFacturaGlobalAsync(r.Desde, r.Hasta, ct);
        if (lista.Count == 0)
        {
            return ErroresFacturacion.SinVentasGlobal;
        }

        var periodicidad = await catalogos.ObtenerParametroAsync(ClavesParametro.FacturaGlobalPeriodicidad, "Mensual", ct);
        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var factura = await constructor.GlobalAsync(emisor, lista, r.Hasta, periodicidad, fechaOperativa.Hoy(), c);
            constructor.Encolar(factura);
            await db.SaveChangesAsync(c);
            var asignada = await ventas.AsignarFacturaAsync(lista.Select(v => v.Id).ToList(), factura.Id, c);
            return asignada.EsExito ? Result<Guid>.Exito(factura.Id) : Result<Guid>.Falla(ErroresFacturacion.VentaFacturada);
        }, ct);

        return resultado.EsExito ? await consultas.ObtenerAsync(resultado.Valor, ct) : resultado.Error!;
    }
}
