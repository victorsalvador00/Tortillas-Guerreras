using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;

namespace TG.SharedKernel;

public static class SharedKernelModule
{
    public static IServiceCollection AddSharedKernel(this IServiceCollection services, IConfiguration configuracion)
    {
        var cadena = configuracion.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default.");
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(cadena).Build());
        services.AddSingleton(TimeProvider.System);
        var zona = configuracion["Negocio:ZonaHoraria"] ?? FechaOperativa.ZonaPredeterminada;
        services.AddSingleton<IFechaOperativa>(sp => new FechaOperativa(sp.GetRequiredService<TimeProvider>(), zona));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<UnitOfWorkInterceptor>();
        services.AddScoped<TransaccionComandoInterceptor>();
        services.AddScoped<AuditoriaInterceptor>();
        services.AddScoped<VersionSyncInterceptor>();
        services.AddScoped<IGeneradorFolios, GeneradorFolios>();

        services.AddHttpContextAccessor();
        services.AddScoped<IUsuarioActual, UsuarioActualHttp>();
        return services;
    }
}
