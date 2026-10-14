using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TG.Logistica.Aplicacion;
using TG.Logistica.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Seguridad;
using P = TG.SharedKernel.Seguridad.Permisos.Logistica;

namespace TG.Logistica.Endpoints;

internal static class LogisticaEndpoints
{
    /// <summary>Cualquiera de los permisos indicados (p. ej. consultar rutas con <c>logistica.ver</c> o <c>configurar</c>).</summary>
    private static TBuilder AlgunPermiso<TBuilder>(this TBuilder builder, params string[] permisos) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(p => p.RequireAuthenticatedUser().RequireClaim(Permisos.Claim, permisos));

    public static IEndpointRouteBuilder Mapear(IEndpointRouteBuilder api)
    {
        var grupo = api.MapGroup("/logistica").WithTags("Logística");

        var vehiculos = grupo.MapGroup("/vehiculos");
        vehiculos.MapGet("/", (bool? activo, VehiculosHandler h, CancellationToken ct) => h.ListarAsync(activo, ct))
            .AlgunPermiso(P.Configurar, P.Ver).WithName("listarVehiculos");
        vehiculos.MapPost("/", async (GuardarVehiculoRequest r, VehiculosHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<GuardarVehiculoRequest>().WithName("crearVehiculo");
        vehiculos.MapPut("/{id:guid}", async (Guid id, GuardarVehiculoRequest r, VehiculosHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<GuardarVehiculoRequest>().WithName("actualizarVehiculo");

        var rutas = grupo.MapGroup("/rutas");
        rutas.MapGet("/", (bool? activo, RutasHandler h, CancellationToken ct) => h.ListarAsync(activo, ct))
            .AlgunPermiso(P.Configurar, P.Ver, P.Cargar, P.Retornos, P.Liquidar).WithName("listarRutas");
        rutas.MapGet("/{id:guid}", async (Guid id, RutasHandler h, CancellationToken ct) => (await h.ObtenerAsync(id, ct)).AHttp())
            .AlgunPermiso(P.Configurar, P.Ver).WithName("obtenerRuta");
        rutas.MapPost("/", async (GuardarRutaRequest r, RutasHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<GuardarRutaRequest>().WithName("crearRuta");
        rutas.MapPut("/{id:guid}", async (Guid id, GuardarRutaRequest r, RutasHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<GuardarRutaRequest>().WithName("actualizarRuta");
        rutas.MapGet("/{id:guid}/clientes", async (Guid id, RutasHandler h, CancellationToken ct) => (await h.ClientesAsync(id, ct)).AHttp())
            .AlgunPermiso(P.Configurar, P.Ver).WithName("listarClientesRuta");
        rutas.MapPut("/{id:guid}/clientes", async (Guid id, ClientesRutaRequest r, RutasHandler h, CancellationToken ct) =>
            (await h.ReemplazarClientesAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<ClientesRutaRequest>()
            .WithName("actualizarClientesRuta");

        var envios = grupo.MapGroup("/envios");
        envios.MapGet("/", (DateOnly? fecha, Guid? rutaId, EstadoEnvio? estado, int? pagina, int? tamano, EnviosHandler h, CancellationToken ct) =>
            h.ListarAsync(fecha, rutaId, estado, pagina, tamano, ct)).AlgunPermiso(P.Ver, P.Cargar).WithName("listarEnvios");
        envios.MapGet("/{id:guid}", async (Guid id, EnviosHandler h, CancellationToken ct) => (await h.ObtenerAsync(id, ct)).AHttp())
            .AlgunPermiso(P.Ver, P.Cargar).WithName("obtenerEnvio");
        envios.MapPost("/", async (GuardarEnvioRequest r, EnviosHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).RequireAuthorization(P.Cargar).Validar<GuardarEnvioRequest>().WithName("crearEnvio");
        envios.MapPut("/{id:guid}", async (Guid id, GuardarEnvioRequest r, EnviosHandler h, CancellationToken ct) =>
            (await h.ActualizarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Cargar).Validar<GuardarEnvioRequest>().WithName("actualizarEnvio");
        envios.MapPost("/{id:guid}/despachar", async (Guid id, EnviosHandler h, CancellationToken ct) =>
            (await h.DespacharAsync(id, ct)).AHttp()).RequireAuthorization(P.Cargar).WithName("despacharEnvio");
        envios.MapPost("/{id:guid}/cancelar", async (Guid id, EnviosHandler h, CancellationToken ct) =>
            (await h.CancelarAsync(id, ct)).AHttp()).RequireAuthorization(P.Cargar).WithName("cancelarEnvio");

        var retornos = grupo.MapGroup("/retornos");
        retornos.MapGet("/", (DateOnly? desde, DateOnly? hasta, Guid? rutaId, TipoRetorno? tipo, int? pagina, int? tamano, RetornosHandler h,
                CancellationToken ct) => h.ListarAsync(desde, hasta, rutaId, tipo, pagina, tamano, ct))
            .AlgunPermiso(P.Retornos, P.Ver).WithName("listarRetornos");
        retornos.MapGet("/{id:guid}", async (Guid id, RetornosHandler h, CancellationToken ct) => (await h.ObtenerAsync(id, ct)).AHttp())
            .AlgunPermiso(P.Retornos, P.Ver).WithName("obtenerRetorno");
        retornos.MapPost("/", async (RegistrarRetornoRequest r, RetornosHandler h, CancellationToken ct) =>
            (await h.RegistrarAsync(r, ct)).AHttp()).RequireAuthorization(P.Retornos).Validar<RegistrarRetornoRequest>().WithName("registrarRetorno");

        var liquidaciones = grupo.MapGroup("/liquidaciones");
        liquidaciones.MapGet("/", (Guid? rutaId, DateOnly? fecha, DateOnly? desde, DateOnly? hasta, EstadoLiquidacion? estado, int? pagina,
                int? tamano, LiquidacionesHandler h, CancellationToken ct) =>
            h.ListarAsync(rutaId, fecha, desde, hasta, estado, pagina, tamano, ct)).AlgunPermiso(P.Ver, P.Liquidar).WithName("listarLiquidaciones");
        liquidaciones.MapGet("/{id:guid}", async (Guid id, LiquidacionesHandler h, CancellationToken ct) => (await h.ObtenerAsync(id, ct)).AHttp())
            .AlgunPermiso(P.Ver, P.Liquidar).WithName("obtenerLiquidacion");
        liquidaciones.MapPut("/{id:guid}/conteo", async (Guid id, CapturarConteoLiquidacionRequest r, LiquidacionesHandler h, CancellationToken ct) =>
            (await h.CapturarConteoAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Liquidar).Validar<CapturarConteoLiquidacionRequest>()
            .WithName("capturarConteoLiquidacion");
        liquidaciones.MapPost("/{id:guid}/cerrar", async (Guid id, CerrarLiquidacionRequest? r, LiquidacionesHandler h, CancellationToken ct) =>
            (await h.CerrarAsync(id, r ?? new CerrarLiquidacionRequest(false), ct)).AHttp()).RequireAuthorization(P.Liquidar)
            .WithName("cerrarLiquidacion");

        return api;
    }
}
