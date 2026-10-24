using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Logistica.Publico;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Api.IntegrationTests.Logistica;

/// <summary>Producto a cobro, cuenta del chofer y corte semanal (T-8.11).</summary>
public sealed class CobranzaChoferesTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record IdRespuesta(Guid Id);

    private sealed record RutaPrueba(Guid Id, string Codigo, Guid AlmacenId, Guid? ClienteGenericoId);

    private sealed record EnvioPrueba(Guid Id, Guid? LiquidacionId);

    private sealed record PartidaPrueba(Guid ProductoId, string Lote, decimal DevueltoClientesNoVendible, decimal SobranteEsperado,
        bool AplicaACobro, decimal CantidadACobroSugerida, decimal? CantidadACobro, decimal? PrecioACobro);

    private sealed record CargoPrueba(Guid Id, string Folio, Guid ChoferId, string Tipo, Guid? ProductoId, decimal? CantidadBase,
        decimal? PrecioUnitario, decimal Importe, DateOnly FechaOperativa, string Estado, Guid? CorteId, string? Notas);

    private sealed record LiquidacionPrueba(Guid Id, string Estado, Guid? ChoferId, decimal EfectivoEsperado, bool AplicaProductoACobro,
        List<PartidaPrueba> Partidas, List<CargoPrueba> Cargos);

    private sealed record CorteResumen(Guid ChoferId, Guid? CorteId, string? Estado, decimal TotalCargos);

    private sealed record CorteDetalle(Guid Id, string Folio, Guid ChoferId, DateOnly Desde, DateOnly Hasta, string Estado, decimal TotalCargos,
        List<CargoPrueba> Cargos);

    // ───────────── Ayudas ─────────────

    private Task<DateOnly> HoyAsync() => api.ConScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IFechaOperativa>().Hoy()));

    private static async Task<RutaPrueba> RutaAsync(HttpClient admin, string canal = "DET") =>
        await admin.PostOkAsync<RutaPrueba>("/api/v1/logistica/rutas", new
        {
            codigo = "K" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), nombre = "Ruta cobranza", canalId = await admin.CanalAsync(canal),
            vendedorId = await admin.CrearChoferAsync(), activo = true,
        });

    /// <summary>Lote con fecha <c>AAMMDD</c> de hace <paramref name="dias"/> días.</summary>
    private async Task<string> LoteAsync(int dias) =>
        (await HoyAsync()).AddDays(-dias).ToString("yyMMdd", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<(ProductoPrueba Tortilla, Guid LiquidacionId)> CargarAsync(HttpClient admin, RutaPrueba ruta, string lote, decimal cantidad)
    {
        var tortilla = await admin.ProductoAsync("PT-TORT-KG");
        await admin.EntradaAsync(await admin.AlmacenAsync("PT"), tortilla.Id, cantidad, 9.5m, lote, (await HoyAsync()).AddDays(2));
        var envio = await admin.PostOkAsync<IdRespuesta>("/api/v1/logistica/envios", new
        {
            rutaId = ruta.Id, tipo = "CargaInicial", partidas = new[] { new { productoId = tortilla.Id, cantidad, lote } },
        });
        var despachado = await admin.PostOkAsync<EnvioPrueba>($"/api/v1/logistica/envios/{envio.Id}/despachar");
        return (tortilla, despachado.LiquidacionId!.Value);
    }

    private static Task<IdRespuesta> DevolverNoVendibleAsync(HttpClient admin, RutaPrueba ruta, Guid productoId, string lote, decimal cantidad) =>
        admin.PostOkAsync<IdRespuesta>("/api/v1/logistica/retornos", new
        {
            tipo = "DevolucionCliente", rutaId = ruta.Id, clienteId = ruta.ClienteGenericoId,
            partidas = new[] { new { productoId, cantidad, lote, condicion = "NoVendible" } },
        });

    private Task VenderAsync(RutaPrueba ruta, Guid productoId, decimal cantidad) =>
        api.ConScopeAsync(async sp =>
        {
            var kg = await (await api.ClienteAsync()).UnidadAsync("KG");
            var resultado = await sp.GetRequiredService<IVentasService>().RegistrarVentaAsync(new RegistrarVentaComando(OrigenVenta.App,
                ruta.ClienteGenericoId!.Value, ruta.AlmacenId, Guid.Empty, sp.GetRequiredService<IFechaOperativa>().Hoy(), CondicionPago.Contado,
                FormaPago.Efectivo, [new PartidaVentaComando(productoId, kg, cantidad)], RutaId: ruta.Id, OpId: Id.Nuevo()), CancellationToken.None);
            resultado.EsExito.ShouldBeTrue(resultado.Error?.Mensaje);
            return true;
        });

    private static string Url(Guid id, string accion = "") => $"/api/v1/logistica/liquidaciones/{id}{accion}";

    private static Task<CargoPrueba> CargoAsync(HttpClient admin, Guid choferId, decimal importe, DateOnly fecha, string notas = "Ajuste") =>
        admin.PostOkAsync<CargoPrueba>("/api/v1/logistica/cargos-chofer", new { tipo = "Otro", importe, choferId, fechaOperativa = fecha, notas });

    // ───────────── Pruebas ─────────────

    [Fact]
    [Trait("Regla", "RN-LOG-24")]
    [Trait("Regla", "RN-LOG-25")]
    [Trait("Regla", "RN-LOG-26")]
    public async Task ProductoACobro_SeSugierePorAntiguedad_YAlConfirmarSeCreanLosCargos_ConFaltantes()
    {
        var admin = await api.ClienteAsync();
        var ruta = await RutaAsync(admin);
        var viejo = await LoteAsync(13);
        var nuevo = await LoteAsync(1);
        var (tortilla, id) = await CargarAsync(admin, ruta, nuevo, 10);
        await VenderAsync(ruta, tortilla.Id, 1);
        await DevolverNoVendibleAsync(admin, ruta, tortilla.Id, viejo, 3);
        await DevolverNoVendibleAsync(admin, ruta, tortilla.Id, nuevo, 2);

        var liquidacion = await admin.GetOkAsync<LiquidacionPrueba>(Url(id));
        liquidacion.AplicaProductoACobro.ShouldBeTrue();
        var partidaVieja = liquidacion.Partidas.Single(p => p.Lote == viejo);
        (partidaVieja.AplicaACobro, partidaVieja.CantidadACobroSugerida).ShouldBe((true, 3m));
        liquidacion.Partidas.Single(p => p.Lote == nuevo).CantidadACobroSugerida.ShouldBe(0m);

        // LOG-022: más de lo devuelto no vendible.
        await (await admin.PutJsonAsync(Url(id, "/conteo"), new
            {
                partidas = new[] { new { productoId = tortilla.Id, lote = nuevo, sobranteContado = 8, destino = "Reintegrar", cantidadACobro = 5 } },
            }))
            .DebeFallarAsync(HttpStatusCode.BadRequest, ErroresLogistica.ACobroInvalido);

        // Esperado 9 en el lote nuevo; cuenta 8 (faltante 1) y almacén cobra 1 de lo devuelto.
        await admin.PutOkAsync<LiquidacionPrueba>(Url(id, "/conteo"), new
        {
            partidas = new[] { new { productoId = tortilla.Id, lote = nuevo, sobranteContado = 8, destino = "Reintegrar", cantidadACobro = 1 } },
        });
        var confirmada = await admin.PostOkAsync<LiquidacionPrueba>(Url(id, "/confirmar-producto"), new { forzar = false });

        var aCobro = confirmada.Cargos.Where(c => c.Tipo == "ProductoACobro").OrderByDescending(c => c.CantidadBase).ToList();
        aCobro.Select(c => c.CantidadBase).ShouldBe([3m, 1m]);
        aCobro.ShouldAllBe(c => c.ChoferId == confirmada.ChoferId && c.Estado == "Pendiente" && c.PrecioUnitario > 0);
        aCobro.ShouldAllBe(c => c.Importe == Math.Round(c.CantidadBase!.Value * c.PrecioUnitario!.Value, 2));
        var faltante = confirmada.Cargos.Single(c => c.Tipo == "FaltanteProducto");
        faltante.CantidadBase.ShouldBe(1m);
        confirmada.Partidas.Single(p => p.Lote == viejo).CantidadACobro.ShouldBe(3m);

        // RN-LOG-26: el faltante de efectivo se carga al cerrar.
        await admin.PostOkAsync<LiquidacionPrueba>(Url(id, "/revisar"));
        var cerrada = await admin.PostOkAsync<LiquidacionPrueba>(Url(id, "/cerrar"), new { efectivoEntregado = confirmada.EfectivoEsperado - 5m });
        cerrada.Cargos.Single(c => c.Tipo == "FaltanteEfectivo").Importe.ShouldBe(5m);
    }

    [Fact]
    [Trait("Regla", "RN-LOG-19")]
    [Trait("Regla", "RN-LOG-24")]
    public async Task Cedis_Simplificado_NoPermiteProductoACobro()
    {
        var admin = await api.ClienteAsync();
        var ruta = await RutaAsync(admin, "CEDIS");
        var lote = await LoteAsync(20);
        var (tortilla, id) = await CargarAsync(admin, ruta, lote, 5);
        await DevolverNoVendibleAsync(admin, ruta, tortilla.Id, lote, 2);

        var liquidacion = await admin.GetOkAsync<LiquidacionPrueba>(Url(id));
        liquidacion.AplicaProductoACobro.ShouldBeFalse();
        liquidacion.Partidas.ShouldAllBe(p => !p.AplicaACobro && p.CantidadACobroSugerida == 0);
        await (await admin.PutJsonAsync(Url(id, "/conteo"), new
            {
                partidas = new[] { new { productoId = tortilla.Id, lote, sobranteContado = 5, destino = "Reintegrar", cantidadACobro = 1 } },
            }))
            .DebeFallarAsync(HttpStatusCode.BadRequest, ErroresLogistica.ACobroInvalido);

        await admin.PutOkAsync<LiquidacionPrueba>(Url(id, "/conteo"), new
        {
            partidas = new[] { new { productoId = tortilla.Id, lote, sobranteContado = 5, destino = "Reintegrar" } },
        });
        (await admin.PostOkAsync<LiquidacionPrueba>(Url(id, "/confirmar-producto"), new { forzar = false })).Cargos.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Regla", "RN-LOG-27")]
    [Trait("Regla", "RN-LOG-28")]
    [Trait("Regla", "RN-LOG-29")]
    [Trait("Regla", "RN-LOG-30")]
    public async Task CorteSemanal_AplicaLosPendientes_CancelacionYCargoAtrasadoAlSiguienteCorte()
    {
        var admin = await api.ClienteAsync();
        var chofer = await admin.CrearChoferAsync();
        var hoy = await HoyAsync();
        await CargoAsync(admin, chofer, 30m, hoy);
        await CargoAsync(admin, chofer, 12.5m, hoy);
        var cancelable = await CargoAsync(admin, chofer, 99m, hoy);

        await (await admin.PostJsonAsync("/api/v1/logistica/cargos-chofer", new { tipo = "Otro", importe = 1m, choferId = chofer }))
            .DebeFallarAsync(HttpStatusCode.BadRequest, "VAL-001");
        await (await admin.PostJsonAsync("/api/v1/logistica/cargos-chofer", new { tipo = "FaltanteEfectivo", importe = 1m, choferId = chofer, notas = "x" }))
            .DebeFallarAsync(HttpStatusCode.BadRequest, "VAL-001");

        (await admin.PostOkAsync<CargoPrueba>($"/api/v1/logistica/cargos-chofer/{cancelable.Id}/cancelar", new { motivo = "Duplicado" }))
            .Estado.ShouldBe("Cancelado");
        await (await admin.PostJsonAsync($"/api/v1/logistica/cargos-chofer/{cancelable.Id}/cancelar", new { motivo = "Otra vez" }))
            .DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.CargoAplicado);

        var resumen = await admin.GetOkAsync<List<CorteResumen>>($"/api/v1/logistica/cortes-chofer?fecha={hoy:yyyy-MM-dd}&choferId={chofer}");
        resumen.ShouldHaveSingleItem().TotalCargos.ShouldBe(42.5m);

        var corte = await admin.PostOkAsync<CorteDetalle>("/api/v1/logistica/cortes-chofer", new { choferId = chofer, fecha = hoy });
        (corte.Estado, corte.Cargos.Count, corte.TotalCargos).ShouldBe(("Abierto", 2, 42.5m));
        (await admin.PostOkAsync<CorteDetalle>("/api/v1/logistica/cortes-chofer", new { choferId = chofer, fecha = hoy })).Id.ShouldBe(corte.Id);

        var cerrado = await admin.PostOkAsync<CorteDetalle>($"/api/v1/logistica/cortes-chofer/{corte.Id}/cerrar");
        cerrado.Estado.ShouldBe("Cerrado");
        cerrado.Cargos.ShouldAllBe(c => c.Estado == "Aplicado" && c.CorteId == corte.Id);
        await (await admin.PostJsonAsync($"/api/v1/logistica/cargos-chofer/{cerrado.Cargos[0].Id}/cancelar", new { motivo = "Tarde" }))
            .DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.CargoAplicado);

        // RN-LOG-29: un cargo nuevo con fecha de la semana cerrada entra al corte siguiente.
        var atrasado = await CargoAsync(admin, chofer, 7m, corte.Desde);
        var siguiente = await admin.PostOkAsync<CorteDetalle>("/api/v1/logistica/cortes-chofer", new { choferId = chofer, fecha = corte.Hasta.AddDays(1) });
        siguiente.Id.ShouldNotBe(corte.Id);
        siguiente.Cargos.ShouldHaveSingleItem().Id.ShouldBe(atrasado.Id);
        (await admin.GetOkAsync<CorteDetalle>($"/api/v1/logistica/cortes-chofer/{corte.Id}")).Cargos.Count.ShouldBe(2);

        var csv = await admin.GetAsync($"/api/v1/logistica/cortes-chofer/{corte.Id}/exportar");
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var texto = await csv.Content.ReadAsStringAsync();
        texto.ShouldContain(corte.Folio);
        texto.ShouldContain("Total a descontar,,,,,,,42.5");
    }

    [Fact]
    [Trait("Regla", "RN-LOG-28")]
    public async Task Corte_ConLiquidacionesSinCerrar_EsLog024()
    {
        var admin = await api.ClienteAsync();
        var ruta = await RutaAsync(admin);
        var (_, id) = await CargarAsync(admin, ruta, await LoteAsync(0), 2);
        var liquidacion = await admin.GetOkAsync<LiquidacionPrueba>(Url(id));

        var corte = await admin.PostOkAsync<CorteDetalle>("/api/v1/logistica/cortes-chofer",
            new { choferId = liquidacion.ChoferId, fecha = await HoyAsync() });
        await (await admin.PostJsonAsync($"/api/v1/logistica/cortes-chofer/{corte.Id}/cerrar"))
            .DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.CorteConLiquidacionesAbiertas);
    }

    [Fact]
    public async Task CuentaDeChoferes_SinPermiso_Recibe403()
    {
        var almacen = await api.ClienteAsync("almacen");
        (await almacen.GetAsync("/api/v1/logistica/cargos-chofer")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var caja = await api.ClienteAsync("liquidaciones");
        (await caja.GetAsync("/api/v1/logistica/cargos-chofer")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
