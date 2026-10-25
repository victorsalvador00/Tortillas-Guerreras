using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TG.Compras.Aplicacion;
using TG.Compras.Dominio;
using TG.SharedKernel.Api;
using P = TG.SharedKernel.Seguridad.Permisos.Compras;

namespace TG.Compras.Endpoints;

internal static class ComprasEndpoints
{
    public static IEndpointRouteBuilder Mapear(IEndpointRouteBuilder api)
    {
        var grupo = api.MapGroup("/compras").WithTags("Compras");

        var proveedores = grupo.MapGroup("/proveedores");
        proveedores.MapGet("/", (string? buscar, bool? activo, int? pagina, int? tamano, ProveedoresHandler h, CancellationToken ct) =>
            h.ListarAsync(buscar, activo, pagina, tamano, ct)).RequireAuthorization(P.Ver).WithName("listarProveedores");
        proveedores.MapGet("/{id:guid}", async (Guid id, ProveedoresHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).RequireAuthorization(P.Ver).WithName("obtenerProveedor");
        proveedores.MapGet("/{id:guid}/precios", async (Guid id, Guid? productoId, ProveedoresHandler h, CancellationToken ct) =>
            (await h.PreciosAsync(id, productoId, ct)).AHttp()).RequireAuthorization(P.Ver).WithName("listarPreciosProveedor");
        proveedores.MapGet("/{id:guid}/productos", async (Guid id, bool? incluirInactivos, ProductosProveedorHandler h, CancellationToken ct) =>
            (await h.ListarAsync(id, incluirInactivos ?? false, ct)).AHttp()).RequireAuthorization(P.Ver).WithName("listarProductosProveedor");
        proveedores.MapPost("/{id:guid}/productos", async (Guid id, AgregarProductoProveedorRequest r, ProductosProveedorHandler h, CancellationToken ct) =>
            (await h.AgregarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Editar).Validar<AgregarProductoProveedorRequest>()
            .WithName("agregarProductoProveedor");
        proveedores.MapPut("/{id:guid}/productos/{productoId:guid}", async (Guid id, Guid productoId, ActualizarProductoProveedorRequest r,
                ProductosProveedorHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, productoId, r, ct)).AHttp()).RequireAuthorization(P.Editar).Validar<ActualizarProductoProveedorRequest>()
            .WithName("actualizarProductoProveedor");
        grupo.MapGet("/productos/{productoId:guid}/proveedores", async (Guid productoId, ProductosProveedorHandler h, CancellationToken ct) =>
            (await h.OfertasAsync(productoId, ct)).AHttp()).RequireAuthorization(P.Ver).WithName("listarProveedoresProducto");
        proveedores.MapPost("/", async (GuardarProveedorRequest r, ProveedoresHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).RequireAuthorization(P.Editar).Validar<GuardarProveedorRequest>().WithName("crearProveedor");
        proveedores.MapPut("/{id:guid}", async (Guid id, GuardarProveedorRequest r, ProveedoresHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Editar).Validar<GuardarProveedorRequest>().WithName("actualizarProveedor");
        proveedores.MapPost("/{id:guid}/desactivar", async (Guid id, ProveedoresHandler h, CancellationToken ct) =>
            (await h.CambiarActivoAsync(id, false, ct)).AHttp()).RequireAuthorization(P.Editar).WithName("desactivarProveedor");
        proveedores.MapPost("/{id:guid}/activar", async (Guid id, ProveedoresHandler h, CancellationToken ct) =>
            (await h.CambiarActivoAsync(id, true, ct)).AHttp()).RequireAuthorization(P.Editar).WithName("activarProveedor");

        var ordenes = grupo.MapGroup("/ordenes-compra");
        ordenes.MapGet("/", (EstadoOrdenCompra? estado, Guid? proveedorId, DateOnly? desde, DateOnly? hasta, string? buscar, int? pagina,
                int? tamano, ConsultasOrdenesCompraHandler h, CancellationToken ct) =>
            h.ListarAsync(estado, proveedorId, desde, hasta, buscar, pagina, tamano, ct)).RequireAuthorization(P.Ver).WithName("listarOrdenesCompra");
        ordenes.MapGet("/{id:guid}", async (Guid id, ConsultasOrdenesCompraHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).RequireAuthorization(P.Ver).WithName("obtenerOrdenCompra");
        ordenes.MapPost("/", async (GuardarOrdenCompraRequest r, GuardarOrdenCompraHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).RequireAuthorization(P.Editar).Validar<GuardarOrdenCompraRequest>().WithName("crearOrdenCompra");
        ordenes.MapPut("/{id:guid}", async (Guid id, GuardarOrdenCompraRequest r, GuardarOrdenCompraHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Editar).Validar<GuardarOrdenCompraRequest>().WithName("actualizarOrdenCompra");
        ordenes.MapPost("/{id:guid}/autorizar", async (Guid id, EstadoOrdenCompraHandler h, CancellationToken ct) =>
            (await h.AutorizarAsync(id, ct)).AHttp()).RequireAuthorization(P.Autorizar).WithName("autorizarOrdenCompra");
        ordenes.MapPost("/{id:guid}/cancelar", async (Guid id, EstadoOrdenCompraHandler h, CancellationToken ct) =>
            (await h.CancelarAsync(id, ct)).AHttp()).RequireAuthorization(P.Autorizar).WithName("cancelarOrdenCompra");
        ordenes.MapPost("/{id:guid}/cerrar", async (Guid id, EstadoOrdenCompraHandler h, CancellationToken ct) =>
            (await h.CerrarAsync(id, ct)).AHttp()).RequireAuthorization(P.Autorizar).WithName("cerrarOrdenCompra");

        var recepciones = grupo.MapGroup("/recepciones");
        recepciones.MapGet("/", (EstadoRecepcion? estado, Guid? proveedorId, Guid? ordenCompraId, DateOnly? desde, DateOnly? hasta,
                int? pagina, int? tamano, ConsultasRecepcionesHandler h, CancellationToken ct) =>
            h.ListarAsync(estado, proveedorId, ordenCompraId, desde, hasta, pagina, tamano, ct)).RequireAuthorization(P.Recibir).WithName("listarRecepciones");
        recepciones.MapGet("/lotes-propuestos", (Guid[] productoIds, ConsultasRecepcionesHandler h, CancellationToken ct) =>
            h.ProponerLotesAsync(productoIds, ct)).RequireAuthorization(P.Recibir).WithName("proponerLotesRecepcion");
        recepciones.MapGet("/{id:guid}", async (Guid id, ConsultasRecepcionesHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).RequireAuthorization(P.Recibir).WithName("obtenerRecepcion");
        recepciones.MapPost("/", async (RegistrarRecepcionRequest r, RegistrarRecepcionHandler h, CancellationToken ct) =>
            (await h.EjecutarAsync(r, ct)).AHttp()).RequireAuthorization(P.Recibir).Validar<RegistrarRecepcionRequest>().WithName("registrarRecepcion");
        recepciones.MapPost("/{id:guid}/cancelar", async (Guid id, CancelarRecepcionHandler h, CancellationToken ct) =>
            (await h.EjecutarAsync(id, ct)).AHttp()).RequireAuthorization(P.Autorizar).WithName("cancelarRecepcion");

        return api;
    }
}
