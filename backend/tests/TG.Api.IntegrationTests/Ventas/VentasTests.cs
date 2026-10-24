using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Inventario.Publico;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Resultados;
using TG.Ventas.Publico;

namespace TG.Api.IntegrationTests.Ventas;

public sealed class VentasTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const decimal LatCliente = 19.18m;
    private const decimal LngCliente = -96.14m;

    private sealed record Escenario(Guid ProductoId, Guid UnidadId, Guid ClienteId, Guid ListaId, Guid AlmacenId, Guid RutaId);

    private sealed record IdRespuesta(Guid Id);

    private sealed record ClienteRespuesta(Guid Id, bool UbicacionAprobada, decimal? Latitud, decimal? LatitudPropuesta);

    private sealed record VentaRespuesta(Guid Id, string Folio, string Estado, decimal Total, bool RequiereRevision);

    private sealed record PedidoRespuesta(Guid Id, string Folio, string Estado, IReadOnlyList<PedidoPartidaRespuesta> Partidas);

    private sealed record PedidoPartidaRespuesta(Guid Id, decimal CantidadEntregadaBase);

    private sealed record SaldoRespuesta(decimal Saldo);

    /// <summary>Producto, lista, cliente y almacén de ruta propios de cada prueba (aislamiento).</summary>
    private async Task<Escenario> EscenarioAsync(decimal precio = 20m, decimal tasaIva = 0m, decimal? tasaIeps = null,
        string condicion = "Contado", decimal? limite = null, bool conUbicacion = true, decimal existencia = 100m,
        bool conPrecio = true)
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA", tasaIva: tasaIva, tasaIeps: tasaIeps);
        var canales = await admin.GetOkAsync<List<IdCodigoCanal>>("/api/v1/ventas/canales");
        var canal = canales.Single(c => c.Clave == "DET").Id;
        var lista = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/listas-precio",
            new { nombre = DatosPrueba.Unico("LISTA"), canalId = canal, vigenteDesde = "2026-01-01", activo = true });
        if (conPrecio)
        {
            await admin.PutOkAsync<IdRespuesta>($"/api/v1/ventas/listas-precio/{lista.Id}/partidas", new
            {
                partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, precioUnitario = precio } },
            });
        }

        var cliente = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/clientes", new
        {
            nombre = DatosPrueba.Unico("Cliente"),
            canalId = canal,
            listaPrecioId = lista.Id,
            condicionPago = condicion,
            diasCredito = condicion == "Credito" ? 15 : 0,
            limiteCredito = limite,
            requiereFactura = false,
            latitud = conUbicacion ? LatCliente : (decimal?)null,
            longitud = conUbicacion ? LngCliente : (decimal?)null,
            activo = true,
        });

        var rutaId = Id.Nuevo();
        var almacen = await api.ConScopeAsync(sp => sp.GetRequiredService<IInventarioService>()
            .CrearAlmacenRutaAsync(rutaId, DatosPrueba.Unico("R")[..9], "Ruta de prueba", CancellationToken.None));
        if (existencia > 0)
        {
            await admin.EntradaAsync(almacen.Valor.Id, producto.Id, existencia, 5m);
        }

        return new Escenario(producto.Id, producto.UnidadBaseId, cliente.Id, lista.Id, almacen.Valor.Id, rutaId);
    }

    private sealed record IdCodigoCanal(Guid Id, string Clave);

    private static RegistrarVentaComando VentaApp(Escenario e, decimal cantidad = 5, decimal? precio = 20m,
        GpsLectura? gps = null, Guid? opId = null, string? folio = null, decimal? totalApp = null,
        CondicionPago condicion = CondicionPago.Contado, DateOnly? fecha = null) =>
        new(OrigenVenta.App, e.ClienteId, e.AlmacenId, Guid.Empty, fecha ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-6)),
            condicion, condicion == CondicionPago.Credito ? FormaPago.Credito : FormaPago.Efectivo,
            [new PartidaVentaComando(e.ProductoId, e.UnidadId, cantidad, precio)],
            OpId: opId ?? Id.Nuevo(), Folio: folio ?? $"R99-D99-{Random.Shared.Next(1, 999999):D6}", RutaId: e.RutaId,
            TotalApp: totalApp, Gps: gps ?? new GpsLectura(LatCliente, LngCliente, 10));

    private Task<Result<VentaRegistrada>> RegistrarAsync(RegistrarVentaComando comando) =>
        api.ConScopeAsync(sp => sp.GetRequiredService<IVentasService>().RegistrarVentaAsync(comando, CancellationToken.None));

    private async Task<HttpResponseMessage> VentaWebAsync(Escenario e, string condicion = "Contado", string forma = "Efectivo",
        decimal cantidad = 2) =>
        await (await api.ClienteAsync()).PostJsonAsync("/api/v1/ventas/ventas", new
        {
            clienteId = e.ClienteId, almacenId = e.AlmacenId, condicionPago = condicion, formaPago = forma, rutaId = e.RutaId,
            partidas = new[] { new { productoId = e.ProductoId, unidadId = e.UnidadId, cantidad } },
        });

    [Fact]
    [Trait("Regla", "RN-VEN-01")]
    public async Task Precio_WebSinPrecioSeRechaza_AppRespetaSuPrecioConIncidencia()
    {
        var sinPrecio = await EscenarioAsync(conPrecio: false);
        await (await VentaWebAsync(sinPrecio)).DebeFallarAsync(HttpStatusCode.BadRequest, "VEN-001");

        var e = await EscenarioAsync(precio: 20m);
        var venta = await RegistrarAsync(VentaApp(e, cantidad: 2, precio: 25m));
        venta.Valor.Total.ShouldBe(50m);
        venta.Valor.Incidencias.ShouldContain(TipoIncidencia.PrecioDistinto);

        var web = await (await VentaWebAsync(e, cantidad: 2)).LeerAsync<VentaRespuesta>();
        web.Total.ShouldBe(40m);
        web.Folio.ShouldStartWith("VTA-");
    }

    [Fact]
    [Trait("Regla", "RN-VEN-02")]
    public async Task Importes_ServidorRecalcula_YDiferenciaMayorACincoCentavosGeneraIncidencia()
    {
        var e = await EscenarioAsync(precio: 16.67m, tasaIva: 0.16m, tasaIeps: 0.08m);

        var exacta = await RegistrarAsync(VentaApp(e, cantidad: 3, precio: 16.67m, totalApp: 62.68m));
        exacta.Valor.Subtotal.ShouldBe(50.01m);
        exacta.Valor.Ieps.ShouldBe(4.00m);
        exacta.Valor.Iva.ShouldBe(8.64m);
        exacta.Valor.Total.ShouldBe(62.65m);
        exacta.Valor.Incidencias.ShouldNotContain(TipoIncidencia.TotalDistinto);

        var distinta = await RegistrarAsync(VentaApp(e, cantidad: 3, precio: 16.67m, totalApp: 62.75m));
        distinta.Valor.Total.ShouldBe(62.65m);
        distinta.Valor.Incidencias.ShouldContain(TipoIncidencia.TotalDistinto);
    }

    [Fact]
    [Trait("Regla", "RN-VEN-03")]
    [Trait("Regla", "RN-INV-03")]
    public async Task Venta_GeneraMovimientoVentaEnAlmacenDeRuta_YNegativoCreaIncidencia()
    {
        var e = await EscenarioAsync(existencia: 3);
        var admin = await api.ClienteAsync();

        var venta = await RegistrarAsync(VentaApp(e, cantidad: 5));

        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>(
            $"/api/v1/inventario/kardex?documentoTipo=Venta&documentoId={venta.Valor.VentaId}");
        kardex.Items.Single().Tipo.ShouldBe("Venta");
        kardex.Items.Single().Cantidad.ShouldBe(-5m);
        kardex.Items.Single().AlmacenId.ShouldBe(e.AlmacenId);
        venta.Valor.Incidencias.ShouldContain(TipoIncidencia.ExistenciaNegativaRuta);
        (await admin.ExistenciaAsync(e.AlmacenId, e.ProductoId)).ShouldBe(-2m);
    }

    [Fact]
    [Trait("Regla", "RN-VEN-04")]
    public async Task Credito_WebRechazaExcesoOClienteDeContado_AppAceptaConIncidencia()
    {
        var contado = await EscenarioAsync();
        await (await VentaWebAsync(contado, "Credito", "Credito")).DebeFallarAsync(HttpStatusCode.Conflict, "VEN-004");

        var credito = await EscenarioAsync(precio: 50m, condicion: "Credito", limite: 100m);
        (await VentaWebAsync(credito, "Credito", "Credito", cantidad: 2)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await VentaWebAsync(credito, "Credito", "Credito", cantidad: 1)).DebeFallarAsync(HttpStatusCode.Conflict, "VEN-004");

        var app = await RegistrarAsync(VentaApp(credito, cantidad: 1, precio: 50m, condicion: CondicionPago.Credito));
        app.EsExito.ShouldBeTrue();
        app.Valor.Incidencias.ShouldContain(TipoIncidencia.CreditoExcedido);

        await (await VentaWebAsync(contado, "Contado", "Credito")).DebeFallarAsync(HttpStatusCode.BadRequest, "VEN-005");
    }

    [Fact]
    [Trait("Regla", "RN-VEN-05")]
    public async Task Gps_A400MetrosDelClienteAprobado_RequiereRevisionYGpsFueraDeRango()
    {
        var e = await EscenarioAsync();

        var lejos = await RegistrarAsync(VentaApp(e, gps: new GpsLectura(LatCliente + 0.0036m, LngCliente, 12)));
        lejos.Valor.RequiereRevision.ShouldBeTrue();
        lejos.Valor.Incidencias.ShouldContain(TipoIncidencia.GpsFueraDeRango);

        var cerca = await RegistrarAsync(VentaApp(e, gps: new GpsLectura(LatCliente + 0.0005m, LngCliente, 12)));
        cerca.Valor.RequiereRevision.ShouldBeFalse();
        cerca.Valor.Incidencias.ShouldBeEmpty();

        var impreciso = await RegistrarAsync(VentaApp(e, gps: new GpsLectura(LatCliente, LngCliente, 250)));
        impreciso.Valor.Incidencias.ShouldContain(TipoIncidencia.GpsImpreciso);

        var sinGps = await RegistrarAsync(VentaApp(e) with { Gps = null });
        sinGps.EsExito.ShouldBeTrue();
        sinGps.Valor.Incidencias.ShouldContain(TipoIncidencia.SinGps);
    }

    [Fact]
    [Trait("Regla", "RN-VEN-06")]
    public async Task Ubicacion_PrimeraVentaConGpsLaPropone_YSupervisorLaAprueba()
    {
        var e = await EscenarioAsync(conUbicacion: false);

        var venta = await RegistrarAsync(VentaApp(e, gps: new GpsLectura(19.19m, -96.15m, 8)));
        venta.Valor.Incidencias.ShouldContain(TipoIncidencia.UbicacionPorAprobar);
        venta.Valor.RequiereRevision.ShouldBeFalse();

        var segunda = await RegistrarAsync(VentaApp(e, gps: new GpsLectura(19.19m, -96.15m, 8)));
        segunda.Valor.Incidencias.ShouldNotContain(TipoIncidencia.UbicacionPorAprobar);

        var supervisor = await api.ClienteAsync("supervisor");
        var aprobado = await supervisor.PostOkAsync<ClienteRespuesta>($"/api/v1/ventas/clientes/{e.ClienteId}/aprobar-ubicacion", new { });
        aprobado.UbicacionAprobada.ShouldBeTrue();
        aprobado.Latitud.ShouldBe(19.19m);
        aprobado.LatitudPropuesta.ShouldBeNull();
    }

    [Fact]
    [Trait("Regla", "RN-VEN-07")]
    public async Task Cancelar_MismoDiaNoFacturada_RegresaExistencia()
    {
        var e = await EscenarioAsync(existencia: 10);
        var admin = await api.ClienteAsync();
        var venta = await (await VentaWebAsync(e, cantidad: 4)).LeerAsync<VentaRespuesta>();
        (await admin.ExistenciaAsync(e.AlmacenId, e.ProductoId)).ShouldBe(6m);

        var cancelada = await admin.PostOkAsync<VentaRespuesta>($"/api/v1/ventas/ventas/{venta.Id}/cancelar", new { motivo = "Error de captura" });
        cancelada.Estado.ShouldBe("Cancelada");
        (await admin.ExistenciaAsync(e.AlmacenId, e.ProductoId)).ShouldBe(10m);
        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>(
            $"/api/v1/inventario/kardex?documentoTipo=Venta&documentoId={venta.Id}");
        kardex.Items.Sum(m => m.Cantidad).ShouldBe(0m);

        await (await admin.PostJsonAsync($"/api/v1/ventas/ventas/{venta.Id}/cancelar", new { motivo = "x" }))
            .DebeFallarAsync(HttpStatusCode.Conflict, "VEN-017");

        var facturada = await (await VentaWebAsync(e, cantidad: 1)).LeerAsync<VentaRespuesta>();
        var asignada = await api.ConScopeAsync(sp => sp.GetRequiredService<IVentasService>()
            .AsignarFacturaAsync([facturada.Id], Id.Nuevo(), CancellationToken.None));
        asignada.EsExito.ShouldBeTrue();
        await (await admin.PostJsonAsync($"/api/v1/ventas/ventas/{facturada.Id}/cancelar", new { motivo = "x" }))
            .DebeFallarAsync(HttpStatusCode.Conflict, "VEN-007");

        var ayer = await RegistrarAsync(VentaApp(e, fecha: DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-6)).AddDays(-1)));
        await (await admin.PostJsonAsync($"/api/v1/ventas/ventas/{ayer.Valor.VentaId}/cancelar", new { motivo = "x" }))
            .DebeFallarAsync(HttpStatusCode.Conflict, "VEN-008");
    }

    [Fact]
    [Trait("Regla", "RN-VEN-08")]
    public async Task EntregaDePedido_ParcialYTotal_ActualizaEstadoYEntregado()
    {
        var e = await EscenarioAsync(precio: 30m, existencia: 50);
        var admin = await api.ClienteAsync();
        var pedido = await admin.PostOkAsync<PedidoRespuesta>("/api/v1/ventas/pedidos", new
        {
            clienteId = e.ClienteId, partidas = new[] { new { productoId = e.ProductoId, unidadId = e.UnidadId, cantidad = 10 } },
        });
        pedido.Folio.ShouldStartWith("PED-");
        await admin.PostOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{pedido.Id}/confirmar");
        var asignado = await admin.PostOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{pedido.Id}/asignar", new { rutaId = e.RutaId });
        asignado.Estado.ShouldBe("Asignado");

        var partidaId = pedido.Partidas.Single().Id;
        async Task<Result<VentaRegistrada>> Entregar(decimal cantidad) => await api.ConScopeAsync(sp =>
            sp.GetRequiredService<IVentasService>().EntregarPedidoAsync(new EntregarPedidoComando(OrigenVenta.App, pedido.Id,
                e.AlmacenId, Guid.Empty, DateOnly.FromDateTime(DateTime.UtcNow), FormaPago.Efectivo,
                [new PartidaEntregaComando(partidaId, cantidad)], OpId: Id.Nuevo(), Folio: $"R98-D98-{Random.Shared.Next(1, 999999):D6}",
                Gps: new GpsLectura(LatCliente, LngCliente, 5)), CancellationToken.None));

        var parcial = await Entregar(4);
        parcial.Valor.Total.ShouldBe(120m);
        (await admin.GetOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{pedido.Id}")).Estado.ShouldBe("EntregadoParcial");

        (await Entregar(7)).Error!.Codigo.ShouldBe("VEN-013");

        (await Entregar(6)).EsExito.ShouldBeTrue();
        var final = await admin.GetOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{pedido.Id}");
        final.Estado.ShouldBe("Entregado");
        final.Partidas.Single().CantidadEntregadaBase.ShouldBe(10m);
        (await admin.ExistenciaAsync(e.AlmacenId, e.ProductoId)).ShouldBe(40m);
    }

    [Fact]
    [Trait("Regla", "RN-VEN-09")]
    public async Task Reenvio_MismoOpIdNoDuplica_YFolioConOtroOpIdSeRechaza()
    {
        var e = await EscenarioAsync(existencia: 20);
        var admin = await api.ClienteAsync();
        var comando = VentaApp(e, cantidad: 3);

        var primera = await RegistrarAsync(comando);
        var reenvio = await RegistrarAsync(comando);

        reenvio.Valor.Duplicada.ShouldBeTrue();
        reenvio.Valor.VentaId.ShouldBe(primera.Valor.VentaId);
        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>(
            $"/api/v1/inventario/kardex?documentoTipo=Venta&documentoId={primera.Valor.VentaId}");
        kardex.Items.Count.ShouldBe(1);
        (await admin.ExistenciaAsync(e.AlmacenId, e.ProductoId)).ShouldBe(17m);

        var otroOp = await RegistrarAsync(comando with { OpId = Id.Nuevo(), VentaId = null });
        otroOp.Error!.Codigo.ShouldBe("VEN-009");
    }

    [Fact]
    [Trait("Regla", "RN-VEN-10")]
    [Trait("Regla", "RN-LOG-31")]
    public async Task ClienteGenerico_PorRuta_EsIdempotente_YSigueElCanalDeLaRuta()
    {
        var resultado = await api.ConScopeAsync(async sp =>
        {
            var ventas = sp.GetRequiredService<IVentasService>();
            var canales = (await ventas.ListarCanalesAsync(CancellationToken.None)).ToDictionary(c => c.Clave, c => c.Id);
            var primero = await ventas.CrearClienteGenericoAsync("R77", canales["DET"], CancellationToken.None);
            var segundo = await ventas.CrearClienteGenericoAsync("R77", canales["DET"], CancellationToken.None);
            var otroCanal = await ventas.CrearClienteGenericoAsync("R77", canales["INST"], CancellationToken.None);
            return (primero.Valor, segundo.Valor, otroCanal.Valor, canales);
        });

        resultado.Item1.Id.ShouldBe(resultado.Item2.Id);
        resultado.Item1.EsGenerico.ShouldBeTrue();
        resultado.Item1.Nombre.ShouldBe("Público en general R77");
        resultado.Item1.CanalClave.ShouldBe("DET");
        resultado.Item3.Id.ShouldBe(resultado.Item1.Id);
        resultado.Item3.CanalClave.ShouldBe("INST");
        resultado.Item3.ListaPrecioId.ShouldNotBe(resultado.Item1.ListaPrecioId);
    }

    [Fact]
    public async Task Cobro_ReduceSaldo_EsIdempotente_YResumenDeRutaCuadraEfectivo()
    {
        var e = await EscenarioAsync(precio: 10m, condicion: "Credito", limite: 1000m, existencia: 50);
        await RegistrarAsync(VentaApp(e, cantidad: 10, precio: 10m, condicion: CondicionPago.Credito));
        await RegistrarAsync(VentaApp(e, cantidad: 3, precio: 10m));
        var admin = await api.ClienteAsync();
        (await admin.GetOkAsync<SaldoRespuesta>($"/api/v1/ventas/clientes/{e.ClienteId}/saldo")).Saldo.ShouldBe(100m);

        var op = Id.Nuevo();
        var comando = new RegistrarCobroComando(e.ClienteId, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-6)), FormaPago.Efectivo,
            40m, OpId: op, RutaId: e.RutaId);
        var cobro = await api.ConScopeAsync(sp => sp.GetRequiredService<IVentasService>().RegistrarCobroAsync(comando, CancellationToken.None));
        var duplicado = await api.ConScopeAsync(sp => sp.GetRequiredService<IVentasService>().RegistrarCobroAsync(comando, CancellationToken.None));

        cobro.Valor.Folio.ShouldStartWith("COB-");
        cobro.Valor.SaldoCliente.ShouldBe(60m);
        duplicado.Valor.Duplicado.ShouldBeTrue();
        duplicado.Valor.CobroId.ShouldBe(cobro.Valor.CobroId);

        var resumen = await api.ConScopeAsync(sp => sp.GetRequiredService<IVentasService>()
            .ResumenRutaDiaAsync(e.RutaId, comando.FechaOperativa, CancellationToken.None));
        resumen.VentasContadoEfectivo.ShouldBe(30m);
        resumen.CobrosEfectivo.ShouldBe(40m);
        resumen.EfectivoEsperado.ShouldBe(70m);
        resumen.Vendido.Single().CantidadBase.ShouldBe(13m);
    }

    [Fact]
    public async Task Vendedor_SinPermisoDeVer_NoListaVentas()
    {
        var vendedor = await api.ClienteAsync("vendedor1");
        (await vendedor.GetAsync("/api/v1/ventas/ventas")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vendedor.GetAsync("/api/v1/ventas/incidencias")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
