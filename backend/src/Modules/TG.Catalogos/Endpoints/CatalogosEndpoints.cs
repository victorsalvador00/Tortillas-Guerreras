using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TG.Catalogos.Aplicacion;
using TG.Catalogos.Dominio;
using TG.SharedKernel.Api;
using TG.SharedKernel.Seguridad;

namespace TG.Catalogos.Endpoints;

internal static class CatalogosEndpoints
{
    public static IEndpointRouteBuilder Mapear(IEndpointRouteBuilder api)
    {
        // Lectura para cualquier usuario autenticado; escritura con catalogos.editar.
        var grupo = api.MapGroup("/catalogos").WithTags("Catálogos").RequireAuthorization();
        const string editar = Permisos.Catalogos.Editar;

        var unidades = grupo.MapGroup("/unidades");
        unidades.MapGet("/", (UnidadesHandler h, CancellationToken ct) => h.ListarAsync(ct)).WithName("listarUnidades");
        unidades.MapPost("/", async (GuardarUnidadRequest r, UnidadesHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).RequireAuthorization(editar).Validar<GuardarUnidadRequest>().WithName("crearUnidad");
        unidades.MapPut("/{id:guid}", async (Guid id, GuardarUnidadRequest r, UnidadesHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, r, ct)).AHttp()).RequireAuthorization(editar).Validar<GuardarUnidadRequest>().WithName("actualizarUnidad");
        unidades.MapPost("/{id:guid}/desactivar", async (Guid id, UnidadesHandler h, CancellationToken ct) =>
            (await h.CambiarActivoAsync(id, false, ct)).AHttp()).RequireAuthorization(editar).WithName("desactivarUnidad");
        unidades.MapPost("/{id:guid}/activar", async (Guid id, UnidadesHandler h, CancellationToken ct) =>
            (await h.CambiarActivoAsync(id, true, ct)).AHttp()).RequireAuthorization(editar).WithName("activarUnidad");

        var productos = grupo.MapGroup("/productos");
        productos.MapGet("/", (string? buscar, TipoProducto? tipo, bool? activo, bool? esVendible, bool? esComprable,
                bool? esProducible, int? pagina, int? tamano, ConsultasProductosHandler h, CancellationToken ct) =>
            h.ListarAsync(buscar, tipo, activo, esVendible, esComprable, esProducible, pagina, tamano, ct)).WithName("listarProductos");
        productos.MapGet("/{id:guid}", async (Guid id, ConsultasProductosHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).WithName("obtenerProducto");
        productos.MapPost("/", async (GuardarProductoRequest r, GuardarProductoHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).RequireAuthorization(editar).Validar<GuardarProductoRequest>().WithName("crearProducto");
        productos.MapPut("/{id:guid}", async (Guid id, GuardarProductoRequest r, GuardarProductoHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, r, ct)).AHttp()).RequireAuthorization(editar).Validar<GuardarProductoRequest>().WithName("actualizarProducto");
        productos.MapPost("/{id:guid}/desactivar", async (Guid id, ActivacionProductoHandler h, CancellationToken ct) =>
            (await h.DesactivarAsync(id, ct)).AHttp()).RequireAuthorization(editar).WithName("desactivarProducto");
        productos.MapPost("/{id:guid}/activar", async (Guid id, ActivacionProductoHandler h, CancellationToken ct) =>
            (await h.ActivarAsync(id, ct)).AHttp()).RequireAuthorization(editar).WithName("activarProducto");

        var motivos = grupo.MapGroup("/motivos-merma");
        motivos.MapGet("/", (string? aplica, bool? activo, MotivosMermaHandler h, CancellationToken ct) =>
            h.ListarAsync(aplica, activo, ct)).WithName("listarMotivosMerma");
        motivos.MapPost("/", async (GuardarMotivoMermaRequest r, MotivosMermaHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).RequireAuthorization(editar).Validar<GuardarMotivoMermaRequest>().WithName("crearMotivoMerma");
        motivos.MapPut("/{id:guid}", async (Guid id, GuardarMotivoMermaRequest r, MotivosMermaHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, r, ct)).AHttp()).RequireAuthorization(editar).Validar<GuardarMotivoMermaRequest>().WithName("actualizarMotivoMerma");
        motivos.MapPost("/{id:guid}/desactivar", async (Guid id, MotivosMermaHandler h, CancellationToken ct) =>
            (await h.CambiarActivoAsync(id, false, ct)).AHttp()).RequireAuthorization(editar).WithName("desactivarMotivoMerma");
        motivos.MapPost("/{id:guid}/activar", async (Guid id, MotivosMermaHandler h, CancellationToken ct) =>
            (await h.CambiarActivoAsync(id, true, ct)).AHttp()).RequireAuthorization(editar).WithName("activarMotivoMerma");

        var parametros = grupo.MapGroup("/parametros");
        parametros.MapGet("/", (ParametrosHandler h, CancellationToken ct) => h.ListarAsync(ct)).WithName("listarParametros");
        parametros.MapPut("/{clave}", async (string clave, ActualizarParametroRequest r, ParametrosHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(clave, r, ct)).AHttp()).RequireAuthorization(editar).WithName("actualizarParametro");

        return api;
    }
}
