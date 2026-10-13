using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TG.SharedKernel.Resultados;

namespace TG.SharedKernel.Api;

/// <summary>Filtro de endpoint que valida el cuerpo con FluentValidation y responde 400 con errores por campo.</summary>
public sealed class FiltroValidacion<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validador = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        var cuerpo = context.Arguments.OfType<T>().FirstOrDefault();
        if (validador is not null && cuerpo is not null)
        {
            var resultado = await validador.ValidateAsync(cuerpo, context.HttpContext.RequestAborted);
            if (!resultado.IsValid)
            {
                var errores = resultado.Errors
                    .GroupBy(e => CamelCase(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
                return Error.Validacion("VAL-001", "Revisa los datos capturados.", errores).AProblema();
            }
        }

        return await next(context);
    }

    private static string CamelCase(string nombre) =>
        string.Join('.', nombre.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}

public static class ValidacionExtensiones
{
    public static RouteHandlerBuilder Validar<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<FiltroValidacion<T>>().ProducesValidationProblem();
}
