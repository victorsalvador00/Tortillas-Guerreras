using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TG.SharedKernel.Datos;
using TG.Ventas.Aplicacion;
using TG.Ventas.Endpoints;
using TG.Ventas.Infraestructura;
using TG.Ventas.Publico;

namespace TG.Ventas;

public static class VentasModule
{
    public static IServiceCollection AddVentasModule(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AgregarDbContextModulo<VentasDbContext>(VentasDbContext.Esquema);
        services.AgregarHandlers(typeof(VentasModule).Assembly);
        services.AddScoped<VentasService>();
        services.AddScoped<IVentasService>(sp => sp.GetRequiredService<VentasService>());
        services.AddScoped<IIncidenciasService, IncidenciasService>();
        services.AddScoped<IConsultasSyncVentas, ConsultasSyncVentas>();
        services.AddScoped<IInicializadorModulo, InicializadorVentas>();
        return services;
    }

    public static IEndpointRouteBuilder MapVentasEndpoints(this IEndpointRouteBuilder api) => VentasEndpoints.Mapear(api);
}
