using TG.Api.IntegrationTests.Infraestructura;
using TG.SharedKernel.Tiempo;
using Microsoft.Extensions.DependencyInjection;

namespace TG.Api.IntegrationTests.Tablero;

/// <summary>Globos de pendientes del menú (07-web, RN-WEB-01).</summary>
public sealed class PendientesTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record IdRespuesta(Guid Id);

    private sealed record Pendientes(int? OrdenesCompraPorAutorizar, int? PlanPorProgramar, int? OrdenesPorLiberar, int? OrdenesPorProducir,
        int? PedidosPorValidar);

    [Fact]
    [Trait("Regla", "RN-WEB-01")]
    public async Task Pendientes_CuentaComoLaLista_SeApagaAlAtender_YSinPermisoLlegaNulo()
    {
        var admin = await api.ClienteAsync();
        var tortilla = await admin.CrearProductoAsync(tipo: "Terminado", unidad: "PZA");
        var receta = await admin.PostOkAsync<IdRespuesta>("/api/v1/produccion/recetas", new
        {
            productoId = tortilla.Id, nombre = "Receta pendientes", rendimientoCantidad = 100, mermaEsperadaPct = 0,
            componentes = new[] { new { productoId = (await admin.CrearProductoAsync()).Id, cantidad = 50m } },
        });
        await admin.PostOkAsync<IdRespuesta>($"/api/v1/produccion/recetas/{receta.Id}/publicar");
        var antes = await admin.GetOkAsync<Pendientes>("/api/v1/pendientes");

        // Un pedido para mañana sin orden: el plan sugerido tiene un producto por programar.
        var manana = (await api.ConScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IFechaOperativa>().Hoy()))).AddDays(1);
        var auto = await admin.CanalAsync("AUTO");
        var lista = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/listas-precio",
            new { nombre = DatosPrueba.Unico("LISTA"), canalId = auto, vigenteDesde = "2026-01-01", activo = true });
        await admin.PutOkAsync<IdRespuesta>($"/api/v1/ventas/listas-precio/{lista.Id}/partidas",
            new { partidas = new[] { new { productoId = tortilla.Id, unidadId = tortilla.UnidadBaseId, precioUnitario = 10m } } });
        var cliente = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/clientes", new
        {
            nombre = DatosPrueba.Unico("Tienda"), canalId = auto, listaPrecioId = lista.Id, condicionPago = "Contado", diasCredito = 0,
            requiereFactura = false, activo = true,
        });
        await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/pedidos", new
        {
            clienteId = cliente.Id, fechaEntrega = manana,
            partidas = new[] { new { productoId = tortilla.Id, unidadId = tortilla.UnidadBaseId, cantidad = 30m } },
        });
        (await admin.GetOkAsync<Pendientes>("/api/v1/pendientes")).PlanPorProgramar.ShouldBe(antes.PlanPorProgramar + 1);

        // Programarla la apaga del plan y la pone por liberar; liberarla la pasa a por producir.
        var orden = await admin.PostOkAsync<IdRespuesta>("/api/v1/produccion/ordenes",
            new { productoId = tortilla.Id, fechaOperativa = manana.AddDays(-1), turno = "Matutino", cantidadPlaneada = 30 });
        var programada = await admin.GetOkAsync<Pendientes>("/api/v1/pendientes");
        (programada.PlanPorProgramar, programada.OrdenesPorLiberar).ShouldBe((antes.PlanPorProgramar, antes.OrdenesPorLiberar + 1));

        await admin.PostOkAsync<IdRespuesta>($"/api/v1/produccion/ordenes/{orden.Id}/liberar");
        var liberada = await admin.GetOkAsync<Pendientes>("/api/v1/pendientes");
        (liberada.OrdenesPorLiberar, liberada.OrdenesPorProducir).ShouldBe((antes.OrdenesPorLiberar, antes.OrdenesPorProducir + 1));

        // El vendedor no autoriza compras ni planea producción: esos conteos no le llegan.
        var vendedor = await (await api.ClienteAsync("vendedor1")).GetOkAsync<Pendientes>("/api/v1/pendientes");
        (vendedor.OrdenesCompraPorAutorizar, vendedor.PlanPorProgramar, vendedor.OrdenesPorLiberar).ShouldBe((null, null, null));
    }
}
