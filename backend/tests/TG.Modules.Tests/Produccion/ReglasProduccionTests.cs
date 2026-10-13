using TG.Produccion.Dominio;

namespace TG.Modules.Tests.Produccion;

public sealed class ReglasProduccionTests
{
    [Fact]
    [Trait("Regla", "RN-PRO-02")]
    public void ConsumoTeorico_Tortilla176Kg_Requiere200KgDeMasa() =>
        ReglasProduccion.ConsumoTeorico(100m, 176m, 88m).ShouldBe(200m);

    [Theory]
    [Trait("Regla", "RN-PRO-03")]
    [InlineData(null, 200, 176, 170, 193.1818)]
    [InlineData(195.5, 200, 176, 170, 195.5)]
    public void ConsumoReal_SinCaptura_UsaTeoricoRecalculadoConProducida(double? capturado, decimal teorico, decimal planeada,
        decimal producida, decimal esperado) =>
        ReglasProduccion.ConsumoReal((decimal?)capturado, teorico, planeada, producida).ShouldBe(esperado);

    [Fact]
    [Trait("Regla", "RN-PRO-05")]
    public void CostoUnitario_ConsumosEntreProducida() =>
        ReglasProduccion.CostoUnitario(850m, 170m).ShouldBe(5m);

    [Theory]
    [Trait("Regla", "RN-PRO-08")]
    [InlineData(100, 106, 0.06, true)]
    [InlineData(100, 95, -0.05, false)]
    [InlineData(100, 94, -0.06, true)]
    public void Variacion_SeMarcaSiSuperaUmbral(decimal teorico, decimal real, decimal esperada, bool fuera)
    {
        var variacion = ReglasProduccion.Variacion(teorico, real);
        variacion.ShouldBe(esperada);
        ReglasProduccion.FueraDeUmbral(variacion, 0.05m).ShouldBe(fuera);
        ReglasProduccion.Variacion(0, 5).ShouldBeNull();
    }

    [Fact]
    [Trait("Regla", "RN-PRO-10")]
    public void Lote_FechaYFolioSinGuion_YCaducidadPorVidaUtil()
    {
        ReglasProduccion.Lote(new DateOnly(2026, 11, 3), "OP-000001").ShouldBe("261103-OP000001");
        ReglasProduccion.Caducidad(new DateOnly(2026, 11, 3), 2).ShouldBe(new DateOnly(2026, 11, 5));
        ReglasProduccion.Caducidad(new DateOnly(2026, 11, 3), null).ShouldBeNull();
    }
}
