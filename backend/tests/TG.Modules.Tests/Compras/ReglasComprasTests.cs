using TG.Compras.Dominio;

namespace TG.Modules.Tests.Compras;

public sealed class ReglasComprasTests
{
    [Theory]
    [Trait("Regla", "RN-COM-04")]
    [InlineData(100, 0, 105, 0.05, false)]
    [InlineData(100, 0, 105.0001, 0.05, true)]
    [InlineData(100, 60, 45, 0.05, false)]
    [InlineData(100, 60, 46, 0.05, true)]
    [InlineData(100, 0, 100, 0, false)]
    public void ExcedeTolerancia(decimal pedida, decimal recibida, decimal nueva, decimal tolerancia, bool esperado) =>
        ReglasCompras.ExcedeTolerancia(pedida, recibida, nueva, tolerancia).ShouldBe(esperado);

    [Theory]
    [Trait("Regla", "RN-COM-05")]
    [InlineData(6500, 1000, 6.5)]
    [InlineData(95, 25, 3.8)]
    [InlineData(10, 3, 3.333333)]
    public void CostoUnitarioBase_PrecioEntreFactor(decimal precio, decimal factor, decimal esperado) =>
        ReglasCompras.CostoUnitarioBase(precio, factor).ShouldBe(esperado);

    [Fact]
    public void Importes_RedondeaPorPartida()
    {
        var (importe, iva) = ReglasCompras.Importes(3, 10.555m, 0.16m);
        importe.ShouldBe(31.67m);
        iva.ShouldBe(5.07m);
    }

    [Fact]
    public void EstadoPorRecepciones_SegunLoRecibido()
    {
        var partidas = new List<OrdenCompraPartida>
        {
            new() { CantidadBase = 10, CantidadRecibidaBase = 0 },
            new() { CantidadBase = 5, CantidadRecibidaBase = 0 },
        };
        ReglasCompras.EstadoPorRecepciones(partidas).ShouldBe(EstadoOrdenCompra.Autorizada);
        partidas[0].CantidadRecibidaBase = 10;
        ReglasCompras.EstadoPorRecepciones(partidas).ShouldBe(EstadoOrdenCompra.RecibidaParcial);
        partidas[1].CantidadRecibidaBase = 5.2m;
        ReglasCompras.EstadoPorRecepciones(partidas).ShouldBe(EstadoOrdenCompra.Recibida);
    }
}
