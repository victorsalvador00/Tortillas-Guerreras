using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TG.Compras;

public static class ComprasModule
{
    public static IServiceCollection AddComprasModule(this IServiceCollection services, IConfiguration configuracion) => services;

    public static IEndpointRouteBuilder MapComprasEndpoints(this IEndpointRouteBuilder api) => api;
}
