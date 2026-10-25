using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Api.Trazabilidad;
using TG.Inventario.Publico;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Api.IntegrationTests.Inventario;

/// <summary>
/// T-8.16: maíz (dos lotes) y cal sin lote → masa → tortilla → carga a ruta → venta. La trazabilidad sale del kardex
/// con las cantidades exactas de cada documento (RN-INV-14 a RN-INV-17).
/// </summary>
public sealed class TrazabilidadTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record IdFolio(Guid Id, string Folio);

    private sealed record OrdenPrueba(Guid Id, string Folio, string Lote, string Estado);

    private sealed record RutaPrueba(Guid Id, string Codigo, Guid AlmacenId, Guid? ClienteGenericoId);

    private sealed record Nodo(string Id, string Tipo, string Titulo, string? Lote, Guid? ProductoId, string? Folio, Guid? DocumentoId,
        decimal? Existencia, IReadOnlyList<ConsumoSinLoteDto> ConsumoSinLote);

    private sealed record Arista(string Desde, string Hacia, string Tipo, decimal? Cantidad, string? Unidad);

    private sealed record Evento(string Folio, string Tipo, string Lote, decimal Cantidad);

    private sealed record Traza(string Direccion, bool Truncado, IReadOnlyList<string> Avisos, IReadOnlyList<Nodo> Nodos,
        IReadOnlyList<Arista> Aristas, IReadOnlyList<Evento> Eventos);

    private sealed record Opcion(string Tipo, Guid? Id, Guid? ProductoId, string? Lote, string Etiqueta);

    private sealed record Cadena(
        ProductoPrueba Maiz,
        ProductoPrueba Cal,
        ProductoPrueba Masa,
        ProductoPrueba Tortilla,
        Guid ProveedorId,
        IdFolio RecepcionA,
        IdFolio RecepcionB,
        OrdenPrueba OrdenMasa,
        OrdenPrueba OrdenTortilla,
        IdFolio Envio,
        RutaPrueba Ruta,
        VentaRegistrada Venta,
        string LoteA,
        string LoteB);

    /// <summary>Una cadena por base de prueba: xUnit crea una instancia de la clase por prueba.</summary>
    private static readonly ConcurrentDictionary<ApiFixture, Cadena> Cadenas = new();

    private Task<DateOnly> HoyAsync() => api.ConScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IFechaOperativa>().Hoy()));

    private static async Task<OrdenPrueba> ProducirAsync(HttpClient admin, Guid productoId, decimal cantidad, DateOnly fecha,
        Guid? mermaMotivo = null, decimal merma = 0)
    {
        var orden = await admin.PostOkAsync<OrdenPrueba>("/api/v1/produccion/ordenes", new
        {
            productoId, fechaOperativa = fecha, turno = "Matutino", cantidadPlaneada = cantidad,
        });
        await admin.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{orden.Id}/liberar");
        await admin.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{orden.Id}/iniciar");
        if (merma > 0)
        {
            await admin.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{orden.Id}/mermas",
                new { productoId, cantidadBase = merma, motivoMermaId = mermaMotivo });
        }

        return await admin.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{orden.Id}/terminar", new { cantidadProducida = cantidad });
    }

    private static async Task PublicarRecetaAsync(HttpClient admin, Guid productoId, decimal rendimiento, object[] componentes)
    {
        var receta = await admin.PostOkAsync<IdFolio>("/api/v1/produccion/recetas", new
        {
            productoId, nombre = "Receta trazabilidad", rendimientoCantidad = rendimiento, mermaEsperadaPct = 0m, componentes,
        });
        await admin.PostOkAsync<IdFolio>($"/api/v1/produccion/recetas/{receta.Id}/publicar");
    }

    /// <summary>La cadena completa se arma una vez por clase: cada prueba la lee desde otra entrada.</summary>
    private async Task<Cadena> CadenaAsync()
    {
        if (Cadenas.TryGetValue(api, out var existente))
        {
            return existente;
        }

        var admin = await api.ClienteAsync();
        var hoy = await HoyAsync();
        var mp = await admin.AlmacenAsync("MP");

        // La cal se deja sin lote a propósito (RN-INV-16).
        var maiz = await admin.CrearProductoAsync(manejaLote: true);
        var cal = await admin.CrearProductoAsync(manejaLote: false);
        var masa = await admin.CrearProductoAsync(tipo: "Semiterminado", vidaUtil: 1, manejaLote: true);
        var tortilla = await admin.CrearProductoAsync(tipo: "Terminado", vidaUtil: 2, manejaLote: true);

        await PublicarRecetaAsync(admin, masa.Id, 100, [new { productoId = maiz.Id, cantidad = 58m }, new { productoId = cal.Id, cantidad = 0.58m }]);
        await PublicarRecetaAsync(admin, tortilla.Id, 50, [new { productoId = masa.Id, cantidad = 50m }]);

        var proveedor = await admin.PostOkAsync<IdFolio>("/api/v1/compras/proveedores", new
        {
            codigo = DatosPrueba.Unico("PRV"), razonSocial = "Molinos de prueba SA", diasCredito = 0,
        });
        var loteA = DatosPrueba.Unico("MZA");
        var loteB = DatosPrueba.Unico("MZB");
        async Task<IdFolio> RecibirAsync(Guid productoId, decimal cantidad, string? lote, DateOnly? caducidad) =>
            await admin.PostOkAsync<IdFolio>("/api/v1/compras/recepciones", new
            {
                proveedorId = proveedor.Id, almacenId = mp, facturaProveedor = "F-TRZ",
                partidas = new[] { new { productoId, unidadId = (await admin.UnidadAsync("KG")), cantidad, precioUnitario = 6m, lote, fechaCaducidad = caducidad } },
            });

        // FEFO: el lote A caduca antes, así que la masa toma 40 kg de A y 18 de B.
        var recepcionA = await RecibirAsync(maiz.Id, 40, loteA, hoy.AddDays(10));
        var recepcionB = await RecibirAsync(maiz.Id, 40, loteB, hoy.AddDays(20));
        await RecibirAsync(cal.Id, 10, null, null);

        var ordenMasa = await ProducirAsync(admin, masa.Id, 100, hoy);
        var ordenTortilla = await ProducirAsync(admin, tortilla.Id, 50, hoy, await admin.MotivoAsync("TORT_ROTA"), 2);

        var ruta = await admin.PostOkAsync<RutaPrueba>("/api/v1/logistica/rutas", new
        {
            codigo = "Z" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), nombre = "Ruta trazabilidad",
            canalId = await admin.CanalAsync("DET"), vendedorId = await admin.CrearChoferAsync(), activo = true,
        });
        var envio = await admin.PostOkAsync<IdFolio>("/api/v1/logistica/envios", new
        {
            rutaId = ruta.Id, tipo = "CargaInicial", partidas = new[] { new { productoId = tortilla.Id, cantidad = 30m, lote = ordenTortilla.Lote } },
        });
        await admin.PostOkAsync<IdFolio>($"/api/v1/logistica/envios/{envio.Id}/despachar");

        var venta = await api.ConScopeAsync(async sp =>
        {
            var resultado = await sp.GetRequiredService<IVentasService>().RegistrarVentaAsync(new RegistrarVentaComando(OrigenVenta.App,
                ruta.ClienteGenericoId!.Value, ruta.AlmacenId, Guid.Empty, hoy, CondicionPago.Contado, FormaPago.Efectivo,
                [new PartidaVentaComando(tortilla.Id, tortilla.UnidadBaseId, 10m, 20m)], RutaId: ruta.Id, OpId: Id.Nuevo()),
                CancellationToken.None);
            resultado.EsExito.ShouldBeTrue(resultado.Error?.Mensaje);
            return resultado.Valor;
        });

        return Cadenas[api] = new Cadena(maiz, cal, masa, tortilla, proveedor.Id, recepcionA, recepcionB, ordenMasa, ordenTortilla, envio,
            ruta, venta, loteA, loteB);
    }

    private static Arista AristaEntre(Traza traza, string desde, string hacia) =>
        traza.Aristas.Single(a => a.Desde == desde && a.Hacia == hacia);

    private static string Doc(Guid id) => $"doc:{id:N}";

    private static string NodoLote(Traza traza, Guid productoId, string lote) =>
        traza.Nodos.Single(n => n.Tipo == "Lote" && n.ProductoId == productoId && n.Lote == lote).Id;

    [Fact]
    [Trait("Regla", "RN-INV-14")]
    public async Task HaciaOrigen_DesdeLaVenta_LlegaAlProveedorConLasCantidadesDelKardex()
    {
        var c = await CadenaAsync();
        var admin = await api.ClienteAsync();

        var traza = await admin.GetOkAsync<Traza>($"/api/v1/inventario/trazabilidad?tipo=Venta&id={c.Venta.VentaId}");

        traza.Direccion.ShouldBe("Origen");
        traza.Nodos.ShouldContain(n => n.Id == $"prov:{c.ProveedorId:N}");
        traza.Nodos.ShouldContain(n => n.Id == Doc(c.RecepcionA.Id));
        traza.Nodos.ShouldContain(n => n.Id == Doc(c.RecepcionB.Id));

        // FEFO repartió el consumo de maíz entre los dos lotes: la masa tomó 40 de A y 18 de B.
        var ordenMasa = Doc(c.OrdenMasa.Id);
        AristaEntre(traza, NodoLote(traza, c.Maiz.Id, c.LoteA), ordenMasa).Cantidad.ShouldBe(40m);
        AristaEntre(traza, NodoLote(traza, c.Maiz.Id, c.LoteB), ordenMasa).Cantidad.ShouldBe(18m);

        // Segundo nivel: el lote de masa alimenta la orden de tortilla.
        AristaEntre(traza, NodoLote(traza, c.Masa.Id, c.OrdenMasa.Lote), Doc(c.OrdenTortilla.Id)).Cantidad.ShouldBe(50m);
        traza.Aristas.ShouldContain(a => a.Desde == Doc(c.Envio.Id) && a.Tipo == "Llegada" && a.Cantidad == 30m);
        traza.Aristas.ShouldContain(a => a.Hacia == Doc(c.Venta.VentaId) && a.Tipo == "Venta" && a.Cantidad == 10m);
        traza.Truncado.ShouldBeFalse();
    }

    [Fact]
    [Trait("Regla", "RN-INV-14")]
    public async Task HaciaDestino_DesdeLaRecepcion_LlegaAlClienteDeLaRuta()
    {
        var c = await CadenaAsync();
        var admin = await api.ClienteAsync();

        var traza = await admin.GetOkAsync<Traza>($"/api/v1/inventario/trazabilidad?tipo=Recepcion&id={c.RecepcionA.Id}");

        traza.Direccion.ShouldBe("Destino");
        var cliente = $"cli:{c.Ruta.ClienteGenericoId:N}";
        traza.Nodos.ShouldContain(n => n.Id == cliente && n.Tipo == "Cliente");
        var entrega = AristaEntre(traza, Doc(c.Venta.VentaId), cliente);
        entrega.Cantidad.ShouldBe(10m);
        entrega.Tipo.ShouldBe("Entrega");

        // El lote B no se recibió en esta recepción: hacia el destino no aparece.
        traza.Nodos.ShouldNotContain(n => n.Lote == c.LoteB);
        traza.Eventos.ShouldContain(e => e.Folio == c.RecepcionA.Folio && e.Tipo == "EntradaCompra" && e.Cantidad == 40m);
    }

    [Fact]
    [Trait("Regla", "RN-INV-14")]
    public async Task DesdeElLote_AmbasDirecciones_YExistenciaPorAlmacen()
    {
        var c = await CadenaAsync();
        var admin = await api.ClienteAsync();

        var traza = await admin.GetOkAsync<Traza>(
            $"/api/v1/inventario/trazabilidad?tipo=Lote&productoId={c.Tortilla.Id}&lote={c.OrdenTortilla.Lote.ToLowerInvariant()}");

        traza.Direccion.ShouldBe("Ambas");
        traza.Nodos.ShouldContain(n => n.Id == $"prov:{c.ProveedorId:N}");
        traza.Nodos.ShouldContain(n => n.Id == $"cli:{c.Ruta.ClienteGenericoId:N}");
        var lotes = traza.Nodos.Where(n => n.Tipo == "Lote" && n.ProductoId == c.Tortilla.Id).ToList();
        lotes.Count.ShouldBe(2); // PT y el almacén de la ruta
        lotes.Sum(l => l.Existencia).ShouldBe(40m); // 50 producidos (52 − 2 de merma) − 10 vendidos
    }

    [Fact]
    [Trait("Regla", "RN-INV-15")]
    public async Task Merma_DeLaOrden_SeDibujaComoNodoDeMerma()
    {
        var c = await CadenaAsync();
        var admin = await api.ClienteAsync();

        var traza = await admin.GetOkAsync<Traza>($"/api/v1/inventario/trazabilidad?tipo=OrdenProduccion&id={c.OrdenTortilla.Id}");

        var merma = traza.Nodos.Single(n => n.Tipo == "Merma");
        merma.Folio.ShouldBe(c.OrdenTortilla.Folio);
        traza.Aristas.Single(a => a.Hacia == merma.Id).Cantidad.ShouldBe(2m);
    }

    [Fact]
    [Trait("Regla", "RN-INV-16")]
    public async Task ComponenteSinLote_ApareceEnLaOrdenSinOrigenDeCompra()
    {
        var c = await CadenaAsync();
        var admin = await api.ClienteAsync();

        var traza = await admin.GetOkAsync<Traza>($"/api/v1/inventario/trazabilidad?tipo=OrdenProduccion&id={c.OrdenMasa.Id}&direccion=Origen");

        traza.Avisos.ShouldContain("componentes_sin_lote");
        var orden = traza.Nodos.Single(n => n.Id == Doc(c.OrdenMasa.Id));
        orden.ConsumoSinLote.Single().ProductoId.ShouldBe(c.Cal.Id);
        orden.ConsumoSinLote.Single().Cantidad.ShouldBe(0.58m);
        traza.Nodos.ShouldNotContain(n => n.ProductoId == c.Cal.Id && n.Tipo == "Lote");
    }

    [Fact]
    [Trait("Regla", "RN-INV-17")]
    public async Task Tope_DeProfundidad_MarcaElResultadoComoTruncado()
    {
        var c = await CadenaAsync();

        var grafo = await api.ConScopeAsync(sp => new ConstructorTraza(sp.GetRequiredService<IConsultaKardex>())
            .DesdeDocumentosAsync([new DocumentoTraza(DocumentoInventario.Venta, c.Venta.VentaId)], new OpcionesTraza(DireccionTraza.Origen, 2),
                CancellationToken.None));

        grafo.Truncado.ShouldBeTrue();
        grafo.Documentos.Keys.ShouldNotContain(d => d.Id == c.RecepcionA.Id);
    }

    [Fact]
    [Trait("Regla", "RN-INV-14")]
    public async Task MateriaPrimaYSemiterminado_ManejanLotePorOmision()
    {
        var admin = await api.ClienteAsync();
        var kg = await admin.UnidadAsync("KG");
        foreach (var (tipo, lote) in new[] { ("MateriaPrima", true), ("Semiterminado", true), ("Empaque", false) })
        {
            var sku = DatosPrueba.Unico("SUP");
            var producto = await admin.PostOkAsync<ProductoPrueba>("/api/v1/catalogos/productos", new
            {
                sku, nombre = $"Producto {sku}", tipo, unidadBaseId = kg, tasaIva = 0m, claveProdServSat = "01010101", conversiones = Array.Empty<object>(),
            });
            producto.ManejaLote.ShouldBe(lote, tipo);
        }
    }

    [Fact]
    public async Task OrdenDeCompra_SeSigueDesdeSusRecepciones()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync(manejaLote: true);
        var proveedor = await admin.PostOkAsync<IdFolio>("/api/v1/compras/proveedores", new
        {
            codigo = DatosPrueba.Unico("PRV"), razonSocial = "Proveedor OC SA", diasCredito = 0,
        });
        await admin.PostOkAsync<IdFolio>($"/api/v1/compras/proveedores/{proveedor.Id}/productos", new
        {
            productoId = producto.Id, unidadId = producto.UnidadBaseId, diasEntrega = 1, preferido = false,
        });
        var orden = await admin.PostOkAsync<OrdenConPartidas>("/api/v1/compras/ordenes-compra", new
        {
            proveedorId = proveedor.Id, almacenDestinoId = await admin.AlmacenAsync("MP"),
            partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, cantidad = 20m, precioUnitario = 5m } },
        });
        orden = await admin.PostOkAsync<OrdenConPartidas>($"/api/v1/compras/ordenes-compra/{orden.Id}/autorizar");
        var lote = DatosPrueba.Unico("OC");
        await admin.PostOkAsync<IdFolio>("/api/v1/compras/recepciones", new
        {
            ordenCompraId = orden.Id, partidas = new[] { new { ordenCompraPartidaId = orden.Partidas[0].Id, cantidad = 20m, lote } },
        });

        var opciones = await admin.GetOkAsync<List<Opcion>>($"/api/v1/inventario/trazabilidad/buscar?q={orden.Folio}");
        opciones.ShouldContain(o => o.Tipo == "OrdenCompra" && o.Id == orden.Id);

        var traza = await admin.GetOkAsync<Traza>($"/api/v1/inventario/trazabilidad?tipo=OrdenCompra&id={orden.Id}");
        traza.Direccion.ShouldBe("Destino");
        traza.Nodos.ShouldContain(n => n.Tipo == "Lote" && n.Lote == lote && n.Existencia == 20m);
        traza.Nodos.ShouldContain(n => n.Id == $"prov:{proveedor.Id:N}");
    }

    private sealed record PartidaOc(Guid Id);

    private sealed record OrdenConPartidas(Guid Id, string Folio, IReadOnlyList<PartidaOc> Partidas);

    [Fact]
    public async Task Buscar_EncuentraLotesYDocumentosPorTexto()
    {
        var c = await CadenaAsync();
        var admin = await api.ClienteAsync();

        var porLote = await admin.GetOkAsync<List<Opcion>>($"/api/v1/inventario/trazabilidad/buscar?q={c.LoteA[..6]}");
        porLote.ShouldContain(o => o.Tipo == "Lote" && o.ProductoId == c.Maiz.Id && o.Lote == c.LoteA);

        var porFolio = await admin.GetOkAsync<List<Opcion>>($"/api/v1/inventario/trazabilidad/buscar?q={c.OrdenMasa.Folio}");
        porFolio.ShouldContain(o => o.Tipo == "OrdenProduccion" && o.Id == c.OrdenMasa.Id);
    }

    [Fact]
    public async Task Permiso_SoloRolesConTrazabilidad_YEntradasInvalidas()
    {
        var c = await CadenaAsync();
        var vendedor = await api.ClienteAsync("vendedor1");
        var almacen = await api.ClienteAsync("almacen");
        var admin = await api.ClienteAsync();

        (await vendedor.GetAsync($"/api/v1/inventario/trazabilidad?tipo=Venta&id={c.Venta.VentaId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await almacen.GetAsync($"/api/v1/inventario/trazabilidad?tipo=Venta&id={c.Venta.VentaId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await (await admin.GetAsync($"/api/v1/inventario/trazabilidad?tipo=Lote&productoId={c.Maiz.Id}"))
            .DebeFallarAsync(HttpStatusCode.BadRequest, ErroresTrazabilidad.EntradaInvalida);
        await (await admin.GetAsync($"/api/v1/inventario/trazabilidad?tipo=Venta&id={Guid.NewGuid()}"))
            .DebeFallarAsync(HttpStatusCode.NotFound, ErroresTrazabilidad.NoEncontrada);
    }
}
