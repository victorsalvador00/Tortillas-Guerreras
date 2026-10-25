using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TG.Logistica.Aplicacion;
using TG.Logistica.Endpoints;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.SharedKernel.Datos;

namespace TG.Logistica;

public static class LogisticaModule
{
    public static IServiceCollection AddLogisticaModule(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AgregarDbContextModulo<LogisticaDbContext>(LogisticaDbContext.Esquema);
        services.AgregarHandlers(typeof(LogisticaModule).Assembly);
        services.AddScoped<IConsultaEnvios>(sp => sp.GetRequiredService<ConsultaEnviosHandler>());
        services.AddScoped<ServicioRetornos>();
        services.AddScoped<ServicioCargosChofer>();
        services.AddScoped<ILogisticaService, LogisticaService>();
        // Sincronización puede reemplazarla registrando la suya (Replace o registro posterior).
        services.TryAddScoped<IEstadoSincronizacionRuta, EstadoSincronizacionRutaPredeterminado>();
        services.AddScoped<IInicializadorModulo, InicializadorLogistica>();
        return services;
    }

    public static IEndpointRouteBuilder MapLogisticaEndpoints(this IEndpointRouteBuilder api) => LogisticaEndpoints.Mapear(api);
}
