using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using TG.SharedKernel.Resultados;

namespace TG.SharedKernel.Api;

/// <summary>Convierte <see cref="Result"/> en respuestas HTTP con <c>ProblemDetails</c> (RFC 9457) y código de negocio.</summary>
public static class ResultadosHttp
{
    public static Results<Ok<T>, ProblemHttpResult> AHttp<T>(this Result<T> resultado) =>
        resultado.EsExito ? TypedResults.Ok(resultado.Valor) : resultado.Error!.AProblema();

    public static Results<NoContent, ProblemHttpResult> AHttp(this Result resultado) =>
        resultado.EsExito ? TypedResults.NoContent() : resultado.Error!.AProblema();

    public static ProblemHttpResult AProblema(this Error error)
    {
        var extensiones = new Dictionary<string, object?> { ["codigo"] = error.Codigo };
        if (error.Errores is not null)
        {
            extensiones["errores"] = error.Errores;
        }

        if (error.Detalle is not null)
        {
            extensiones["detalle"] = error.Detalle;
        }

        return TypedResults.Problem(
            title: error.Mensaje,
            statusCode: Estado(error.Tipo),
            extensions: extensiones);
    }

    public static int Estado(TipoError tipo) => tipo switch
    {
        TipoError.Validacion => StatusCodes.Status400BadRequest,
        TipoError.NoEncontrado => StatusCodes.Status404NotFound,
        TipoError.Conflicto => StatusCodes.Status409Conflict,
        TipoError.Prohibido => StatusCodes.Status403Forbidden,
        TipoError.NoAutorizado => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status422UnprocessableEntity,
    };
}
