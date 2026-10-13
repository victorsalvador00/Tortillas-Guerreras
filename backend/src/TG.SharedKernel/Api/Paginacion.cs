using Microsoft.EntityFrameworkCore;

namespace TG.SharedKernel.Api;

/// <summary>Página de un listado: <c>{ items, total, pagina, tamano }</c> (02-convenciones).</summary>
public sealed record Paginado<T>(IReadOnlyList<T> Items, int Total, int Pagina, int Tamano);

public static class Paginacion
{
    public const int TamanoPredeterminado = 50;
    public const int TamanoMaximo = 200;

    public static (int Pagina, int Tamano) Normalizar(int? pagina, int? tamano) =>
        (Math.Max(1, pagina ?? 1), Math.Clamp(tamano ?? TamanoPredeterminado, 1, TamanoMaximo));

    public static async Task<Paginado<T>> PaginarAsync<T>(this IQueryable<T> consulta, int? pagina, int? tamano,
        CancellationToken ct)
    {
        var (p, t) = Normalizar(pagina, tamano);
        var total = await consulta.CountAsync(ct);
        var items = await consulta.Skip((p - 1) * t).Take(t).ToListAsync(ct);
        return new Paginado<T>(items, total, p, t);
    }

    public static Paginado<TOut> Map<TIn, TOut>(this Paginado<TIn> pagina, Func<TIn, TOut> map) =>
        new(pagina.Items.Select(map).ToList(), pagina.Total, pagina.Pagina, pagina.Tamano);
}
