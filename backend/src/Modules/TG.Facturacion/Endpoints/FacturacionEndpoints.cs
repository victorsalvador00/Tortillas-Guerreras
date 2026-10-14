using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using TG.Facturacion.Aplicacion;
using TG.Facturacion.Dominio;
using TG.SharedKernel.Api;
using TG.SharedKernel.Resultados;
using P = TG.SharedKernel.Seguridad.Permisos.Facturacion;

namespace TG.Facturacion.Endpoints;

internal static class FacturacionEndpoints
{
    public static IEndpointRouteBuilder Mapear(IEndpointRouteBuilder api)
    {
        var grupo = api.MapGroup("/facturacion").WithTags("Facturación");

        grupo.MapGet("/emisor", async (EmisorHandler h, CancellationToken ct) => (await h.ObtenerAsync(ct)).AHttp())
            .RequireAuthorization(P.Ver).WithName("obtenerEmisor");
        grupo.MapPut("/emisor", async (GuardarEmisorRequest r, EmisorHandler h, CancellationToken ct) => (await h.GuardarAsync(r, ct)).AHttp())
            .RequireAuthorization(P.Configurar).Validar<GuardarEmisorRequest>().WithName("guardarEmisor");

        var facturas = grupo.MapGroup("/facturas");
        facturas.MapGet("/", (EstadoFactura? estado, TipoFactura? tipo, DateOnly? desde, DateOnly? hasta, Guid? clienteId, string? buscar,
                int? pagina, int? tamano, ConsultasFacturasHandler h, CancellationToken ct) =>
            h.ListarAsync(estado, tipo, desde, hasta, clienteId, buscar, pagina, tamano, ct))
            .RequireAuthorization(P.Ver).WithName("listarFacturas");
        facturas.MapGet("/{id:guid}", async (Guid id, ConsultasFacturasHandler h, CancellationToken ct) => (await h.ObtenerAsync(id, ct)).AHttp())
            .RequireAuthorization(P.Ver).WithName("obtenerFactura");
        facturas.MapPost("/", async (CrearFacturaRequest r, CrearFacturaHandler h, CancellationToken ct) => (await h.EjecutarAsync(r, ct)).AHttp())
            .RequireAuthorization(P.Emitir).Validar<CrearFacturaRequest>().WithName("crearFactura");
        facturas.MapPost("/{id:guid}/reintentar", async (Guid id, AccionesFacturaHandler h, CancellationToken ct) =>
            (await h.ReintentarAsync(id, ct)).AHttp()).RequireAuthorization(P.Emitir).WithName("reintentarFactura");
        facturas.MapPost("/{id:guid}/cancelar", async (Guid id, CancelarFacturaRequest r, AccionesFacturaHandler h, CancellationToken ct) =>
            (await h.CancelarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Cancelar).Validar<CancelarFacturaRequest>().WithName("cancelarFactura");
        facturas.MapGet("/{id:guid}/xml", async (Guid id, ConsultasFacturasHandler h, CancellationToken ct) =>
            Archivo(await h.ArchivoAsync(id, true, ct))).RequireAuthorization(P.Ver).WithName("descargarXmlFactura");
        facturas.MapGet("/{id:guid}/pdf", async (Guid id, ConsultasFacturasHandler h, CancellationToken ct) =>
            Archivo(await h.ArchivoAsync(id, false, ct))).RequireAuthorization(P.Ver).WithName("descargarPdfFactura");

        grupo.MapGet("/solicitudes", (EstadoSolicitud? estado, string? buscar, int? pagina, int? tamano, PortalHandler h, CancellationToken ct) =>
            h.ListarAsync(estado, buscar, pagina, tamano, ct)).RequireAuthorization(P.Ver).WithName("listarSolicitudesAutofactura");

        grupo.MapGet("/factura-global/previsualizar", (DateOnly desde, DateOnly hasta, FacturaGlobalHandler h, CancellationToken ct) =>
            h.PrevisualizarAsync(desde, hasta, ct)).RequireAuthorization(P.Emitir).WithName("previsualizarFacturaGlobal");
        grupo.MapPost("/factura-global", async (CrearFacturaGlobalRequest r, FacturaGlobalHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).RequireAuthorization(P.Emitir).Validar<CrearFacturaGlobalRequest>().WithName("crearFacturaGlobal");

        // Portal público de autofactura: anónimo y con límite por IP (RN-FAC-08) en las operaciones sensibles.
        var portal = api.MapGroup("/portal").WithTags("Portal de autofactura").AllowAnonymous();
        portal.MapGet("/captcha", (PortalHandler h) => TypedResults.Ok(h.NuevoCaptcha())).WithName("captchaPortal");
        portal.MapGet("/tickets/{folio}", async (string folio, decimal total, PortalHandler h, CancellationToken ct) =>
            (await h.ConsultarTicketAsync(folio, total, ct)).AHttp()).RequireRateLimiting("portal").WithName("consultarTicketPortal");
        portal.MapPost("/solicitudes", async (SolicitudAutofacturaRequest r, PortalHandler h, HttpContext http, CancellationToken ct) =>
                (await h.SolicitarAsync(r, http.Connection.RemoteIpAddress?.ToString(), ct)).AHttp())
            .RequireRateLimiting("portal").Validar<SolicitudAutofacturaRequest>().WithName("solicitarAutofactura");
        portal.MapGet("/solicitudes/{id:guid}", async (Guid id, PortalHandler h, CancellationToken ct) => (await h.ObtenerAsync(id, ct)).AHttp())
            .WithName("obtenerSolicitudPortal");
        portal.MapGet("/solicitudes/{id:guid}/xml", async (Guid id, PortalHandler h, CancellationToken ct) =>
            Archivo(await h.ArchivoAsync(id, true, ct))).WithName("descargarXmlPortal");
        portal.MapGet("/solicitudes/{id:guid}/pdf", async (Guid id, PortalHandler h, CancellationToken ct) =>
            Archivo(await h.ArchivoAsync(id, false, ct))).WithName("descargarPdfPortal");

        return api;
    }

    private static Results<FileContentHttpResult, ProblemHttpResult> Archivo(Result<ArchivoDescarga> r) =>
        r.EsExito
            ? TypedResults.File(r.Valor.Contenido, r.Valor.TipoContenido, r.Valor.Nombre)
            : r.Error!.AProblema();
}
