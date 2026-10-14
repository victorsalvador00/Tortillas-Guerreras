using System.Text;
using TG.Facturacion.Aplicacion;
using TG.Facturacion.Dominio;
using TG.Facturacion.Infraestructura;
using TG.Facturacion.Publico;

namespace TG.Modules.Tests.Facturacion;

public sealed class ReglasFacturacionTests
{
    [Theory]
    [Trait("Regla", "RN-FAC-02")]
    [InlineData("2026-11-03", "2026-11-30", "fin_de_mes", true)]
    [InlineData("2026-11-03", "2026-12-01", "fin_de_mes", false)]
    [InlineData("2026-02-27", "2026-02-28", "fin_de_mes", true)]
    [InlineData("2026-11-03", "2026-11-10", "7", true)]
    [InlineData("2026-11-03", "2026-11-11", "7", false)]
    public void AutofacturaVigente_SegunLimite(string venta, string hoy, string limite, bool esperado) =>
        ReglasFacturacion.AutofacturaVigente(DateOnly.Parse(venta, System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.Parse(hoy, System.Globalization.CultureInfo.InvariantCulture), limite).ShouldBe(esperado);

    [Fact]
    public void SiguienteIntento_Esperas1_5_15_60_YMaximo10()
    {
        var ahora = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        ReglasFacturacion.SiguienteIntento(1, ahora).ShouldBe(ahora.AddMinutes(1));
        ReglasFacturacion.SiguienteIntento(2, ahora).ShouldBe(ahora.AddMinutes(5));
        ReglasFacturacion.SiguienteIntento(3, ahora).ShouldBe(ahora.AddMinutes(15));
        ReglasFacturacion.SiguienteIntento(4, ahora).ShouldBe(ahora.AddMinutes(60));
        ReglasFacturacion.SiguienteIntento(9, ahora).ShouldBe(ahora.AddMinutes(60));
        ReglasFacturacion.SiguienteIntento(10, ahora).ShouldBeNull();
    }

    [Theory]
    [Trait("Regla", "RN-FAC-07")]
    [InlineData(false, false, "PUE", "01")]
    [InlineData(false, true, "PUE", "03")]
    [InlineData(true, false, "PPD", "99")]
    public void MetodoYForma_SegunCondicion(bool credito, bool transferencia, string metodo, string forma) =>
        ReglasFacturacion.MetodoYForma(credito, transferencia).ShouldBe((metodo, forma));

    [Theory]
    [Trait("Regla", "RN-FAC-03")]
    [InlineData("XAXX010101000", true)]
    [InlineData("AAA010101AAA", true)]
    [InlineData("AAA0101", false)]
    [InlineData("AAA011301AAA", false)]
    public void Rfc_Formato(string rfc, bool valido) => CatalogosSat.RfcValido(rfc).ShouldBe(valido);

    [Fact]
    [Trait("Regla", "RN-FAC-03")]
    public void Validador_RechazaRegimenUsoYCodigoPostalFueraDeCatalogo()
    {
        var r = new SolicitudAutofacturaValidator().Validate(new SolicitudAutofacturaRequest("R01-D01-000001", 10, "AAA010101AAA",
            "X", "999", "123", "Z99", "correo", Guid.NewGuid(), 1));
        r.Errors.Select(e => e.PropertyName).ShouldBe(["RegimenFiscal", "CodigoPostal", "UsoCfdi", "Email"], ignoreOrder: true);
    }

    [Fact]
    public async Task ProveedorSimulado_GeneraXmlYPdf()
    {
        var factura = new FacturaParaTimbrar(Guid.NewGuid(), "Ingreso", "F", "1", DateTimeOffset.UtcNow,
            new EmisorParaTimbrar("EKU9003173C9", "ESCUELA KEMPER URGATE", "601", "91700"),
            new ReceptorParaTimbrar("XAXX010101000", "PÚBLICO EN GENERAL", "616", "91700", "S01"), "PUE", "01", null, null, null,
            100m, 16m, 8m, 124m,
            [new ConceptoParaTimbrar("01010101", "PT-TOTOPO", 2, "XPK", "Totopo (bolsa 500 g)", 50m, 100m, "02", 0.16m, 16m, 0.08m, 8m)]);
        var resultado = await new ProveedorFacturacionSimulado(TimeProvider.System).TimbrarAsync(factura, CancellationToken.None);

        resultado.Exito.ShouldBeTrue();
        var xml = Encoding.UTF8.GetString(resultado.Xml!);
        xml.ShouldContain("Version=\"4.0\"");
        xml.ShouldContain("Impuesto=\"003\"");
        xml.ShouldContain(resultado.UuidFiscal!.Value.ToString().ToUpperInvariant());
        Encoding.ASCII.GetString(resultado.Pdf![..8]).ShouldStartWith("%PDF-1.4");
    }
}
