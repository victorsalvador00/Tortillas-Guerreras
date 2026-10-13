using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TG.Produccion;

public static class ProduccionModule
{
    public static IServiceCollection AddProduccionModule(this IServiceCollection services, IConfiguration configuracion) => services;

    public static IEndpointRouteBuilder MapProduccionEndpoints(this IEndpointRouteBuilder api) => api;
}
