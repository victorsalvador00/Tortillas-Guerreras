using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TG.Catalogos;

public static class CatalogosModule
{
    public static IServiceCollection AddCatalogosModule(this IServiceCollection services, IConfiguration configuracion) => services;

    public static IEndpointRouteBuilder MapCatalogosEndpoints(this IEndpointRouteBuilder api) => api;
}
