namespace TG.SharedKernel.Resultados;

public enum TipoError
{
    Validacion,
    NoEncontrado,
    Conflicto,
    Negocio,
    Prohibido,
    NoAutorizado,
}

/// <summary>Error de negocio con código estable (<c>INV-001</c>) y, opcionalmente, errores por campo.</summary>
public sealed record Error(
    string Codigo,
    string Mensaje,
    TipoError Tipo = TipoError.Negocio,
    IReadOnlyDictionary<string, string[]>? Errores = null,
    object? Detalle = null)
{
    public static Error NoEncontrado(string codigo, string mensaje) => new(codigo, mensaje, TipoError.NoEncontrado);

    public static Error Conflicto(string codigo, string mensaje) => new(codigo, mensaje, TipoError.Conflicto);

    public static Error Validacion(string codigo, string mensaje, IReadOnlyDictionary<string, string[]>? errores = null) =>
        new(codigo, mensaje, TipoError.Validacion, errores);

    public static Error Prohibido(string codigo, string mensaje) => new(codigo, mensaje, TipoError.Prohibido);

    public static Error Campo(string codigo, string campo, string mensaje) =>
        new(codigo, mensaje, TipoError.Validacion, new Dictionary<string, string[]> { [campo] = [mensaje] });
}
