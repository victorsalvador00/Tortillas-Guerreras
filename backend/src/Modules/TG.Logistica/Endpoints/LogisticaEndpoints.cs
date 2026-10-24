using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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

        grupo.MapGet("/vendedores", (TG.Seguridad.Publico.ISeguridadService seguridad, CancellationToken ct) =>
                seguridad.UsuariosConRolAsync("Vendedor", ct))
            .AlgunPermiso(P.Configurar, P.Ver).WithName("listarVendedores");

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

        // v0.2: cambios pendientes de clientes (RN-LOG-16).
        var pendientes = grupo.MapGroup("/cambios-pendientes");
        pendientes.MapGet("/", (Guid? clienteId, Guid? rutaId, EstadoCambioPendiente? estado, CambiosPendientesHandler h, CancellationToken ct) =>
            h.ListarAsync(clienteId, rutaId, estado, ct)).RequireAuthorization(P.Ver).WithName("listarCambiosPendientes");
        pendientes.MapPost("/{id:guid}/cancelar", async (Guid id, CancelarCambioPendienteRequest r, CambiosPendientesHandler h, CancellationToken ct) =>
            (await h.CancelarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.CambiosCancelar).Validar<CancelarCambioPendienteRequest>()
            .WithName("cancelarCambioPendiente");

        var envios = grupo.MapGroup("/envios");
        envios.MapGet("/", (DateOnly? fecha, Guid? rutaId, EstadoEnvio? estado, int? pagina, int? tamano, EnviosHandler h, CancellationToken ct) =>
            h.ListarAsync(fecha, rutaId, estado, pagina, tamano, ct)).AlgunPermiso(P.Ver, P.Cargar).WithName("listarEnvios");
        envios.MapGet("/sugeridos", (DateOnly? fecha, SugeridosHandler h, CancellationToken ct) => h.ObtenerAsync(fecha, ct))
            .AlgunPermiso(P.Ver, P.Cargar).WithName("obtenerCargasSugeridas");
        envios.MapPost("/sugeridos", (GenerarSugeridasRequest r, SugeridosHandler h, CancellationToken ct) => h.GenerarAsync(r, ct))
            .RequireAuthorization(P.Cargar).WithName("generarCargasSugeridas");
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
            (await h.CapturarConteoAsync(id, r, ct)).AHttp()).RequireAuthorization(P.LiquidarProducto).Validar<CapturarConteoLiquidacionRequest>()
            .WithName("capturarConteoLiquidacion");
        liquidaciones.MapPost("/{id:guid}/confirmar-producto", async (Guid id, CerrarLiquidacionRequest? r, LiquidacionesHandler h, CancellationToken ct) =>
            (await h.ConfirmarProductoAsync(id, r ?? new CerrarLiquidacionRequest(), ct)).AHttp()).RequireAuthorization(P.LiquidarProducto)
            .WithName("confirmarProductoLiquidacion");
        liquidaciones.MapPost("/{id:guid}/revisar", async (Guid id, LiquidacionesHandler h, CancellationToken ct) =>
            (await h.RevisarAsync(id, ct)).AHttp()).RequireAuthorization(P.RevisarLiquidacion)
            .WithName("revisarLiquidacion");
        liquidaciones.MapPost("/{id:guid}/regresar", async (Guid id, RegresarLiquidacionRequest r, LiquidacionesHandler h, CancellationToken ct) =>
            (await h.RegresarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.RevisarLiquidacion)
            .WithName("regresarLiquidacion");
        liquidaciones.MapPost("/{id:guid}/cerrar", async (Guid id, CerrarLiquidacionRequest? r, LiquidacionesHandler h, CancellationToken ct) =>
            (await h.CerrarAsync(id, r ?? new CerrarLiquidacionRequest(), ct)).AHttp()).RequireAuthorization(P.Liquidar)
            .WithName("cerrarLiquidacion");
        liquidaciones.MapGet("/{id:guid}/gastos", async (Guid id, LiquidacionesHandler h, CancellationToken ct) =>
            (await h.GastosAsync(id, ct)).AHttp()).RequireAuthorization(P.Liquidar)
            .WithName("listarGastosLiquidacion");
        liquidaciones.MapPost("/{id:guid}/gastos", async (Guid id, GastoLiquidacionRequest r, LiquidacionesHandler h, CancellationToken ct) =>
            (await h.AgregarGastoAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Liquidar).Validar<GastoLiquidacionRequest>()
            .WithName("agregarGastoLiquidacion");
        liquidaciones.MapPut("/{id:guid}/gastos/{gastoId:guid}", async (Guid id, Guid gastoId, GastoLiquidacionRequest r, LiquidacionesHandler h,
                CancellationToken ct) => (await h.ActualizarGastoAsync(id, gastoId, r, ct)).AHttp())
            .RequireAuthorization(P.Liquidar).Validar<GastoLiquidacionRequest>()
            .WithName("actualizarGastoLiquidacion");
        liquidaciones.MapGet("/{id:guid}/hoja", async (Guid id, LiquidacionesHandler h, CancellationToken ct) =>
            (await h.HojaAsync(id, ct)).AHttp()).RequireAuthorization(P.Ver)
            .WithName("hojaLiquidacion");

        // v0.2: cuenta y corte semanal de los choferes (RN-LOG-25 a 30).
        var cargos = grupo.MapGroup("/cargos-chofer").RequireAuthorization(P.CargosChofer);
        cargos.MapGet("/", (Guid? choferId, EstadoCargoChofer? estado, DateOnly? desde, DateOnly? hasta, Guid? liquidacionId, Guid? corteId,
                CargosChoferHandler h, CancellationToken ct) => h.ListarAsync(choferId, estado, desde, hasta, liquidacionId, corteId, ct))
            .WithName("listarCargosChofer");
        cargos.MapPost("/", async (CrearCargoChoferRequest r, CargosChoferHandler h, CancellationToken ct) => (await h.CrearAsync(r, ct)).AHttp())
            .Validar<CrearCargoChoferRequest>().WithName("crearCargoChofer");
        cargos.MapPost("/{id:guid}/cancelar", async (Guid id, CancelarCargoChoferRequest r, CargosChoferHandler h, CancellationToken ct) =>
            (await h.CancelarAsync(id, r, ct)).AHttp()).WithName("cancelarCargoChofer");

        var cortes = grupo.MapGroup("/cortes-chofer").RequireAuthorization(P.CargosChofer);
        cortes.MapGet("/", (DateOnly fecha, Guid? choferId, CortesChoferHandler h, CancellationToken ct) => h.ResumenAsync(fecha, choferId, ct))
            .WithName("resumenCortesChofer");
        cortes.MapPost("/", async (AbrirCorteRequest r, CortesChoferHandler h, CancellationToken ct) => (await h.AbrirAsync(r, ct)).AHttp())
            .WithName("abrirCorteChofer");
        cortes.MapGet("/{id:guid}", async (Guid id, CortesChoferHandler h, CancellationToken ct) => (await h.ObtenerAsync(id, ct)).AHttp())
            .WithName("obtenerCorteChofer");
        cortes.MapPost("/{id:guid}/cerrar", async (Guid id, CortesChoferHandler h, CancellationToken ct) => (await h.CerrarAsync(id, ct)).AHttp())
            .WithName("cerrarCorteChofer");
        cortes.MapGet("/{id:guid}/exportar", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (Guid id, CortesChoferHandler h,
                CancellationToken ct) =>
            {
                var r = await h.ExportarAsync(id, ct);
                return r.EsExito ? TypedResults.File(r.Valor.Contenido, r.Valor.TipoContenido, r.Valor.Nombre) : r.Error!.AProblema();
            })
            .WithName("exportarCorteChofer");

        return api;
    }
}
