using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Inventario.Publico;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Infraestructura;
using TG.Ventas.Publico;

namespace TG.Api.IntegrationTests.Ventas;

/// <summary>Pedidos v0.2 y <c>CapturarPedido</c> desde la app (T-8.6).</summary>
public sealed class PedidosV02Tests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record IdRespuesta(Guid Id);

    private sealed record PartidaRespuesta(Guid Id, Guid ProductoId, decimal Cantidad, decimal PrecioUnitario, decimal Importe, string Tipo,
        decimal? CantidadSolicitada);

    private sealed record PedidoRespuesta(Guid Id, string Estado, DateOnly? FechaEntrega, decimal Subtotal, string Origen, bool EsRuta,
        Guid? ConfirmadoPor, DateTimeOffset? ConfirmadoEn, List<PartidaRespuesta> Partidas);

    private sealed record RutaPrueba(Guid Id, string Codigo, Guid? ClienteGenericoId);

    private sealed record DispositivoPrueba(Guid Id);

    private sealed record Resultado(string Estado, Dictionary<string, Guid>? IdsServidor, string? Codigo);

    private sealed record RespuestaPush(List<Resultado> Resultados);

    private sealed record Registro(Guid EntidadId, string Tabla, string Accion, JsonElement Cambios);

    private sealed record Bitacora(List<Registro> Items);

    private sealed record Escenario(Guid ProductoId, Guid UnidadId, Guid ClienteId, Guid AlmacenId);

    // ───────────── Ayudas ─────────────

    /// <summary>Producto de reventa a 10 en una lista propia, cliente del canal indicado y existencia en un almacén de ruta.</summary>
    private async Task<Escenario> EscenarioAsync(string canal = "AUTO")
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA");
        var canalId = await admin.CanalAsync(canal);
        var lista = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/listas-precio",
            new { nombre = DatosPrueba.Unico("LISTA"), canalId, vigenteDesde = "2026-01-01", activo = true });
        await admin.PutOkAsync<IdRespuesta>($"/api/v1/ventas/listas-precio/{lista.Id}/partidas",
            new { partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, precioUnitario = 10m } } });
        var cliente = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/clientes", new
        {
            nombre = DatosPrueba.Unico("Cliente"), canalId, listaPrecioId = lista.Id, condicionPago = "Contado", diasCredito = 0,
            requiereFactura = false, activo = true,
        });
        var almacen = await api.ConScopeAsync(sp => sp.GetRequiredService<IInventarioService>()
            .CrearAlmacenRutaAsync(Id.Nuevo(), DatosPrueba.Unico("R")[..9], "Ruta de prueba", CancellationToken.None));
        await admin.EntradaAsync(almacen.Valor.Id, producto.Id, 100, 5m);
        return new Escenario(producto.Id, producto.UnidadBaseId, cliente.Id, almacen.Valor.Id);
    }

    private static object Pedido(Guid clienteId, DateOnly? fechaEntrega, params object[] partidas) =>
        new { clienteId, fechaEntrega, partidas };

    private static object Partida(Escenario e, decimal cantidad, string tipo = "Venta") =>
        new { productoId = e.ProductoId, unidadId = e.UnidadId, cantidad, tipo };

    private Task<DateOnly> HoyAsync() => api.ConScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IFechaOperativa>().Hoy()));

    private async Task<RutaPrueba> RutaDetalleAsync(Guid? choferId = null)
    {
        var admin = await api.ClienteAsync();
        var codigo = "P" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return await admin.PostOkAsync<RutaPrueba>("/api/v1/logistica/rutas", new
        {
            codigo, nombre = $"Ruta {codigo}", canalId = await admin.CanalAsync("DET"),
            vendedorId = choferId ?? await admin.CrearChoferAsync(), activo = true,
        });
    }

    private async Task<(HttpClient Cliente, Guid DispositivoId)> SesionAppAsync(string usuario)
    {
        var solicitado = await (await api.ClienteAsync(usuario)).PostOkAsync<DispositivoPrueba>("/api/v1/seguridad/dispositivos/solicitar",
            new { nombre = $"Teléfono {usuario}", plataforma = "android", versionApp = "1.0.0" });
        await (await api.ClienteAsync()).PostOkAsync<DispositivoPrueba>($"/api/v1/seguridad/dispositivos/{solicitado.Id}/aprobar", new { });
        var sesion = await api.ClienteAnonimo().PostOkAsync<SesionPrueba>("/api/v1/seguridad/auth/login",
            new { usuario, contrasena = ApiFactory.Contrasena, dispositivoId = solicitado.Id });
        var cliente = api.ClienteAnonimo();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion.AccessToken);
        return (cliente, solicitado.Id);
    }

    private sealed record EntregaPrueba(Guid Id, string Folio, string Tipo);

    private sealed record PaginaEntregas(IReadOnlyList<EntregaPrueba> Items);

    private static object CapturarPedido(Guid opId, Guid pedidoId, Guid clienteId, DateOnly fechaEntrega, Guid productoId, Guid unidadId,
        decimal cantidad) => new
    {
        opId,
        secuencia = 1,
        tipo = "CapturarPedido",
        creadaEn = DateTimeOffset.UtcNow,
        datos = new
        {
            pedidoId, clienteId, fechaEntrega, ordenCompraCliente = (string?)null, esRuta = true, notas = (string?)null, gps = (object?)null,
            partidas = new[] { new { productoId, unidadId, cantidad, tipo = "Venta", notas = (string?)null } },
        },
    };

    private static Task<RespuestaPush> PushAsync(HttpClient app, Guid dispositivo, object operacion) =>
        app.PostOkAsync<RespuestaPush>("/api/v1/sync/push",
            new { dispositivoId = dispositivo, loteId = Id.Nuevo(), versionApp = "1.0.0", operaciones = new[] { operacion } });

    // ───────────── Reglas ─────────────

    [Fact]
    [Trait("Regla", "RN-VEN-15")]
    public async Task FechaEntrega_AnteriorAHoySeRechaza_HoyYFuturasSeAceptan_YCorregirUnPedidoViejoNoExigeCambiarla()
    {
        var admin = await api.ClienteAsync();
        var e = await EscenarioAsync();
        var hoy = await HoyAsync();

        await (await admin.PostJsonAsync("/api/v1/ventas/pedidos", Pedido(e.ClienteId, hoy.AddDays(-1), Partida(e, 1))))
            .DebeFallarAsync(HttpStatusCode.BadRequest, ErroresVentas.FechaEntregaPasada);
        (await admin.PostOkAsync<PedidoRespuesta>("/api/v1/ventas/pedidos", Pedido(e.ClienteId, hoy, Partida(e, 1)))).FechaEntrega.ShouldBe(hoy);
        var futuro = await admin.PostOkAsync<PedidoRespuesta>("/api/v1/ventas/pedidos", Pedido(e.ClienteId, hoy.AddDays(60), Partida(e, 1)));

        // Un pedido que quedó de ayer se corrige en cantidades sin tocar su fecha.
        await api.ConScopeAsync(sp => sp.GetRequiredService<VentasDbContext>().Pedidos.Where(p => p.Id == futuro.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.FechaEntrega, hoy.AddDays(-1))));
        var corregido = await admin.PutOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{futuro.Id}",
            Pedido(e.ClienteId, hoy.AddDays(-1), Partida(e, 4)));
        corregido.Subtotal.ShouldBe(40m);
    }

    [Fact]
    [Trait("Regla", "RN-VEN-16")]
    public async Task PedidoDeRuta_UnoPorRutaYFecha_PedidoPorClientePermiteVarios()
    {
        var admin = await api.ClienteAsync();
        var ruta = await RutaDetalleAsync();
        var salsa = await admin.ProductoAsync("RV-SALSA");
        var manana = (await HoyAsync()).AddDays(1);
        object PedidoRuta() => new
        {
            clienteId = ruta.ClienteGenericoId, fechaEntrega = manana,
            partidas = new[] { new { productoId = salsa.Id, unidadId = salsa.UnidadBaseId, cantidad = 5 } },
        };

        var primero = await admin.PostOkAsync<PedidoRespuesta>("/api/v1/ventas/pedidos", PedidoRuta());
        primero.EsRuta.ShouldBeTrue();
        await (await admin.PostJsonAsync("/api/v1/ventas/pedidos", PedidoRuta())).DebeFallarAsync(HttpStatusCode.Conflict, ErroresVentas.PedidoRutaDuplicado);
        (await admin.PutOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{primero.Id}", PedidoRuta())).EsRuta.ShouldBeTrue();

        // Cancelado ya no cuenta.
        await admin.PostOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{primero.Id}/cancelar");
        (await admin.PostOkAsync<PedidoRespuesta>("/api/v1/ventas/pedidos", PedidoRuta())).EsRuta.ShouldBeTrue();

        var e = await EscenarioAsync("AUTO");
        var uno = await admin.PostOkAsync<PedidoRespuesta>("/api/v1/ventas/pedidos", Pedido(e.ClienteId, manana, Partida(e, 1)));
        var dos = await admin.PostOkAsync<PedidoRespuesta>("/api/v1/ventas/pedidos", Pedido(e.ClienteId, manana, Partida(e, 2)));
        uno.EsRuta.ShouldBeFalse();
        dos.Id.ShouldNotBe(uno.Id);
    }

    [Fact]
    [Trait("Regla", "RN-VEN-17")]
    [Trait("Regla", "RN-VEN-08")]
    public async Task CambioFisico_APrecioCero_NoSumaNiCuentaParaElEstado_NiSeEntregaConVenta()
    {
        var admin = await api.ClienteAsync();
        var e = await EscenarioAsync();
        var pedido = await admin.PostOkAsync<PedidoRespuesta>("/api/v1/ventas/pedidos",
            Pedido(e.ClienteId, await HoyAsync(), Partida(e, 3), Partida(e, 2, "CambioFisico")));
        pedido.Subtotal.ShouldBe(30m);
        var cambio = pedido.Partidas.Single(p => p.Tipo == "CambioFisico");
        cambio.PrecioUnitario.ShouldBe(0m);
        cambio.Importe.ShouldBe(0m);
        await admin.PostOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{pedido.Id}/confirmar");

        Task<TG.SharedKernel.Resultados.Result<VentaRegistrada>> Entregar(Guid partidaId, decimal cantidad) => api.ConScopeAsync(async sp =>
            await sp.GetRequiredService<IVentasService>().EntregarPedidoAsync(new EntregarPedidoComando(OrigenVenta.Web, pedido.Id, e.AlmacenId,
                Guid.Empty, await HoyAsync(), FormaPago.Efectivo, [new PartidaEntregaComando(partidaId, cantidad)]), CancellationToken.None));

        (await Entregar(cambio.Id, 2)).EsExito.ShouldBeFalse();
        var entrega = await Entregar(pedido.Partidas.Single(p => p.Tipo == "Venta").Id, 3);
        entrega.EsExito.ShouldBeTrue();
        entrega.Valor.Total.ShouldBe(30m);
        (await admin.GetOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{pedido.Id}")).Estado.ShouldBe("Entregado");

        // El detalle del pedido lista sus entregas filtrando las ventas por pedido.
        var entregas = await admin.GetOkAsync<PaginaEntregas>($"/api/v1/ventas/ventas?pedidoId={pedido.Id}");
        entregas.Items.Select(v => v.Folio).ShouldBe([entrega.Valor.Folio]);
        entregas.Items.Single().Tipo.ShouldBe("EntregaPedido");
    }

    [Fact]
    [Trait("Regla", "RN-VEN-18")]
    [Trait("Regla", "RN-SYN-10")]
    [Trait("Regla", "RN-SEG-06")]
    public async Task PedidoDeLaApp_EntraCapturado_SeCorrigeEnOficinaConBitacora_YAlConfirmarQuedaFijo()
    {
        var admin = await api.ClienteAsync();
        var (choferId, usuario) = await admin.CrearUsuarioChoferAsync();
        var ruta = await RutaDetalleAsync(choferId);
        var (app, dispositivo) = await SesionAppAsync(usuario);
        var salsa = await admin.ProductoAsync("RV-SALSA");
        var manana = (await HoyAsync()).AddDays(1);
        var opId = Id.Nuevo();
        var pedidoId = Id.Nuevo();

        var push = await PushAsync(app, dispositivo, CapturarPedido(opId, pedidoId, ruta.ClienteGenericoId!.Value, manana, salsa.Id, salsa.UnidadBaseId, 20));
        var resultado = push.Resultados.Single();
        resultado.Estado.ShouldBe("Aplicada", resultado.Codigo);
        resultado.IdsServidor!["pedidoId"].ShouldBe(pedidoId);

        // Reenvío: no duplica.
        (await PushAsync(app, dispositivo, CapturarPedido(opId, pedidoId, ruta.ClienteGenericoId.Value, manana, salsa.Id, salsa.UnidadBaseId, 20)))
            .Resultados.Single().Estado.ShouldBe("Duplicada");
        (await api.ConScopeAsync(sp => sp.GetRequiredService<VentasDbContext>().Pedidos.CountAsync(p => p.Id == pedidoId))).ShouldBe(1);

        var capturado = await admin.GetOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{pedidoId}");
        capturado.Estado.ShouldBe("Capturado");
        capturado.Origen.ShouldBe("App");
        capturado.EsRuta.ShouldBeTrue();
        var partida = capturado.Partidas.ShouldHaveSingleItem();
        partida.CantidadSolicitada.ShouldBe(20m);

        var corregido = await admin.PutOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{pedidoId}", new
        {
            clienteId = ruta.ClienteGenericoId, fechaEntrega = manana,
            partidas = new[] { new { id = partida.Id, productoId = salsa.Id, unidadId = salsa.UnidadBaseId, cantidad = 15 } },
        });
        var final = corregido.Partidas.ShouldHaveSingleItem();
        final.Id.ShouldBe(partida.Id);
        final.Cantidad.ShouldBe(15m);
        final.CantidadSolicitada.ShouldBe(20m);
        var bitacora = await admin.GetOkAsync<Bitacora>($"/api/v1/seguridad/bitacora?tabla=pedidos_partidas&entidadId={partida.Id}");
        var cambio = bitacora.Items.First(r => r.Accion == "Cambio").Cambios.GetProperty("cantidad");
        cambio[0].GetDecimal().ShouldBe(20m);
        cambio[1].GetDecimal().ShouldBe(15m);

        var confirmado = await admin.PostOkAsync<PedidoRespuesta>($"/api/v1/ventas/pedidos/{pedidoId}/confirmar");
        confirmado.ConfirmadoPor.ShouldNotBeNull();
        confirmado.ConfirmadoEn.ShouldNotBeNull();
        await (await admin.PutJsonAsync($"/api/v1/ventas/pedidos/{pedidoId}", new
        {
            clienteId = ruta.ClienteGenericoId, fechaEntrega = manana,
            partidas = new[] { new { id = partida.Id, productoId = salsa.Id, unidadId = salsa.UnidadBaseId, cantidad = 10 } },
        })).DebeFallarAsync(HttpStatusCode.Conflict, ErroresVentas.EstadoPedido);

        // Otro pedido de la ruta para la misma fecha (otro op_id): VEN-020.
        var otro = await PushAsync(app, dispositivo, CapturarPedido(Id.Nuevo(), Id.Nuevo(), ruta.ClienteGenericoId.Value, manana, salsa.Id, salsa.UnidadBaseId, 3));
        otro.Resultados.Single().Codigo.ShouldBe(ErroresVentas.PedidoRutaDuplicado);
    }

    [Fact]
    [Trait("Regla", "RN-SYN-10")]
    public async Task CapturarPedido_ClienteFueraDeLaRuta_SYN007_YSinPermiso_SYN008()
    {
        var admin = await api.ClienteAsync();
        var (choferId, usuario) = await admin.CrearUsuarioChoferAsync();
        await RutaDetalleAsync(choferId);
        var (app, dispositivo) = await SesionAppAsync(usuario);
        var e = await EscenarioAsync();
        var manana = (await HoyAsync()).AddDays(1);

        var fuera = await PushAsync(app, dispositivo, CapturarPedido(Id.Nuevo(), Id.Nuevo(), e.ClienteId, manana, e.ProductoId, e.UnidadId, 1));
        fuera.Resultados.Single().Codigo.ShouldBe("SYN-007");

        // Usuario que sincroniza pero no captura pedidos.
        var rol = DatosPrueba.Unico("SOLOSYNC");
        await admin.PostOkAsync<IdRespuesta>("/api/v1/seguridad/roles", new { nombre = rol, descripcion = "Prueba", permisos = new[] { "sync.operar" } });
        var nombre = DatosPrueba.Unico("sinped").ToLowerInvariant();
        await admin.PostOkAsync<IdRespuesta>("/api/v1/seguridad/usuarios", new
        {
            usuario = nombre, nombreCompleto = "Sin pedidos", email = $"{nombre}@tortillas.local", activo = true, roles = new[] { rol },
            contrasena = ApiFactory.Contrasena,
        });
        var (appSinPermiso, dispositivoSinPermiso) = await SesionAppAsync(nombre);
        var sinPermiso = await PushAsync(appSinPermiso, dispositivoSinPermiso,
            CapturarPedido(Id.Nuevo(), Id.Nuevo(), e.ClienteId, manana, e.ProductoId, e.UnidadId, 1));
        sinPermiso.Resultados.Single().Codigo.ShouldBe("SYN-008");
    }
}
