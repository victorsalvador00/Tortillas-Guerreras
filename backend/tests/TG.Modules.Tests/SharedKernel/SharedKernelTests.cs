using Microsoft.Extensions.Time.Testing;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Tiempo;

namespace TG.Modules.Tests.SharedKernel;

public sealed class FoliosTests
{
    [Theory]
    [InlineData("OC", 1, "OC-000001")]
    [InlineData("REC", 45, "REC-000045")]
    [InlineData("OP", 123456, "OP-123456")]
    [InlineData("VTA", 1234567, "VTA-1234567")]
    public void Formatear_Consecutivo_RellenaASeisDigitos(string prefijo, long consecutivo, string esperado) =>
        GeneradorFolios.Formatear(prefijo, consecutivo).ShouldBe(esperado);

    [Fact]
    [Trait("Regla", "RN-VEN-09")]
    public void FormatearApp_RutaDispositivoConsecutivo_FormatoDeRuta()
    {
        var folio = GeneradorFolios.FormatearApp("R01", "D03", 123);

        folio.ShouldBe("R01-D03-000123");
        GeneradorFolios.EsFolioApp(folio).ShouldBeTrue();
        GeneradorFolios.EsFolioApp("VTA-000001").ShouldBeFalse();
    }

    [Theory]
    [InlineData("compras", "OC", "compras.folio_oc_seq")]
    [InlineData("inventario", "TR", "inventario.folio_tr_seq")]
    public void NombreSecuencia_EsquemaYPrefijo_SecuenciaPorPrefijo(string esquema, string prefijo, string esperado) =>
        GeneradorFolios.NombreSecuencia(esquema, prefijo).ShouldBe(esperado);

    [Fact]
    public void NombreSecuencia_ConCaracteresInvalidos_Falla() =>
        Should.Throw<ArgumentException>(() => GeneradorFolios.NombreSecuencia("compras;drop", "OC"));
}

public sealed class FechaOperativaTests
{
    private static readonly DateTimeOffset AntesDeMedianoche = new(2026, 11, 3, 23, 59, 0, TimeSpan.FromHours(-6));
    private static readonly DateTimeOffset DespuesDeMedianoche = new(2026, 11, 4, 0, 1, 0, TimeSpan.FromHours(-6));

    [Fact]
    public void De_2359HoraDeMexico_EsElMismoDia()
    {
        var fecha = new FechaOperativa(TimeProvider.System);

        // 23:59 en México son las 05:59 UTC del día siguiente.
        AntesDeMedianoche.UtcDateTime.Day.ShouldBe(4);
        fecha.De(AntesDeMedianoche).ShouldBe(new DateOnly(2026, 11, 3));
    }

    [Fact]
    public void De_0001HoraDeMexico_EsElDiaSiguiente()
    {
        var fecha = new FechaOperativa(TimeProvider.System);

        fecha.De(DespuesDeMedianoche).ShouldBe(new DateOnly(2026, 11, 4));
    }

    [Fact]
    public void Hoy_UsaElRelojInyectado()
    {
        var reloj = new FakeTimeProvider(AntesDeMedianoche.ToUniversalTime());
        var fecha = new FechaOperativa(reloj);

        fecha.Hoy().ShouldBe(new DateOnly(2026, 11, 3));
        reloj.Advance(TimeSpan.FromMinutes(2));
        fecha.Hoy().ShouldBe(new DateOnly(2026, 11, 4));
    }

    [Fact]
    public void InicioUtc_DiaOperativo_SeisHorasUtc()
    {
        var fecha = new FechaOperativa(TimeProvider.System);

        fecha.InicioUtc(new DateOnly(2026, 11, 4)).ShouldBe(new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.Zero));
    }
}

public sealed class DineroYResultadoTests
{
    [Theory]
    [InlineData(2.345, 2.35)]
    [InlineData(2.344, 2.34)]
    [InlineData(-2.345, -2.35)]
    [InlineData(0.005, 0.01)]
    public void Redondear_MitadSeAlejaDeCero(decimal importe, decimal esperado) =>
        Dinero.Redondear(importe).ShouldBe(esperado);

    [Fact]
    public void Result_Falla_NoExponeValor()
    {
        Result<int> resultado = new Error("X-001", "Falla");

        resultado.EsExito.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => resultado.Valor);
    }

    [Fact]
    public void Id_Nuevo_EsUuidV7()
    {
        Id.Nuevo().Version.ShouldBe(7);
    }
}
