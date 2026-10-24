using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Logistica.Publico;
using TG.Seguridad.Publico;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Api.IntegrationTests.Logistica;

public sealed class LogisticaTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record RutaPrueba(Guid Id, string Codigo, Guid AlmacenId, string AlmacenCodigo, Guid CanalId, string CanalNombre,
        Guid? ClienteGenericoId, int Clientes);

    private sealed record ClienteRutaPrueba(Guid ClienteId, string Codigo, short OrdenVisita, bool EsGenerico);

    private sealed record EnvioPartidaPrueba(Guid ProductoId, string Lote, decimal CantidadBase);

    private sealed record EnvioPrueba(Guid Id, string Folio, string Estado, Guid? LiquidacionId, IReadOnlyList<EnvioPartidaPrueba> Partidas);

    private sealed record LiquidacionPartidaPrueba(Guid ProductoId, string Lote, decimal Cargado, decimal Vendido,
        decimal DevueltoClientesVendible, decimal DevueltoClientesNoVendible, decimal SobranteEsperado, decimal? SobranteContado,
        decimal? Diferencia);

    private sealed record LiquidacionPrueba(Guid Id, string Folio, string Estado, decimal EfectivoEsperado, decimal? EfectivoEntregado,
        decimal? DiferenciaEfectivo, int OperacionesPendientes, IReadOnlyList<LiquidacionPartidaPrueba> Partidas);

    private sealed record RetornoPrueba(Guid Id, string Folio, string Tipo);

    private sealed record UsuarioPrueba(Guid Id, string Usuario);

    // ───────────── Ayudas ─────────────

    /// <summary>Ruta de Detalle; sin vendedor indicado, con un chofer nuevo (RN-LOG-13: uno por ruta y día).</summary>
    private async Task<RutaPrueba> CrearRutaAsync(Guid? vendedorId = null)
    {
        var admin = await api.ClienteAsync();
        var codigo = "T" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return await admin.PostOkAsync<RutaPrueba>("/api/v1/logistica/rutas", new
        {
            codigo, nombre = $"Ruta de prueba {codigo}", canalId = await admin.CanalAsync("DET"),
            vendedorId = vendedorId ?? await admin.CrearChoferAsync(), activo = true,
        });
    }

    private async Task<Guid> UsuarioAsync(string usuario)
    {
        var admin = await api.ClienteAsync();
        var pagina = await admin.GetOkAsync<PaginaPrueba<UsuarioPrueba>>($"/api/v1/seguridad/usuarios?buscar={usuario}");
        return pagina.Items.Single(u => u.Usuario == usuario).Id;
    }

    private Task<DateOnly> HoyAsync() => api.ConScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IFechaOperativa>().Hoy()));

    /// <summary>Entrada a PT de tortilla con un lote propio de la prueba y carga + despacho a la ruta.</summary>
    private async Task<(ProductoPrueba Tortilla, string Lote, EnvioPrueba Envio)> CargarAsync(RutaPrueba ruta, decimal cantidad)
    {
        var admin = await api.ClienteAsync();
        var tortilla = await admin.ProductoAsync("PT-TORT-KG");
        var lote = "L" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await admin.EntradaAsync(await admin.AlmacenAsync("PT"), tortilla.Id, cantidad, 9.5m, lote, (await HoyAsync()).AddDays(2));
        var envio = await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", new
        {
            rutaId = ruta.Id, tipo = "CargaInicial", partidas = new[] { new { productoId = tortilla.Id, cantidad, lote } },
        });
        var despachado = await admin.PostOkAsync<EnvioPrueba>($"/api/v1/logistica/envios/{envio.Id}/despachar");
        return (tortilla, lote, despachado);
    }

    private Task<VentaRegistrada> VenderAsync(RutaPrueba ruta, Guid productoId, decimal cantidad) =>
        api.ConScopeAsync(async sp =>
        {
            var ventas = sp.GetRequiredService<IVentasService>();
            var kg = await (await api.ClienteAsync()).UnidadAsync("KG");
            var hoy = sp.GetRequiredService<IFechaOperativa>().Hoy();
            var resultado = await ventas.RegistrarVentaAsync(new RegistrarVentaComando(OrigenVenta.App, ruta.ClienteGenericoId!.Value,
                ruta.AlmacenId, Guid.Empty, hoy, CondicionPago.Contado, FormaPago.Efectivo,
                [new PartidaVentaComando(productoId, kg, cantidad, 20m)], RutaId: ruta.Id, OpId: Id.Nuevo()), CancellationToken.None);
            resultado.EsExito.ShouldBeTrue(resultado.Error?.Mensaje);
            return resultado.Valor;
        });

    private async Task<LiquidacionPrueba> LiquidacionAsync(Guid id) =>
        await (await api.ClienteAsync()).GetOkAsync<LiquidacionPrueba>($"/api/v1/logistica/liquidaciones/{id}");

    // ───────────── Pruebas ─────────────

    [Fact]
    public async Task Semilla_RutasR01aR03_ConAlmacenGenericoYClientes()
    {
        var admin = await api.ClienteAsync();
        var rutas = await admin.GetOkAsync<List<RutaPrueba>>("/api/v1/logistica/rutas");
        // Genérico + hasta 5 clientes del canal de la ruta (la semilla tiene 4 de Autoservicio).
        foreach (var (codigo, canal, clientes) in new[] { ("R01", "Detalle", 6), ("R02", "Autoservicio", 5), ("R03", "Institucional", 6) })
        {
            var ruta = rutas.Single(r => r.Codigo == codigo);
            ruta.AlmacenCodigo.ShouldBe(codigo);
            ruta.CanalNombre.ShouldBe(canal);
            ruta.ClienteGenericoId.ShouldNotBeNull();
            ruta.Clientes.ShouldBe(clientes);
        }
    }

    [Fact]
    [Trait("Regla", "RN-LOG-01")]
    [Trait("Regla", "RN-VEN-10")]
    public async Task CrearRuta_CreaAlmacenDeRutaYClienteGenerico_YValidaVendedor()
    {
        var ruta = await CrearRutaAsync(await UsuarioAsync("vendedor1"));

        var almacen = await api.ConScopeAsync(sp => sp.GetRequiredService<TG.Inventario.Publico.IInventarioService>()
            .AlmacenDeRutaAsync(ruta.Id, CancellationToken.None));
        almacen.ShouldNotBeNull();
        almacen.Id.ShouldBe(ruta.AlmacenId);
        almacen.Tipo.ShouldBe(TG.Inventario.Publico.TipoAlmacen.Ruta);
        almacen.PermiteNegativo.ShouldBeTrue();

        var admin = await api.ClienteAsync();
        var clientes = await admin.GetOkAsync<List<ClienteRutaPrueba>>($"/api/v1/logistica/rutas/{ruta.Id}/clientes");
        clientes.Single().EsGenerico.ShouldBeTrue();

        var noVendedor = await admin.PostJsonAsync("/api/v1/logistica/rutas", new
        {
            codigo = "X" + Guid.NewGuid().ToString("N")[..5], nombre = "X", canalId = await admin.CanalAsync("DET"),
            vendedorId = await UsuarioAsync("almacen"), activo = true,
        });
        await noVendedor.DebeFallarAsync(HttpStatusCode.BadRequest, ErroresLogistica.VendedorInvalido);
    }

    [Fact]
    public async Task ClientesDeRuta_Reemplazar_DesactivaLosQueSalen_YConservaGenerico()
    {
        var admin = await api.ClienteAsync();
        var ruta = await CrearRutaAsync();
        var ventasClientes = (await api.ConScopeAsync(sp => sp.GetRequiredService<IVentasService>().ListarClientesAsync(false, CancellationToken.None)))
            .Where(c => c.CanalClave == "DET").ToList();
        var a = ventasClientes[0].Id;
        var b = ventasClientes[1].Id;
        await admin.PutOkAsync<List<ClienteRutaPrueba>>($"/api/v1/logistica/rutas/{ruta.Id}/clientes",
            new { clientes = new[] { new { clienteId = a, ordenVisita = 1, diasVisita = new[] { 1, 3 } }, new { clienteId = b, ordenVisita = 2, diasVisita = new[] { 2 } } } });
        var despues = await admin.PutOkAsync<List<ClienteRutaPrueba>>($"/api/v1/logistica/rutas/{ruta.Id}/clientes",
            new { clientes = new[] { new { clienteId = b, ordenVisita = 1, diasVisita = new[] { 2 } } } });

        despues.Select(c => c.ClienteId).ShouldBe([ruta.ClienteGenericoId!.Value, b], ignoreOrder: true);
        var sync = await api.ConScopeAsync(sp => sp.GetRequiredService<ILogisticaService>().ClientesDeRutaAsync(ruta.Id, 0, CancellationToken.None));
        sync.Single(c => c.ClienteId == a).Activo.ShouldBeFalse();

        var diasInvalidos = await admin.PutAsync($"/api/v1/logistica/rutas/{ruta.Id}/clientes", Contenido(new
        {
            clientes = new[] { new { clienteId = b, ordenVisita = 1, diasVisita = new[] { 8 } } },
        }));
        await diasInvalidos.DebeFallarAsync(HttpStatusCode.BadRequest, "VAL-001");
    }

    [Fact]
    [Trait("Regla", "RN-LOG-02")]
    [Trait("Regla", "RN-INV-06")]
    public async Task Despachar_SinExistencia_Falla_YConExistencia_TraspasaLotesYCosto()
    {
        var admin = await api.ClienteAsync();
        var ruta = await CrearRutaAsync();
        var producto = await admin.CrearProductoAsync(tipo: "Terminado", manejaLote: true);
        var pt = await admin.AlmacenAsync("PT");

        var sinExistencia = await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", new
        {
            rutaId = ruta.Id, tipo = "CargaInicial", partidas = new[] { new { productoId = producto.Id, cantidad = 10 } },
        });
        var fallo = await admin.PostJsonAsync($"/api/v1/logistica/envios/{sinExistencia.Id}/despachar");
        await fallo.DebeFallarAsync(HttpStatusCode.Conflict, "INV-001");
        (await admin.GetOkAsync<EnvioPrueba>($"/api/v1/logistica/envios/{sinExistencia.Id}")).Estado.ShouldBe("Preparado");

        await admin.EntradaAsync(pt, producto.Id, 6, 4, "TARDE", new DateOnly(2030, 1, 10));
        await admin.EntradaAsync(pt, producto.Id, 6, 4, "PRONTO", new DateOnly(2030, 1, 5));
        var despachado = await admin.PostOkAsync<EnvioPrueba>($"/api/v1/logistica/envios/{sinExistencia.Id}/despachar");

        despachado.Estado.ShouldBe("EnRuta");
        despachado.Partidas.Single(p => p.Lote == "PRONTO").CantidadBase.ShouldBe(6m);
        despachado.Partidas.Single(p => p.Lote == "TARDE").CantidadBase.ShouldBe(4m);
        (await admin.ExistenciaAsync(ruta.AlmacenId, producto.Id)).ShouldBe(10m);
        (await admin.ExistenciaAsync(pt, producto.Id)).ShouldBe(2m);

        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>($"/api/v1/inventario/kardex?documentoTipo=Envio&documentoId={despachado.Id}");
        kardex.Items.Select(m => m.CostoUnitario).Distinct().Single().ShouldBe(4m);

        var cancelar = await admin.PostJsonAsync($"/api/v1/logistica/envios/{despachado.Id}/cancelar");
        await cancelar.DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.EstadoEnvio);
    }

    [Fact]
    [Trait("Regla", "RN-LOG-03")]
    public async Task PrimeraCargaDelDia_AbreLiquidacionUnica()
    {
        var ruta = await CrearRutaAsync();
        var (_, _, carga) = await CargarAsync(ruta, 10);
        var (_, _, recarga) = await CargarAsync(ruta, 5);

        carga.LiquidacionId.ShouldNotBeNull();
        recarga.LiquidacionId.ShouldBe(carga.LiquidacionId);
        var admin = await api.ClienteAsync();
        var lista = await admin.GetOkAsync<PaginaPrueba<JsonObject>>($"/api/v1/logistica/liquidaciones?rutaId={ruta.Id}");
        lista.Total.ShouldBe(1);
        lista.Items[0]["estado"]!.GetValue<string>().ShouldBe("Abierta");
    }

    [Fact]
    [Trait("Regla", "RN-LOG-04")]
    public async Task Devolucion_NoVendibleEsMerma_VendibleRegresaAlCamion()
    {
        var admin = await api.ClienteAsync();
        var ruta = await CrearRutaAsync();
        var (tortilla, lote, _) = await CargarAsync(ruta, 20);
        await VenderAsync(ruta, tortilla.Id, 10);

        // [SUP-13]: sin condición, la tortilla devuelta es no vendible.
        var noVendible = await admin.PostOkAsync<RetornoPrueba>("/api/v1/logistica/retornos", new
        {
            tipo = "DevolucionCliente", rutaId = ruta.Id, clienteId = ruta.ClienteGenericoId,
            partidas = new[] { new { productoId = tortilla.Id, cantidad = 2 } },
        });
        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>($"/api/v1/inventario/kardex?documentoTipo=Retorno&documentoId={noVendible.Id}");
        kardex.Items.Select(m => m.Tipo).ShouldBe(["RetornoCliente", "Merma"], ignoreOrder: true);
        kardex.Items.All(m => m.Lote == lote).ShouldBeTrue();
        (await admin.ExistenciaAsync(ruta.AlmacenId, tortilla.Id)).ShouldBe(10m);

        await admin.PostOkAsync<RetornoPrueba>("/api/v1/logistica/retornos", new
        {
            tipo = "DevolucionCliente", rutaId = ruta.Id,
            partidas = new[] { new { productoId = tortilla.Id, cantidad = 3, condicion = "Vendible" } },
        });
        (await admin.ExistenciaAsync(ruta.AlmacenId, tortilla.Id)).ShouldBe(13m);
    }

    [Fact]
    [Trait("Regla", "RN-LOG-05")]
    public async Task RechazoEntrega_NoGeneraMovimientos()
    {
        var admin = await api.ClienteAsync();
        var ruta = await CrearRutaAsync();
        var (tortilla, _, _) = await CargarAsync(ruta, 8);

        var rechazo = await admin.PostOkAsync<RetornoPrueba>("/api/v1/logistica/retornos", new
        {
            tipo = "RechazoEntrega", rutaId = ruta.Id, partidas = new[] { new { productoId = tortilla.Id, cantidad = 8 } },
        });
        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>($"/api/v1/inventario/kardex?documentoTipo=Retorno&documentoId={rechazo.Id}");
        kardex.Total.ShouldBe(0);
        (await admin.ExistenciaAsync(ruta.AlmacenId, tortilla.Id)).ShouldBe(8m);
    }

    [Fact]
    public async Task RetornoDeApp_MismoOpId_NoDuplica()
    {
        var ruta = await CrearRutaAsync();
        var (tortilla, _, _) = await CargarAsync(ruta, 5);
        var opId = Id.Nuevo();
        var hoy = await HoyAsync();

        var resultados = await api.ConScopeAsync(async sp =>
        {
            var lista = new List<RetornoRegistrado>();
            for (var i = 0; i < 2; i++)
            {
                await using var scope = sp.CreateAsyncScope();
                var logistica = scope.ServiceProvider.GetRequiredService<ILogisticaService>();
                var r = await logistica.RegistrarRetornoAsync(new RegistrarRetornoComando(TipoRetorno.DevolucionCliente, ruta.Id, hoy,
                    Guid.Empty, [new PartidaRetornoComando(tortilla.Id, 1, Condicion: CondicionRetorno.Vendible)], OpId: opId,
                    Folio: $"{ruta.Codigo}-D99-000001"), CancellationToken.None);
                lista.Add(r.Valor);
            }

            return lista;
        });

        resultados[0].Duplicado.ShouldBeFalse();
        resultados[1].Duplicado.ShouldBeTrue();
        resultados[1].RetornoId.ShouldBe(resultados[0].RetornoId);
        var admin = await api.ClienteAsync();
        (await admin.ExistenciaAsync(ruta.AlmacenId, tortilla.Id)).ShouldBe(6m);
    }

    [Fact]
    [Trait("Regla", "RN-LOG-06")]
    [Trait("Regla", "RN-LOG-07")]
    public async Task CriterioDeAceptacion_Carga300Vende260Devuelve5Cuenta44_AjustaMas4YDejaRutaEnCero()
    {
        var admin = await api.ClienteAsync();
        var ruta = await CrearRutaAsync();
        var (tortilla, lote, carga) = await CargarAsync(ruta, 300);
        await VenderAsync(ruta, tortilla.Id, 260);
        await admin.PostOkAsync<RetornoPrueba>("/api/v1/logistica/retornos", new
        {
            tipo = "DevolucionCliente", rutaId = ruta.Id, clienteId = ruta.ClienteGenericoId,
            partidas = new[] { new { productoId = tortilla.Id, cantidad = 5, condicion = "NoVendible" } },
        });

        var liquidacion = await LiquidacionAsync(carga.LiquidacionId!.Value);
        var partida = liquidacion.Partidas.Single(p => p.ProductoId == tortilla.Id && p.Lote == lote);
        partida.Cargado.ShouldBe(300m);
        partida.Vendido.ShouldBe(260m);
        partida.DevueltoClientesNoVendible.ShouldBe(5m);
        partida.SobranteEsperado.ShouldBe(40m);

        var conteo = await admin.PutOkAsync<LiquidacionPrueba>($"/api/v1/logistica/liquidaciones/{liquidacion.Id}/conteo", new
        {
            efectivoEntregado = liquidacion.EfectivoEsperado,
            partidas = new[] { new { productoId = tortilla.Id, lote, sobranteContado = 44, destino = "Reintegrar" } },
        });
        conteo.Partidas.Single(p => p.Lote == lote).Diferencia.ShouldBe(4m);

        var ptAntes = await admin.ExistenciaAsync(await admin.AlmacenAsync("PT"), tortilla.Id, lote);
        var cerrada = await admin.PostOkAsync<LiquidacionPrueba>($"/api/v1/logistica/liquidaciones/{liquidacion.Id}/cerrar", new { forzar = false });

        cerrada.Estado.ShouldBe("Cerrada");
        (await admin.ExistenciaAsync(ruta.AlmacenId, tortilla.Id)).ShouldBe(0m);
        (await admin.ExistenciaAsync(await admin.AlmacenAsync("PT"), tortilla.Id, lote)).ShouldBe(ptAntes + 44m);
        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>(
            $"/api/v1/inventario/kardex?documentoTipo=Liquidacion&documentoId={liquidacion.Id}");
        kardex.Items.Single(m => m.Tipo == "AjusteEntrada").Cantidad.ShouldBe(4m);
        kardex.Items.Single(m => m.Tipo == "TraspasoSalida").Cantidad.ShouldBe(-44m);
        kardex.Items.Single(m => m.Tipo == "TraspasoEntrada").Cantidad.ShouldBe(44m);

        var incidencias = await admin.GetOkAsync<PaginaPrueba<JsonObject>>(
            $"/api/v1/ventas/incidencias?tipo=DiferenciaLiquidacion&entidadId={liquidacion.Id}");
        incidencias.Total.ShouldBe(1);

        var envio = await admin.GetOkAsync<EnvioPrueba>($"/api/v1/logistica/envios/{carga.Id}");
        envio.Estado.ShouldBe("Liquidado");
    }

    [Fact]
    [Trait("Regla", "RN-LOG-08")]
    public async Task Efectivo_EntregadoMenorAlEsperado_GeneraIncidencia()
    {
        var admin = await api.ClienteAsync();
        var ruta = await CrearRutaAsync();
        var (tortilla, lote, carga) = await CargarAsync(ruta, 10);
        await VenderAsync(ruta, tortilla.Id, 10);

        var liquidacion = await LiquidacionAsync(carga.LiquidacionId!.Value);
        liquidacion.EfectivoEsperado.ShouldBe(200m);
        await admin.PutOkAsync<LiquidacionPrueba>($"/api/v1/logistica/liquidaciones/{liquidacion.Id}/conteo", new
        {
            efectivoEntregado = 150m,
            partidas = new[] { new { productoId = tortilla.Id, lote, sobranteContado = 0, destino = "Reintegrar" } },
        });
        var cerrada = await admin.PostOkAsync<LiquidacionPrueba>($"/api/v1/logistica/liquidaciones/{liquidacion.Id}/cerrar", new { forzar = false });

        cerrada.DiferenciaEfectivo.ShouldBe(-50m);
        var incidencias = await admin.GetOkAsync<PaginaPrueba<JsonObject>>(
            $"/api/v1/ventas/incidencias?tipo=DiferenciaLiquidacion&entidadId={liquidacion.Id}");
        incidencias.Total.ShouldBe(1);
    }

    [Fact]
    [Trait("Regla", "RN-LOG-09")]
    public async Task Cierre_ConOperacionesPendientes_SeRechaza_SalvoForzarConPermiso()
    {
        var vendedorId = await UsuarioAsync("vendedor3");
        var ruta = await CrearRutaAsync(vendedorId);
        var (tortilla, lote, carga) = await CargarAsync(ruta, 5);
        var vendedor = await api.ClienteAsync("vendedor3");
        var dispositivo = await vendedor.PostOkAsync<JsonObject>("/api/v1/seguridad/dispositivos/solicitar",
            new { nombre = "Teléfono ruta prueba", plataforma = "android" });
        var dispositivoId = Guid.Parse(dispositivo["id"]!.GetValue<string>());
        var admin = await api.ClienteAsync();
        await admin.PostOkAsync<JsonObject>($"/api/v1/seguridad/dispositivos/{dispositivoId}/aprobar");
        await api.ConScopeAsync(async sp =>
        {
            await sp.GetRequiredService<ISeguridadService>().RegistrarContactoAsync(dispositivoId, "1.0.0", null, 3, CancellationToken.None);
            return true;
        });

        var liquidacionId = carga.LiquidacionId!.Value;
        (await LiquidacionAsync(liquidacionId)).OperacionesPendientes.ShouldBe(3);
        await admin.PutOkAsync<LiquidacionPrueba>($"/api/v1/logistica/liquidaciones/{liquidacionId}/conteo", new
        {
            efectivoEntregado = 0m,
            partidas = new[] { new { productoId = tortilla.Id, lote, sobranteContado = 5, destino = "Reintegrar" } },
        });

        var logistica = await api.ClienteAsync("logistica");
        var sinPermiso = await logistica.PostJsonAsync($"/api/v1/logistica/liquidaciones/{liquidacionId}/cerrar", new { forzar = true });
        await sinPermiso.DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.PendientesSync);

        var sinForzar = await admin.PostJsonAsync($"/api/v1/logistica/liquidaciones/{liquidacionId}/cerrar", new { forzar = false });
        await sinForzar.DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.PendientesSync);

        var forzada = await admin.PostOkAsync<LiquidacionPrueba>($"/api/v1/logistica/liquidaciones/{liquidacionId}/cerrar", new { forzar = true });
        forzada.Estado.ShouldBe("Cerrada");
    }

    [Fact]
    [Trait("Regla", "RN-LOG-10")]
    public async Task LiquidacionCerrada_NoSeReabreNiSeModifica()
    {
        var admin = await api.ClienteAsync();
        var ruta = await CrearRutaAsync();
        var (tortilla, lote, carga) = await CargarAsync(ruta, 4);
        var id = carga.LiquidacionId!.Value;
        await admin.PutOkAsync<LiquidacionPrueba>($"/api/v1/logistica/liquidaciones/{id}/conteo", new
        {
            efectivoEntregado = 0m,
            partidas = new[] { new { productoId = tortilla.Id, lote, sobranteContado = 4, destino = "Merma" } },
        });
        await admin.PostOkAsync<LiquidacionPrueba>($"/api/v1/logistica/liquidaciones/{id}/cerrar", new { forzar = false });

        var conteo = await admin.PutAsync($"/api/v1/logistica/liquidaciones/{id}/conteo", Contenido(new { efectivoEntregado = 1m, partidas = Array.Empty<object>() }));
        await conteo.DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.LiquidacionCerrada);
        var otraVez = await admin.PostJsonAsync($"/api/v1/logistica/liquidaciones/{id}/cerrar", new { forzar = false });
        await otraVez.DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.LiquidacionCerrada);

        // Una carga nueva para ese día ya no se puede despachar.
        var envio = await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", new
        {
            rutaId = ruta.Id, tipo = "Recarga", partidas = new[] { new { productoId = tortilla.Id, cantidad = 1, lote } },
        });
        var despacho = await admin.PostJsonAsync($"/api/v1/logistica/envios/{envio.Id}/despachar");
        await despacho.DebeFallarAsync(HttpStatusCode.Conflict, ErroresLogistica.LiquidacionCerrada);
    }

    [Fact]
    public async Task Cierre_SinEfectivoOConteo_SeRechaza()
    {
        var admin = await api.ClienteAsync();
        var ruta = await CrearRutaAsync();
        var (_, _, carga) = await CargarAsync(ruta, 3);
        var id = carga.LiquidacionId!.Value;

        var sinEfectivo = await admin.PostJsonAsync($"/api/v1/logistica/liquidaciones/{id}/cerrar", new { forzar = false });
        await sinEfectivo.DebeFallarAsync(HttpStatusCode.BadRequest, ErroresLogistica.SinConteo);

        await admin.PutOkAsync<LiquidacionPrueba>($"/api/v1/logistica/liquidaciones/{id}/conteo", new { efectivoEntregado = 0m, partidas = Array.Empty<object>() });
        var sinConteo = await admin.PostJsonAsync($"/api/v1/logistica/liquidaciones/{id}/cerrar", new { forzar = false });
        await sinConteo.DebeFallarAsync(HttpStatusCode.BadRequest, ErroresLogistica.SinConteo);
    }

    [Fact]
    public async Task Vendedor_SinPermiso_Recibe403()
    {
        var vendedor = await api.ClienteAsync("vendedor1");
        (await vendedor.GetAsync("/api/v1/logistica/rutas")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vendedor.GetAsync("/api/v1/logistica/liquidaciones")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ServicioPublico_RutaDeChoferYEnviosParaLaApp()
    {
        var hoy = await HoyAsync();
        var (ruta, envios) = await api.ConScopeAsync(async sp =>
        {
            var logistica = sp.GetRequiredService<ILogisticaService>();
            var vendedor = await sp.GetRequiredService<ISeguridadService>().UsuarioPorNombreAsync("vendedor1", CancellationToken.None);
            RutaInfo? r = await logistica.RutaDeChoferAsync(vendedor!.Value, hoy, CancellationToken.None);
            return (r, await logistica.EnviosDeRutaAsync(r!.Id, 0, hoy, CancellationToken.None));
        });
        ruta!.Codigo.ShouldBe("R01");
        envios.ShouldAllBe(e => e.Estado == EstadoEnvio.EnRuta || e.Estado == EstadoEnvio.Liquidado);
    }

    private sealed record IdPrueba(Guid Id);

    private sealed record CanalPrueba(Guid Id, string Clave);

    private sealed record PedidoPrueba(Guid Id, string Folio, string Estado, Guid? RutaId, Guid? EnvioId);

    [Fact]
    [Trait("Regla", "RN-LOG-11")]
    public async Task CargaConPedidos_LosLiga_RechazaOtraRutaYOtraCarga_YAlCancelarLosLibera()
    {
        var admin = await api.ClienteAsync();
        var ruta = await CrearRutaAsync();
        var otraRuta = await CrearRutaAsync();
        var producto = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA");
        var canal = (await admin.GetOkAsync<List<CanalPrueba>>("/api/v1/ventas/canales")).Single(c => c.Clave == "DET").Id;
        var lista = await admin.PostOkAsync<IdPrueba>("/api/v1/ventas/listas-precio",
            new { nombre = DatosPrueba.Unico("LISTA"), canalId = canal, vigenteDesde = "2026-01-01", activo = true });
        await admin.PutOkAsync<IdPrueba>($"/api/v1/ventas/listas-precio/{lista.Id}/partidas",
            new { partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, precioUnitario = 10m } } });
        var cliente = await admin.PostOkAsync<IdPrueba>("/api/v1/ventas/clientes", new
        {
            nombre = DatosPrueba.Unico("Cliente"), canalId = canal, listaPrecioId = lista.Id, condicionPago = "Contado",
            diasCredito = 0, requiereFactura = false, activo = true,
        });
        var pedido = await admin.PostOkAsync<PedidoPrueba>("/api/v1/ventas/pedidos", new
        {
            clienteId = cliente.Id, partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, cantidad = 10 } },
        });
        await admin.PostOkAsync<PedidoPrueba>($"/api/v1/ventas/pedidos/{pedido.Id}/confirmar");
        await admin.PostOkAsync<PedidoPrueba>($"/api/v1/ventas/pedidos/{pedido.Id}/asignar", new { rutaId = ruta.Id });
        object Carga(RutaPrueba r) => new
        {
            rutaId = r.Id, tipo = "CargaInicial", pedidoIds = new[] { pedido.Id },
            partidas = new[] { new { productoId = producto.Id, cantidad = 10m } },
        };

        await (await admin.PostJsonAsync("/api/v1/logistica/envios", Carga(otraRuta))).DebeFallarAsync(HttpStatusCode.Conflict, "VEN-018");

        var carga = await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", Carga(ruta));
        var ligados = await admin.GetOkAsync<PaginaPrueba<PedidoPrueba>>($"/api/v1/ventas/pedidos?envioId={carga.Id}");
        ligados.Items.ShouldHaveSingleItem().Id.ShouldBe(pedido.Id);
        (await admin.GetOkAsync<PaginaPrueba<PedidoPrueba>>($"/api/v1/ventas/pedidos?rutaId={ruta.Id}&sinCarga=true")).Total.ShouldBe(0);

        await (await admin.PostJsonAsync("/api/v1/logistica/envios", Carga(ruta))).DebeFallarAsync(HttpStatusCode.Conflict, "VEN-018");

        await admin.PostOkAsync<EnvioPrueba>($"/api/v1/logistica/envios/{carga.Id}/cancelar");
        var liberado = await admin.GetOkAsync<PedidoPrueba>($"/api/v1/ventas/pedidos/{pedido.Id}");
        liberado.Estado.ShouldBe("Asignado");
        liberado.RutaId.ShouldBe(ruta.Id);
        liberado.EnvioId.ShouldBeNull();
        (await admin.GetOkAsync<PaginaPrueba<PedidoPrueba>>($"/api/v1/ventas/pedidos?rutaId={ruta.Id}&sinCarga=true"))
            .Items.ShouldContain(p => p.Id == pedido.Id);
    }

    private static StringContent Contenido(object cuerpo) =>
        new(System.Text.Json.JsonSerializer.Serialize(cuerpo, ExtensionesHttp.Json), System.Text.Encoding.UTF8, "application/json");
}
