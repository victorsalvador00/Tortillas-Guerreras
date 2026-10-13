namespace TG.SharedKernel.Dominio;

/// <summary>Redondeos de negocio: importes a 2 decimales, AwayFromZero, por partida y luego suma.</summary>
public static class Dinero
{
    public static decimal Redondear(decimal importe) => Math.Round(importe, 2, MidpointRounding.AwayFromZero);

    public static decimal RedondearCantidad(decimal cantidad) => Math.Round(cantidad, 4, MidpointRounding.AwayFromZero);

    public static decimal RedondearCosto(decimal costo) => Math.Round(costo, 6, MidpointRounding.AwayFromZero);
}
