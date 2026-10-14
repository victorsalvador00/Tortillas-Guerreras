using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TG.Facturacion.Aplicacion;
using TG.Facturacion.Dominio;
using TG.Facturacion.Endpoints;
using TG.Facturacion.Infraestructura;
using TG.Facturacion.Publico;
using TG.SharedKernel.Datos;

namespace TG.Facturacion;

public static class FacturacionModule
{
    public static IServiceCollection AddFacturacionModule(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AgregarDbContextModulo<FacturacionDbContext>(FacturacionDbContext.Esquema);
        services.AgregarHandlers(typeof(FacturacionModule).Assembly);
        services.AddScoped<ConstructorFacturas>();
        services.AddScoped<ProcesadorOutboxFacturacion>();
        services.AddSingleton<CaptchaPortal>();
        services.AddSingleton<BandejaCorreos>();
        services.AddScoped<IInicializadorModulo, InicializadorFacturacion>();

        // D-06 pendiente: solo existe el proveedor simulado.
        var proveedor = configuracion["Facturacion:Proveedor"] ?? ProveedorFacturacionSimulado.Nombre;
        if (!string.Equals(proveedor, ProveedorFacturacionSimulado.Nombre, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Proveedor de facturación '{proveedor}' no disponible (D-06 pendiente).");
        }

        services.AddScoped<IProveedorFacturacion, ProveedorFacturacionSimulado>();

        if (string.Equals(configuracion["Archivos:Proveedor"], "Spaces", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAlmacenArchivos, AlmacenArchivosSpaces>();
        }
        else
        {
            services.AddSingleton<IAlmacenArchivos, AlmacenArchivosLocal>();
        }

        if (string.IsNullOrWhiteSpace(configuracion["Smtp:Host"]))
        {
            services.AddScoped<IEnviadorCorreo, EnviadorCorreoRegistro>();
        }
        else
        {
            services.AddScoped<IEnviadorCorreo, EnviadorCorreoSmtp>();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapFacturacionEndpoints(this IEndpointRouteBuilder api) => FacturacionEndpoints.Mapear(api);
}

/// <summary>Emisor de ejemplo (se reemplaza por el real en la pantalla Emisor).</summary>
internal sealed class InicializadorFacturacion : InicializadorModulo<FacturacionDbContext>
{
    public override int Orden => 80;

    public override async Task SembrarAsync(IServiceProvider servicios, bool datosDemo, CancellationToken ct)
    {
        var db = servicios.GetRequiredService<FacturacionDbContext>();
        if (!await db.Emisores.AnyAsync(ct))
        {
            // RFC de pruebas publicado por el SAT; marcado como ejemplo.
            db.Emisores.Add(new Emisor
            {
                Rfc = "EKU9003173C9",
                RazonSocial = "ESCUELA KEMPER URGATE",
                RegimenFiscal = "601",
                CodigoPostalExpedicion = "91700",
                EsEjemplo = true,
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
