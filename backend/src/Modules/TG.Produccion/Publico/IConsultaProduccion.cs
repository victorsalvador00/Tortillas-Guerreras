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

/// <summary>RN-WEB-01: lo que Producción tiene esperando al usuario, para los globos del menú.</summary>
public interface IPendientesProduccion
{
    /// <summary>Renglones del plan sugerido (RN-PRO-13) por producir con faltante, como abre la pantalla (con referencia).</summary>
    Task<int> PlanPorProgramarAsync(DateOnly fechaEntrega, CancellationToken ct);
}
