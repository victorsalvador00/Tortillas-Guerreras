using TG.Logistica.Dominio;
using TG.Logistica.Publico;

namespace TG.Modules.Tests.Logistica;

public sealed class ReglasLogisticaTests
{
    [Fact]
    [Trait("Regla", "RN-LOG-06")]
    public void SobranteEsperado_CriterioDeAceptacion()
    {
        // Carga 300, vende 260, devolución no vendible 5 (ya salió como merma): esperado 40; cuenta 44 → +4.
        var esperado = ReglasLogistica.SobranteEsperado(300, 260, 0);
        esperado.ShouldBe(40m);
        ReglasLogistica.Diferencia(44, esperado).ShouldBe(4m);
    }

    [Fact]
    [Trait("Regla", "RN-LOG-06")]
    public void SobranteEsperado_SumaDevolucionesVendibles() =>
        ReglasLogistica.SobranteEsperado(100, 70, 5).ShouldBe(35m);

    [Fact]
    [Trait("Regla", "RN-LOG-08")]
    public void DiferenciaEfectivo_RedondeaACentavos() =>
        ReglasLogistica.DiferenciaEfectivo(1499.995m, 1500m).ShouldBe(-0.01m);

    [Theory]
    [Trait("Regla", "RN-LOG-04")]
    [InlineData("Terminado", 2, CondicionRetorno.NoVendible)]
    [InlineData("Terminado", 1, CondicionRetorno.NoVendible)]
    [InlineData("Terminado", 30, CondicionRetorno.Vendible)]
    [InlineData("Terminado", null, CondicionRetorno.Vendible)]
    [InlineData("Reventa", 2, CondicionRetorno.Vendible)]
    public void CondicionPredeterminada_Sup13(string tipo, int? vidaUtil, CondicionRetorno esperada) =>
        ReglasLogistica.CondicionPredeterminada(tipo, vidaUtil).ShouldBe(esperada);

    [Fact]
    public void DiasVisita_DeUnoASiete()
    {
        ReglasLogistica.DiasValidos([1, 7]).ShouldBeTrue();
        ReglasLogistica.DiasValidos([0]).ShouldBeFalse();
        ReglasLogistica.DiasValidos([8]).ShouldBeFalse();
    }

    [Fact]
    [Trait("Regla", "RN-LOG-18")]
    public void SobranteEsperado_RestaLosCambiosEntregados() =>
        ReglasLogistica.SobranteEsperado(20, 0, 1, 3).ShouldBe(18m);

    [Theory]
    [Trait("Regla", "RN-LOG-16")]
    [InlineData(5, 5, 3, 2)]   // retira 5, entrega 3 → debe 2
    [InlineData(5, 0, 3, 0)]   // "no queda pendiente"
    [InlineData(5, 5, 7, 0)]   // entrega de más: no hay pendiente
    [InlineData(5, 1, 0, 1)]   // solo una partida de 1 deja pendiente
    public void NuevoPendiente_LoRetiradoNoEntregado(decimal retirado, decimal conPendiente, decimal entregado, decimal esperado) =>
        ReglasLogistica.NuevoPendiente(retirado, conPendiente, entregado).ShouldBe(esperado);
}
