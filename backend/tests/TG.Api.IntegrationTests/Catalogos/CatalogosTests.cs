using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Catalogos.Publico;

namespace TG.Api.IntegrationTests.Catalogos;

public sealed class CatalogosTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record ProductoCompleto(Guid Id, string Sku, string Tipo, bool EsComprable, bool EsProducible,
        bool EsVendible, bool ManejaLote, bool Activo);

    [Fact]
    public async Task Semilla_ProductosUnidadesYParametros_Existen()
    {
        var cliente = await api.ClienteAsync("vendedor1");
        var tortilla = await cliente.ProductoAsync("PT-TORT-KG");
        tortilla.ManejaLote.ShouldBeTrue();

        var parametros = await cliente.GetOkAsync<List<ParametroInfoPrueba>>("/api/v1/catalogos/parametros");
        parametros.ShouldContain(p => p.Clave == "gps.umbral_distancia_m" && p.Valor == "150");
    }

    [Fact]
    [Trait("Regla", "RN-CAT-01")]
    public async Task Sku_Duplicado_O_CambioConMovimientos_SeRechaza()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync();

        var duplicado = await admin.PostJsonAsync("/api/v1/catalogos/productos", new
        {
            sku = producto.Sku, nombre = "Otro", tipo = "MateriaPrima", unidadBaseId = producto.UnidadBaseId, tasaIva = 0,
        });
        await duplicado.DebeFallarAsync(HttpStatusCode.BadRequest, "CAT-001");

        // Sin movimientos el SKU sí se puede cambiar.
        var nuevoSku = DatosPrueba.Unico("PRB");
        await admin.PutOkAsync<ProductoCompleto>($"/api/v1/catalogos/productos/{producto.Id}", Guardar(producto, nuevoSku));

        await admin.EntradaAsync(await admin.AlmacenAsync("MP"), producto.Id, 10, 5);
        var cambio = await admin.PutAsync($"/api/v1/catalogos/productos/{producto.Id}",
            Contenido(Guardar(producto, DatosPrueba.Unico("PRB"))));
        await cambio.DebeFallarAsync(HttpStatusCode.BadRequest, "CAT-001");
    }

    [Fact]
    [Trait("Regla", "RN-CAT-02")]
    public async Task UnidadBase_ConMovimientos_NoSePuedeCambiar()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync();
        await admin.EntradaAsync(await admin.AlmacenAsync("MP"), producto.Id, 3, 5);

        var cambio = await admin.PutAsync($"/api/v1/catalogos/productos/{producto.Id}",
            Contenido(Guardar(producto, producto.Sku) with { UnidadBaseId = await admin.UnidadAsync("L") }));
        await cambio.DebeFallarAsync(HttpStatusCode.BadRequest, "CAT-002");
    }

    [Fact]
    [Trait("Regla", "RN-CAT-03")]
    [Trait("Regla", "RN-CAT-06")]
    public async Task ConvertirABase_SinConversionODecimalesInvalidos_Falla()
    {
        var cliente = await api.ClienteAsync();
        var maiz = await cliente.ProductoAsync("MP-MAIZ-BCO");
        var tortilla = await cliente.ProductoAsync("PT-TORT-KG");
        var ton = await cliente.UnidadAsync("TON");
        var kg = await cliente.UnidadAsync("KG");
        var paq = await cliente.UnidadAsync("PAQ");

        await api.ConScopeAsync(async sp =>
        {
            var catalogos = sp.GetRequiredService<ICatalogosService>();

            var tonelada = await catalogos.ConvertirABaseAsync(maiz.Id, ton, 1.5m, UsoUnidad.Compra, CancellationToken.None);
            tonelada.Valor.Base.ShouldBe(1500m);

            var sinConversion = await catalogos.ConvertirABaseAsync(tortilla.Id, ton, 1, null, CancellationToken.None);
            sinConversion.Error!.Codigo.ShouldBe("CAT-003");

            var usoIncorrecto = await catalogos.ConvertirABaseAsync(maiz.Id, ton, 1, UsoUnidad.Venta, CancellationToken.None);
            usoIncorrecto.Error!.Codigo.ShouldBe("CAT-003");

            var base1 = await catalogos.ConvertirABaseAsync(tortilla.Id, kg, 1.234m, UsoUnidad.Venta, CancellationToken.None);
            base1.Valor.Base.ShouldBe(1.234m);

            var demasiadosDecimales = await catalogos.ConvertirABaseAsync(tortilla.Id, kg, 1.2345m, null, CancellationToken.None);
            demasiadosDecimales.Error!.Codigo.ShouldBe("CAT-006");

            var paqueteFraccion = await catalogos.ConvertirABaseAsync(tortilla.Id, paq, 1.5m, UsoUnidad.Venta, CancellationToken.None);
            paqueteFraccion.Error!.Codigo.ShouldBe("CAT-006");
            return true;
        });
    }

    [Fact]
    [Trait("Regla", "RN-CAT-04")]
    public async Task Banderas_SeProponenPorTipo_YSonEditables()
    {
        var admin = await api.ClienteAsync();
        var reventa = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA");
        var detalle = await admin.GetOkAsync<ProductoCompleto>($"/api/v1/catalogos/productos/{reventa.Id}");
        detalle.EsComprable.ShouldBeTrue();
        detalle.EsVendible.ShouldBeTrue();
        detalle.EsProducible.ShouldBeFalse();

        var masaVendible = await admin.CrearProductoAsync(tipo: "Semiterminado", vendible: true);
        var masa = await admin.GetOkAsync<ProductoCompleto>($"/api/v1/catalogos/productos/{masaVendible.Id}");
        masa.EsProducible.ShouldBeTrue();
        masa.EsVendible.ShouldBeTrue();
    }

    [Fact]
    [Trait("Regla", "RN-CAT-05")]
    public async Task Desactivar_ConExistencia_SeRechaza()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync();
        var mp = await admin.AlmacenAsync("MP");
        await admin.EntradaAsync(mp, producto.Id, 4, 2);

        var conExistencia = await admin.PostJsonAsync($"/api/v1/catalogos/productos/{producto.Id}/desactivar");
        await conExistencia.DebeFallarAsync(HttpStatusCode.Conflict, "CAT-005");

        (await admin.SalidaAsync(mp, producto.Id, 4)).EnsureSuccessStatusCode();
        var desactivado = await admin.PostOkAsync<ProductoCompleto>($"/api/v1/catalogos/productos/{producto.Id}/desactivar");
        desactivado.Activo.ShouldBeFalse();
    }

    [Fact]
    public async Task Escritura_SinPermiso_Recibe403()
    {
        var vendedor = await api.ClienteAsync("vendedor1");
        var respuesta = await vendedor.PostJsonAsync("/api/v1/catalogos/motivos-merma",
            new { clave = "X", nombre = "X", aplicaProduccion = true });
        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Parametro_ValorInvalido_SeRechaza_YValidoSeGuarda()
    {
        var admin = await api.ClienteAsync();
        var invalido = await admin.PutAsync("/api/v1/catalogos/parametros/gps.umbral_distancia_m", Contenido(new { valor = "abc" }));
        await invalido.DebeFallarAsync(HttpStatusCode.BadRequest, "CAT-009");

        await admin.PutOkAsync<ParametroInfoPrueba>("/api/v1/catalogos/parametros/ruta.dias_retencion_local", new { valor = "10" });
        var valor = await api.ConScopeAsync(sp => sp.GetRequiredService<ICatalogosService>()
            .ObtenerParametroAsync(ClavesParametro.RutaDiasRetencion, 7, CancellationToken.None));
        valor.ShouldBe(10);
    }

    private sealed record ParametroInfoPrueba(string Clave, string Valor);

    private sealed record GuardarProducto(string Sku, string Nombre, string Tipo, Guid UnidadBaseId, decimal TasaIva);

    private static GuardarProducto Guardar(ProductoPrueba p, string sku) => new(sku, p.Nombre, "MateriaPrima", p.UnidadBaseId, 0m);

    private static StringContent Contenido(object cuerpo) =>
        new(System.Text.Json.JsonSerializer.Serialize(cuerpo, ExtensionesHttp.Json), System.Text.Encoding.UTF8, "application/json");
}
