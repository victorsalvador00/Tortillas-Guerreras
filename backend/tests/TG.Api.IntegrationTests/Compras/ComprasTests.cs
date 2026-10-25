using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Inventario.Publico;

namespace TG.Api.IntegrationTests.Compras;

public sealed class ComprasTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record ProveedorPrueba(Guid Id, string Codigo);

    private sealed record PartidaOcPrueba(Guid Id, Guid ProductoId, decimal CantidadBase, decimal CantidadRecibidaBase, decimal Importe, decimal Iva);

    private sealed record OrdenPrueba(Guid Id, string Folio, string Estado, decimal Subtotal, decimal Iva, decimal Total,
        IReadOnlyList<PartidaOcPrueba> Partidas);

    private sealed record PartidaRecPrueba(Guid ProductoId, decimal CantidadBase, decimal CostoUnitarioBase, string? Lote);

    private sealed record RecepcionPrueba(Guid Id, string Folio, string Estado, IReadOnlyList<PartidaRecPrueba> Partidas);

    private sealed record PrecioPrueba(Guid ProductoId, decimal UltimoPrecio, IReadOnlyList<object> Historial);

    private async Task<ProveedorPrueba> ProveedorAsync(HttpClient compras) =>
        await compras.PostOkAsync<ProveedorPrueba>("/api/v1/compras/proveedores", new
        {
            codigo = DatosPrueba.Unico("PRV"), razonSocial = "Proveedor de prueba SA", diasCredito = 0,
        });

    private async Task<ProductoPrueba> ProductoPorToneladaAsync(HttpClient admin) =>
        await admin.CrearProductoAsync(conversiones:
        [
            new { unidadId = await admin.UnidadAsync("TON"), factorABase = 1000m, usoCompra = true, usoVenta = false, usoProduccion = false, predeterminadaVenta = false },
        ]);

    private sealed record ProductoProveedorPrueba(Guid ProductoId, Guid UnidadId, string Unidad, decimal? Precio, decimal? PrecioBase,
        bool Preferido, bool Activo, decimal? UltimoPrecio, string? UltimaUnidad, string? ClaveProveedor);

    private sealed record OfertaPrueba(Guid ProveedorId, decimal? Precio, decimal? PrecioBase, bool Preferido);

    /// <summary>RN-COM-12: la orden solo acepta productos del catálogo del proveedor.</summary>
    private static Task<ProductoProveedorPrueba> CatalogoAsync(HttpClient compras, Guid proveedorId, Guid productoId, Guid unidadId,
        decimal? precio = null, bool preferido = false, string? clave = null) =>
        compras.PostOkAsync<ProductoProveedorPrueba>($"/api/v1/compras/proveedores/{proveedorId}/productos", new
        {
            productoId, unidadId, claveProveedor = clave, precio, diasEntrega = 2, preferido,
        });

    private static async Task<OrdenPrueba> OrdenAutorizadaAsync(HttpClient compras, Guid proveedorId, Guid almacenId, Guid productoId,
        Guid unidadId, decimal cantidad, decimal precio)
    {
        await CatalogoAsync(compras, proveedorId, productoId, unidadId);
        var orden = await compras.PostOkAsync<OrdenPrueba>("/api/v1/compras/ordenes-compra", new
        {
            proveedorId, almacenDestinoId = almacenId,
            partidas = new[] { new { productoId, unidadId, cantidad, precioUnitario = precio } },
        });
        return await compras.PostOkAsync<OrdenPrueba>($"/api/v1/compras/ordenes-compra/{orden.Id}/autorizar");
    }

    private static Task<HttpResponseMessage> RecibirAsync(HttpClient cliente, OrdenPrueba orden, decimal cantidad, string? lote = null) =>
        cliente.PostJsonAsync("/api/v1/compras/recepciones", new
        {
            ordenCompraId = orden.Id,
            facturaProveedor = "F-123",
            partidas = new[] { new { ordenCompraPartidaId = orden.Partidas[0].Id, cantidad, lote } },
        });

    [Fact]
    [Trait("Regla", "RN-COM-05")]
    [Trait("Regla", "RN-COM-06")]
    [Trait("Regla", "RN-COM-08")]
    public async Task Recibir_1TonA6500_GeneraEntradaDe1000KgACosto6_5()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var producto = await ProductoPorToneladaAsync(admin);
        var proveedor = await ProveedorAsync(compras);
        var mp = await admin.AlmacenAsync("MP");
        var orden = await OrdenAutorizadaAsync(compras, proveedor.Id, mp, producto.Id, await admin.UnidadAsync("TON"), 1, 6500);
        orden.Total.ShouldBe(6500m);
        orden.Estado.ShouldBe("Autorizada");

        var recepcion = await (await RecibirAsync(compras, orden, 1)).LeerAsync<RecepcionPrueba>();

        recepcion.Folio.ShouldStartWith("REC-");
        recepcion.Partidas.Single().CantidadBase.ShouldBe(1000m);
        recepcion.Partidas.Single().CostoUnitarioBase.ShouldBe(6.5m);
        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>(
            $"/api/v1/inventario/kardex?documentoTipo=Recepcion&documentoId={recepcion.Id}");
        var movimiento = kardex.Items.Single();
        movimiento.Tipo.ShouldBe("EntradaCompra");
        movimiento.Cantidad.ShouldBe(1000m);
        movimiento.CostoUnitario.ShouldBe(6.5m);
        (await admin.ExistenciaAsync(mp, producto.Id)).ShouldBe(1000m);
        var costo = await api.ConScopeAsync(sp => sp.GetRequiredService<IInventarioService>().ObtenerCostoPromedioAsync(producto.Id, CancellationToken.None));
        costo.ShouldBe(6.5m);

        var detalle = await compras.GetOkAsync<OrdenPrueba>($"/api/v1/compras/ordenes-compra/{orden.Id}");
        detalle.Estado.ShouldBe("Recibida");

        var precios = await compras.GetOkAsync<List<PrecioPrueba>>($"/api/v1/compras/proveedores/{proveedor.Id}/precios");
        precios.Single(p => p.ProductoId == producto.Id).UltimoPrecio.ShouldBe(6500m);
        precios.Single(p => p.ProductoId == producto.Id).Historial.Count.ShouldBe(1);
    }

    [Fact]
    [Trait("Regla", "RN-COM-04")]
    public async Task DosRecepcionesParciales_QueSumanLoPedido_DejanLaOrdenRecibida()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var producto = await admin.CrearProductoAsync();
        var proveedor = await ProveedorAsync(compras);
        var orden = await OrdenAutorizadaAsync(compras, proveedor.Id, await admin.AlmacenAsync("MP"), producto.Id,
            producto.UnidadBaseId, 100, 10);

        (await RecibirAsync(compras, orden, 40)).EnsureSuccessStatusCode();
        (await compras.GetOkAsync<OrdenPrueba>($"/api/v1/compras/ordenes-compra/{orden.Id}")).Estado.ShouldBe("RecibidaParcial");

        (await RecibirAsync(compras, orden, 60)).EnsureSuccessStatusCode();
        var final = await compras.GetOkAsync<OrdenPrueba>($"/api/v1/compras/ordenes-compra/{orden.Id}");
        final.Estado.ShouldBe("Recibida");
        final.Partidas.Single().CantidadRecibidaBase.ShouldBe(100m);
    }

    [Fact]
    [Trait("Regla", "RN-COM-04")]
    public async Task Recepcion_QueExcedeLaTolerancia_DevuelveCOM004()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var producto = await admin.CrearProductoAsync();
        var proveedor = await ProveedorAsync(compras);
        var orden = await OrdenAutorizadaAsync(compras, proveedor.Id, await admin.AlmacenAsync("MP"), producto.Id,
            producto.UnidadBaseId, 100, 10);

        await (await RecibirAsync(compras, orden, 106)).DebeFallarAsync(HttpStatusCode.BadRequest, "COM-004");

        // 5 % de tolerancia [SUP-04]: 105 sí se acepta.
        (await RecibirAsync(compras, orden, 105)).EnsureSuccessStatusCode();
        (await compras.GetOkAsync<OrdenPrueba>($"/api/v1/compras/ordenes-compra/{orden.Id}")).Estado.ShouldBe("Recibida");
    }

    [Fact]
    [Trait("Regla", "RN-COM-01")]
    public async Task Orden_ConProductoNoComprable_DevuelveCOM001()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var tortilla = await admin.ProductoAsync("PT-TORT-KG");
        var proveedor = await ProveedorAsync(compras);

        var respuesta = await compras.PostJsonAsync("/api/v1/compras/ordenes-compra", new
        {
            proveedorId = proveedor.Id, almacenDestinoId = await admin.AlmacenAsync("PT"),
            partidas = new[] { new { productoId = tortilla.Id, unidadId = tortilla.UnidadBaseId, cantidad = 10, precioUnitario = 20 } },
        });
        await respuesta.DebeFallarAsync(HttpStatusCode.BadRequest, "COM-001");
    }

    [Fact]
    [Trait("Regla", "RN-COM-02")]
    public async Task Orden_Autorizada_NoSeEdita()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var producto = await admin.CrearProductoAsync(tasaIva: 0.16m);
        var proveedor = await ProveedorAsync(compras);
        var mp = await admin.AlmacenAsync("MP");
        var cuerpo = new
        {
            proveedorId = proveedor.Id, almacenDestinoId = mp,
            partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, cantidad = 3, precioUnitario = 10.555m } },
        };
        await CatalogoAsync(compras, proveedor.Id, producto.Id, producto.UnidadBaseId);
        var orden = await compras.PostOkAsync<OrdenPrueba>("/api/v1/compras/ordenes-compra", cuerpo);
        orden.Estado.ShouldBe("Borrador");
        orden.Subtotal.ShouldBe(31.67m);
        orden.Iva.ShouldBe(5.07m);
        orden.Total.ShouldBe(36.74m);

        // En Borrador sí se edita.
        await compras.PutOkAsync<OrdenPrueba>($"/api/v1/compras/ordenes-compra/{orden.Id}", cuerpo);
        await compras.PostOkAsync<OrdenPrueba>($"/api/v1/compras/ordenes-compra/{orden.Id}/autorizar");

        var edicion = await compras.PutAsJsonAsync($"/api/v1/compras/ordenes-compra/{orden.Id}", cuerpo, ExtensionesHttp.Json);
        await edicion.DebeFallarAsync(HttpStatusCode.Conflict, "COM-002");
    }

    [Fact]
    [Trait("Regla", "RN-COM-03")]
    public async Task Autorizar_SinPartidasOSinPermiso_SeRechaza()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var proveedor = await ProveedorAsync(compras);
        var vacia = await compras.PostOkAsync<OrdenPrueba>("/api/v1/compras/ordenes-compra", new
        {
            proveedorId = proveedor.Id, almacenDestinoId = await admin.AlmacenAsync("MP"), partidas = Array.Empty<object>(),
        });

        await (await compras.PostJsonAsync($"/api/v1/compras/ordenes-compra/{vacia.Id}/autorizar"))
            .DebeFallarAsync(HttpStatusCode.BadRequest, "COM-003");

        var almacen = await api.ClienteAsync("almacen");
        (await almacen.PostJsonAsync($"/api/v1/compras/ordenes-compra/{vacia.Id}/autorizar")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Regla", "RN-COM-07")]
    public async Task CancelarRecepcion_GeneraAjusteSalida_YRechazaSiNoAlcanza()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var producto = await admin.CrearProductoAsync();
        var proveedor = await ProveedorAsync(compras);
        var mp = await admin.AlmacenAsync("MP");
        var orden = await OrdenAutorizadaAsync(compras, proveedor.Id, mp, producto.Id, producto.UnidadBaseId, 100, 4);

        var primera = await (await RecibirAsync(compras, orden, 50)).LeerAsync<RecepcionPrueba>();
        (await admin.SalidaAsync(mp, producto.Id, 30)).EnsureSuccessStatusCode();
        await (await compras.PostJsonAsync($"/api/v1/compras/recepciones/{primera.Id}/cancelar"))
            .DebeFallarAsync(HttpStatusCode.Conflict, "INV-001");

        var segunda = await (await RecibirAsync(compras, orden, 50)).LeerAsync<RecepcionPrueba>();
        var cancelada = await compras.PostOkAsync<RecepcionPrueba>($"/api/v1/compras/recepciones/{segunda.Id}/cancelar");
        cancelada.Estado.ShouldBe("Cancelada");
        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>(
            $"/api/v1/inventario/kardex?documentoTipo=Recepcion&documentoId={segunda.Id}");
        kardex.Items.Single(m => m.Tipo == "AjusteSalida").Cantidad.ShouldBe(-50m);
        (await admin.ExistenciaAsync(mp, producto.Id)).ShouldBe(20m);

        var ordenDespues = await compras.GetOkAsync<OrdenPrueba>($"/api/v1/compras/ordenes-compra/{orden.Id}");
        ordenDespues.Estado.ShouldBe("RecibidaParcial");
        ordenDespues.Partidas.Single().CantidadRecibidaBase.ShouldBe(50m);

        await (await compras.PostJsonAsync($"/api/v1/compras/recepciones/{segunda.Id}/cancelar"))
            .DebeFallarAsync(HttpStatusCode.Conflict, "COM-007");
    }

    [Fact]
    [Trait("Regla", "RN-COM-09")]
    public async Task Recepcion_DeProductoConLote_SinLote_DevuelveCOM009()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var producto = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA", manejaLote: true);
        var proveedor = await ProveedorAsync(compras);
        var pt = await admin.AlmacenAsync("PT");
        var orden = await OrdenAutorizadaAsync(compras, proveedor.Id, pt, producto.Id, producto.UnidadBaseId, 10, 15);

        await (await RecibirAsync(compras, orden, 10)).DebeFallarAsync(HttpStatusCode.BadRequest, "COM-009");

        var recepcion = await (await RecibirAsync(compras, orden, 10, "lt-01")).LeerAsync<RecepcionPrueba>();
        recepcion.Partidas.Single().Lote.ShouldBe("LT-01");
        (await admin.ExistenciaAsync(pt, producto.Id, "LT-01")).ShouldBe(10m);
    }

    [Fact]
    public async Task CompraDirecta_RequierePermiso()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var producto = await admin.CrearProductoAsync();
        var proveedor = await ProveedorAsync(compras);
        var cuerpo = new
        {
            proveedorId = proveedor.Id, almacenId = await admin.AlmacenAsync("MP"),
            partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, cantidad = 8, precioUnitario = 3 } },
        };

        var almacen = await api.ClienteAsync("almacen");
        await (await almacen.PostJsonAsync("/api/v1/compras/recepciones", cuerpo)).DebeFallarAsync(HttpStatusCode.Forbidden, "COM-005");

        var recepcion = await compras.PostOkAsync<RecepcionPrueba>("/api/v1/compras/recepciones", cuerpo);
        recepcion.Partidas.Single().CostoUnitarioBase.ShouldBe(3m);
    }

    [Fact]
    public async Task Orden_ConRecepciones_NoSeCancela_PeroSeCierraConFaltante()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var producto = await admin.CrearProductoAsync();
        var proveedor = await ProveedorAsync(compras);
        var orden = await OrdenAutorizadaAsync(compras, proveedor.Id, await admin.AlmacenAsync("MP"), producto.Id,
            producto.UnidadBaseId, 100, 1);
        (await RecibirAsync(compras, orden, 30)).EnsureSuccessStatusCode();

        await (await compras.PostJsonAsync($"/api/v1/compras/ordenes-compra/{orden.Id}/cancelar"))
            .DebeFallarAsync(HttpStatusCode.Conflict, "COM-006");
        var cerrada = await compras.PostOkAsync<OrdenPrueba>($"/api/v1/compras/ordenes-compra/{orden.Id}/cerrar");
        cerrada.Estado.ShouldBe("Cerrada");
        await (await RecibirAsync(compras, orden, 10)).DebeFallarAsync(HttpStatusCode.Conflict, "COM-008");
    }

    // ───────────── Catálogo de productos por proveedor ─────────────

    [Fact]
    [Trait("Regla", "RN-COM-10")]
    public async Task Catalogo_SoloComprables_SinRepetir_YConUnidadDeCompra()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var proveedor = await ProveedorAsync(compras);
        var producto = await ProductoPorToneladaAsync(admin);
        var ton = await admin.UnidadAsync("TON");

        // El precio pactado es opcional.
        var sinPrecio = await CatalogoAsync(compras, proveedor.Id, producto.Id, ton, clave: "MZ-01");
        sinPrecio.Precio.ShouldBeNull();
        sinPrecio.Unidad.ShouldBe("TON");
        sinPrecio.ClaveProveedor.ShouldBe("MZ-01");

        await (await compras.PostJsonAsync($"/api/v1/compras/proveedores/{proveedor.Id}/productos",
                new { productoId = producto.Id, unidadId = ton, precio = 1m, diasEntrega = 0, preferido = false }))
            .DebeFallarAsync(HttpStatusCode.BadRequest, "COM-018");

        var tortilla = await admin.ProductoAsync("PT-TORT-KG");
        await (await compras.PostJsonAsync($"/api/v1/compras/proveedores/{proveedor.Id}/productos",
                new { productoId = tortilla.Id, unidadId = tortilla.UnidadBaseId, diasEntrega = 0, preferido = false }))
            .DebeFallarAsync(HttpStatusCode.BadRequest, "COM-001");

        // PAQ no es unidad de compra del producto.
        var otro = await admin.CrearProductoAsync();
        (await compras.PostJsonAsync($"/api/v1/compras/proveedores/{proveedor.Id}/productos",
                new { productoId = otro.Id, unidadId = await admin.UnidadAsync("PAQ"), diasEntrega = 0, preferido = false }))
            .IsSuccessStatusCode.ShouldBeFalse();

        // Con precio: se informa por unidad de compra y por unidad base.
        var conPrecio = await compras.PutOkAsync<ProductoProveedorPrueba>($"/api/v1/compras/proveedores/{proveedor.Id}/productos/{producto.Id}",
            new { unidadId = ton, claveProveedor = "MZ-01", precio = 6500m, diasEntrega = 3, preferido = false, activo = true });
        conPrecio.PrecioBase.ShouldBe(6.5m);
    }

    [Fact]
    [Trait("Regla", "RN-COM-11")]
    public async Task Preferido_UnoPorProducto_YSeQuitaAlDesactivarElProveedor()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var producto = await admin.CrearProductoAsync();
        var a = await ProveedorAsync(compras);
        var b = await ProveedorAsync(compras);
        await CatalogoAsync(compras, a.Id, producto.Id, producto.UnidadBaseId, precio: 10m, preferido: true);
        await CatalogoAsync(compras, b.Id, producto.Id, producto.UnidadBaseId, precio: 8m, preferido: true);

        var ofertas = await compras.GetOkAsync<List<OfertaPrueba>>($"/api/v1/compras/productos/{producto.Id}/proveedores");
        ofertas.Count.ShouldBe(2);
        ofertas[0].ProveedorId.ShouldBe(b.Id);
        ofertas.Count(o => o.Preferido).ShouldBe(1);

        // Inactivo nunca es preferido.
        await (await compras.PutAsJsonAsync($"/api/v1/compras/proveedores/{b.Id}/productos/{producto.Id}",
                new { unidadId = producto.UnidadBaseId, precio = 8m, diasEntrega = 0, preferido = true, activo = false }, ExtensionesHttp.Json))
            .DebeFallarAsync(HttpStatusCode.BadRequest, null);

        await compras.PostOkAsync<ProveedorPrueba>($"/api/v1/compras/proveedores/{b.Id}/desactivar");
        var despues = await compras.GetOkAsync<List<OfertaPrueba>>($"/api/v1/compras/productos/{producto.Id}/proveedores");
        despues.Single().ProveedorId.ShouldBe(a.Id);
        despues.Single().Preferido.ShouldBeFalse();

        // Los productos de un proveedor inactivo no se editan.
        await (await compras.PutAsJsonAsync($"/api/v1/compras/proveedores/{b.Id}/productos/{producto.Id}",
                new { unidadId = producto.UnidadBaseId, precio = 8m, diasEntrega = 0, preferido = false, activo = true }, ExtensionesHttp.Json))
            .DebeFallarAsync(HttpStatusCode.Conflict, "COM-020");
    }

    [Fact]
    [Trait("Regla", "RN-COM-12")]
    public async Task Orden_ConProductoFueraDelCatalogoDelProveedor_DevuelveCOM017()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var producto = await admin.CrearProductoAsync();
        var proveedor = await ProveedorAsync(compras);
        var cuerpo = new
        {
            proveedorId = proveedor.Id, almacenDestinoId = await admin.AlmacenAsync("MP"),
            partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, cantidad = 5, precioUnitario = 2 } },
        };
        await (await compras.PostJsonAsync("/api/v1/compras/ordenes-compra", cuerpo)).DebeFallarAsync(HttpStatusCode.BadRequest, "COM-017");

        // Desactivado en su catálogo tampoco se puede pedir.
        await CatalogoAsync(compras, proveedor.Id, producto.Id, producto.UnidadBaseId);
        await compras.PutOkAsync<ProductoProveedorPrueba>($"/api/v1/compras/proveedores/{proveedor.Id}/productos/{producto.Id}",
            new { unidadId = producto.UnidadBaseId, diasEntrega = 0, preferido = false, activo = false });
        await (await compras.PostJsonAsync("/api/v1/compras/ordenes-compra", cuerpo)).DebeFallarAsync(HttpStatusCode.BadRequest, "COM-017");

        await compras.PutOkAsync<ProductoProveedorPrueba>($"/api/v1/compras/proveedores/{proveedor.Id}/productos/{producto.Id}",
            new { unidadId = producto.UnidadBaseId, diasEntrega = 0, preferido = false, activo = true });
        (await compras.PostJsonAsync("/api/v1/compras/ordenes-compra", cuerpo)).EnsureSuccessStatusCode();
    }

    [Fact]
    [Trait("Regla", "RN-COM-08")]
    public async Task CompraDirecta_AgregaElProductoAlCatalogo_ConSuUltimoPrecio()
    {
        var admin = await api.ClienteAsync();
        var compras = await api.ClienteAsync("compras");
        var producto = await admin.CrearProductoAsync();
        var proveedor = await ProveedorAsync(compras);
        await compras.PostOkAsync<RecepcionPrueba>("/api/v1/compras/recepciones", new
        {
            proveedorId = proveedor.Id, almacenId = await admin.AlmacenAsync("MP"),
            partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, cantidad = 8, precioUnitario = 3.5m } },
        });

        var catalogo = await compras.GetOkAsync<List<ProductoProveedorPrueba>>($"/api/v1/compras/proveedores/{proveedor.Id}/productos");
        var registro = catalogo.Single(p => p.ProductoId == producto.Id);
        registro.Activo.ShouldBeTrue();
        registro.Precio.ShouldBe(3.5m);
        registro.UltimoPrecio.ShouldBe(3.5m);
        registro.UltimaUnidad.ShouldNotBeNull();
    }

    [Fact]
    [Trait("Regla", "RN-SEG-04")]
    public async Task Vendedor_NoAccedeACompras()
    {
        var vendedor = await api.ClienteAsync("vendedor1");
        (await vendedor.GetAsync("/api/v1/compras/ordenes-compra")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vendedor.GetAsync("/api/v1/compras/proveedores")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Semilla_ProveedoresDemo_ConPrecios()
    {
        var compras = await api.ClienteAsync("compras");
        var pagina = await compras.GetOkAsync<PaginaPrueba<ProveedorPrueba>>("/api/v1/compras/proveedores?buscar=GRANOS");
        var granos = pagina.Items.Single(p => p.Codigo == "GRANOS");
        var precios = await compras.GetOkAsync<List<PrecioPrueba>>($"/api/v1/compras/proveedores/{granos.Id}/precios");
        precios.ShouldNotBeEmpty();
    }
}
