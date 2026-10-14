using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TG.Api.IntegrationTests.Infraestructura;

namespace TG.Api.IntegrationTests.Importacion;

public sealed class ImportacionTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record ErrorFila(int Fila, string? Campo, string Mensaje);

    private sealed record Resultado(bool Aplicado, int Filas, int Creados, int Actualizados, List<ErrorFila> Errores);

    private static MultipartFormDataContent Archivo(string csv)
    {
        var contenido = new ByteArrayContent(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray());
        contenido.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { contenido, "archivo", "datos.csv" } };
    }

    private static async Task<Resultado> ImportarAsync(HttpClient cliente, string entidad, string csv, bool validarSolo = false)
    {
        var respuesta = await cliente.PostAsync($"/api/v1/importacion/{entidad}?validarSolo={validarSolo.ToString().ToLowerInvariant()}", Archivo(csv));
        return await respuesta.LeerAsync<Resultado>();
    }

    [Fact]
    public async Task Productos_AltaYActualizacionPorSku_ConConversiones()
    {
        var admin = await api.ClienteAsync();
        var sku = DatosPrueba.Unico("IMP");
        var csv = $"""
            SKU;Nombre;Tipo;Unidad base;Conversiones;IVA pct;Stock mínimo
            {sku};Harina importada;MateriaPrima;KG;BUL=20:compra;0;50
            PT-TORT-KG;Tortilla de maíz;Terminado;KG;PAQ=1:venta*;0;150
            """;

        var r = await ImportarAsync(admin, "productos", csv);

        r.Errores.ShouldBeEmpty();
        r.Aplicado.ShouldBeTrue();
        r.Creados.ShouldBe(1);
        r.Actualizados.ShouldBe(1);
        var producto = await admin.ProductoAsync(sku);
        producto.Nombre.ShouldBe("Harina importada");
    }

    [Fact]
    public async Task VistaPrevia_NoGuardaCambios()
    {
        var admin = await api.ClienteAsync();
        var codigo = DatosPrueba.Unico("PRV");
        var r = await ImportarAsync(admin, "proveedores", $"codigo,razon_social\n{codigo},Proveedor de prueba\n", validarSolo: true);

        r.Errores.ShouldBeEmpty();
        r.Aplicado.ShouldBeFalse();
        r.Creados.ShouldBe(1);
        var lista = await admin.GetOkAsync<PaginaPrueba<IdCodigo>>($"/api/v1/compras/proveedores?buscar={codigo}");
        lista.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ArchivoConErrores_NoImportaNinguna_YReportaFilaYCampo()
    {
        var admin = await api.ClienteAsync();
        var buena = DatosPrueba.Unico("IMP");
        var csv = $"""
            sku,nombre,tipo,unidad_base
            {buena},Producto bueno,Empaque,PZA
            ,Sin sku,Empaque,PZA
            {DatosPrueba.Unico("IMP")},Unidad mala,Empaque,XYZ
            """;

        var r = await ImportarAsync(admin, "productos", csv);

        r.Aplicado.ShouldBeFalse();
        r.Errores.ShouldContain(e => e.Fila == 3 && e.Campo == "sku");
        r.Errores.ShouldContain(e => e.Fila == 4 && e.Campo == "unidad_base");
        var pagina = await admin.GetOkAsync<PaginaPrueba<ProductoPrueba>>($"/api/v1/catalogos/productos?buscar={buena}");
        pagina.Items.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Regla", "RN-INV-01")]
    public async Task ExistenciasIniciales_EntranPorKardexComoAjusteEntradaConCosto()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync(tipo: "Terminado", manejaLote: true);
        var csv = $"""
            almacen,sku,lote,caducidad,cantidad,costo_unitario
            PT,{producto.Sku},L-INI,2026-11-20,120,9.80
            MP,MP-SAL,,,40,"3,50"
            """;

        var r = await ImportarAsync(admin, "existencias", csv);

        r.Errores.ShouldBeEmpty();
        r.Aplicado.ShouldBeTrue();
        (await admin.ExistenciaAsync(await admin.AlmacenAsync("PT"), producto.Id, "L-INI")).ShouldBe(120m);
        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>($"/api/v1/inventario/kardex?productoId={producto.Id}");
        var movimiento = kardex.Items.Single();
        movimiento.Tipo.ShouldBe("AjusteEntrada");
        movimiento.DocumentoTipo.ShouldBe("Ajuste");
        movimiento.CostoUnitario.ShouldBe(9.8m);
    }

    [Fact]
    public async Task Recetas_SeAgrupanPorProductoYSePublican()
    {
        var admin = await api.ClienteAsync();
        var csv = """
            producto_sku,nombre,rendimiento,merma_esperada_pct,componente_sku,componente_unidad,componente_cantidad,publicar
            PT-MASA-KG,Masa para venta importada,100,0,SEM-MASA,KG,100,sí
            """;
        var r = await ImportarAsync(admin, "recetas", csv);
        r.Errores.ShouldBeEmpty();
        r.Creados.ShouldBe(1);
    }

    [Fact]
    public async Task Clientes_ConCanalPorClave()
    {
        var admin = await api.ClienteAsync();
        var csv = "nombre,canal,requiere_factura,rfc,razon_social\nTienda importada,TRAD,no,,\n";
        var r = await ImportarAsync(admin, "clientes", csv);
        r.Errores.ShouldBeEmpty();
        r.Creados.ShouldBe(1);
    }

    [Fact]
    public async Task Plantilla_SeDescarga_YSinPermisoNoSeImporta()
    {
        var vendedor = await api.ClienteAsync("vendedor1");
        var plantilla = await vendedor.GetAsync("/api/v1/importacion/existencias/plantilla");
        plantilla.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await plantilla.Content.ReadAsStringAsync()).ShouldContain("almacen,sku,lote,caducidad,cantidad,costo_unitario");

        var respuesta = await vendedor.PostAsync("/api/v1/importacion/productos", Archivo("sku,nombre\nX,Y\n"));
        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
