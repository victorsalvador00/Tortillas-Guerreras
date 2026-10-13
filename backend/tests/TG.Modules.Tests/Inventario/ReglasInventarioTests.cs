using TG.Inventario.Dominio;
using TG.Inventario.Publico;

namespace TG.Modules.Tests.Inventario;

public sealed class ReglasInventarioTests
{
    [Theory]
    [Trait("Regla", "RN-INV-05")]
    [InlineData(100, 10, 100, 20, 15)]
    [InlineData(0, 0, 1000, 6.5, 6.5)]
    [InlineData(-5, 10, 20, 12, 12)]
    [InlineData(300, 6.5, 100, 7.5, 6.75)]
    public void CostoPromedioPonderado_Formula(decimal existencia, decimal actual, decimal cantidad, decimal entrada, decimal esperado) =>
        ReglasInventario.CostoPromedioPonderado(existencia, actual, cantidad, entrada).ShouldBe(esperado);

    [Fact]
    [Trait("Regla", "RN-INV-07")]
    public void OrdenFefo_PrimeroCaducidadLuegoFechaDeEntrada()
    {
        var hoy = DateTimeOffset.UtcNow;
        var lotes = new[]
        {
            new Existencia { Lote = "SIN-CAD-VIEJO", FechaEntrada = hoy.AddDays(-5) },
            new Existencia { Lote = "CAD-10", FechaCaducidad = new DateOnly(2026, 11, 10), FechaEntrada = hoy },
            new Existencia { Lote = "CAD-05", FechaCaducidad = new DateOnly(2026, 11, 5), FechaEntrada = hoy },
            new Existencia { Lote = "SIN-CAD-NUEVO", FechaEntrada = hoy },
        };

        ReglasInventario.OrdenFefo(lotes).Select(l => l.Lote)
            .ShouldBe(["CAD-05", "CAD-10", "SIN-CAD-VIEJO", "SIN-CAD-NUEVO"]);
    }

    [Theory]
    [InlineData(TipoMovimiento.EntradaCompra, true)]
    [InlineData(TipoMovimiento.RetornoCliente, true)]
    [InlineData(TipoMovimiento.Venta, false)]
    [InlineData(TipoMovimiento.Merma, false)]
    public void EsEntrada_PorTipo(TipoMovimiento tipo, bool esperado) => ReglasInventario.EsEntrada(tipo).ShouldBe(esperado);

    [Fact]
    [Trait("Regla", "RN-INV-05")]
    public void RecalculaCosto_SoloEntradasConCosto()
    {
        ReglasInventario.RecalculaCosto(TipoMovimiento.EntradaCompra, null).ShouldBeTrue();
        ReglasInventario.RecalculaCosto(TipoMovimiento.AjusteEntrada, 5m).ShouldBeTrue();
        ReglasInventario.RecalculaCosto(TipoMovimiento.AjusteEntrada, null).ShouldBeFalse();
        ReglasInventario.RecalculaCosto(TipoMovimiento.TraspasoEntrada, 5m).ShouldBeFalse();
    }
}
