using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Seguridad;
using TG.Sincronizacion.Aplicacion;
using TG.Sincronizacion.Infraestructura;

namespace TG.Sincronizacion;

public static class SincronizacionModule
{
    public static IServiceCollection AddSincronizacionModule(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AgregarDbContextModulo<SincronizacionDbContext>(SincronizacionDbContext.Esquema);
        services.AgregarHandlers(typeof(SincronizacionModule).Assembly);
        services.AddScoped<IInicializadorModulo, InicializadorSincronizacion>();
        return services;
    }

    public static IEndpointRouteBuilder MapSincronizacionEndpoints(this IEndpointRouteBuilder api)
    {
        var grupo = api.MapGroup("/sync").WithTags("Sincronización");
        var app = grupo.MapGroup("").RequireAuthorization(Permisos.Sincronizacion.Operar);

        app.MapGet("/pull", async (long? desde, int? limite, Guid? rutaId, PullHandler h, CancellationToken ct) =>
            (await h.EjecutarAsync(desde, rutaId, ct)).AHttp()).WithName("syncPull");
        app.MapPost("/push", async (SolicitudPush solicitud, PushHandler h, CancellationToken ct) =>
            (await h.EjecutarAsync(solicitud, ct)).AHttp()).WithName("syncPush");
        app.MapGet("/dispositivo", async (EstadoDispositivoHandler h, CancellationToken ct) =>
            (await h.EjecutarAsync(ct)).AHttp()).WithName("syncEstadoDispositivo");

        grupo.MapGet("/operaciones", (Guid? dispositivoId, bool? soloRechazadas, ConsultaOperacionesHandler h, CancellationToken ct) =>
                h.ListarAsync(dispositivoId, soloRechazadas, ct))
            .RequireAuthorization(Permisos.Seguridad.Dispositivos).WithName("listarOperacionesSync");
        return api;
    }
}
