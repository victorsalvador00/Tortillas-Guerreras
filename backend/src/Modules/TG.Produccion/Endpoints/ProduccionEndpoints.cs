using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TG.Produccion.Aplicacion;
using TG.Produccion.Dominio;
using TG.SharedKernel.Api;
using TG.SharedKernel.Tiempo;
using P = TG.SharedKernel.Seguridad.Permisos.Produccion;

namespace TG.Produccion.Endpoints;

internal static class ProduccionEndpoints
{
    public static IEndpointRouteBuilder Mapear(IEndpointRouteBuilder api)
    {
        var grupo = api.MapGroup("/produccion").WithTags("Producción");

        var lineas = grupo.MapGroup("/lineas");
        lineas.MapGet("/", (bool? activo, LineasHandler h, CancellationToken ct) => h.ListarAsync(activo, ct))
            .RequireAuthorization(P.Ver).WithName("listarLineas");
        lineas.MapPost("/", async (GuardarLineaRequest r, LineasHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).RequireAuthorization(P.Recetas).Validar<GuardarLineaRequest>().WithName("crearLinea");
        lineas.MapPut("/{id:guid}", async (Guid id, GuardarLineaRequest r, LineasHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Recetas).Validar<GuardarLineaRequest>().WithName("actualizarLinea");

        var recetas = grupo.MapGroup("/recetas");
        recetas.MapGet("/", (Guid? productoId, EstadoReceta? estado, RecetasHandler h, CancellationToken ct) =>
            h.ListarAsync(productoId, estado, ct)).RequireAuthorization(P.Ver).WithName("listarRecetas");
        recetas.MapGet("/{id:guid}", async (Guid id, RecetasHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).RequireAuthorization(P.Ver).WithName("obtenerReceta");
        recetas.MapPost("/", async (GuardarRecetaRequest r, RecetasHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).RequireAuthorization(P.Recetas).Validar<GuardarRecetaRequest>().WithName("crearReceta");
        recetas.MapPut("/{id:guid}", async (Guid id, GuardarRecetaRequest r, RecetasHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Recetas).Validar<GuardarRecetaRequest>().WithName("actualizarReceta");
        recetas.MapPost("/{id:guid}/nueva-version", async (Guid id, RecetasHandler h, CancellationToken ct) =>
            (await h.NuevaVersionAsync(id, ct)).AHttp()).RequireAuthorization(P.Recetas).WithName("nuevaVersionReceta");
        recetas.MapPost("/{id:guid}/publicar", async (Guid id, RecetasHandler h, CancellationToken ct) =>
            (await h.PublicarAsync(id, ct)).AHttp()).RequireAuthorization(P.Recetas).WithName("publicarReceta");

        var ordenes = grupo.MapGroup("/ordenes");
        ordenes.MapGet("/", (DateOnly? fecha, DateOnly? desde, DateOnly? hasta, Turno? turno, Guid? lineaId, EstadoOrden? estado,
                Guid? productoId, OrdenesHandler h, CancellationToken ct) =>
            h.ListarAsync(fecha, desde, hasta, turno, lineaId, estado, productoId, ct)).RequireAuthorization(P.Ver).WithName("listarOrdenesProduccion");
        ordenes.MapGet("/{id:guid}", async (Guid id, OrdenesHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).RequireAuthorization(P.Ver).WithName("obtenerOrdenProduccion");
        ordenes.MapPost("/", async (PlanearOrdenRequest r, OrdenesHandler h, CancellationToken ct) =>
            (await h.PlanearAsync(r, ct)).AHttp()).RequireAuthorization(P.Planear).Validar<PlanearOrdenRequest>().WithName("planearOrdenProduccion");
        ordenes.MapPut("/{id:guid}", async (Guid id, PlanearOrdenRequest r, OrdenesHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Planear).Validar<PlanearOrdenRequest>().WithName("actualizarOrdenProduccion");
        ordenes.MapPost("/{id:guid}/liberar", async (Guid id, OrdenesHandler h, CancellationToken ct) =>
            (await h.LiberarAsync(id, ct)).AHttp()).RequireAuthorization(P.Planear).WithName("liberarOrdenProduccion");
        ordenes.MapPost("/{id:guid}/iniciar", async (Guid id, OrdenesHandler h, CancellationToken ct) =>
            (await h.IniciarAsync(id, ct)).AHttp()).RequireAuthorization(P.Planear).WithName("iniciarOrdenProduccion");
        ordenes.MapPost("/{id:guid}/cancelar", async (Guid id, OrdenesHandler h, CancellationToken ct) =>
            (await h.CancelarAsync(id, ct)).AHttp()).RequireAuthorization(P.Planear).WithName("cancelarOrdenProduccion");
        ordenes.MapPost("/{id:guid}/mermas", async (Guid id, RegistrarMermaRequest r, OrdenesHandler h, CancellationToken ct) =>
            (await h.RegistrarMermaAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Operar).Validar<RegistrarMermaRequest>().WithName("registrarMermaProduccion");
        ordenes.MapPut("/{id:guid}/consumos", async (Guid id, CapturarConsumosRequest r, OrdenesHandler h, CancellationToken ct) =>
            (await h.CapturarConsumosAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Operar).Validar<CapturarConsumosRequest>().WithName("capturarConsumosProduccion");
        ordenes.MapPost("/{id:guid}/terminar", async (Guid id, TerminarOrdenRequest r, OrdenesHandler h, CancellationToken ct) =>
            (await h.TerminarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Cerrar).Validar<TerminarOrdenRequest>().WithName("terminarOrdenProduccion");

        grupo.MapGet("/reportes/variacion", (DateOnly? desde, DateOnly? hasta, Guid? productoId, ReportesProduccionHandler h,
                IFechaOperativa hoy, CancellationToken ct) =>
            h.VariacionAsync(desde ?? hoy.Hoy().AddDays(-30), hasta ?? hoy.Hoy(), productoId, ct))
            .RequireAuthorization(P.Ver).WithName("reporteVariacionProduccion");

        return api;
    }
}
