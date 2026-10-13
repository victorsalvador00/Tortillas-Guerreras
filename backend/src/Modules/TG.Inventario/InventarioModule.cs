using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TG.Inventario;

public static class InventarioModule
{
    public static IServiceCollection AddInventarioModule(this IServiceCollection services, IConfiguration configuracion) => services;

    public static IEndpointRouteBuilder MapInventarioEndpoints(this IEndpointRouteBuilder api) => api;
}
