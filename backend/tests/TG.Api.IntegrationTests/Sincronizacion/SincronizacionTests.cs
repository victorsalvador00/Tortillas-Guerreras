using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Inventario.Infraestructura;
using TG.Inventario.Publico;
using TG.SharedKernel.Dominio;
using TG.Ventas.Infraestructura;
using TG.Ventas.Publico;

namespace TG.Api.IntegrationTests.Sincronizacion;

public sealed class SincronizacionTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record DispositivoRespuesta(Guid Id, string? Codigo);

    private sealed record Resultado(Guid OpId, string Estado, Dictionary<string, Guid>? IdsServidor, List<string>? Incidencias,
        string? Codigo, string? Mensaje);

    private sealed record RespuestaPush(List<Resultado> Resultados);

    private sealed record EstadoDispositivo(Guid DispositivoId, string Codigo, string? RutaCodigo, int UltimoConsecutivo);

    private sealed record ClienteRuta(Guid Id, string Nombre, bool EsGenerico, Guid ListaPrecioId);

    private sealed record RutaPull(Guid Id, string Codigo, Guid AlmacenId);

    private sealed record ProductoPull(Guid Id, string Sku, Guid UnidadBaseId);

    private sealed record PrecioPull(Guid ListaPrecioId, Guid ProductoId, Guid UnidadId, decimal PrecioUnitario);

    private sealed record PedidoPull(Guid Id, string Folio, Guid? EnvioId, string? EnvioFolio);

    private sealed record CargaPull(Guid Id, string Folio);

    private sealed record Cambios(List<ProductoPull> Productos, List<ClienteRuta> Clientes, List<PrecioPull> Precios,
        List<JsonObject> RutasClientes, List<PedidoPull> Pedidos, List<JsonObject>? CambiosPendientes = null);

    private sealed record RespuestaPull(long Cursor, bool HayMas, RutaPull? Ruta, Cambios Cambios);

    private sealed record Sesion(HttpClient Cliente, Guid DispositivoId, string Codigo);

    private static readonly SemaphoreSlim Candado = new(1, 1);
    private static readonly Dictionary<string, Sesion> Sesiones = new();

    /// <summary>Dispositivo aprobado para el vendedor y cliente HTTP con el token ligado a él.</summary>
    private async Task<Sesion> SesionAppAsync(string usuario = "vendedor1")
    {
        await Candado.WaitAsync();
        try
        {
            var clave = $"{api.CadenaConexion}|{usuario}";
            if (Sesiones.TryGetValue(clave, out var existente))
            {
                return existente;
            }

            var vendedor = await api.ClienteAsync(usuario);
            var solicitado = await vendedor.PostOkAsync<DispositivoRespuesta>("/api/v1/seguridad/dispositivos/solicitar",
                new { nombre = $"Teléfono {usuario}", plataforma = "android", versionApp = "1.0.0" });
            var admin = await api.ClienteAsync();
            var aprobado = await admin.PostOkAsync<DispositivoRespuesta>($"/api/v1/seguridad/dispositivos/{solicitado.Id}/aprobar", new { });
            var sesion = await api.ClienteAnonimo().PostOkAsync<SesionPrueba>("/api/v1/seguridad/auth/login",
                new { usuario, contrasena = ApiFactory.Contrasena, dispositivoId = solicitado.Id });
            var cliente = api.ClienteAnonimo();
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion.AccessToken);
            var nueva = new Sesion(cliente, solicitado.Id, aprobado.Codigo!);
            Sesiones[clave] = nueva;
            return nueva;
        }
        finally
        {
            Candado.Release();
        }
    }

    private async Task<(RespuestaPull Pull, ClienteRuta Cliente, ProductoPull Producto, PrecioPull Precio)> DatosRutaAsync(Sesion s)
    {
        var pull = await s.Cliente.GetOkAsync<RespuestaPull>("/api/v1/sync/pull?desde=0");
        var cliente = pull.Cambios.Clientes.First(c => !c.EsGenerico);
        var producto = pull.Cambios.Productos.Single(p => p.Sku == "RV-SALSA");
        var precio = pull.Cambios.Precios.First(p => p.ListaPrecioId == cliente.ListaPrecioId && p.ProductoId == producto.Id);
        return (pull, cliente, producto, precio);
    }

    private static object Venta(Guid opId, long secuencia, string folio, int consecutivo, Guid clienteId, PrecioPull precio,
        decimal cantidad, DateTimeOffset? creadaEn = null) => new
    {
        opId,
        secuencia,
        tipo = "RegistrarVenta",
        creadaEn = creadaEn ?? DateTimeOffset.UtcNow,
        datos = new
        {
            ventaId = Id.Nuevo(),
            folio,
            consecutivo,
            clienteId,
            condicionPago = "Contado",
            formaPago = "Efectivo",
            partidas = new[] { new { productoId = precio.ProductoId, unidadId = precio.UnidadId, cantidad, precioUnitario = precio.PrecioUnitario } },
            totalApp = Math.Round(cantidad * precio.PrecioUnitario * 1.16m, 2),
            gps = (object?)null,
        },
    };

    private static string Folio(Sesion s, int consecutivo) => $"R01-{s.Codigo}-{consecutivo:D6}";

    [Fact]
    [Trait("Regla", "RN-LOG-11")]
    public async Task Pull_PedidoIndicaLaCargaEnQueVa_YAlCancelarlaQuedaSinCarga()
    {
        var s = await SesionAppAsync();
        var (pull, cliente, producto, precio) = await DatosRutaAsync(s);
        var admin = await api.ClienteAsync();
        var pedido = await admin.PostOkAsync<PedidoPull>("/api/v1/ventas/pedidos", new
        {
            clienteId = cliente.Id, partidas = new[] { new { productoId = producto.Id, unidadId = precio.UnidadId, cantidad = 2 } },
        });
        await admin.PostOkAsync<PedidoPull>($"/api/v1/ventas/pedidos/{pedido.Id}/confirmar");
        await admin.PostOkAsync<PedidoPull>($"/api/v1/ventas/pedidos/{pedido.Id}/asignar", new { rutaId = pull.Ruta!.Id });
        var carga = await admin.PostOkAsync<CargaPull>("/api/v1/logistica/envios", new
        {
            rutaId = pull.Ruta.Id, tipo = "Recarga", pedidoIds = new[] { pedido.Id },
            partidas = new[] { new { productoId = producto.Id, cantidad = 2m } },
        });

        var conCarga = await s.Cliente.GetOkAsync<RespuestaPull>($"/api/v1/sync/pull?desde={pull.Cursor}&rutaId={pull.Ruta.Id}");
        var enCarga = conCarga.Cambios.Pedidos.Single(p => p.Id == pedido.Id);
        enCarga.EnvioId.ShouldBe(carga.Id);
        enCarga.EnvioFolio.ShouldStartWith("ENV-");

        await admin.PostOkAsync<CargaPull>($"/api/v1/logistica/envios/{carga.Id}/cancelar");
        var sinCarga = await s.Cliente.GetOkAsync<RespuestaPull>($"/api/v1/sync/pull?desde={conCarga.Cursor}&rutaId={pull.Ruta.Id}");
        var liberado = sinCarga.Cambios.Pedidos.Single(p => p.Id == pedido.Id);
        liberado.EnvioId.ShouldBeNull();
        liberado.EnvioFolio.ShouldBeNull();
    }

    [Fact]
    public async Task Pull_DevuelveAlcanceDeLaRuta_YConCursorNoRepiteCambios()
    {
        var s = await SesionAppAsync();
        var (pull, _, _, _) = await DatosRutaAsync(s);

        pull.Ruta!.Codigo.ShouldBe("R01");
        pull.Cambios.Productos.ShouldAllBe(p => p.Sku.StartsWith("PT-") || p.Sku.StartsWith("RV-"));
        pull.Cambios.Clientes.ShouldContain(c => c.EsGenerico);
        pull.Cambios.RutasClientes.Count.ShouldBe(6);
        pull.Cursor.ShouldBeGreaterThan(0);

        var siguiente = await s.Cliente.GetOkAsync<RespuestaPull>($"/api/v1/sync/pull?desde={pull.Cursor}&rutaId={pull.Ruta.Id}");
        siguiente.Cambios.Productos.ShouldBeEmpty();
        siguiente.Cambios.Clientes.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Regla", "RN-SYN-02")]
    [Trait("Regla", "RN-SYN-03")]
    public async Task Push_ReenvioDelMismoLote_NoDuplicaVentaNiMovimiento()
    {
        var s = await SesionAppAsync();
        var (_, cliente, _, precio) = await DatosRutaAsync(s);
        var opId = Id.Nuevo();
        var lote = new
        {
            dispositivoId = s.DispositivoId,
            loteId = Id.Nuevo(),
            versionApp = "1.0.0",
            operaciones = new[] { Venta(opId, 1, Folio(s, 101), 101, cliente.Id, precio, 2) },
        };

        var primero = await s.Cliente.PostOkAsync<RespuestaPush>("/api/v1/sync/push", lote);
        primero.Resultados.Single().Estado.ShouldBe("Aplicada");
        var ventaId = primero.Resultados.Single().IdsServidor!["ventaId"];

        var segundo = await s.Cliente.PostOkAsync<RespuestaPush>("/api/v1/sync/push", lote);
        segundo.Resultados.Single().Estado.ShouldBe("Duplicada");
        segundo.Resultados.Single().IdsServidor!["ventaId"].ShouldBe(ventaId);

        var movimientos = await api.ConScopeAsync(sp => sp.GetRequiredService<InventarioDbContext>().Movimientos
            .CountAsync(m => m.DocumentoTipo == DocumentoInventario.Venta && m.DocumentoId == ventaId));
        movimientos.ShouldBe(1);
    }

    [Fact]
    [Trait("Regla", "RN-SYN-01")]
    [Trait("Regla", "RN-SYN-04")]
    public async Task Push_LoteConRechazosMezclados_AplicaLasDemasEnOrden()
    {
        var s = await SesionAppAsync();
        var (_, cliente, _, precio) = await DatosRutaAsync(s);
        var buena1 = Id.Nuevo();
        var mala = Id.Nuevo();
        var buena2 = Id.Nuevo();

        var respuesta = await s.Cliente.PostOkAsync<RespuestaPush>("/api/v1/sync/push", new
        {
            dispositivoId = s.DispositivoId,
            loteId = Id.Nuevo(),
            versionApp = "1.0.0",
            // Desordenadas a propósito: se procesan por secuencia.
            operaciones = new[]
            {
                Venta(buena2, 3, Folio(s, 203), 203, cliente.Id, precio, 1),
                Venta(mala, 2, Folio(s, 202), 202, Id.Nuevo(), precio, 1),
                Venta(buena1, 1, Folio(s, 201), 201, cliente.Id, precio, 1),
            },
        });

        respuesta.Resultados.Select(r => r.OpId).ShouldBe([buena1, mala, buena2]);
        respuesta.Resultados.Single(r => r.OpId == buena1).Estado.ShouldBe("Aplicada");
        respuesta.Resultados.Single(r => r.OpId == buena2).Estado.ShouldBe("Aplicada");
        var rechazada = respuesta.Resultados.Single(r => r.OpId == mala);
        rechazada.Estado.ShouldBe("Rechazada");
        rechazada.Codigo.ShouldNotBeNullOrEmpty();

        // El reenvío de la rechazada devuelve el mismo rechazo (no se reintenta).
        var reenvio = await s.Cliente.PostOkAsync<RespuestaPush>("/api/v1/sync/push", new
        {
            dispositivoId = s.DispositivoId,
            loteId = Id.Nuevo(),
            versionApp = "1.0.0",
            operaciones = new[] { Venta(mala, 2, Folio(s, 202), 202, Id.Nuevo(), precio, 1) },
        });
        reenvio.Resultados.Single().Estado.ShouldBe("Rechazada");
    }

    [Fact]
    [Trait("Regla", "RN-SYN-05")]
    public async Task Push_SinDispositivoOConDispositivoAjeno_Recibe403()
    {
        var sinDispositivo = await api.ClienteAsync("vendedor3");
        var respuesta = await sinDispositivo.PostJsonAsync("/api/v1/sync/push",
            new { dispositivoId = Id.Nuevo(), loteId = Id.Nuevo(), versionApp = "1", operaciones = Array.Empty<object>() });
        await respuesta.DebeFallarAsync(HttpStatusCode.Forbidden, "SYN-001");

        var s = await SesionAppAsync();
        var ajeno = await s.Cliente.PostJsonAsync("/api/v1/sync/push",
            new { dispositivoId = Id.Nuevo(), loteId = Id.Nuevo(), versionApp = "1", operaciones = Array.Empty<object>() });
        await ajeno.DebeFallarAsync(HttpStatusCode.Forbidden, "SYN-001");

        var compras = await api.ClienteAsync("compras");
        (await compras.GetAsync("/api/v1/sync/pull")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Regla", "RN-SYN-06")]
    public async Task Push_FechaOperativaSaleDeCreadaEnEnHoraDeMexico()
    {
        var s = await SesionAppAsync();
        var (_, cliente, _, precio) = await DatosRutaAsync(s);
        // 23:59 del 2 de noviembre en México = 05:59 UTC del 3.
        var creadaEn = new DateTimeOffset(2026, 11, 2, 23, 59, 0, TimeSpan.FromHours(-6));
        var respuesta = await s.Cliente.PostOkAsync<RespuestaPush>("/api/v1/sync/push", new
        {
            dispositivoId = s.DispositivoId,
            loteId = Id.Nuevo(),
            versionApp = "1.0.0",
            operaciones = new[] { Venta(Id.Nuevo(), 1, Folio(s, 301), 301, cliente.Id, precio, 1, creadaEn) },
        });

        var ventaId = respuesta.Resultados.Single().IdsServidor!["ventaId"];
        var admin = await api.ClienteAsync();
        var venta = await admin.GetOkAsync<JsonObject>($"/api/v1/ventas/ventas/{ventaId}");
        venta["fechaOperativa"]!.GetValue<string>().ShouldBe("2026-11-02");
    }

    [Fact]
    [Trait("Regla", "RN-SYN-07")]
    public async Task Push_MasDe200Operaciones_SeRechaza()
    {
        var s = await SesionAppAsync();
        var operaciones = Enumerable.Range(1, 201).Select(i => new
        {
            opId = Id.Nuevo(), secuencia = i, tipo = "ReportarEstado", creadaEn = DateTimeOffset.UtcNow,
            datos = new { pendientes = 0, bateria = (decimal?)null, versionApp = "1", ultimoConsecutivo = 0 },
        }).ToArray();
        var respuesta = await s.Cliente.PostJsonAsync("/api/v1/sync/push",
            new { dispositivoId = s.DispositivoId, loteId = Id.Nuevo(), versionApp = "1", operaciones });
        await respuesta.DebeFallarAsync(HttpStatusCode.BadRequest, "SYN-003");
    }

    [Fact]
    [Trait("Regla", "RN-SYN-08")]
    public async Task Dispositivo_DevuelveElUltimoConsecutivoRecibido()
    {
        var s = await SesionAppAsync("vendedor2");
        var (_, cliente, _, precio) = await DatosRutaAsync(s);
        await s.Cliente.PostOkAsync<RespuestaPush>("/api/v1/sync/push", new
        {
            dispositivoId = s.DispositivoId,
            loteId = Id.Nuevo(),
            versionApp = "1.2.0",
            operaciones = new object[]
            {
                Venta(Id.Nuevo(), 1, $"R02-{s.Codigo}-000457", 457, cliente.Id, precio, 1),
                new
                {
                    opId = Id.Nuevo(), secuencia = 2, tipo = "ReportarEstado", creadaEn = DateTimeOffset.UtcNow,
                    datos = new { pendientes = 3, bateria = 0.8m, versionApp = "1.2.0", ultimoConsecutivo = 457 },
                },
            },
        });

        var estado = await s.Cliente.GetOkAsync<EstadoDispositivo>("/api/v1/sync/dispositivo");
        estado.UltimoConsecutivo.ShouldBe(457);
        estado.RutaCodigo.ShouldBe("R02");

        // Los pendientes reportados alimentan RN-LOG-09.
        var admin = await api.ClienteAsync();
        var dispositivos = await admin.GetOkAsync<List<JsonObject>>("/api/v1/seguridad/dispositivos");
        dispositivos.Single(d => d["id"]!.GetValue<Guid>() == s.DispositivoId)["operacionesPendientes"]!.GetValue<int>().ShouldBe(3);
    }

    [Fact]
    public async Task Push_RetornoYCobro_SeAplican()
    {
        var s = await SesionAppAsync();
        var (_, cliente, producto, _) = await DatosRutaAsync(s);
        var respuesta = await s.Cliente.PostOkAsync<RespuestaPush>("/api/v1/sync/push", new
        {
            dispositivoId = s.DispositivoId,
            loteId = Id.Nuevo(),
            versionApp = "1.0.0",
            operaciones = new object[]
            {
                new
                {
                    opId = Id.Nuevo(), secuencia = 1, tipo = "RegistrarRetorno", creadaEn = DateTimeOffset.UtcNow,
                    datos = new
                    {
                        retornoId = Id.Nuevo(), folio = Folio(s, 401), consecutivo = 401, tipo = "DevolucionCliente",
                        clienteId = cliente.Id, ventaId = (Guid?)null, pedidoId = (Guid?)null,
                        partidas = new[] { new { productoId = producto.Id, cantidad = 1m, condicion = "Vendible", motivoMermaId = (Guid?)null } },
                        gps = (object?)null,
                    },
                },
                new
                {
                    opId = Id.Nuevo(), secuencia = 2, tipo = "ProponerUbicacionCliente", creadaEn = DateTimeOffset.UtcNow,
                    datos = new { clienteId = cliente.Id, gps = new { lat = 19.18m, lng = -96.14m, precisionM = 10m, tomadoEn = DateTimeOffset.UtcNow } },
                },
            },
        });

        respuesta.Resultados.ShouldAllBe(r => r.Estado == "Aplicada");
        respuesta.Resultados[0].IdsServidor!.ShouldContainKey("retornoId");
    }

    [Fact]
    [Trait("Regla", "RN-SYN-11")]
    [Trait("Regla", "RN-LOG-16")]
    public async Task Push_CambioFisico_DejaPendiente_QueBajaEnElPull_YElReenvioNoLoDuplica()
    {
        var s = await SesionAppAsync();
        var (_, cliente, producto, _) = await DatosRutaAsync(s);
        var operacion = new
        {
            opId = Id.Nuevo(), secuencia = 1, tipo = "RegistrarRetorno", creadaEn = DateTimeOffset.UtcNow,
            datos = new
            {
                retornoId = Id.Nuevo(), folio = Folio(s, 451), consecutivo = 451, tipo = "CambioFisico",
                clienteId = cliente.Id, ventaId = (Guid?)null, pedidoId = (Guid?)null,
                partidas = new object[]
                {
                    new { productoId = producto.Id, cantidad = 3m, condicion = "Vendible", motivoMermaId = (Guid?)null, sentido = "Retirado", dejarPendiente = true },
                    new { productoId = producto.Id, cantidad = 1m, condicion = "Vendible", motivoMermaId = (Guid?)null, sentido = "Entregado" },
                },
                gps = (object?)null,
            },
        };
        object Lote() => new { dispositivoId = s.DispositivoId, loteId = Id.Nuevo(), versionApp = "1.0.0", operaciones = new object[] { operacion } };

        (await s.Cliente.PostOkAsync<RespuestaPush>("/api/v1/sync/push", Lote())).Resultados.ShouldHaveSingleItem().Estado.ShouldBe("Aplicada");
        await s.Cliente.PostOkAsync<RespuestaPush>("/api/v1/sync/push", Lote());

        var pull = await s.Cliente.GetOkAsync<RespuestaPull>("/api/v1/sync/pull?desde=0");
        var pendiente = pull.Cambios.CambiosPendientes!.ShouldHaveSingleItem();
        pendiente["clienteId"]!.GetValue<Guid>().ShouldBe(cliente.Id);
        pendiente["cantidadBase"]!.GetValue<decimal>().ShouldBe(2m);
    }
    [Fact]
    [Trait("Regla", "RN-SYN-10")]
    [Trait("Criterio", "06-v0.2-modo-avion")]
    public async Task Push_TresPedidosYUnCambioFisicoCapturadosSinRed_QuedanCapturadosDeOrigenApp_YUnPendiente()
    {
        var s = await SesionAppAsync();
        var (_, cliente, producto, _) = await DatosRutaAsync(s);
        var entrega = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var pedidoIds = Enumerable.Range(0, 3).Select(_ => Id.Nuevo()).ToList();
        var operaciones = pedidoIds.Select((pedidoId, i) => (object)new
        {
            opId = Id.Nuevo(), secuencia = i + 1, tipo = "CapturarPedido", creadaEn = DateTimeOffset.UtcNow,
            datos = new
            {
                pedidoId, clienteId = cliente.Id, fechaEntrega = entrega, ordenCompraCliente = (string?)null, esRuta = false,
                notas = (string?)null, gps = (object?)null,
                partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, cantidad = 2m + i, tipo = "Venta", notas = (string?)null } },
            },
        }).Append(new
        {
            opId = Id.Nuevo(), secuencia = 4, tipo = "RegistrarRetorno", creadaEn = DateTimeOffset.UtcNow,
            datos = new
            {
                retornoId = Id.Nuevo(), folio = Folio(s, 461), consecutivo = 461, tipo = "CambioFisico",
                clienteId = cliente.Id, ventaId = (Guid?)null, pedidoId = (Guid?)null,
                partidas = new object[]
                {
                    new { productoId = producto.Id, cantidad = 2m, condicion = "NoVendible", motivoMermaId = (Guid?)null, sentido = "Retirado", dejarPendiente = true },
                },
                gps = (object?)null,
            },
        }).ToArray();

        var push = await s.Cliente.PostOkAsync<RespuestaPush>("/api/v1/sync/push",
            new { dispositivoId = s.DispositivoId, loteId = Id.Nuevo(), versionApp = "1.0.0", operaciones });
        push.Resultados.Count.ShouldBe(4);
        push.Resultados.ShouldAllBe(r => r.Estado == "Aplicada");

        var pedidos = await api.ConScopeAsync(sp => sp.GetRequiredService<VentasDbContext>().Pedidos.AsNoTracking()
            .Where(p => pedidoIds.Contains(p.Id)).ToListAsync());
        pedidos.Count.ShouldBe(3);
        pedidos.ShouldAllBe(p => p.Estado == EstadoPedido.Capturado && p.Origen == OrigenVenta.App);
        var pull = await s.Cliente.GetOkAsync<RespuestaPull>("/api/v1/sync/pull?desde=0");
        pull.Cambios.CambiosPendientes!.ShouldContain(p => p["clienteId"]!.GetValue<Guid>() == cliente.Id && p["cantidadBase"]!.GetValue<decimal>() == 2m);
    }
}
