using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TG.Produccion.Aplicacion;
using TG.Produccion.Endpoints;
using TG.Produccion.Infraestructura;
using TG.Produccion.Publico;
using TG.SharedKernel.Datos;

namespace TG.Produccion;

public static class ProduccionModule
{
    public static IServiceCollection AddProduccionModule(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AgregarDbContextModulo<ProduccionDbContext>(ProduccionDbContext.Esquema);
        services.AgregarHandlers(typeof(ProduccionModule).Assembly);
        services.AddScoped<IConsultaProduccion>(sp => sp.GetRequiredService<ReportesProduccionHandler>());
        services.AddScoped<IInicializadorModulo, InicializadorProduccion>();
        return services;
    }

    public static IEndpointRouteBuilder MapProduccionEndpoints(this IEndpointRouteBuilder api) => ProduccionEndpoints.Mapear(api);
}
