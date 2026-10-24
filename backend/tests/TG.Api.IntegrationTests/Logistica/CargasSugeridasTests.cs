using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Api.IntegrationTests.Logistica;

/// <summary>Cargas sugeridas desde los pedidos confirmados (T-8.8).</summary>
public sealed class CargasSugeridasTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record IdRespuesta(Guid Id);

    private sealed record RutaPrueba(Guid Id, string Codigo);

    private sealed record PartidaSugerida(Guid ProductoId, decimal CantidadBase, decimal CambioFisicoBase);

    private sealed record PedidoSugerido(Guid PedidoId);

    private sealed record CargaSugerida(Guid RutaId, Guid? EnvioId, List<PedidoSugerido> Pedidos, List<PartidaSugerida> Partidas);

    private sealed record Sugeridas(List<CargaSugerida> Rutas);

    private sealed record Generada(Guid RutaId, Guid EnvioId, bool Nueva, int Pedidos);

    private sealed record Omitida(Guid RutaId, string Codigo);

    private sealed record Generar(List<Generada> Generadas, List<Omitida> Omitidas);

    private sealed record EnvioPartida(Guid ProductoId, decimal CantidadBase);

    private sealed record Envio(Guid Id, string Estado, string Tipo, List<EnvioPartida> Partidas);

    private sealed record Pedido(Guid Id, string Estado);

    private async Task<DateOnly> FechaAsync() =>
        (await api.ConScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IFechaOperativa>().Hoy()))).AddDays(Random.Shared.Next(60, 3000));

    private static async Task<RutaPrueba> RutaAsync(HttpClient admin, Guid canalId, bool conChofer = true) =>
        await admin.PostOkAsync<RutaPrueba>("/api/v1/logistica/rutas", new
        {
            codigo = "S" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), nombre = "Ruta sugerida", canalId,
            vendedorId = conChofer ? await admin.CrearChoferAsync() : (Guid?)null, activo = true,
        });

    private static async Task<Guid> ClienteEnRutaAsync(HttpClient admin, Guid canalId, RutaPrueba ruta, params ProductoPrueba[] productos)
    {
        var lista = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/listas-precio",
            new { nombre = DatosPrueba.Unico("LISTA"), canalId, vigenteDesde = "2026-01-01", activo = true });
        await admin.PutOkAsync<IdRespuesta>($"/api/v1/ventas/listas-precio/{lista.Id}/partidas",
            new { partidas = productos.Select(p => new { productoId = p.Id, unidadId = p.UnidadBaseId, precioUnitario = 10m }).ToArray() });
        var cliente = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/clientes", new
        {
            nombre = DatosPrueba.Unico("Tienda"), canalId, listaPrecioId = lista.Id, condicionPago = "Contado", diasCredito = 0,
            requiereFactura = false, activo = true,
        });
        await admin.PutOkAsync<List<object>>($"/api/v1/logistica/rutas/{ruta.Id}/clientes",
            new { clientes = new[] { new { clienteId = cliente.Id, ordenVisita = 1, diasVisita = new[] { 1, 2, 3, 4, 5, 6 } } } });
        return cliente.Id;
    }

    private static async Task<Guid> PedidoConfirmadoAsync(HttpClient admin, Guid clienteId, DateOnly fecha, ProductoPrueba producto, decimal cantidad)
    {
        var pedido = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/pedidos", new
        {
            clienteId, fechaEntrega = fecha, partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, cantidad } },
        });
        await admin.PostOkAsync<IdRespuesta>($"/api/v1/ventas/pedidos/{pedido.Id}/confirmar");
        return pedido.Id;
    }

    [Fact]
    [Trait("Regla", "RN-LOG-14")]
    [Trait("Regla", "RN-LOG-11")]
    public async Task CargasSugeridas_ArmanUnaCargaPorRuta_ConPedidosYCambiosFisicos_YLosNuevosSeAgreganALaMisma()
    {
        var admin = await api.ClienteAsync();
        var fecha = await FechaAsync();
        var auto = await admin.CanalAsync("AUTO");
        var tortilla = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA");
        var tostada = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA");
        var ruta = await RutaAsync(admin, auto);
        var vacia = await RutaAsync(admin, auto);
        var cliente = await ClienteEnRutaAsync(admin, auto, ruta, tortilla, tostada);

        // Pedido web (sin ruta asignada: cae en la ruta del cliente) y pedido de la app con un cambio físico.
        var web = await PedidoConfirmadoAsync(admin, cliente, fecha, tortilla, 30);
        var app = Guid.CreateVersion7();
        var hoy = await api.ConScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IFechaOperativa>().Hoy()));
        (await api.ConScopeAsync(sp => sp.GetRequiredService<IVentasService>().CapturarPedidoAsync(new CapturarPedidoComando(app,
            Guid.CreateVersion7(), cliente, fecha, hoy, null, null,
            [
                new PartidaPedidoComando(tortilla.Id, tortilla.UnidadBaseId, 20, TipoPartidaPedido.Venta, null),
                new PartidaPedidoComando(tostada.Id, tostada.UnidadBaseId, 4, TipoPartidaPedido.CambioFisico, "rotas"),
            ], Guid.Empty, null, ruta.Id), CancellationToken.None))).EsExito.ShouldBeTrue();
        await admin.PostOkAsync<IdRespuesta>($"/api/v1/ventas/pedidos/{app}/confirmar");

        var vista = await admin.GetOkAsync<Sugeridas>($"/api/v1/logistica/envios/sugeridos?fecha={fecha:yyyy-MM-dd}");
        vista.Rutas.ShouldNotContain(r => r.RutaId == vacia.Id);
        var sugerida = vista.Rutas.Single(r => r.RutaId == ruta.Id);
        sugerida.EnvioId.ShouldBeNull();
        sugerida.Pedidos.Select(p => p.PedidoId).ShouldBe([web, app], ignoreOrder: true);
        sugerida.Partidas.Single(p => p.ProductoId == tortilla.Id).CantidadBase.ShouldBe(50m);
        sugerida.Partidas.Single(p => p.ProductoId == tostada.Id).CambioFisicoBase.ShouldBe(4m);

        var generadas = await admin.PostOkAsync<Generar>("/api/v1/logistica/envios/sugeridos", new { fecha, rutaIds = new[] { ruta.Id } });
        var generada = generadas.Generadas.ShouldHaveSingleItem();
        (generada.Nueva, generada.Pedidos).ShouldBe((true, 2));
        var carga = await admin.GetOkAsync<Envio>($"/api/v1/logistica/envios/{generada.EnvioId}");
        (carga.Estado, carga.Tipo).ShouldBe(("Preparado", "CargaInicial"));
        carga.Partidas.Single(p => p.ProductoId == tortilla.Id).CantidadBase.ShouldBe(50m);
        carga.Partidas.Single(p => p.ProductoId == tostada.Id).CantidadBase.ShouldBe(4m);
        (await admin.GetOkAsync<Pedido>($"/api/v1/ventas/pedidos/{web}")).Estado.ShouldBe("Asignado");

        // Ya no se sugiere lo que va en la carga; un pedido nuevo se agrega a la misma carga Preparado.
        (await admin.GetOkAsync<Sugeridas>($"/api/v1/logistica/envios/sugeridos?fecha={fecha:yyyy-MM-dd}")).Rutas
            .ShouldNotContain(r => r.RutaId == ruta.Id);
        await PedidoConfirmadoAsync(admin, cliente, fecha, tortilla, 10);
        var otra = await admin.PostOkAsync<Generar>("/api/v1/logistica/envios/sugeridos", new { fecha, rutaIds = new[] { ruta.Id } });
        var agregada = otra.Generadas.ShouldHaveSingleItem();
        (agregada.EnvioId, agregada.Nueva).ShouldBe((carga.Id, false));
        (await admin.GetOkAsync<Envio>($"/api/v1/logistica/envios/{carga.Id}")).Partidas
            .Single(p => p.ProductoId == tortilla.Id).CantidadBase.ShouldBe(60m);
    }

    [Fact]
    [Trait("Regla", "RN-LOG-14")]
    [Trait("Regla", "RN-LOG-12")]
    public async Task RutaSinChofer_SeOmiteConSuError_YLasDemasSeGeneran()
    {
        var admin = await api.ClienteAsync();
        var fecha = await FechaAsync();
        var auto = await admin.CanalAsync("AUTO");
        var producto = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA");
        var sinChofer = await RutaAsync(admin, auto, conChofer: false);
        var conChofer = await RutaAsync(admin, auto);
        await PedidoConfirmadoAsync(admin, await ClienteEnRutaAsync(admin, auto, sinChofer, producto), fecha, producto, 5);
        await PedidoConfirmadoAsync(admin, await ClienteEnRutaAsync(admin, auto, conChofer, producto), fecha, producto, 7);

        var r = await admin.PostOkAsync<Generar>("/api/v1/logistica/envios/sugeridos",
            new { fecha, rutaIds = new[] { sinChofer.Id, conChofer.Id } });
        r.Generadas.ShouldHaveSingleItem().RutaId.ShouldBe(conChofer.Id);
        var omitida = r.Omitidas.ShouldHaveSingleItem();
        (omitida.RutaId, omitida.Codigo).ShouldBe((sinChofer.Id, "LOG-017"));
    }
}
