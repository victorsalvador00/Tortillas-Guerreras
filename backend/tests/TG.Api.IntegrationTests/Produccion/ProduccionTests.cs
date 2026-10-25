using System.Net;
using System.Text.Json.Nodes;
using TG.Api.IntegrationTests.Infraestructura;

namespace TG.Api.IntegrationTests.Produccion;

public sealed class ProduccionTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly DateOnly Fecha = new(2026, 11, 3);

    private sealed record RecetaPrueba(Guid Id, Guid ProductoId, int Version, string Estado, string Nombre);

    private sealed record ConsumoPrueba(Guid ProductoId, string Sku, decimal CantidadTeoricaBase, decimal? CantidadRealBase,
        decimal CostoUnitario);

    private sealed record MovimientoOrdenPrueba(string Tipo, Guid AlmacenId, Guid ProductoId, string Lote, decimal Cantidad,
        decimal CostoUnitario);

    private sealed record OrdenPrueba(Guid Id, string Folio, Guid RecetaId, int RecetaVersion, string Lote, string Estado,
        decimal? CantidadProducida, decimal? CostoTotal, decimal? CostoUnitario, Guid AlmacenDestinoId,
        IReadOnlyList<ConsumoPrueba> Consumos, IReadOnlyList<MovimientoOrdenPrueba> Movimientos);

    private sealed record ComponenteVariacion(Guid ProductoId, decimal TeoricoAjustado, decimal Real, decimal? VariacionPct,
        bool FueraDeUmbral);

    private sealed record OrdenVariacion(Guid OrdenId, bool FueraDeUmbral, IReadOnlyList<ComponenteVariacion> Componentes);

    private sealed record ReporteVariacion(IReadOnlyList<OrdenVariacion> Ordenes);

    private sealed record Escenario(ProductoPrueba Producto, ProductoPrueba CompA, ProductoPrueba CompB, RecetaPrueba Receta);

    /// <summary>Producto terminado propio con receta publicada: 50 KG salen de 10 KG de A y 20 KG de B.</summary>
    private async Task<Escenario> EscenarioAsync(HttpClient admin, int? vidaUtil = 3)
    {
        var producto = await admin.CrearProductoAsync(tipo: "Terminado", vidaUtil: vidaUtil);
        var a = await admin.CrearProductoAsync();
        var b = await admin.CrearProductoAsync();
        var receta = await admin.PostOkAsync<RecetaPrueba>("/api/v1/produccion/recetas", new
        {
            productoId = producto.Id, nombre = "Receta de prueba", rendimientoCantidad = 50, mermaEsperadaPct = 0.02,
            componentes = new[] { new { productoId = a.Id, cantidad = 10m }, new { productoId = b.Id, cantidad = 20m } },
        });
        receta = await admin.PostOkAsync<RecetaPrueba>($"/api/v1/produccion/recetas/{receta.Id}/publicar");
        return new Escenario(producto, a, b, receta);
    }

    private static async Task<OrdenPrueba> PlanearAsync(HttpClient cliente, Guid productoId, decimal cantidad, bool liberar = true,
        bool iniciar = true)
    {
        var orden = await cliente.PostOkAsync<OrdenPrueba>("/api/v1/produccion/ordenes", new
        {
            productoId, fechaOperativa = Fecha, turno = "Matutino", cantidadPlaneada = cantidad,
        });
        if (liberar)
        {
            orden = await cliente.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{orden.Id}/liberar");
        }

        if (liberar && iniciar)
        {
            orden = await cliente.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{orden.Id}/iniciar");
        }

        return orden;
    }

    [Fact]
    [Trait("Regla", "RN-PRO-02")]
    public async Task Criterio_Tortilla176Kg_CalculaConsumoTeoricoDe200KgDeMasa()
    {
        var produccion = await api.ClienteAsync("produccion");
        var tortilla = await produccion.ProductoAsync("PT-TORT-KG");
        var masa = await produccion.ProductoAsync("SEM-MASA");

        var orden = await PlanearAsync(produccion, tortilla.Id, 176, iniciar: false);

        orden.Estado.ShouldBe("Liberada");
        orden.Consumos.Single(c => c.ProductoId == masa.Id).CantidadTeoricaBase.ShouldBe(200m);
        orden.Consumos.Single(c => c.Sku == "MP-CONSERV").CantidadTeoricaBase.ShouldBe(0.6m);
        orden.Consumos.Single(c => c.Sku == "EMP-BOLSA-1K").CantidadTeoricaBase.ShouldBe(176m);
    }

    [Fact]
    [Trait("Regla", "RN-PRO-04")]
    [Trait("Regla", "RN-PRO-07")]
    public async Task Criterio_Terminar170KgCon4KgDeMerma_GeneraMovimientosYLoteQuedaEn170()
    {
        var admin = await api.ClienteAsync();
        var produccion = await api.ClienteAsync("produccion");
        var tortilla = await admin.ProductoAsync("PT-TORT-KG");
        var mp = await admin.AlmacenAsync("MP");
        var pt = await admin.AlmacenAsync("PT");
        // [SUP-02] masa y conservador manejan lote.
        await admin.EntradaAsync(mp, (await admin.ProductoAsync("SEM-MASA")).Id, 300, 4, DatosPrueba.Unico("L"), Fecha.AddDays(2));
        await admin.EntradaAsync(mp, (await admin.ProductoAsync("MP-CONSERV")).Id, 5, 50, DatosPrueba.Unico("L"), Fecha.AddDays(90));
        await admin.EntradaAsync(mp, (await admin.ProductoAsync("EMP-BOLSA-1K")).Id, 500, 0.5m);

        var orden = await PlanearAsync(produccion, tortilla.Id, 176);
        var motivoNoProduccion = await admin.MotivoAsync("CADUCADO");
        var invalida = await produccion.PostJsonAsync($"/api/v1/produccion/ordenes/{orden.Id}/mermas",
            new { productoId = tortilla.Id, cantidadBase = 4, motivoMermaId = motivoNoProduccion });
        await invalida.DebeFallarAsync(HttpStatusCode.BadRequest, "PRO-007");

        await produccion.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{orden.Id}/mermas",
            new { productoId = tortilla.Id, cantidadBase = 4, motivoMermaId = await admin.MotivoAsync("TORT_ROTA") });
        var terminada = await produccion.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{orden.Id}/terminar",
            new { cantidadProducida = 170 });

        terminada.Estado.ShouldBe("Terminada");
        terminada.Movimientos.Count(m => m.Tipo == "SalidaConsumo").ShouldBe(3);
        terminada.Movimientos.Single(m => m.Tipo == "EntradaProduccion").Cantidad.ShouldBe(174m);
        terminada.Movimientos.Single(m => m.Tipo == "Merma").Cantidad.ShouldBe(-4m);
        terminada.Movimientos.Single(m => m.Tipo == "EntradaProduccion").AlmacenId.ShouldBe(pt);
        (await admin.ExistenciaAsync(pt, tortilla.Id, terminada.Lote)).ShouldBe(170m);
    }

    [Fact]
    [Trait("Regla", "RN-PRO-01")]
    public async Task Criterio_PublicarV2_DejaV1Obsoleta_YOrdenesLiberadasSiguenConV1()
    {
        var admin = await api.ClienteAsync();
        var escenario = await EscenarioAsync(admin);
        escenario.Receta.Estado.ShouldBe("Vigente");

        var edicion = await admin.PutAsync($"/api/v1/produccion/recetas/{escenario.Receta.Id}", Json(new
        {
            productoId = escenario.Producto.Id, nombre = "x", rendimientoCantidad = 1, mermaEsperadaPct = 0,
            componentes = new[] { new { productoId = escenario.CompA.Id, cantidad = 1 } },
        }));
        await edicion.DebeFallarAsync(HttpStatusCode.Conflict, "PRO-004");

        var liberada = await PlanearAsync(admin, escenario.Producto.Id, 50, iniciar: false);
        var planeada = await PlanearAsync(admin, escenario.Producto.Id, 50, liberar: false);

        var v2 = await admin.PostOkAsync<RecetaPrueba>($"/api/v1/produccion/recetas/{escenario.Receta.Id}/nueva-version");
        v2.Version.ShouldBe(2);
        v2.Estado.ShouldBe("Borrador");
        await admin.PostOkAsync<RecetaPrueba>($"/api/v1/produccion/recetas/{v2.Id}/publicar");

        var v1 = await admin.GetOkAsync<RecetaPrueba>($"/api/v1/produccion/recetas/{escenario.Receta.Id}");
        v1.Estado.ShouldBe("Obsoleta");
        var vigentes = await admin.GetOkAsync<List<RecetaPrueba>>(
            $"/api/v1/produccion/recetas?productoId={escenario.Producto.Id}&estado=Vigente");
        vigentes.Single().Id.ShouldBe(v2.Id);

        (await admin.GetOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{liberada.Id}")).RecetaVersion.ShouldBe(1);
        var liberadaDespues = await admin.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{planeada.Id}/liberar");
        liberadaDespues.RecetaVersion.ShouldBe(2);
    }

    [Fact]
    [Trait("Regla", "RN-PRO-03")]
    [Trait("Regla", "RN-PRO-05")]
    [Trait("Regla", "RN-PRO-10")]
    public async Task Terminar_SinConsumoReal_UsaTeoricoConProducida_CosteaYGeneraLote()
    {
        var admin = await api.ClienteAsync();
        var escenario = await EscenarioAsync(admin, vidaUtil: 3);
        var mp = await admin.AlmacenAsync("MP");
        var pt = await admin.AlmacenAsync("PT");
        await admin.EntradaAsync(mp, escenario.CompA.Id, 100, 5);
        await admin.EntradaAsync(mp, escenario.CompB.Id, 100, 2);

        var orden = await PlanearAsync(admin, escenario.Producto.Id, 100);
        orden.Lote.ShouldBe($"261103-{orden.Folio.Replace("-", string.Empty, StringComparison.Ordinal)}");

        var terminada = await admin.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{orden.Id}/terminar",
            new { cantidadProducida = 80 });

        // Teórico planeado: A 20, B 40; con 80 producidos: A 16, B 32.
        terminada.Consumos.Single(c => c.ProductoId == escenario.CompA.Id).CantidadRealBase.ShouldBe(16m);
        terminada.Consumos.Single(c => c.ProductoId == escenario.CompB.Id).CantidadRealBase.ShouldBe(32m);
        // Costo: 16 × 5 + 32 × 2 = 144; 144 / 80 = 1.8 por KG.
        terminada.CostoTotal.ShouldBe(144m);
        terminada.CostoUnitario.ShouldBe(1.8m);
        terminada.Movimientos.Single(m => m.Tipo == "EntradaProduccion").CostoUnitario.ShouldBe(1.8m);

        var existencias = await admin.GetOkAsync<PaginaPrueba<JsonObject>>(
            $"/api/v1/inventario/existencias?almacenId={pt}&productoId={escenario.Producto.Id}");
        var fila = existencias.Items.Single();
        fila["lote"]!.GetValue<string>().ShouldBe(terminada.Lote);
        fila["cantidad"]!.GetValue<decimal>().ShouldBe(80m);
        fila["fechaCaducidad"]!.GetValue<string>().ShouldBe("2026-11-06");
    }

    [Fact]
    [Trait("Regla", "RN-PRO-06")]
    public async Task Terminar_SinExistenciaDeComponente_DevuelvePRO002_SalvoParametro()
    {
        var admin = await api.ClienteAsync();
        var escenario = await EscenarioAsync(admin);
        var mp = await admin.AlmacenAsync("MP");
        await admin.EntradaAsync(mp, escenario.CompA.Id, 100, 5);

        var orden = await PlanearAsync(admin, escenario.Producto.Id, 50);
        var fallo = await admin.PostJsonAsync($"/api/v1/produccion/ordenes/{orden.Id}/terminar", new { cantidadProducida = 50 });
        var error = await fallo.DebeFallarAsync(HttpStatusCode.Conflict, "PRO-002");
        error["detalle"]!["productoId"]!.GetValue<Guid>().ShouldBe(escenario.CompB.Id);
        (await admin.ExistenciaAsync(mp, escenario.CompA.Id)).ShouldBe(100m);

        await admin.PutOkAsync<JsonObject>("/api/v1/catalogos/parametros/produccion.permitir_consumo_sin_existencia", new { valor = "true" });
        try
        {
            var terminada = await admin.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{orden.Id}/terminar",
                new { cantidadProducida = 50 });
            terminada.Estado.ShouldBe("Terminada");
            (await admin.ExistenciaAsync(mp, escenario.CompB.Id)).ShouldBe(-20m);
        }
        finally
        {
            await admin.PutOkAsync<JsonObject>("/api/v1/catalogos/parametros/produccion.permitir_consumo_sin_existencia", new { valor = "false" });
        }
    }

    [Fact]
    [Trait("Regla", "RN-PRO-08")]
    public async Task ReporteVariacion_MarcaComponentesFueraDeUmbral()
    {
        var admin = await api.ClienteAsync();
        var escenario = await EscenarioAsync(admin);
        var mp = await admin.AlmacenAsync("MP");
        await admin.EntradaAsync(mp, escenario.CompA.Id, 100, 5);
        await admin.EntradaAsync(mp, escenario.CompB.Id, 100, 2);

        var orden = await PlanearAsync(admin, escenario.Producto.Id, 50);
        await admin.PutOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{orden.Id}/consumos", new
        {
            consumos = new[]
            {
                new { productoId = escenario.CompA.Id, cantidadRealBase = 11m }, // +10 %
                new { productoId = escenario.CompB.Id, cantidadRealBase = 20.5m }, // +2.5 %
            },
        });
        await admin.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{orden.Id}/terminar", new { cantidadProducida = 50 });

        var reporte = await admin.GetOkAsync<ReporteVariacion>(
            $"/api/v1/produccion/reportes/variacion?desde=2026-11-01&hasta=2026-11-30&productoId={escenario.Producto.Id}");
        var fila = reporte.Ordenes.Single(o => o.OrdenId == orden.Id);
        fila.FueraDeUmbral.ShouldBeTrue();
        var a = fila.Componentes.Single(c => c.ProductoId == escenario.CompA.Id);
        a.VariacionPct.ShouldBe(0.1m);
        a.FueraDeUmbral.ShouldBeTrue();
        fila.Componentes.Single(c => c.ProductoId == escenario.CompB.Id).FueraDeUmbral.ShouldBeFalse();
    }

    [Fact]
    [Trait("Regla", "RN-PRO-09")]
    public async Task Terminar_ConCantidadCero_SeRechaza()
    {
        var admin = await api.ClienteAsync();
        var escenario = await EscenarioAsync(admin);
        var orden = await PlanearAsync(admin, escenario.Producto.Id, 50);

        var respuesta = await admin.PostJsonAsync($"/api/v1/produccion/ordenes/{orden.Id}/terminar", new { cantidadProducida = 0 });
        await respuesta.DebeFallarAsync(HttpStatusCode.BadRequest, "VAL-001");
    }

    [Fact]
    public async Task Estados_TransicionesInvalidas_DevuelvenPRO001()
    {
        var admin = await api.ClienteAsync();
        var escenario = await EscenarioAsync(admin);
        var planeada = await PlanearAsync(admin, escenario.Producto.Id, 50, liberar: false);

        var iniciar = await admin.PostJsonAsync($"/api/v1/produccion/ordenes/{planeada.Id}/iniciar");
        await iniciar.DebeFallarAsync(HttpStatusCode.Conflict, "PRO-001");

        var cancelada = await admin.PostOkAsync<OrdenPrueba>($"/api/v1/produccion/ordenes/{planeada.Id}/cancelar");
        cancelada.Estado.ShouldBe("Cancelada");
        cancelada.Movimientos.ShouldBeEmpty();
        var liberar = await admin.PostJsonAsync($"/api/v1/produccion/ordenes/{planeada.Id}/liberar");
        await liberar.DebeFallarAsync(HttpStatusCode.Conflict, "PRO-001");
    }

    [Fact]
    public async Task Receta_ConProductoNoProducible_SeRechaza()
    {
        var admin = await api.ClienteAsync();
        var materia = await admin.CrearProductoAsync();
        var componente = await admin.CrearProductoAsync();
        var respuesta = await admin.PostJsonAsync("/api/v1/produccion/recetas", new
        {
            productoId = materia.Id, nombre = "No producible", rendimientoCantidad = 1, mermaEsperadaPct = 0,
            componentes = new[] { new { productoId = componente.Id, cantidad = 1 } },
        });
        await respuesta.DebeFallarAsync(HttpStatusCode.BadRequest, "PRO-005");
    }

    [Fact]
    public async Task Vendedor_NoAccedeAProduccion()
    {
        var vendedor = await api.ClienteAsync("vendedor1");
        (await vendedor.GetAsync("/api/v1/produccion/ordenes")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vendedor.GetAsync("/api/v1/produccion/recetas")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Semilla_LineasYRecetasVigentes()
    {
        var produccion = await api.ClienteAsync("produccion");
        var lineas = await produccion.GetOkAsync<List<JsonObject>>("/api/v1/produccion/lineas");
        lineas.Count.ShouldBeGreaterThanOrEqualTo(5);
        var vigentes = await produccion.GetOkAsync<List<RecetaPrueba>>("/api/v1/produccion/recetas?estado=Vigente");
        vigentes.Count(r => r.Nombre.EndsWith(" v1", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(4);
    }

    private static StringContent Json(object cuerpo) =>
        new(System.Text.Json.JsonSerializer.Serialize(cuerpo, ExtensionesHttp.Json), System.Text.Encoding.UTF8, "application/json");
}
