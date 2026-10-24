using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.SharedKernel.Tiempo;

namespace TG.Api.IntegrationTests.Logistica;

/// <summary>Cambios físicos y cambios pendientes (T-8.9).</summary>
public sealed class CambiosFisicosTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record IdRespuesta(Guid Id);

    private sealed record RutaPrueba(Guid Id, string Codigo, Guid AlmacenId, Guid? ClienteGenericoId);

    private sealed record EnvioPrueba(Guid Id, Guid? LiquidacionId);

    private sealed record RetornoPartida(Guid ProductoId, string Lote, decimal CantidadBase, string Sentido, Guid? CambioPendienteId);

    private sealed record RetornoPrueba(Guid Id, string Folio, List<RetornoPartida> Partidas);

    private sealed record MovimientoPrueba(string Tipo, string Lote, decimal Cantidad);

    private sealed record PaginaPrueba<T>(List<T> Items, int Total);

    private sealed record Pendiente(Guid Id, Guid ClienteId, Guid ProductoId, decimal CantidadBase, decimal CantidadSaldadaBase,
        decimal RestanteBase, string Estado, string? MotivoCancelacion);

    private sealed record LiquidacionPartida(Guid ProductoId, string Lote, decimal Cargado, decimal DevueltoClientesNoVendible,
        decimal CambiosEntregados, decimal SobranteEsperado);

    private sealed record Liquidacion(Guid Id, List<LiquidacionPartida> Partidas);

    private sealed record FilaPlan(Guid ProductoId, decimal Demanda, decimal CambiosPendientes);

    private sealed record Plan(List<FilaPlan> Filas);

    private sealed record PartidaSugerida(Guid ProductoId, decimal CantidadBase, decimal PendienteBase);

    private sealed record CargaSugerida(Guid RutaId, List<PartidaSugerida> Partidas);

    private sealed record Sugeridas(List<CargaSugerida> Rutas);

    // ───────────── Ayudas ─────────────

    private Task<DateOnly> HoyAsync() => api.ConScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IFechaOperativa>().Hoy()));

    private static async Task<RutaPrueba> RutaAsync(HttpClient admin) =>
        await admin.PostOkAsync<RutaPrueba>("/api/v1/logistica/rutas", new
        {
            codigo = "C" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), nombre = "Ruta cambios", canalId = await admin.CanalAsync("DET"),
            vendedorId = await admin.CrearChoferAsync(), activo = true,
        });

    /// <summary>Cliente de Detalle en la ruta, visitado todos los días, con precio para los productos.</summary>
    private static async Task<Guid> ClienteAsync(HttpClient admin, RutaPrueba ruta, params ProductoPrueba[] productos)
    {
        var det = await admin.CanalAsync("DET");
        var lista = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/listas-precio",
            new { nombre = DatosPrueba.Unico("LISTA"), canalId = det, vigenteDesde = "2026-01-01", activo = true });
        await admin.PutOkAsync<IdRespuesta>($"/api/v1/ventas/listas-precio/{lista.Id}/partidas",
            new { partidas = productos.Select(p => new { productoId = p.Id, unidadId = p.UnidadBaseId, precioUnitario = 10m }).ToArray() });
        var cliente = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/clientes", new
        {
            nombre = DatosPrueba.Unico("Tienda"), canalId = det, listaPrecioId = lista.Id, condicionPago = "Contado", diasCredito = 0,
            requiereFactura = false, activo = true,
        });
        await admin.PutOkAsync<List<object>>($"/api/v1/logistica/rutas/{ruta.Id}/clientes",
            new { clientes = new[] { new { clienteId = cliente.Id, ordenVisita = 1, diasVisita = new[] { 1, 2, 3, 4, 5, 6, 7 } } } });
        return cliente.Id;
    }

    /// <summary>Tortilla con lote propio, cargada y despachada a la ruta.</summary>
    private async Task<(ProductoPrueba Tortilla, string Lote, EnvioPrueba Envio)> CargarAsync(HttpClient admin, RutaPrueba ruta, decimal cantidad)
    {
        var tortilla = await admin.ProductoAsync("PT-TORT-KG");
        var lote = "L" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await admin.EntradaAsync(await admin.AlmacenAsync("PT"), tortilla.Id, cantidad, 9.5m, lote, (await HoyAsync()).AddDays(2));
        var envio = await admin.PostOkAsync<IdRespuesta>("/api/v1/logistica/envios", new
        {
            rutaId = ruta.Id, tipo = "CargaInicial", partidas = new[] { new { productoId = tortilla.Id, cantidad, lote } },
        });
        return (tortilla, lote, await admin.PostOkAsync<EnvioPrueba>($"/api/v1/logistica/envios/{envio.Id}/despachar"));
    }

    private static object Cambio(RutaPrueba ruta, Guid? clienteId, params object[] partidas) =>
        new { tipo = "CambioFisico", rutaId = ruta.Id, clienteId, partidas };

    private static Task<List<Pendiente>> PendientesAsync(HttpClient admin, Guid clienteId) =>
        admin.GetOkAsync<List<Pendiente>>($"/api/v1/logistica/cambios-pendientes?clienteId={clienteId}");

    // ───────────── Pruebas ─────────────

    [Fact]
    [Trait("Regla", "RN-LOG-15")]
    [Trait("Regla", "RN-LOG-16")]
    [Trait("Regla", "RN-LOG-18")]
    [Trait("Regla", "RN-INV-11")]
    public async Task CambioFisico_RetiradoEsDevolucion_EntregadoEsSalidaCambio_LoFaltanteQuedaPendiente_YLaLiquidacionCuadra()
    {
        var admin = await api.ClienteAsync();
        var ruta = await RutaAsync(admin);
        var (tortilla, lote, carga) = await CargarAsync(admin, ruta, 20);
        var cliente = await ClienteAsync(admin, ruta, tortilla);

        // Sin cliente o con el genérico: LOG-019.
        await (await admin.PostJsonAsync("/api/v1/logistica/retornos",
                Cambio(ruta, ruta.ClienteGenericoId, new { productoId = tortilla.Id, cantidad = 1 })))
            .DebeFallarAsync(HttpStatusCode.BadRequest, "LOG-019");

        // Retira 5 (tortilla: no vendible [SUP-13]) y entrega 3.
        var cambio = await admin.PostOkAsync<RetornoPrueba>("/api/v1/logistica/retornos", Cambio(ruta, cliente,
            new { productoId = tortilla.Id, cantidad = 5 },
            new { productoId = tortilla.Id, cantidad = 3, sentido = "Entregado" }));
        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>($"/api/v1/inventario/kardex?documentoTipo=Retorno&documentoId={cambio.Id}");
        kardex.Items.Select(m => (m.Tipo, m.Cantidad)).ShouldBe([("RetornoCliente", 5m), ("Merma", -5m), ("SalidaCambio", -3m)], ignoreOrder: true);
        kardex.Items.ShouldAllBe(m => m.Lote == lote);
        cambio.Partidas.Single(p => p.Sentido == "Entregado").Lote.ShouldBe(lote);
        (await admin.ExistenciaAsync(ruta.AlmacenId, tortilla.Id)).ShouldBe(17m);

        var pendiente = (await PendientesAsync(admin, cliente)).ShouldHaveSingleItem();
        (pendiente.ProductoId, pendiente.CantidadBase, pendiente.Estado).ShouldBe((tortilla.Id, 2m, "Pendiente"));

        // "No queda pendiente" (cambio no autorizado): no se crea.
        await admin.PostOkAsync<RetornoPrueba>("/api/v1/logistica/retornos", Cambio(ruta, cliente,
            new { productoId = tortilla.Id, cantidad = 1, condicion = "Vendible", dejarPendiente = false }));
        (await PendientesAsync(admin, cliente)).Count.ShouldBe(1);

        // RN-LOG-18: carga 20 = cambios 3 + sobrante esperado 18 − devuelto vendible 1.
        var liquidacion = await admin.GetOkAsync<Liquidacion>($"/api/v1/logistica/liquidaciones/{carga.LiquidacionId}");
        var partida = liquidacion.Partidas.Single(p => p.ProductoId == tortilla.Id && p.Lote == lote);
        (partida.Cargado, partida.CambiosEntregados, partida.DevueltoClientesNoVendible, partida.SobranteEsperado).ShouldBe((20m, 3m, 5m, 18m));
    }

    [Fact]
    [Trait("Regla", "RN-INV-11")]
    public async Task EntregarMasDeLoQueLlevaLaCamioneta_DejaNegativoElAlmacenDeRuta()
    {
        var admin = await api.ClienteAsync();
        var ruta = await RutaAsync(admin);
        var (tortilla, _, _) = await CargarAsync(admin, ruta, 2);
        var cliente = await ClienteAsync(admin, ruta, tortilla);

        await admin.PostOkAsync<RetornoPrueba>("/api/v1/logistica/retornos",
            Cambio(ruta, cliente, new { productoId = tortilla.Id, cantidad = 5, sentido = "Entregado" }));
        (await admin.ExistenciaAsync(ruta.AlmacenId, tortilla.Id)).ShouldBe(-3m);
    }

    [Fact]
    [Trait("Regla", "RN-LOG-17")]
    public async Task EntregaQueSaldaUnPendiente_LoSalda_ElExcedenteEsEntregaNormal_YCancelarPideMotivo()
    {
        var admin = await api.ClienteAsync();
        var ruta = await RutaAsync(admin);
        var (tortilla, _, _) = await CargarAsync(admin, ruta, 30);
        var cliente = await ClienteAsync(admin, ruta, tortilla);
        await admin.PostOkAsync<RetornoPrueba>("/api/v1/logistica/retornos", Cambio(ruta, cliente, new { productoId = tortilla.Id, cantidad = 4 }));
        var pendiente = (await PendientesAsync(admin, cliente)).ShouldHaveSingleItem();

        // Entrega 6 contra el pendiente de 4: salda 4; los otros 2 son entrega normal (no generan otro pendiente).
        var saldo = await admin.PostOkAsync<RetornoPrueba>("/api/v1/logistica/retornos", Cambio(ruta, cliente,
            new { productoId = tortilla.Id, cantidad = 6, sentido = "Entregado", cambioPendienteId = pendiente.Id }));
        saldo.Partidas.ShouldAllBe(p => p.CambioPendienteId == pendiente.Id);
        var saldado = (await PendientesAsync(admin, cliente)).ShouldHaveSingleItem();
        (saldado.CantidadSaldadaBase, saldado.Estado).ShouldBe((4m, "Saldado"));

        await (await admin.PostJsonAsync("/api/v1/logistica/retornos", Cambio(ruta, cliente,
                new { productoId = tortilla.Id, cantidad = 1, sentido = "Entregado", cambioPendienteId = pendiente.Id })))
            .DebeFallarAsync(HttpStatusCode.BadRequest, "LOG-020");

        // Cancelar requiere motivo; un pendiente ya cancelado no se vuelve a cancelar.
        await admin.PostOkAsync<RetornoPrueba>("/api/v1/logistica/retornos", Cambio(ruta, cliente, new { productoId = tortilla.Id, cantidad = 2 }));
        var otro = (await PendientesAsync(admin, cliente)).Single(p => p.Estado == "Pendiente");
        await (await admin.PostJsonAsync($"/api/v1/logistica/cambios-pendientes/{otro.Id}/cancelar", new { motivo = "" }))
            .DebeFallarAsync(HttpStatusCode.BadRequest);
        var cancelado = await admin.PostOkAsync<Pendiente>($"/api/v1/logistica/cambios-pendientes/{otro.Id}/cancelar",
            new { motivo = "Cambio no autorizado" });
        (cancelado.Estado, cancelado.MotivoCancelacion).ShouldBe(("Cancelado", "Cambio no autorizado"));
        await (await admin.PostJsonAsync($"/api/v1/logistica/cambios-pendientes/{otro.Id}/cancelar", new { motivo = "otra vez" }))
            .DebeFallarAsync(HttpStatusCode.Conflict, "LOG-020");
    }

    [Fact]
    [Trait("Regla", "RN-LOG-14")]
    [Trait("Regla", "RN-PRO-13")]
    public async Task PendientesDelDiaDeVisita_SeSumanAlPlanSugerido_YALaCargaSugerida()
    {
        var admin = await api.ClienteAsync();
        var hoy = await HoyAsync();
        var ruta = await RutaAsync(admin);
        var salsa = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA");
        var cliente = await ClienteAsync(admin, ruta, salsa);
        await admin.PostOkAsync<RetornoPrueba>("/api/v1/logistica/retornos", Cambio(ruta, cliente,
            new { productoId = salsa.Id, cantidad = 4, condicion = "Vendible" }));

        var plan = await admin.GetOkAsync<Plan>($"/api/v1/produccion/plan-sugerido?fechaEntrega={hoy:yyyy-MM-dd}");
        var fila = plan.Filas.Single(f => f.ProductoId == salsa.Id);
        (fila.CambiosPendientes, fila.Demanda).ShouldBe((4m, 4m));

        var pedido = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/pedidos", new
        {
            clienteId = cliente, fechaEntrega = hoy, partidas = new[] { new { productoId = salsa.Id, unidadId = salsa.UnidadBaseId, cantidad = 6 } },
        });
        await admin.PostOkAsync<IdRespuesta>($"/api/v1/ventas/pedidos/{pedido.Id}/confirmar");
        var sugeridas = await admin.GetOkAsync<Sugeridas>($"/api/v1/logistica/envios/sugeridos?fecha={hoy:yyyy-MM-dd}");
        var partida = sugeridas.Rutas.Single(r => r.RutaId == ruta.Id).Partidas.Single(p => p.ProductoId == salsa.Id);
        (partida.CantidadBase, partida.PendienteBase).ShouldBe((10m, 4m));
    }
}
