using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TG.Catalogos.Aplicacion;
using TG.Catalogos.Endpoints;
using TG.Catalogos.Infraestructura;
using TG.Catalogos.Publico;
using TG.SharedKernel.Datos;

namespace TG.Catalogos;

public static class CatalogosModule
{
    public static IServiceCollection AddCatalogosModule(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AddMemoryCache();
        services.AgregarDbContextModulo<CatalogosDbContext>(CatalogosDbContext.Esquema);
        services.AgregarHandlers(typeof(CatalogosModule).Assembly);
        services.AddScoped<ICatalogosService, CatalogosService>();
        services.AddScoped<IInicializadorModulo, InicializadorCatalogos>();
        return services;
    }

    public static IEndpointRouteBuilder MapCatalogosEndpoints(this IEndpointRouteBuilder api) => CatalogosEndpoints.Mapear(api);
}
