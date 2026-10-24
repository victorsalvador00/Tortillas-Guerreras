using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Inventario.Publico;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Infraestructura;
using TG.Ventas.Publico;

namespace TG.Api.IntegrationTests.Ventas;

/// <summary>Grupos de clientes, precios especiales y productos exclusivos (T-8.5).</summary>
public sealed class PreciosEspecialesTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record IdRespuesta(Guid Id);

    private sealed record ClienteRespuesta(Guid Id, Guid ListaPrecioId, Guid? GrupoClienteId, string? Grupo);

    private sealed record VentaRespuesta(Guid Id, decimal Subtotal, decimal Total);

    private sealed record PedidoRespuesta(Guid Id, string Estado, decimal Subtotal);

    private sealed record PrecioRespuesta(Guid ProductoId, decimal PrecioUnitario, decimal? PrecioLista);

    private sealed record Escenario(Guid ProductoId, Guid UnidadId, Guid Canal, Guid ListaId, Guid AlmacenId);

    /// <summary>Producto de reventa con precio 10 en una lista de Detalle propia y existencia en un almacén de ruta.</summary>
    private async Task<Escenario> EscenarioAsync()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA");
        var canal = await admin.CanalAsync("DET");
        var lista = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/listas-precio",
            new { nombre = DatosPrueba.Unico("LISTA"), canalId = canal, vigenteDesde = "2026-01-01", activo = true });
        await admin.PutOkAsync<IdRespuesta>($"/api/v1/ventas/listas-precio/{lista.Id}/partidas",
            new { partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, precioUnitario = 10m } } });
        var almacen = await api.ConScopeAsync(sp => sp.GetRequiredService<IInventarioService>()
            .CrearAlmacenRutaAsync(Id.Nuevo(), DatosPrueba.Unico("R")[..9], "Ruta de prueba", CancellationToken.None));
        await admin.EntradaAsync(almacen.Valor.Id, producto.Id, 100, 5m);
        return new Escenario(producto.Id, producto.UnidadBaseId, canal, lista.Id, almacen.Valor.Id);
    }

    private async Task<ClienteRespuesta> ClienteAsync(Escenario e, Guid? grupoId = null, bool conLista = true) =>
        await (await api.ClienteAsync()).PostOkAsync<ClienteRespuesta>("/api/v1/ventas/clientes", new
        {
            nombre = DatosPrueba.Unico("Cliente"), canalId = e.Canal, listaPrecioId = conLista ? e.ListaId : (Guid?)null,
            grupoClienteId = grupoId, condicionPago = "Contado", diasCredito = 0, requiereFactura = false, activo = true,
        });

    private async Task<HttpResponseMessage> VentaWebAsync(Escenario e, Guid clienteId) =>
        await (await api.ClienteAsync()).PostJsonAsync("/api/v1/ventas/ventas", new
        {
            clienteId, almacenId = e.AlmacenId, condicionPago = "Contado", formaPago = "Efectivo",
            partidas = new[] { new { productoId = e.ProductoId, unidadId = e.UnidadId, cantidad = 2 } },
        });

    private Task<DateOnly> HoyAsync() => api.ConScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IFechaOperativa>().Hoy()));

    private Task<decimal?> PrecioReferenciaAsync(Guid ventaId) => api.ConScopeAsync(sp =>
        sp.GetRequiredService<VentasDbContext>().VentasPartidas.Where(p => p.VentaId == ventaId).Select(p => p.PrecioReferencia).SingleAsync());

    private async Task<Guid> GrupoAsync(Escenario e, Guid? listaId = null) =>
        (await (await api.ClienteAsync()).PostOkAsync<IdRespuesta>("/api/v1/ventas/grupos-clientes", new
        {
            clave = DatosPrueba.Unico("GRP"), nombre = "Grupo de prueba", canalId = e.Canal, listaPrecioId = listaId, activo = true,
        })).Id;

    [Fact]
    [Trait("Regla", "RN-VEN-12")]
    [Trait("Regla", "RN-VEN-01")]
    public async Task PrecioEspecial_GanaSobreLaLista_SoloDesdeSuVigencia_YQuedaComoReferencia()
    {
        var admin = await api.ClienteAsync();
        var e = await EscenarioAsync();
        var conEspecial = await ClienteAsync(e);
        var conFuturo = await ClienteAsync(e);
        var otro = await ClienteAsync(e);
        var hoy = await HoyAsync();
        var precios = await admin.PutOkAsync<List<PrecioRespuesta>>($"/api/v1/ventas/clientes/{conEspecial.Id}/precios", new
        {
            precios = new[] { new { productoId = e.ProductoId, unidadId = e.UnidadId, precioUnitario = 8m, vigenteDesde = hoy } },
        });
        precios.Single().PrecioLista.ShouldBe(10m);
        await admin.PutOkAsync<List<PrecioRespuesta>>($"/api/v1/ventas/clientes/{conFuturo.Id}/precios", new
        {
            precios = new[] { new { productoId = e.ProductoId, unidadId = e.UnidadId, precioUnitario = 7m, vigenteDesde = hoy.AddDays(1) } },
        });

        var especial = await (await VentaWebAsync(e, conEspecial.Id)).LeerAsync<VentaRespuesta>();
        especial.Subtotal.ShouldBe(16m);
        (await PrecioReferenciaAsync(especial.Id)).ShouldBe(8m);
        (await (await VentaWebAsync(e, conFuturo.Id)).LeerAsync<VentaRespuesta>()).Subtotal.ShouldBe(20m);
        (await (await VentaWebAsync(e, otro.Id)).LeerAsync<VentaRespuesta>()).Subtotal.ShouldBe(20m);

        // En la app se respeta el precio que trae; la incidencia compara contra el especial, no contra la lista.
        var app = await api.ConScopeAsync(sp => sp.GetRequiredService<IVentasService>().RegistrarVentaAsync(new RegistrarVentaComando(
            OrigenVenta.App, conEspecial.Id, e.AlmacenId, Guid.Empty, hoy, CondicionPago.Contado, FormaPago.Efectivo,
            [new PartidaVentaComando(e.ProductoId, e.UnidadId, 1, 8m)], OpId: Id.Nuevo(), Folio: $"R98-D98-{Random.Shared.Next(1, 999999):D6}"),
            CancellationToken.None));
        app.Valor.Incidencias.ShouldNotContain(TipoIncidencia.PrecioDistinto);
        (await PrecioReferenciaAsync(app.Valor.VentaId)).ShouldBe(8m);

        // Quitarlo lo desactiva: vuelve la lista.
        await admin.PutOkAsync<List<PrecioRespuesta>>($"/api/v1/ventas/clientes/{conEspecial.Id}/precios", new { precios = Array.Empty<object>() });
        (await (await VentaWebAsync(e, conEspecial.Id)).LeerAsync<VentaRespuesta>()).Subtotal.ShouldBe(20m);
    }

    [Fact]
    [Trait("Regla", "RN-VEN-12")]
    public async Task ClienteNuevoDeUnGrupo_HeredaLaListaDelGrupo()
    {
        var e = await EscenarioAsync();
        var grupo = await GrupoAsync(e, e.ListaId);
        var cliente = await ClienteAsync(e, grupo, conLista: false);
        cliente.ListaPrecioId.ShouldBe(e.ListaId);
        cliente.GrupoClienteId.ShouldBe(grupo);
        cliente.Grupo.ShouldBe("Grupo de prueba");

        var sinLista = await GrupoAsync(e);
        var respuesta = await (await api.ClienteAsync()).PostJsonAsync("/api/v1/ventas/clientes", new
        {
            nombre = DatosPrueba.Unico("Cliente"), canalId = e.Canal, grupoClienteId = sinLista, condicionPago = "Contado",
            diasCredito = 0, requiereFactura = false, activo = true,
        });
        await respuesta.DebeFallarAsync(HttpStatusCode.BadRequest, ErroresVentas.NoEncontrado);
    }

    [Fact]
    [Trait("Regla", "RN-VEN-13")]
    public async Task PedidoCapturado_TomaElPrecioVigenteAlConfirmar_YConfirmadoYaNoCambia()
    {
        var admin = await api.ClienteAsync();
        var e = await EscenarioAsync();
        var cliente = await ClienteAsync(e);
        object Pedido() => new { clienteId = cliente.Id, partidas = new[] { new { productoId = e.ProductoId, unidadId = e.UnidadId, cantidad = 3 } } };
        var capturado = await admin.PostOkAsync<PedidoRespuesta>("/api/v1/ventas/pedidos", Pedido());
        capturado.Subtotal.ShouldBe(30m);
        var confirmado = await admin.PostOkAsync<PedidoRespuesta>("/api/v1/ventas/pedidos", Pedido());
        await admin.PostOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{confirmado.Id}/confirmar");

        await admin.PutOkAsync<List<PrecioRespuesta>>($"/api/v1/ventas/clientes/{cliente.Id}/precios", new
        {
            precios = new[] { new { productoId = e.ProductoId, unidadId = e.UnidadId, precioUnitario = 9m, vigenteDesde = await HoyAsync() } },
        });

        (await admin.PostOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{capturado.Id}/confirmar")).Subtotal.ShouldBe(27m);
        (await admin.GetOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{confirmado.Id}")).Subtotal.ShouldBe(30m);
    }

    [Fact]
    [Trait("Regla", "RN-VEN-14")]
    public async Task ProductoExclusivo_SoloParaSusClientesYGrupos_WebYPedidoRechazan_AppConIncidencia()
    {
        var admin = await api.ClienteAsync();
        var e = await EscenarioAsync();
        var grupo = await GrupoAsync(e, e.ListaId);
        var delGrupo = await ClienteAsync(e, grupo);
        var listado = await ClienteAsync(e);
        var ajeno = await ClienteAsync(e);
        await admin.PutOkAsync<List<object>>($"/api/v1/ventas/productos/{e.ProductoId}/exclusivos", new
        {
            exclusivos = new object[] { new { grupoClienteId = grupo }, new { clienteId = listado.Id } },
        });

        (await VentaWebAsync(e, delGrupo.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await VentaWebAsync(e, listado.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await VentaWebAsync(e, ajeno.Id)).DebeFallarAsync(HttpStatusCode.BadRequest, ErroresVentas.ProductoExclusivo);
        var pedido = await admin.PostJsonAsync("/api/v1/ventas/pedidos", new
        {
            clienteId = ajeno.Id, partidas = new[] { new { productoId = e.ProductoId, unidadId = e.UnidadId, cantidad = 1 } },
        });
        await pedido.DebeFallarAsync(HttpStatusCode.BadRequest, ErroresVentas.ProductoExclusivo);

        var app = await api.ConScopeAsync(async sp => await sp.GetRequiredService<IVentasService>().RegistrarVentaAsync(new RegistrarVentaComando(
            OrigenVenta.App, ajeno.Id, e.AlmacenId, Guid.Empty, await HoyAsync(), CondicionPago.Contado, FormaPago.Efectivo,
            [new PartidaVentaComando(e.ProductoId, e.UnidadId, 1, 10m)], OpId: Id.Nuevo(), Folio: $"R98-D98-{Random.Shared.Next(1, 999999):D6}"),
            CancellationToken.None));
        app.EsExito.ShouldBeTrue();
        app.Valor.Incidencias.ShouldContain(TipoIncidencia.ProductoExclusivo);

        // Sin registros activos el producto vuelve a estar disponible para todos.
        await admin.PutOkAsync<List<object>>($"/api/v1/ventas/productos/{e.ProductoId}/exclusivos", new { exclusivos = Array.Empty<object>() });
        (await VentaWebAsync(e, ajeno.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ───────────── Bajada a la app ─────────────

    private sealed record DispositivoPrueba(Guid Id);

    private sealed record PrecioClientePull(Guid ClienteId, Guid ProductoId, decimal PrecioUnitario, bool Activo);

    private sealed record ExclusivoPull(Guid Id, Guid ProductoId, Guid? ClienteId, Guid? GrupoClienteId);

    private sealed record ClientePull(Guid Id, bool EsGenerico);

    private sealed record CambiosPull(List<ClientePull> Clientes, List<PrecioClientePull> PreciosCliente, List<ExclusivoPull> ProductosExclusivos,
        Dictionary<string, List<Guid>> Eliminados);

    private sealed record RespuestaPull(long Cursor, CambiosPull Cambios);

    [Fact]
    [Trait("Regla", "RN-VEN-12")]
    [Trait("Regla", "RN-VEN-14")]
    public async Task Pull_BajaPreciosEspecialesDeLaRuta_YTodasLasExclusividades()
    {
        var admin = await api.ClienteAsync();
        var vendedor = await api.ClienteAsync("vendedor1");
        var solicitado = await vendedor.PostOkAsync<DispositivoPrueba>("/api/v1/seguridad/dispositivos/solicitar",
            new { nombre = "Teléfono precios", plataforma = "android", versionApp = "1.0.0" });
        await admin.PostOkAsync<DispositivoPrueba>($"/api/v1/seguridad/dispositivos/{solicitado.Id}/aprobar", new { });
        var sesion = await api.ClienteAnonimo().PostOkAsync<SesionPrueba>("/api/v1/seguridad/auth/login",
            new { usuario = "vendedor1", contrasena = ApiFactory.Contrasena, dispositivoId = solicitado.Id });
        var app = api.ClienteAnonimo();
        app.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion.AccessToken);

        var inicial = await app.GetOkAsync<RespuestaPull>("/api/v1/sync/pull?desde=0");
        var clienteRuta = inicial.Cambios.Clientes.First(c => !c.EsGenerico).Id;
        var e = await EscenarioAsync();
        var fuera = await ClienteAsync(e);
        await admin.PutOkAsync<List<PrecioRespuesta>>($"/api/v1/ventas/clientes/{clienteRuta}/precios", new
        {
            precios = new[] { new { productoId = e.ProductoId, unidadId = e.UnidadId, precioUnitario = 8m, vigenteDesde = await HoyAsync() } },
        });
        await admin.PutOkAsync<List<PrecioRespuesta>>($"/api/v1/ventas/clientes/{fuera.Id}/precios", new
        {
            precios = new[] { new { productoId = e.ProductoId, unidadId = e.UnidadId, precioUnitario = 7m, vigenteDesde = await HoyAsync() } },
        });
        await admin.PutOkAsync<List<object>>($"/api/v1/ventas/productos/{e.ProductoId}/exclusivos",
            new { exclusivos = new[] { new { clienteId = fuera.Id } } });

        var cambios = await app.GetOkAsync<RespuestaPull>($"/api/v1/sync/pull?desde={inicial.Cursor}");
        cambios.Cambios.PreciosCliente.ShouldHaveSingleItem().ClienteId.ShouldBe(clienteRuta);
        var exclusivo = cambios.Cambios.ProductosExclusivos.ShouldHaveSingleItem();
        exclusivo.ClienteId.ShouldBe(fuera.Id);

        await admin.PutOkAsync<List<PrecioRespuesta>>($"/api/v1/ventas/clientes/{clienteRuta}/precios", new { precios = Array.Empty<object>() });
        await admin.PutOkAsync<List<object>>($"/api/v1/ventas/productos/{e.ProductoId}/exclusivos", new { exclusivos = Array.Empty<object>() });
        var bajas = await app.GetOkAsync<RespuestaPull>($"/api/v1/sync/pull?desde={cambios.Cursor}");
        bajas.Cambios.PreciosCliente.ShouldHaveSingleItem().Activo.ShouldBeFalse();
        bajas.Cambios.Eliminados["productosExclusivos"].ShouldContain(exclusivo.Id);
    }
}
