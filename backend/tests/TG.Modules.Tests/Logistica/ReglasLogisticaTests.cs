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

    [Fact]
    [Trait("Regla", "RN-LOG-08")]
    public void EfectivoEsperado_RestaGastosAutorizados() =>
        ReglasLogistica.EfectivoEsperado(1000m, 250.50m, 80m).ShouldBe(1170.50m);

    [Theory]
    [Trait("Regla", "RN-LOG-20")]
    [InlineData(EstadoLiquidacion.Abierta, true, false)]
    [InlineData(EstadoLiquidacion.Abierta, false, false)]
    [InlineData(EstadoLiquidacion.ProductoConfirmado, true, false)]
    [InlineData(EstadoLiquidacion.ProductoConfirmado, false, true)]
    [InlineData(EstadoLiquidacion.Revisada, true, true)]
    [InlineData(EstadoLiquidacion.Cerrada, false, false)]
    public void PuedeCerrar_SegunEtapaYRevision(EstadoLiquidacion estado, bool requiereRevision, bool esperado) =>
        ReglasLogistica.PuedeCerrar(estado, requiereRevision).ShouldBe(esperado);

    [Fact]
    public void Repartir_ProporcionalYElRedondeoAlUltimo()
    {
        ReglasLogistica.Repartir(100m, [1m, 1m, 1m]).ShouldBe([33.33m, 33.33m, 33.34m]);
        ReglasLogistica.Repartir(50m, [3m, 1m]).ShouldBe([37.50m, 12.50m]);
        ReglasLogistica.Repartir(10m, []).ShouldBeEmpty();
    }

    [Theory]
    [Trait("Regla", "RN-LOG-24")]
    [InlineData("261011-ABC", null, null, 13)]          // lote AAMMDD
    [InlineData("SINFECHA", "2026-10-20", 2, 6)]        // caducidad − vida útil = 18-oct
    [InlineData("SINFECHA", null, 2, null)]
    [InlineData("269999-X", "2026-10-23", 1, 2)]        // prefijo que no es fecha
    public void AntiguedadLote_PorFechaDelLoteOCaducidad(string lote, string? caducidad, int? vida, int? esperado) =>
        ReglasLogistica.AntiguedadLote(lote, caducidad is null ? null : DateOnly.Parse(caducidad), vida, new DateOnly(2026, 10, 24))
            .ShouldBe(esperado);

    [Theory]
    [Trait("Regla", "RN-LOG-24")]
    [InlineData(13, 11, 3)]
    [InlineData(11, 11, 0)]
    public void ACobroSugerido_SoloSiSuperaLaTolerancia(int antiguedad, int tolerancia, decimal esperado) =>
        ReglasLogistica.ACobroSugerido(3m, antiguedad, tolerancia).ShouldBe(esperado);

    [Theory]
    [Trait("Regla", "RN-LOG-28")]
    [InlineData("2026-10-24", 1, "2026-10-19")]   // sábado → lunes
    [InlineData("2026-10-25", 1, "2026-10-19")]   // domingo → lunes anterior
    [InlineData("2026-10-19", 1, "2026-10-19")]
    [InlineData("2026-10-24", 7, "2026-10-18")]   // semana de domingo a sábado
    public void InicioSemana_SegunDiaDeInicio(string fecha, int dia, string esperado) =>
        ReglasLogistica.InicioSemana(DateOnly.Parse(fecha), dia).ShouldBe(DateOnly.Parse(esperado));

    [Fact]
    [Trait("Regla", "RN-LOG-25")]
    public void ImporteCargo_Redondeado() => ReglasLogistica.ImporteCargo(3m, 12.3456m).ShouldBe(37.04m);
}
