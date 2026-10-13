using TG.Ventas.Dominio;

namespace TG.Modules.Tests.Ventas;

public sealed class CalculoVentasTests
{
    [Fact]
    [Trait("Regla", "RN-VEN-02")]
    public void Partida_IepsSobreImporte_IvaSobreImporteMasIeps_RedondeoPorPartida()
    {
        var (importe, ieps, iva) = CalculoImportes.Partida(3m, 16.67m, 0.16m, 0.08m);

        importe.ShouldBe(50.01m);
        ieps.ShouldBe(4.00m);
        iva.ShouldBe(8.64m);
    }

    [Theory]
    [Trait("Regla", "RN-VEN-02")]
    [InlineData(5, 22, 0, 110)]
    [InlineData(1.234, 22, 0, 27.15)]
    [InlineData(0.125, 1, 0, 0.13)]
    public void Partida_ImporteRedondeaAwayFromZero(decimal cantidad, decimal precio, decimal iva, decimal esperado) =>
        CalculoImportes.Partida(cantidad, precio, iva, null).Importe.ShouldBe(esperado);

    [Theory]
    [Trait("Regla", "RN-VEN-02")]
    [InlineData(100, 100.05, false)]
    [InlineData(100, 100.06, true)]
    [InlineData(100, null, false)]
    public void TotalDifiere_ToleranciaCincoCentavos(decimal servidor, double? app, bool esperado) =>
        CalculoImportes.TotalDifiere(servidor, app is null ? null : (decimal)app).ShouldBe(esperado);

    [Fact]
    [Trait("Regla", "RN-VEN-05")]
    public void Haversine_UnMilesimoDeGradoDeLatitud_UnosCientoOnceMetros()
    {
        var d = Geo.DistanciaMetros(19.18m, -96.14m, 19.181m, -96.14m);
        d.ShouldBeInRange(110m, 112m);

        Geo.DistanciaMetros(19.18m, -96.14m, 19.1836m, -96.14m).ShouldBeInRange(395m, 405m);
        Geo.DistanciaMetros(19.18m, -96.14m, 19.18m, -96.14m).ShouldBe(0m);
    }
}
