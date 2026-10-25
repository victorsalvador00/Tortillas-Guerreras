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

    // ───────────── v0.2 (T-8.14): grupos, rutas, clientes por ruta y precios ─────────────

    private sealed record RutaImportada(Guid Id, string Codigo, string CanalNombre, Guid? VendedorId, Guid? VehiculoId);

    private sealed record ClienteRutaImportado(Guid ClienteId, string Codigo, short OrdenVisita, List<short> DiasVisita, bool EsGenerico);

    private sealed record ClienteImportado(Guid Id, string Codigo, Guid? GrupoClienteId, Guid? ListaPrecioId);

    private sealed record Pagina<T>(List<T> Items);

    private sealed record PrecioImportado(string Sku, decimal PrecioUnitario);

    private sealed record ListaImportada(Guid Id, string Nombre, List<PrecioImportado> Partidas);

    private sealed record ListaResumen(Guid Id, string Nombre);

    private sealed record GrupoImportado(Guid Id, string Clave, Guid? ListaPrecioId);

    private static async Task<Resultado> ImportarPlantillaAsync(HttpClient cliente, string entidad)
    {
        var plantilla = await cliente.GetStringAsync($"/api/v1/importacion/{entidad}/plantilla");
        return await ImportarAsync(cliente, entidad, plantilla.TrimStart('\uFEFF'));
    }

    private static async Task<ClienteImportado> ClienteAsync(HttpClient admin, string codigo) =>
        (await admin.GetOkAsync<Pagina<ClienteImportado>>($"/api/v1/ventas/clientes?buscar={codigo}")).Items.Single(c => c.Codigo == codigo);

    [Fact]
    [Trait("Regla", "RN-VEN-12")]
    public async Task PlantillasDeEjemplo_EnOrden_DejanRutaClientesYPreciosListosParaOperar()
    {
        var admin = await api.ClienteAsync();
        foreach (var entidad in new[] { "grupos_clientes", "clientes", "rutas", "rutas_clientes", "precios" })
        {
            var r = await ImportarPlantillaAsync(admin, entidad);
            r.Errores.ShouldBeEmpty($"{entidad}: {string.Join("; ", r.Errores.Select(e => $"{e.Fila} {e.Campo} {e.Mensaje}"))}");
            r.Aplicado.ShouldBeTrue();
        }

        // Ruta con canal, chofer y vehículo; su genérico y el cliente asignado con sus días.
        var ruta = (await admin.GetOkAsync<List<RutaImportada>>("/api/v1/logistica/rutas")).Single(x => x.Codigo == "R90");
        ruta.VendedorId.ShouldNotBeNull();
        ruta.VehiculoId.ShouldNotBeNull();
        var clientesRuta = await admin.GetOkAsync<List<ClienteRutaImportado>>($"/api/v1/logistica/rutas/{ruta.Id}/clientes");
        clientesRuta.ShouldContain(c => c.EsGenerico);
        var lupita = clientesRuta.Single(c => c.Codigo == "LUPITA");
        lupita.OrdenVisita.ShouldBe((short)1);
        lupita.DiasVisita.ShouldBe([1, 3, 5]);

        // El cliente del grupo hereda la lista del grupo.
        var grupo = (await admin.GetOkAsync<List<GrupoImportado>>("/api/v1/ventas/grupos-clientes")).Single(g => g.Clave == "SANBORNS");
        var sanborns = await ClienteAsync(admin, "SANB-CENTRO");
        sanborns.GrupoClienteId.ShouldBe(grupo.Id);
        sanborns.ListaPrecioId.ShouldBe(grupo.ListaPrecioId);

        // Precio de lista actualizado y precio especial del cliente.
        var listas = await admin.GetOkAsync<List<ListaResumen>>("/api/v1/ventas/listas-precio");
        var detalle = await admin.GetOkAsync<ListaImportada>($"/api/v1/ventas/listas-precio/{listas.Single(l => l.Nombre == "Lista Detalle").Id}");
        detalle.Partidas.ShouldContain(p => p.Sku == "PT-TOSTADA" && p.PrecioUnitario == 18.50m);
        detalle.Partidas.Count.ShouldBeGreaterThan(1, "combina: no borra las demás partidas");
        var precios = await admin.GetOkAsync<List<PrecioImportado>>($"/api/v1/ventas/clientes/{lupita.ClienteId}/precios");
        precios.ShouldContain(p => p.Sku == "PT-TOSTADA" && p.PrecioUnitario == 17.00m);

        // Reimportar es idempotente: actualiza, no duplica.
        var otra = await ImportarPlantillaAsync(admin, "rutas");
        otra.Creados.ShouldBe(0);
        otra.Actualizados.ShouldBe(1);
    }

    [Fact]
    [Trait("Regla", "RN-LOG-31")]
    public async Task RutasClientes_ClienteDeOtroCanal_NoSeAsignaNadie()
    {
        var admin = await api.ClienteAsync();
        (await ImportarAsync(admin, "rutas", "codigo,nombre,canal\nR91,Ruta 91,DET\n")).Errores.ShouldBeEmpty();
        (await ImportarAsync(admin, "clientes", "codigo,nombre,canal\nDET-91,Tienda 91,DET\nAUTO-91,Super 91,AUTO\n")).Errores.ShouldBeEmpty();

        var r = await ImportarAsync(admin, "rutas_clientes", "ruta,cliente,orden,dias\nR91,DET-91,1,\nR91,AUTO-91,2,L|jueves\n");
        r.Errores.ShouldNotBeEmpty();
        r.Aplicado.ShouldBeFalse();

        var ruta = (await admin.GetOkAsync<List<RutaImportada>>("/api/v1/logistica/rutas")).Single(x => x.Codigo == "R91");
        (await admin.GetOkAsync<List<ClienteRutaImportado>>($"/api/v1/logistica/rutas/{ruta.Id}/clientes")).ShouldAllBe(c => c.EsGenerico);
    }

    [Fact]
    public async Task Precios_ValidaListaOClienteSkuYDias()
    {
        var admin = await api.ClienteAsync();
        var r = await ImportarAsync(admin, "precios",
            "lista,cliente,sku,unidad,precio,vigente_desde\nLista Detalle,X,PT-TOSTADA,,10,\n,,PT-TOSTADA,,10,\nLista Detalle,,NO-EXISTE,,10,\n");
        r.Aplicado.ShouldBeFalse();
        r.Errores.ShouldContain(e => e.Fila == 2 && e.Campo == "lista");
        r.Errores.ShouldContain(e => e.Fila == 3 && e.Campo == "lista");
        r.Errores.ShouldContain(e => e.Fila == 4 && e.Campo == "sku");

        var dias = await ImportarAsync(admin, "rutas_clientes", "ruta,cliente,orden,dias\nR90,LUPITA,1,lunes|feriado\n", validarSolo: true);
        dias.Errores.ShouldContain(e => e.Campo == "dias");
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
