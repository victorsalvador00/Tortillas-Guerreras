using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace TG.SharedKernel.Datos;

public static class RegistroHandlers
{
    /// <summary>
    /// Registra como <c>scoped</c> cada clase concreta cuyo nombre termina en <c>Handler</c> (un handler por caso de uso,
    /// sin MediatR) y los validadores de FluentValidation del ensamblado.
    /// </summary>
    public static IServiceCollection AgregarHandlers(this IServiceCollection services, Assembly ensamblado)
    {
        foreach (var tipo in ensamblado.GetTypes().Where(t =>
                     t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } &&
                     t.Name.EndsWith("Handler", StringComparison.Ordinal)))
        {
            services.AddScoped(tipo);
        }

        services.AddValidatorsFromAssembly(ensamblado, ServiceLifetime.Singleton, includeInternalTypes: true);
        return services;
    }
}
