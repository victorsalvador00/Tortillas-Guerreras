using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TG.Logistica;

public static class LogisticaModule
{
    public static IServiceCollection AddLogisticaModule(this IServiceCollection services, IConfiguration configuracion) => services;

    public static IEndpointRouteBuilder MapLogisticaEndpoints(this IEndpointRouteBuilder api) => api;
}
