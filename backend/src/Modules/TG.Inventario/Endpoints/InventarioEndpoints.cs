using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TG.Inventario.Aplicacion;
using TG.Inventario.Dominio;
using TG.Inventario.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Tiempo;
using P = TG.SharedKernel.Seguridad.Permisos.Inventario;

namespace TG.Inventario.Endpoints;

internal static class InventarioEndpoints
{
    public static IEndpointRouteBuilder Mapear(IEndpointRouteBuilder api)
    {
        var grupo = api.MapGroup("/inventario").WithTags("Inventario");

        var almacenes = grupo.MapGroup("/almacenes");
        // Lectura abierta a usuarios autenticados: los selectores de almacén aparecen en varios módulos.
        almacenes.MapGet("/", (TipoAlmacen? tipo, bool? activo, AlmacenesHandler h, CancellationToken ct) =>
            h.ListarAsync(tipo, activo, ct)).RequireAuthorization().WithName("listarAlmacenes");
        almacenes.MapPost("/", async (GuardarAlmacenRequest r, AlmacenesHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<GuardarAlmacenRequest>().WithName("crearAlmacen");
        almacenes.MapPut("/{id:guid}", async (Guid id, GuardarAlmacenRequest r, AlmacenesHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Configurar).WithName("actualizarAlmacen");

        grupo.MapGet("/existencias", (Guid? almacenId, Guid? productoId, string? buscar, bool? soloConExistencia, int? pagina,
                int? tamano, ConsultasInventarioHandler h, CancellationToken ct) =>
            h.ExistenciasAsync(almacenId, productoId, buscar, soloConExistencia, pagina, tamano, ct))
            .RequireAuthorization(P.Ver).WithName("listarExistencias");
        grupo.MapGet("/kardex", (Guid? productoId, Guid? almacenId, DateOnly? desde, DateOnly? hasta,
                DocumentoInventario? documentoTipo, Guid? documentoId, int? pagina, int? tamano, ConsultasInventarioHandler h,
                CancellationToken ct) =>
            h.KardexAsync(productoId, almacenId, desde, hasta, documentoTipo, documentoId, pagina, tamano, ct))
            .RequireAuthorization(P.Ver).WithName("consultarKardex");
        grupo.MapGet("/valuacion", (DateOnly? fecha, bool? incluirRutas, ConsultasInventarioHandler h, IFechaOperativa hoy,
                CancellationToken ct) => h.ValuacionAsync(fecha ?? hoy.Hoy(), incluirRutas ?? true, ct))
            .RequireAuthorization(P.Costos).WithName("consultarValuacion");

        var ajustes = grupo.MapGroup("/ajustes").RequireAuthorization(P.Ajustar);
        ajustes.MapGet("/", (DateOnly? desde, DateOnly? hasta, Guid? almacenId, AjustesHandler h, CancellationToken ct) =>
            h.ListarAsync(desde, hasta, almacenId, ct)).WithName("listarAjustes");
        ajustes.MapPost("/", async (RegistrarAjusteRequest r, AjustesHandler h, CancellationToken ct) =>
            (await h.RegistrarAsync(r, ct)).AHttp()).Validar<RegistrarAjusteRequest>().WithName("registrarAjuste");

        var traspasos = grupo.MapGroup("/traspasos").RequireAuthorization(P.Traspasar);
        traspasos.MapGet("/", (DateOnly? desde, DateOnly? hasta, TraspasosHandler h, CancellationToken ct) =>
            h.ListarAsync(desde, hasta, ct)).WithName("listarTraspasos");
        traspasos.MapGet("/{id:guid}", async (Guid id, TraspasosHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).WithName("obtenerTraspaso");
        traspasos.MapPost("/", async (RegistrarTraspasoRequest r, TraspasosHandler h, CancellationToken ct) =>
            (await h.RegistrarAsync(r, ct)).AHttp()).Validar<RegistrarTraspasoRequest>().WithName("registrarTraspaso");
        traspasos.MapPost("/{id:guid}/cancelar", async (Guid id, TraspasosHandler h, CancellationToken ct) =>
            (await h.CancelarAsync(id, ct)).AHttp()).WithName("cancelarTraspaso");

        var conteos = grupo.MapGroup("/conteos").RequireAuthorization(P.Contar);
        conteos.MapGet("/", (EstadoConteo? estado, ConteosHandler h, CancellationToken ct) => h.ListarAsync(estado, ct))
            .WithName("listarConteos");
        conteos.MapGet("/{id:guid}", async (Guid id, ConteosHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).WithName("obtenerConteo");
        conteos.MapPost("/", async (AbrirConteoRequest r, ConteosHandler h, CancellationToken ct) =>
            (await h.AbrirAsync(r, ct)).AHttp()).WithName("abrirConteo");
        conteos.MapPost("/{id:guid}/partidas", async (Guid id, CapturarConteoRequest r, ConteosHandler h, CancellationToken ct) =>
            (await h.CapturarAsync(id, r, ct)).AHttp()).Validar<CapturarConteoRequest>().WithName("capturarConteo");
        conteos.MapPost("/{id:guid}/cerrar", async (Guid id, ConteosHandler h, CancellationToken ct) =>
            (await h.CerrarAsync(id, ct)).AHttp()).WithName("cerrarConteo");
        conteos.MapPost("/{id:guid}/cancelar", async (Guid id, ConteosHandler h, CancellationToken ct) =>
            (await h.CancelarAsync(id, ct)).AHttp()).WithName("cancelarConteo");

        return api;
    }
}
