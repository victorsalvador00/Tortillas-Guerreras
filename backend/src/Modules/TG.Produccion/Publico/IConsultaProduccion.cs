namespace TG.Produccion.Publico;

public sealed record ResumenProductoProduccion(Guid ProductoId, decimal Planeado, decimal Producido, decimal Merma);

/// <param name="MermaPct">Merma / (producido + merma) de las órdenes terminadas.</param>
public sealed record ResumenProduccion(DateOnly Desde, DateOnly Hasta, int Ordenes, int Terminadas, decimal Producido,
    decimal Merma, decimal MermaPct, IReadOnlyList<ResumenProductoProduccion> PorProducto);

/// <summary>Lo que el tablero y otros módulos pueden consultar de Producción.</summary>
public interface IConsultaProduccion
{
    Task<ResumenProduccion> ResumenAsync(DateOnly desde, DateOnly hasta, CancellationToken ct);
}
