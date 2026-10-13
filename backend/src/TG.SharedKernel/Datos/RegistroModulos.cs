using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TG.SharedKernel.Datos;

public static class RegistroModulos
{
    /// <summary>
    /// Registra el <c>DbContext</c> de un módulo sobre la conexión compartida de la unidad de trabajo, con nombres
    /// <c>snake_case</c>, historial de migraciones en el esquema del módulo e interceptores comunes.
    /// </summary>
    public static IServiceCollection AgregarDbContextModulo<TContexto>(this IServiceCollection services, string esquema)
        where TContexto : DbContext
    {
        services.AddDbContext<TContexto>((sp, opciones) =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            opciones
                .UseNpgsql(unitOfWork.Conexion, npgsql => npgsql.MigrationsHistoryTable("__ef_migraciones", esquema))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(
                    sp.GetRequiredService<UnitOfWorkInterceptor>(),
                    sp.GetRequiredService<TransaccionComandoInterceptor>(),
                    sp.GetRequiredService<AuditoriaInterceptor>(),
                    sp.GetRequiredService<VersionSyncInterceptor>());
        });
        return services;
    }
}
