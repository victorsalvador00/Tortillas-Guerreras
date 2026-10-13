using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TG.Facturacion;

public static class FacturacionModule
{
    public static IServiceCollection AddFacturacionModule(this IServiceCollection services, IConfiguration configuracion) => services;

    public static IEndpointRouteBuilder MapFacturacionEndpoints(this IEndpointRouteBuilder api) => api;
}
