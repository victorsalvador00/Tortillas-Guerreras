using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TG.Compras.Aplicacion;
using TG.Compras.Endpoints;
using TG.Compras.Infraestructura;
using TG.Compras.Publico;
using TG.SharedKernel.Datos;

namespace TG.Compras;

public static class ComprasModule
{
    public static IServiceCollection AddComprasModule(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AgregarDbContextModulo<ComprasDbContext>(ComprasDbContext.Esquema);
        services.AgregarHandlers(typeof(ComprasModule).Assembly);
        services.AddScoped<IConsultaCompras>(sp => sp.GetRequiredService<ConsultaComprasHandler>());
        services.AddScoped<IInicializadorModulo, InicializadorCompras>();
        return services;
    }

    public static IEndpointRouteBuilder MapComprasEndpoints(this IEndpointRouteBuilder api) => ComprasEndpoints.Mapear(api);
}
