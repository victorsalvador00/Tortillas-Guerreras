using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TG.Sincronizacion;

public static class SincronizacionModule
{
    public static IServiceCollection AddSincronizacionModule(this IServiceCollection services, IConfiguration configuracion) => services;

    public static IEndpointRouteBuilder MapSincronizacionEndpoints(this IEndpointRouteBuilder api) => api;
}
