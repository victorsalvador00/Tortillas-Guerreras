using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.SharedKernel.Tiempo;

namespace TG.Api.IntegrationTests.Produccion;

/// <summary>Estimado de producción, plan sugerido y copiar receta (T-8.7).</summary>
public sealed class EstimadoTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record IdRespuesta(Guid Id);

    private sealed record RutaPrueba(Guid Id, string Codigo, Guid? ClienteGenericoId);

    private sealed record ClientePrueba(Guid Id, Guid ListaPrecioId);

    private sealed record PartidaLista(Guid ProductoId, Guid UnidadId, decimal PrecioUnitario);

    private sealed record ListaPrueba(Guid Id, List<PartidaLista> Partidas);

    private sealed record Celda(Guid ProductoId, decimal Cantidad);

    private sealed record Fila(Guid RutaId, bool Editable, bool EsReferencia, List<Celda> Celdas, List<Guid> PedidoIds, int Capturados);

    private sealed record Columna(Guid ProductoId, string Encabezado);

    private sealed record TotalProducto(Guid ProductoId, decimal Total, string? Cajas);

    private sealed record Estimado(List<Columna> Columnas, List<Fila> Filas, List<TotalProducto> Totales, int PedidosCapturados);

    private sealed record Resultado(int Pedidos);

    private sealed record FilaPlan(Guid ProductoId, string Accion, decimal Demanda, decimal Disponible, decimal EnOrdenes, decimal Faltante,
        bool TieneReceta);

    private sealed record Plan(List<FilaPlan> Filas);

    private sealed record Creada(Guid ProductoId, string Folio);

    private sealed record Omitida(Guid ProductoId, string Codigo);

    private sealed record CrearOrdenes(List<Creada> Creadas, List<Omitida> Omitidas);

    private sealed record RecetaPrueba(Guid Id, Guid ProductoId, string Estado, decimal RendimientoCantidad, List<IdRespuesta> Componentes);

    // ───────────── Ayudas ─────────────

    /// <summary>Fecha de entrega propia de cada prueba para no mezclar pedidos de otras pruebas de la clase.</summary>
    private async Task<DateOnly> FechaAsync() =>
        (await api.ConScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IFechaOperativa>().Hoy()))).AddDays(Random.Shared.Next(60, 3000));

    /// <summary>Terminado en paquetes con caja de 24 (RN-CAT-09).</summary>
    private async Task<ProductoPrueba> TerminadoConCajaAsync(HttpClient admin) =>
        await admin.CrearProductoAsync(tipo: "Terminado", unidad: "PAQ",
            conversiones: [new { unidadId = await admin.UnidadAsync("CAJ"), factorABase = 24m, usoTraslado = true, predeterminadaTraslado = true }]);

    private async Task<RutaPrueba> RutaAsync(HttpClient admin, string canal) =>
        await admin.PostOkAsync<RutaPrueba>("/api/v1/logistica/rutas", new
        {
            codigo = "E" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), nombre = "Ruta estimado", canalId = await admin.CanalAsync(canal),
            vendedorId = await admin.CrearChoferAsync(), activo = true,
        });

    private static async Task<Guid> ListaConPrecioAsync(HttpClient admin, Guid canalId, params ProductoPrueba[] productos)
    {
        var lista = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/listas-precio",
            new { nombre = DatosPrueba.Unico("LISTA"), canalId, vigenteDesde = "2026-01-01", activo = true });
        await admin.PutOkAsync<IdRespuesta>($"/api/v1/ventas/listas-precio/{lista.Id}/partidas",
            new { partidas = productos.Select(p => new { productoId = p.Id, unidadId = p.UnidadBaseId, precioUnitario = 10m }).ToArray() });
        return lista.Id;
    }

    private static async Task<Guid> ClienteAsync(HttpClient admin, Guid canalId, Guid listaId) =>
        (await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/clientes", new
        {
            nombre = DatosPrueba.Unico("Tienda"), canalId, listaPrecioId = listaId, condicionPago = "Contado", diasCredito = 0,
            requiereFactura = false, activo = true,
        })).Id;

    /// <summary>Agrega el producto a la lista del genérico de la ruta (conserva sus demás precios).</summary>
    private static async Task PrecioEnGenericoAsync(HttpClient admin, RutaPrueba ruta, ProductoPrueba producto)
    {
        var generico = await admin.GetOkAsync<ClientePrueba>($"/api/v1/ventas/clientes/{ruta.ClienteGenericoId}");
        var lista = await admin.GetOkAsync<ListaPrueba>($"/api/v1/ventas/listas-precio/{generico.ListaPrecioId}");
        var partidas = lista.Partidas.Where(p => p.ProductoId != producto.Id).Append(new PartidaLista(producto.Id, producto.UnidadBaseId, 10m))
            .Select(p => new { productoId = p.ProductoId, unidadId = p.UnidadId, precioUnitario = p.PrecioUnitario }).ToArray();
        await admin.PutOkAsync<IdRespuesta>($"/api/v1/ventas/listas-precio/{generico.ListaPrecioId}/partidas", new { partidas });
    }

    private static Task<IdRespuesta> PedidoAsync(HttpClient admin, Guid clienteId, DateOnly fecha, ProductoPrueba producto, decimal cantidad) =>
        admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/pedidos", new
        {
            clienteId, fechaEntrega = fecha, partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, cantidad } },
        });

    private static object CeldasDet(DateOnly fecha, Guid canalId, RutaPrueba ruta, ProductoPrueba producto, decimal cantidad) => new
    {
        fechaEntrega = fecha, canalId,
        filas = new[] { new { rutaId = ruta.Id, celdas = new[] { new { productoId = producto.Id, cantidad } } } },
    };

    private static decimal CeldaDe(Estimado e, Guid rutaId, Guid productoId) =>
        e.Filas.Single(f => f.RutaId == rutaId).Celdas.SingleOrDefault(c => c.ProductoId == productoId)?.Cantidad ?? 0;

    // ───────────── Estimado ─────────────

    [Fact]
    [Trait("Regla", "RN-PRO-11")]
    [Trait("Regla", "RN-VEN-16")]
    [Trait("Regla", "RN-CAT-09")]
    public async Task Estimado_SumaLasTiendasDeLaRutaEnAuto_YEnDetalleEscribeElPedidoDeLaRuta()
    {
        var admin = await api.ClienteAsync();
        var fecha = await FechaAsync();
        var producto = await TerminadoConCajaAsync(admin);
        var auto = await admin.CanalAsync("AUTO");
        var det = await admin.CanalAsync("DET");

        // AUTO: tres tiendas de R1 con 24, 48 y 48.
        var r1 = await RutaAsync(admin, "AUTO");
        var lista = await ListaConPrecioAsync(admin, auto, producto);
        var tiendas = new[] { await ClienteAsync(admin, auto, lista), await ClienteAsync(admin, auto, lista), await ClienteAsync(admin, auto, lista) };
        await admin.PutOkAsync<List<object>>($"/api/v1/logistica/rutas/{r1.Id}/clientes", new
        {
            clientes = tiendas.Select((t, i) => new { clienteId = t, ordenVisita = i + 1, diasVisita = new[] { 1, 2, 3, 4, 5, 6 } }).ToArray(),
        });
        foreach (var (tienda, cantidad) in tiendas.Zip(new[] { 24m, 48m, 48m }))
        {
            await PedidoAsync(admin, tienda, fecha, producto, cantidad);
        }

        var estimadoAuto = await admin.GetOkAsync<Estimado>($"/api/v1/produccion/estimado?fechaEntrega={fecha:yyyy-MM-dd}&canalId={auto}");
        estimadoAuto.Columnas.ShouldContain(c => c.ProductoId == producto.Id && c.Encabezado == producto.Sku);
        CeldaDe(estimadoAuto, r1.Id, producto.Id).ShouldBe(120m);
        estimadoAuto.Filas.Single(f => f.RutaId == r1.Id).Editable.ShouldBeFalse();
        var totalAuto = estimadoAuto.Totales.Single(t => t.ProductoId == producto.Id);
        totalAuto.Total.ShouldBe(120m);
        totalAuto.Cajas.ShouldStartWith("5 CAJ");
        await (await admin.PutJsonAsync("/api/v1/produccion/estimado", CeldasDet(fecha, auto, r1, producto, 10)))
            .DebeFallarAsync(HttpStatusCode.BadRequest, "PRO-015");

        // DET: la celda es el pedido global de R51.
        var r51 = await RutaAsync(admin, "DET");
        await PrecioEnGenericoAsync(admin, r51, producto);
        var estimadoDet = await admin.PutOkAsync<Estimado>("/api/v1/produccion/estimado", CeldasDet(fecha, det, r51, producto, 96));
        CeldaDe(estimadoDet, r51.Id, producto.Id).ShouldBe(96m);
        estimadoDet.Filas.Single(f => f.RutaId == r51.Id).Editable.ShouldBeTrue();
        estimadoDet.Totales.Single(t => t.ProductoId == producto.Id).Cajas.ShouldStartWith("4 CAJ");

        // Corregir la celda edita el mismo pedido.
        var corregido = await admin.PutOkAsync<Estimado>("/api/v1/produccion/estimado", CeldasDet(fecha, det, r51, producto, 72));
        CeldaDe(corregido, r51.Id, producto.Id).ShouldBe(72m);
        corregido.Filas.Single(f => f.RutaId == r51.Id).PedidoIds.ShouldHaveSingleItem();

        // Confirmar estimado: queda fijo.
        (await admin.PostOkAsync<Resultado>($"/api/v1/produccion/estimado/confirmar?fechaEntrega={fecha:yyyy-MM-dd}&canalId={det}")).Pedidos.ShouldBe(1);
        var confirmado = await admin.GetOkAsync<Estimado>($"/api/v1/produccion/estimado?fechaEntrega={fecha:yyyy-MM-dd}&canalId={det}");
        confirmado.Filas.Single(f => f.RutaId == r51.Id).Editable.ShouldBeFalse();
        await (await admin.PutJsonAsync("/api/v1/produccion/estimado", CeldasDet(fecha, det, r51, producto, 50)))
            .DebeFallarAsync(HttpStatusCode.Conflict, "VEN-012");
    }

    [Fact]
    [Trait("Regla", "RN-PRO-12")]
    public async Task RutaSinPedido_MuestraLaReferenciaDeHaceSieteDias_YUsarReferenciaLaCopiaComoCapturado()
    {
        var admin = await api.ClienteAsync();
        var fecha = await FechaAsync();
        var producto = await TerminadoConCajaAsync(admin);
        var det = await admin.CanalAsync("DET");
        var ruta = await RutaAsync(admin, "DET");
        await PrecioEnGenericoAsync(admin, ruta, producto);
        await admin.PutOkAsync<Estimado>("/api/v1/produccion/estimado", CeldasDet(fecha.AddDays(-7), det, ruta, producto, 30));

        var url = $"/api/v1/produccion/estimado?fechaEntrega={fecha:yyyy-MM-dd}&canalId={det}";
        var conReferencia = await admin.GetOkAsync<Estimado>(url);
        var fila = conReferencia.Filas.Single(f => f.RutaId == ruta.Id);
        fila.EsReferencia.ShouldBeTrue();
        CeldaDe(conReferencia, ruta.Id, producto.Id).ShouldBe(30m);
        conReferencia.Totales.Single(t => t.ProductoId == producto.Id).Total.ShouldBe(0m);

        (await admin.PostOkAsync<Resultado>("/api/v1/produccion/estimado/usar-referencia",
            new { fechaEntrega = fecha, canalId = det, rutaId = ruta.Id })).Pedidos.ShouldBe(1);
        var usado = await admin.GetOkAsync<Estimado>(url);
        var propia = usado.Filas.Single(f => f.RutaId == ruta.Id);
        propia.EsReferencia.ShouldBeFalse();
        propia.Capturados.ShouldBe(1);
        CeldaDe(usado, ruta.Id, producto.Id).ShouldBe(30m);
        await (await admin.PostJsonAsync("/api/v1/produccion/estimado/usar-referencia", new { fechaEntrega = fecha, canalId = det, rutaId = ruta.Id }))
            .DebeFallarAsync(HttpStatusCode.BadRequest, "PRO-017");
    }

    // ───────────── Plan sugerido ─────────────

    [Fact]
    [Trait("Regla", "RN-PRO-13")]
    public async Task PlanSugerido_DemandaMenosDisponibleMenosOrdenes_ReventaSeCompra_YSinRecetaNoSeCrea()
    {
        var admin = await api.ClienteAsync();
        var fecha = await FechaAsync();
        var auto = await admin.CanalAsync("AUTO");
        var tortilla = await admin.CrearProductoAsync(tipo: "Terminado", unidad: "PZA", vidaUtil: 5);
        var sinReceta = await admin.CrearProductoAsync(tipo: "Terminado", unidad: "PZA");
        var salsa = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA");
        var masa = await admin.CrearProductoAsync();
        var receta = await admin.PostOkAsync<IdRespuesta>("/api/v1/produccion/recetas", new
        {
            productoId = tortilla.Id, nombre = "Receta plan", rendimientoCantidad = 100, mermaEsperadaPct = 0,
            componentes = new[] { new { productoId = masa.Id, cantidad = 50m } },
        });
        await admin.PostOkAsync<IdRespuesta>($"/api/v1/produccion/recetas/{receta.Id}/publicar");
        var cliente = await ClienteAsync(admin, auto, await ListaConPrecioAsync(admin, auto, tortilla, sinReceta, salsa));
        await PedidoAsync(admin, cliente, fecha, tortilla, 200);
        await PedidoAsync(admin, cliente, fecha, sinReceta, 5);
        await PedidoAsync(admin, cliente, fecha, salsa, 10);

        // 150 vigentes en PT para la fecha y 40 que caducan antes (no cuentan).
        var pt = await admin.AlmacenAsync("PT");
        await admin.EntradaAsync(pt, tortilla.Id, 150, 3m, lote: DatosPrueba.Unico("L"), caducidad: fecha.AddDays(1));
        await admin.EntradaAsync(pt, tortilla.Id, 40, 3m, lote: DatosPrueba.Unico("L"), caducidad: fecha.AddDays(-1));

        var url = $"/api/v1/produccion/plan-sugerido?fechaEntrega={fecha:yyyy-MM-dd}";
        var plan = await admin.GetOkAsync<Plan>(url);
        var fila = plan.Filas.Single(f => f.ProductoId == tortilla.Id);
        (fila.Demanda, fila.Disponible, fila.Faltante, fila.Accion).ShouldBe((200m, 150m, 50m, "Producir"));
        var compra = plan.Filas.Single(f => f.ProductoId == salsa.Id);
        (compra.Accion, compra.Faltante).ShouldBe(("Comprar", 10m));
        plan.Filas.Single(f => f.ProductoId == sinReceta.Id).TieneReceta.ShouldBeFalse();

        await admin.PostOkAsync<IdRespuesta>("/api/v1/produccion/ordenes",
            new { productoId = tortilla.Id, fechaOperativa = fecha.AddDays(-1), turno = "Matutino", cantidadPlaneada = 30 });
        var conOrden = (await admin.GetOkAsync<Plan>(url)).Filas.Single(f => f.ProductoId == tortilla.Id);
        (conOrden.EnOrdenes, conOrden.Faltante).ShouldBe((30m, 20m));

        var creadas = await admin.PostOkAsync<CrearOrdenes>("/api/v1/produccion/plan-sugerido/crear-ordenes", new
        {
            fechaOperativa = fecha.AddDays(-1), turno = "Matutino",
            partidas = new[] { new { productoId = tortilla.Id, cantidad = 20m }, new { productoId = sinReceta.Id, cantidad = 5m } },
        });
        creadas.Creadas.ShouldHaveSingleItem().ProductoId.ShouldBe(tortilla.Id);
        creadas.Omitidas.ShouldHaveSingleItem().Codigo.ShouldBe("PRO-008");
        (await admin.GetOkAsync<Plan>(url)).Filas.Single(f => f.ProductoId == tortilla.Id).Faltante.ShouldBe(0m);
    }

    // ───────────── Copiar receta ─────────────

    [Fact]
    [Trait("Regla", "RN-PRO-14")]
    public async Task CopiarReceta_CreaBorradorConLosMismosComponentes_YRendimientoPorElFactor()
    {
        var admin = await api.ClienteAsync();
        var t800 = await admin.CrearProductoAsync(tipo: "Terminado", unidad: "PAQ");
        var t450 = await admin.CrearProductoAsync(tipo: "Terminado", unidad: "PAQ");
        var masa = await admin.CrearProductoAsync();
        var origen = await admin.PostOkAsync<RecetaPrueba>("/api/v1/produccion/recetas", new
        {
            productoId = t800.Id, nombre = "T-800", rendimientoCantidad = 90, mermaEsperadaPct = 0.02,
            componentes = new[] { new { productoId = masa.Id, cantidad = 72m } },
        });

        var copia = await admin.PostOkAsync<RecetaPrueba>($"/api/v1/produccion/recetas/{origen.Id}/copiar",
            new { productoDestinoId = t450.Id, factorRendimiento = 800m / 450m });
        copia.ProductoId.ShouldBe(t450.Id);
        copia.Estado.ShouldBe("Borrador");
        copia.RendimientoCantidad.ShouldBe(160m);
        copia.Componentes.ShouldHaveSingleItem();

        await (await admin.PostJsonAsync($"/api/v1/produccion/recetas/{origen.Id}/copiar", new { productoDestinoId = masa.Id }))
            .DebeFallarAsync(HttpStatusCode.BadRequest, "PRO-005");
    }
}
