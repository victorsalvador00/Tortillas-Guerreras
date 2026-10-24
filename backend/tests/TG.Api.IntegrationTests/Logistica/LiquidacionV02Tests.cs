using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Logistica.Publico;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Api.IntegrationTests.Logistica;

/// <summary>Liquidación v0.2 (T-8.10): etapas, formato por canal, gastos, dinero de solo lectura y hoja.</summary>
public sealed class LiquidacionV02Tests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record IdRespuesta(Guid Id);

    private sealed record RutaPrueba(Guid Id, string Codigo, Guid AlmacenId, Guid? ClienteGenericoId);

    private sealed record EnvioPrueba(Guid Id, Guid? LiquidacionId);

    private sealed record MovimientoPrueba(string Tipo, decimal Cantidad);

    private sealed record PaginaPrueba<T>(List<T> Items, int Total);

    private sealed record PartidaPrueba(Guid ProductoId, string Lote, decimal Cargado, decimal CargadoInicial, decimal Recargado,
        decimal Vendido, decimal ImporteVenta, decimal CambiosEntregados, decimal DevueltoClientesNoVendible, decimal SobranteEsperado,
        decimal? Diferencia);

    private sealed record GastoPrueba(Guid Id, string Concepto, decimal Importe, bool Autorizado);

    private sealed record LiquidacionPrueba(Guid Id, string Estado, string Formato, decimal TotalVendido, decimal VentasContadoEfectivo,
        decimal TotalGastos, decimal EfectivoEsperado, decimal? DiferenciaEfectivo, decimal TotalTransferencias, decimal TotalCheques,
        decimal DiferenciaPrecios, string? ProductoConfirmadoPor, string? RevisadaPor, string? CerradaPor, string? Notas,
        List<PartidaPrueba> Partidas, List<GastoPrueba> Gastos);

    private sealed record HojaPrueba(LiquidacionPrueba Liquidacion, string? FirmaAlmacen, string? FirmaVendedor, string? FirmaSupervisor,
        string? FirmaLiquidaciones);

    // ───────────── Ayudas ─────────────

    private Task<DateOnly> HoyAsync() => api.ConScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IFechaOperativa>().Hoy()));

    private static async Task<RutaPrueba> RutaAsync(HttpClient admin, string canal = "DET") =>
        await admin.PostOkAsync<RutaPrueba>("/api/v1/logistica/rutas", new
        {
            codigo = "Q" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), nombre = "Ruta liquidación", canalId = await admin.CanalAsync(canal),
            vendedorId = await admin.CrearChoferAsync(), activo = true,
        });

    /// <summary>Entrada a PT de tortilla con lote propio; despacha una carga inicial y, opcional, una recarga.</summary>
    private async Task<(ProductoPrueba Tortilla, string Lote, Guid LiquidacionId)> CargarAsync(HttpClient admin, RutaPrueba ruta, decimal inicial,
        decimal recarga = 0)
    {
        var tortilla = await admin.ProductoAsync("PT-TORT-KG");
        var lote = "L" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await admin.EntradaAsync(await admin.AlmacenAsync("PT"), tortilla.Id, inicial + recarga, 9.5m, lote, (await HoyAsync()).AddDays(2));
        EnvioPrueba? despachado = null;
        foreach (var (tipo, cantidad) in new[] { ("CargaInicial", inicial), ("Recarga", recarga) }.Where(x => x.Item2 > 0))
        {
            var envio = await admin.PostOkAsync<IdRespuesta>("/api/v1/logistica/envios", new
            {
                rutaId = ruta.Id, tipo, partidas = new[] { new { productoId = tortilla.Id, cantidad, lote } },
            });
            despachado = await admin.PostOkAsync<EnvioPrueba>($"/api/v1/logistica/envios/{envio.Id}/despachar");
        }

        return (tortilla, lote, despachado!.LiquidacionId!.Value);
    }

    private Task<VentaRegistrada> VenderAsync(RutaPrueba ruta, Guid productoId, decimal cantidad, FormaPago forma = FormaPago.Efectivo,
        decimal? precio = null) =>
        api.ConScopeAsync(async sp =>
        {
            var ventas = sp.GetRequiredService<IVentasService>();
            var kg = await (await api.ClienteAsync()).UnidadAsync("KG");
            var hoy = sp.GetRequiredService<IFechaOperativa>().Hoy();
            var resultado = await ventas.RegistrarVentaAsync(new RegistrarVentaComando(OrigenVenta.App, ruta.ClienteGenericoId!.Value,
                ruta.AlmacenId, Guid.Empty, hoy, CondicionPago.Contado, forma,
                [new PartidaVentaComando(productoId, kg, cantidad, precio)], RutaId: ruta.Id, OpId: Id.Nuevo()), CancellationToken.None);
            resultado.EsExito.ShouldBeTrue(resultado.Error?.Mensaje);
            return resultado.Valor;
        });

    private static string Url(Guid id, string accion = "") => $"/api/v1/logistica/liquidaciones/{id}{accion}";

    private static Task<LiquidacionPrueba> ContarAsync(HttpClient cliente, Guid id, Guid productoId, string lote, decimal sobrante) =>
        cliente.PutOkAsync<LiquidacionPrueba>(Url(id, "/conteo"), new
        {
            partidas = new[] { new { productoId, lote, sobranteContado = sobrante, destino = "Reintegrar" } },
        });

    // ───────────── Pruebas ─────────────

    [Fact]
    [Trait("Regla", "RN-LOG-06")]
    [Trait("Regla", "RN-LOG-07")]
    public async Task CriterioV02_CargaYRecarga_MenosVentaYCambios_DiferenciaSeAjustaAlConfirmarProducto()
    {
        var admin = await api.ClienteAsync();
        var ruta = await RutaAsync(admin);
        var (tortilla, lote, liquidacionId) = await CargarAsync(admin, ruta, 96, 24);
        var venta = await VenderAsync(ruta, tortilla.Id, 99);

        // Cambio físico con un cliente de la ruta: entrega 10 y retira 6 no vendibles.
        var det = await admin.CanalAsync("DET");
        var lista = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/listas-precio",
            new { nombre = DatosPrueba.Unico("LISTA"), canalId = det, vigenteDesde = "2026-01-01", activo = true });
        await admin.PutOkAsync<IdRespuesta>($"/api/v1/ventas/listas-precio/{lista.Id}/partidas",
            new { partidas = new[] { new { productoId = tortilla.Id, unidadId = tortilla.UnidadBaseId, precioUnitario = 10m } } });
        var cliente = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/clientes", new
        {
            nombre = DatosPrueba.Unico("Tienda"), canalId = det, listaPrecioId = lista.Id, condicionPago = "Contado", diasCredito = 0,
            requiereFactura = false, activo = true,
        });
        await admin.PutOkAsync<List<object>>($"/api/v1/logistica/rutas/{ruta.Id}/clientes",
            new { clientes = new[] { new { clienteId = cliente.Id, ordenVisita = 1, diasVisita = new[] { 1, 2, 3, 4, 5, 6, 7 } } } });
        await admin.PostOkAsync<IdRespuesta>("/api/v1/logistica/retornos", new
        {
            tipo = "CambioFisico", rutaId = ruta.Id, clienteId = cliente.Id,
            partidas = new object[]
            {
                new { productoId = tortilla.Id, cantidad = 6, lote },
                new { productoId = tortilla.Id, cantidad = 10, sentido = "Entregado", lote },
            },
        });

        var conteo = await ContarAsync(admin, liquidacionId, tortilla.Id, lote, 13);
        var partida = conteo.Partidas.Single(p => p.Lote == lote);
        (partida.CargadoInicial, partida.Recargado, partida.Cargado).ShouldBe((96m, 24m, 120m));
        (partida.Vendido, partida.CambiosEntregados, partida.DevueltoClientesNoVendible).ShouldBe((99m, 10m, 6m));
        partida.SobranteEsperado.ShouldBe(11m);
        partida.Diferencia.ShouldBe(2m);
        partida.ImporteVenta.ShouldBe(venta.Total);

        var confirmada = await admin.PostOkAsync<LiquidacionPrueba>(Url(liquidacionId, "/confirmar-producto"), new { forzar = false });
        confirmada.Estado.ShouldBe("ProductoConfirmado");
        (await admin.ExistenciaAsync(ruta.AlmacenId, tortilla.Id)).ShouldBe(0m);
        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>(
            $"/api/v1/inventario/kardex?documentoTipo=Liquidacion&documentoId={liquidacionId}");
        kardex.Items.Single(m => m.Tipo == "AjusteEntrada").Cantidad.ShouldBe(2m);
        var guardada = confirmada.Partidas.Single(p => p.Lote == lote);
        (guardada.CargadoInicial, guardada.Recargado, guardada.ImporteVenta).ShouldBe((96m, 24m, venta.Total));
    }

    [Fact]
    [Trait("Regla", "RN-LOG-20")]
    [Trait("Regla", "RN-LOG-21")]
    [Trait("Regla", "RN-LOG-22")]
    public async Task Etapas_AlmacenSupervisorLiquidaciones_TransicionFueraDeEtapaEsLog021()
    {
        var admin = await api.ClienteAsync();
        var almacen = await api.ClienteAsync("almacen");
        var supervisor = await api.ClienteAsync("supervisor");
        var caja = await api.ClienteAsync("liquidaciones");
        var ruta = await RutaAsync(admin);
        var (tortilla, lote, id) = await CargarAsync(admin, ruta, 5);

        await (await supervisor.PostJsonAsync(Url(id, "/revisar"))).DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.TransicionInvalida);
        await (await caja.PostJsonAsync(Url(id, "/cerrar"), new { efectivoEntregado = 0 }))
            .DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.TransicionInvalida);
        (await caja.PutAsync(Url(id, "/conteo"), Contenido(new { partidas = Array.Empty<object>() }))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await ContarAsync(almacen, id, tortilla.Id, lote, 5);
        var confirmada = await almacen.PostOkAsync<LiquidacionPrueba>(Url(id, "/confirmar-producto"), new { forzar = false });
        confirmada.ProductoConfirmadoPor.ShouldNotBeNull();
        (await almacen.PostJsonAsync(Url(id, "/revisar"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await (await almacen.PutAsync(Url(id, "/conteo"), Contenido(new { partidas = Array.Empty<object>() })))
            .DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.TransicionInvalida);
        await (await supervisor.PostJsonAsync(Url(id, "/regresar"), new { nota = "x" }))
            .DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.TransicionInvalida);
        // Con revisión del supervisor (predeterminado) caja no cierra desde ProductoConfirmado.
        await (await caja.PostJsonAsync(Url(id, "/cerrar"), new { efectivoEntregado = 0 }))
            .DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.TransicionInvalida);

        var revisada = await supervisor.PostOkAsync<LiquidacionPrueba>(Url(id, "/revisar"));
        (revisada.Estado, revisada.RevisadaPor).ShouldBe(("Revisada", "Supervisor de Rutas"));
        var regresada = await supervisor.PostOkAsync<LiquidacionPrueba>(Url(id, "/regresar"), new { nota = "Corregir gastos" });
        regresada.Estado.ShouldBe("ProductoConfirmado");
        regresada.Notas.ShouldNotBeNull().ShouldContain("Corregir gastos");
        await supervisor.PostOkAsync<LiquidacionPrueba>(Url(id, "/revisar"));

        var cerrada = await caja.PostOkAsync<LiquidacionPrueba>(Url(id, "/cerrar"), new { efectivoEntregado = 0 });
        cerrada.Estado.ShouldBe("Cerrada");
        await (await supervisor.PostJsonAsync(Url(id, "/regresar"), new { nota = "tarde" }))
            .DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.LiquidacionCerrada);

        var hoja = await admin.GetOkAsync<HojaPrueba>(Url(id, "/hoja"));
        hoja.Liquidacion.Formato.ShouldBe("Completo");
        new[] { hoja.FirmaAlmacen, hoja.FirmaVendedor, hoja.FirmaSupervisor, hoja.FirmaLiquidaciones }.ShouldAllBe(f => f != null);
    }

    [Fact]
    [Trait("Regla", "RN-LOG-20")]
    public async Task SinRevisionDelSupervisor_SeCierraDesdeProductoConfirmado()
    {
        var admin = await api.ClienteAsync();
        var ruta = await RutaAsync(admin);
        var (tortilla, lote, id) = await CargarAsync(admin, ruta, 2);
        await ContarAsync(admin, id, tortilla.Id, lote, 2);
        await admin.PostOkAsync<LiquidacionPrueba>(Url(id, "/confirmar-producto"), new { forzar = false });

        await admin.PutOkAsync<JsonObject>("/api/v1/catalogos/parametros/logistica.requiere_revision_supervisor", new { valor = "false" });
        try
        {
            (await admin.PostOkAsync<LiquidacionPrueba>(Url(id, "/cerrar"), new { efectivoEntregado = 0 })).Estado.ShouldBe("Cerrada");
        }
        finally
        {
            await admin.PutOkAsync<JsonObject>("/api/v1/catalogos/parametros/logistica.requiere_revision_supervisor", new { valor = "true" });
        }
    }

    [Fact]
    [Trait("Regla", "RN-LOG-08")]
    public async Task EfectivoEsperado_RestaGastosAutorizados_YTransferenciaOChequeNoSuman()
    {
        var admin = await api.ClienteAsync();
        var ruta = await RutaAsync(admin);
        var (tortilla, lote, id) = await CargarAsync(admin, ruta, 10);
        var efectivo = await VenderAsync(ruta, tortilla.Id, 4);
        var transferencia = await VenderAsync(ruta, tortilla.Id, 3, FormaPago.Transferencia);
        var cheque = await VenderAsync(ruta, tortilla.Id, 2, FormaPago.Cheque);

        await admin.PostOkAsync<LiquidacionPrueba>(Url(id, "/gastos"), new { concepto = "Diésel", importe = 5m, autorizado = true });
        var conGastos = await admin.PostOkAsync<LiquidacionPrueba>(Url(id, "/gastos"),
            new { concepto = "Comida", importe = 7m, comprobante = "Ticket 12", autorizado = false });
        conGastos.Gastos.Count.ShouldBe(2);
        conGastos.TotalGastos.ShouldBe(5m);
        conGastos.EfectivoEsperado.ShouldBe(efectivo.Total - 5m);
        conGastos.TotalTransferencias.ShouldBe(transferencia.Total);
        conGastos.TotalCheques.ShouldBe(cheque.Total);

        // Autorizar el segundo gasto también lo resta.
        var comida = conGastos.Gastos.Single(g => g.Concepto == "Comida");
        var autorizado = await admin.PutOkAsync<LiquidacionPrueba>(Url(id, $"/gastos/{comida.Id}"),
            new { concepto = "Comida", importe = 7m, autorizado = true });
        autorizado.EfectivoEsperado.ShouldBe(efectivo.Total - 12m);

        await ContarAsync(admin, id, tortilla.Id, lote, 1);
        await admin.PostOkAsync<LiquidacionPrueba>(Url(id, "/confirmar-producto"), new { forzar = false });
        await admin.PostOkAsync<LiquidacionPrueba>(Url(id, "/revisar"));
        var cerrada = await admin.PostOkAsync<LiquidacionPrueba>(Url(id, "/cerrar"), new { efectivoEntregado = efectivo.Total - 12m });
        (cerrada.DiferenciaEfectivo, cerrada.TotalCheques).ShouldBe((0m, cheque.Total));
        cerrada.TotalVendido.ShouldBe(efectivo.Total + transferencia.Total + cheque.Total);

        await (await admin.PostJsonAsync(Url(id, "/gastos"), new { concepto = "Caseta", importe = 5m, autorizado = true }))
            .DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.LiquidacionCerrada);
    }

    [Fact]
    [Trait("Regla", "RN-LOG-19")]
    public async Task Formato_SeCopiaDelCanalDeLaRuta_CedisEsSimplificado()
    {
        var admin = await api.ClienteAsync();
        var cedis = await RutaAsync(admin, "CEDIS");
        var (_, _, id) = await CargarAsync(admin, cedis, 1);
        (await admin.GetOkAsync<LiquidacionPrueba>(Url(id))).Formato.ShouldBe("Simplificado");

        var det = await RutaAsync(admin);
        var (_, _, idDet) = await CargarAsync(admin, det, 1);
        (await admin.GetOkAsync<LiquidacionPrueba>(Url(idDet))).Formato.ShouldBe("Completo");
    }

    [Fact]
    [Trait("Regla", "RN-LOG-23")]
    public async Task DiferenciaDePrecios_ReferenciaDelClienteMenosPrecioCobrado()
    {
        var admin = await api.ClienteAsync();
        var ruta = await RutaAsync(admin);
        var (tortilla, _, id) = await CargarAsync(admin, ruta, 10);
        // Tortilla sin impuestos: el total de 1 kg al precio vigente es el precio de referencia.
        var referencia = (await VenderAsync(ruta, tortilla.Id, 1)).Total;
        await VenderAsync(ruta, tortilla.Id, 5, precio: referencia - 2m);
        await VenderAsync(ruta, tortilla.Id, 1, precio: referencia + 1m);

        (await admin.GetOkAsync<LiquidacionPrueba>(Url(id))).DiferenciaPrecios.ShouldBe(9m);
    }

    private static StringContent Contenido(object cuerpo) =>
        new(System.Text.Json.JsonSerializer.Serialize(cuerpo, ExtensionesHttp.Json), System.Text.Encoding.UTF8, "application/json");
}
