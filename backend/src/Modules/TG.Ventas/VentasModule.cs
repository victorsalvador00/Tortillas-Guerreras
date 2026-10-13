using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TG.Ventas;

public static class VentasModule
{
    public static IServiceCollection AddVentasModule(this IServiceCollection services, IConfiguration configuracion) => services;

    public static IEndpointRouteBuilder MapVentasEndpoints(this IEndpointRouteBuilder api) => api;
}
