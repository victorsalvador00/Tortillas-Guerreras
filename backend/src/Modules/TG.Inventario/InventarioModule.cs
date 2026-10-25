using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TG.Catalogos.Publico;
using TG.Inventario.Aplicacion;
using TG.Inventario.Dominio;
using TG.Inventario.Endpoints;
using TG.Inventario.Infraestructura;
using TG.Inventario.Publico;
using TG.SharedKernel.Datos;

namespace TG.Inventario;

public static class InventarioModule
{
    public static IServiceCollection AddInventarioModule(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AgregarDbContextModulo<InventarioDbContext>(InventarioDbContext.Esquema);
        services.AgregarHandlers(typeof(InventarioModule).Assembly);
        services.AddScoped<IInventarioService, InventarioService>();
        services.AddScoped<IConsultaInventarioProducto, ConsultaInventarioProducto>();
        services.AddScoped<IConsultaKardex, ConsultaKardex>();
        services.AddScoped<IInicializadorModulo, InicializadorInventario>();
        return services;
    }

    public static IEndpointRouteBuilder MapInventarioEndpoints(this IEndpointRouteBuilder api) => InventarioEndpoints.Mapear(api);
}

/// <summary>Almacenes base <c>MP</c> y <c>PT</c>; los de ruta los crea Logística con cada ruta (RN-LOG-01).</summary>
internal sealed class InicializadorInventario : InicializadorModulo<InventarioDbContext>
{
    public override int Orden => 30;

    public override async Task SembrarAsync(IServiceProvider servicios, bool datosDemo, CancellationToken ct)
    {
        var db = servicios.GetRequiredService<InventarioDbContext>();
        if (!await db.Almacenes.AnyAsync(a => a.Codigo == "MP", ct))
        {
            db.Almacenes.Add(new Almacen { Codigo = "MP", Nombre = "Materias primas", Tipo = TipoAlmacen.MateriaPrima });
        }

        if (!await db.Almacenes.AnyAsync(a => a.Codigo == "PT", ct))
        {
            db.Almacenes.Add(new Almacen { Codigo = "PT", Nombre = "Producto terminado", Tipo = TipoAlmacen.ProductoTerminado });
        }

        await db.SaveChangesAsync(ct);
    }
}
