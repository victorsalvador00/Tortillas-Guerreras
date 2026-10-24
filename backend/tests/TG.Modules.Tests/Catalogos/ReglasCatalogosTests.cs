using TG.Catalogos.Aplicacion;
using TG.Catalogos.Dominio;
using TG.Catalogos.Publico;

namespace TG.Modules.Tests.Catalogos;

public sealed class ReglasCatalogosTests
{
    private static readonly Guid Kg = Guid.CreateVersion7();
    private static readonly Guid Paq = Guid.CreateVersion7();
    private static readonly Guid Caj = Guid.CreateVersion7();

    private static ConversionInfo Conversion(Guid unidad, string clave, decimal factor, bool venta = false, bool traslado = false) =>
        new(unidad, clave, clave, "XX", 0, factor, false, venta, false, venta, traslado, traslado);

    private static ProductoInfo Producto(params ConversionInfo[] conversiones) => new(
        Guid.CreateVersion7(), "PV01", "Tortilla T-800", "T-800", "Terminado", Kg, "KG", "KGM", 3, false, true, true, true, 2,
        null, "02", 0m, null, null, true, null, true, 1, conversiones);

    [Theory]
    [Trait("Regla", "RN-CAT-09")]
    [InlineData(50, 2, 2)]
    [InlineData(48, 2, 0)]
    [InlineData(23, 0, 23)]
    [InlineData(0, 0, 0)]
    public void DesglosarCajas_PaquetesPorCaja24(decimal paquetes, decimal cajas, decimal resto)
    {
        // Paquete de 0.8 kg y caja de 24 paquetes (19.2 kg) [SUP-26].
        var producto = Producto(Conversion(Paq, "PAQ", 0.8m, venta: true), Conversion(Caj, "CAJ", 19.2m, traslado: true));

        var desglose = DesgloseCajas.Calcular(producto, paquetes * 0.8m)!;

        desglose.Cajas.ShouldBe(cajas);
        desglose.Paquetes.ShouldBe(resto);
        desglose.UnidadTraslado.ShouldBe("CAJ");
        desglose.UnidadVenta.ShouldBe("PAQ");
    }

    [Fact]
    [Trait("Regla", "RN-CAT-09")]
    public void DesglosarCajas_SinUnidadDeTraslado_EsNulo_YSinVentaUsaLaBase()
    {
        DesgloseCajas.Calcular(Producto(Conversion(Paq, "PAQ", 1m, venta: true)), 10m).ShouldBeNull();

        var soloBase = DesgloseCajas.Calcular(Producto(Conversion(Caj, "CAJ", 15m, traslado: true)), 32m)!;
        soloBase.ToString().ShouldBe("2 CAJ + 2 KG");
    }

    [Fact]
    [Trait("Regla", "RN-CAT-08")]
    public void NombreCorto_MasDe15Caracteres_SeRechaza()
    {
        var validator = new GuardarProductoValidator();
        var request = Request() with { NombreCorto = "TORTILLA BLANCA 1" };

        var resultado = validator.Validate(request);

        resultado.IsValid.ShouldBeFalse();
        resultado.Errors.ShouldContain(e => e.ErrorCode == "CAT-012");
        validator.Validate(Request() with { NombreCorto = "FIT BLANCA" }).IsValid.ShouldBeTrue();
    }

    [Fact]
    [Trait("Regla", "RN-CAT-10")]
    public void DiasTolerancia_SoloConProductoACobro_YMayorQueCero()
    {
        var validator = new GuardarProductoValidator();

        validator.Validate(Request() with { DiasToleranciaDevolucion = 11 }).IsValid.ShouldBeFalse();
        validator.Validate(Request() with { AplicaProductoACobro = true, DiasToleranciaDevolucion = 0 }).IsValid.ShouldBeFalse();
        validator.Validate(Request() with { AplicaProductoACobro = true, DiasToleranciaDevolucion = 11 }).IsValid.ShouldBeTrue();
        validator.Validate(Request() with { AplicaProductoACobro = true }).IsValid.ShouldBeTrue();
    }

    [Fact]
    [Trait("Regla", "RN-CAT-09")]
    public void Conversiones_DosPredeterminadasDeTraslado_SeRechazan()
    {
        var validator = new GuardarProductoValidator();
        var request = Request() with
        {
            Conversiones =
            [
                new ConversionRequest(Paq, 1m, false, true, false, true),
                new ConversionRequest(Caj, 24m, false, false, false, false, true, true),
                new ConversionRequest(Guid.CreateVersion7(), 12m, false, false, false, false, true, true),
            ],
        };

        validator.Validate(request).IsValid.ShouldBeFalse();
    }

    private static GuardarProductoRequest Request() => new("PV01", "Tortilla T-800", null, TipoProducto.Terminado, Kg, null, null,
        null, null, 2, null, null, 0m, null, null, null, []);
}
