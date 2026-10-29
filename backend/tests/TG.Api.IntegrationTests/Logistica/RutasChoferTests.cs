using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Api.IntegrationTests.Logistica;

/// <summary>Rutas con canal, chofer y vehículo por carga (T-8.4). Base propia: una prueba baja y sube la migración.</summary>
public sealed class RutasChoferTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record RutaPrueba(Guid Id, string Codigo, Guid CanalId, string CanalNombre, Guid? VendedorId, Guid? VehiculoId,
        Guid? ClienteGenericoId);

    private sealed record EnvioPrueba(Guid Id, string Estado, Guid? ChoferId, string? ChoferNombre, Guid? VehiculoId, Guid? LiquidacionId);

    private sealed record LiquidacionPrueba(Guid Id, Guid? ChoferId, string? ChoferNombre);

    private sealed record VehiculoPrueba(Guid Id);

    private sealed record DispositivoPrueba(Guid Id, string? Codigo);

    private sealed record RutaPull(Guid Id, string Codigo);

    private sealed record ProductoPull(Guid Id, string Sku);

    private sealed record PrecioPull(Guid ListaPrecioId, Guid ProductoId, Guid UnidadId, decimal PrecioUnitario);

    private sealed record ClientePull(Guid Id, bool EsGenerico, Guid ListaPrecioId);

    private sealed record ConsumoPull(Guid ClienteId, Guid ProductoId, decimal PromedioBase);

    private sealed record CanalPull(Guid Id, string Clave, string PedidoPor, string FormatoLiquidacion);

    private sealed record CambiosPull(List<ProductoPull> Productos, List<ClientePull> Clientes, List<PrecioPull> Precios,
        List<ConsumoPull>? ConsumosClientes = null, List<CanalPull>? Canales = null);

    private sealed record RespuestaPull(long Cursor, RutaPull? Ruta, CambiosPull Cambios, bool RutaCambiada);

    private sealed record Resultado(string Estado, Dictionary<string, Guid>? IdsServidor, string? Codigo);

    private sealed record RespuestaPush(List<Resultado> Resultados);

    // ───────────── Ayudas ─────────────

    private async Task<RutaPrueba> CrearRutaAsync(string canal = "DET", Guid? choferId = null, Guid? vehiculoId = null, bool conChofer = true)
    {
        var admin = await api.ClienteAsync();
        var codigo = "C" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return await admin.PostOkAsync<RutaPrueba>("/api/v1/logistica/rutas", new
        {
            codigo, nombre = $"Ruta {codigo}", canalId = await admin.CanalAsync(canal),
            vendedorId = conChofer ? choferId ?? await admin.CrearChoferAsync() : (Guid?)null, vehiculoId, activo = true,
        });
    }

    private async Task<object> CargaAsync(RutaPrueba ruta, Guid? choferId = null, Guid? vehiculoId = null, DateOnly? fecha = null)
    {
        var tortilla = await (await api.ClienteAsync()).ProductoAsync("PT-TORT-KG");
        return new
        {
            rutaId = ruta.Id, tipo = "CargaInicial", choferId, vehiculoId, fechaOperativa = fecha,
            partidas = new[] { new { productoId = tortilla.Id, cantidad = 1m } },
        };
    }

    private Task<DateOnly> HoyAsync() => api.ConScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IFechaOperativa>().Hoy()));

    private async Task<(HttpClient Cliente, string Codigo, Guid DispositivoId)> SesionAppAsync(string usuario)
    {
        var chofer = await api.ClienteAsync(usuario);
        var solicitado = await chofer.PostOkAsync<DispositivoPrueba>("/api/v1/seguridad/dispositivos/solicitar",
            new { nombre = $"Teléfono {usuario}", plataforma = "android", versionApp = "1.0.0" });
        var aprobado = await (await api.ClienteAsync()).PostOkAsync<DispositivoPrueba>($"/api/v1/seguridad/dispositivos/{solicitado.Id}/aprobar", new { });
        var sesion = await api.ClienteAnonimo().PostOkAsync<SesionPrueba>("/api/v1/seguridad/auth/login",
            new { usuario, contrasena = ApiFactory.Contrasena, dispositivoId = solicitado.Id });
        var cliente = api.ClienteAnonimo();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion.AccessToken);
        return (cliente, aprobado.Codigo!, solicitado.Id);
    }

    private static object Venta(string folio, Guid clienteId, PrecioPull precio, DateTimeOffset creadaEn) => new
    {
        opId = Id.Nuevo(),
        secuencia = 1,
        tipo = "RegistrarVenta",
        creadaEn,
        datos = new
        {
            ventaId = Id.Nuevo(), folio, consecutivo = 1, clienteId, condicionPago = "Contado", formaPago = "Efectivo",
            partidas = new[] { new { productoId = precio.ProductoId, unidadId = precio.UnidadId, cantidad = 1m, precioUnitario = precio.PrecioUnitario } },
            totalApp = Math.Round(precio.PrecioUnitario * 1.16m, 2),
            gps = (object?)null,
        },
    };

    // ───────────── RN-LOG-31 ─────────────

    [Fact]
    [Trait("Regla", "RN-LOG-31")]
    public async Task Ruta_GenericoConSuCanal_RechazaClientesDeOtroCanal_YCambiarCanalExigeClientesDelNuevo()
    {
        var admin = await api.ClienteAsync();
        var ruta = await CrearRutaAsync("INST");
        var clientes = await api.ConScopeAsync(sp => sp.GetRequiredService<IVentasService>().ListarClientesAsync(true, CancellationToken.None));
        clientes.Single(c => c.Id == ruta.ClienteGenericoId).CanalClave.ShouldBe("INST");

        var detalle = clientes.First(c => c.CanalClave == "DET" && !c.EsGenerico);
        var institucional = clientes.First(c => c.CanalClave == "INST" && !c.EsGenerico);
        var otroCanal = await admin.PutAsync($"/api/v1/logistica/rutas/{ruta.Id}/clientes", Contenido(new
        {
            clientes = new[] { new { clienteId = detalle.Id, ordenVisita = 1 } },
        }));
        await otroCanal.DebeFallarAsync(HttpStatusCode.BadRequest, ErroresLogistica.ClienteOtroCanal);

        await admin.PutOkAsync<List<object>>($"/api/v1/logistica/rutas/{ruta.Id}/clientes",
            new { clientes = new[] { new { clienteId = institucional.Id, ordenVisita = 1 } } });
        object Ruta(string canalId) => new { codigo = ruta.Codigo, nombre = "Ruta editada", canalId, vendedorId = ruta.VendedorId, activo = true };

        var cambio = await admin.PutAsync($"/api/v1/logistica/rutas/{ruta.Id}", Contenido(Ruta((await admin.CanalAsync("DET")).ToString())));
        await cambio.DebeFallarAsync(HttpStatusCode.BadRequest, ErroresLogistica.ClienteOtroCanal);

        // Sin clientes ajenos el cambio procede y el genérico sigue a la ruta.
        await admin.PutOkAsync<List<object>>($"/api/v1/logistica/rutas/{ruta.Id}/clientes", new { clientes = Array.Empty<object>() });
        var editada = await admin.PutOkAsync<RutaPrueba>($"/api/v1/logistica/rutas/{ruta.Id}", Ruta((await admin.CanalAsync("DET")).ToString()));
        editada.CanalNombre.ShouldBe("Detalle");
        var generico = await api.ConScopeAsync(sp => sp.GetRequiredService<IVentasService>().ClienteGenericoDeRutaAsync(ruta.Codigo, CancellationToken.None));
        generico!.CanalClave.ShouldBe("DET");
    }

    // ───────────── RN-LOG-12 / RN-LOG-13 ─────────────

    [Fact]
    [Trait("Regla", "RN-LOG-12")]
    public async Task Carga_ProponeChoferYVehiculoDeLaRuta_YValidaElChofer()
    {
        var admin = await api.ClienteAsync();
        var vehiculo = await admin.PostOkAsync<VehiculoPrueba>("/api/v1/logistica/vehiculos",
            new { placas = DatosPrueba.Unico("PL"), descripcion = "Camioneta de prueba", activo = true });
        var ruta = await CrearRutaAsync(vehiculoId: vehiculo.Id);

        var carga = await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", await CargaAsync(ruta));
        carga.ChoferId.ShouldBe(ruta.VendedorId);
        carga.ChoferNombre.ShouldNotBeNullOrEmpty();
        carga.VehiculoId.ShouldBe(vehiculo.Id);

        var sinPredeterminado = await CrearRutaAsync(conChofer: false);
        await (await admin.PostJsonAsync("/api/v1/logistica/envios", await CargaAsync(sinPredeterminado)))
            .DebeFallarAsync(HttpStatusCode.BadRequest, ErroresLogistica.ChoferInvalido);

        var almacenista = (await admin.GetOkAsync<PaginaPrueba<UsuarioPrueba>>("/api/v1/seguridad/usuarios?buscar=almacen"))
            .Items.Single(u => u.Usuario == "almacen").Id;
        await (await admin.PostJsonAsync("/api/v1/logistica/envios", await CargaAsync(sinPredeterminado, almacenista)))
            .DebeFallarAsync(HttpStatusCode.BadRequest, ErroresLogistica.ChoferInvalido);

        var otroChofer = await admin.CrearChoferAsync();
        var conChofer = await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", await CargaAsync(sinPredeterminado, otroChofer));
        conChofer.ChoferId.ShouldBe(otroChofer);
    }

    [Fact]
    [Trait("Regla", "RN-LOG-13")]
    public async Task Chofer_UnaRutaPorDia_LaJornadaLlevaUnSoloChofer_YLaLiquidacionLoHereda()
    {
        var admin = await api.ClienteAsync();
        var chofer = await admin.CrearChoferAsync();
        var rutaA = await CrearRutaAsync(choferId: chofer);
        var rutaB = await CrearRutaAsync();

        var primera = await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", await CargaAsync(rutaA));
        await (await admin.PostJsonAsync("/api/v1/logistica/envios", await CargaAsync(rutaB, chofer)))
            .DebeFallarAsync(HttpStatusCode.BadRequest, ErroresLogistica.ChoferEnOtraRuta);

        // Otra carga en Preparado con otro chofer arrastra a la primera (un chofer por jornada).
        var relevo = await admin.CrearChoferAsync();
        await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", await CargaAsync(rutaA, relevo));
        (await admin.GetOkAsync<EnvioPrueba>($"/api/v1/logistica/envios/{primera.Id}")).ChoferId.ShouldBe(relevo);

        // Despachada, la liquidación toma ese chofer y ya no se acepta otro en la jornada.
        var tortilla = await admin.ProductoAsync("PT-TORT-KG");
        await admin.EntradaAsync(await admin.AlmacenAsync("PT"), tortilla.Id, 5, 9.5m, DatosPrueba.Unico("L"), (await HoyAsync()).AddDays(2));
        var despachada = await admin.PostOkAsync<EnvioPrueba>($"/api/v1/logistica/envios/{primera.Id}/despachar");
        var liquidacion = await admin.GetOkAsync<LiquidacionPrueba>($"/api/v1/logistica/liquidaciones/{despachada.LiquidacionId}");
        liquidacion.ChoferId.ShouldBe(relevo);
        liquidacion.ChoferNombre.ShouldNotBeNullOrEmpty();

        await (await admin.PostJsonAsync("/api/v1/logistica/envios", await CargaAsync(rutaA, chofer)))
            .DebeFallarAsync(HttpStatusCode.BadRequest, ErroresLogistica.ChoferDistintoJornada);
    }

    // ───────────── RN-SYN-09 ─────────────

    [Fact]
    [Trait("Regla", "RN-SYN-09")]
    public async Task Alcance_EsLaRutaDeLaCarga_SinCargaLaPredeterminada_YAvisaCuandoCambia()
    {
        var admin = await api.ClienteAsync();
        var (choferId, usuario) = await admin.CrearUsuarioChoferAsync();
        var predeterminada = await CrearRutaAsync(choferId: choferId);
        var otra = await CrearRutaAsync();
        var (app, _, _) = await SesionAppAsync(usuario);

        var inicial = await app.GetOkAsync<RespuestaPull>("/api/v1/sync/pull?desde=0");
        inicial.Ruta!.Id.ShouldBe(predeterminada.Id);
        inicial.RutaCambiada.ShouldBeFalse();

        await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", await CargaAsync(otra, choferId));

        var cambio = await app.GetOkAsync<RespuestaPull>($"/api/v1/sync/pull?desde={inicial.Cursor}&rutaId={predeterminada.Id}");
        cambio.RutaCambiada.ShouldBeTrue();
        cambio.Cursor.ShouldBe(0);
        cambio.Ruta!.Id.ShouldBe(otra.Id);
        cambio.Cambios.Clientes.ShouldBeEmpty();

        var completa = await app.GetOkAsync<RespuestaPull>($"/api/v1/sync/pull?desde=0&rutaId={otra.Id}");
        completa.RutaCambiada.ShouldBeFalse();
        completa.Cambios.Clientes.ShouldContain(c => c.Id == otra.ClienteGenericoId);
    }

    [Fact]
    [Trait("Regla", "RN-SYN-09")]
    public async Task Alcance_DeSinRutaAUnaCarga_TambienEsCambio_YBajaLosClientesAnteriores()
    {
        var admin = await api.ClienteAsync();
        var (choferId, usuario) = await admin.CrearUsuarioChoferAsync();
        var ruta = await CrearRutaAsync();
        var (app, _, _) = await SesionAppAsync(usuario);

        // Sin carga ni ruta predeterminada baja lo general y el cursor ya queda adelante de los clientes de la ruta.
        var sinRuta = await app.GetOkAsync<RespuestaPull>("/api/v1/sync/pull?desde=0");
        sinRuta.Ruta.ShouldBeNull();
        sinRuta.Cursor.ShouldBeGreaterThan(0);

        await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", await CargaAsync(ruta, choferId));

        var cambio = await app.GetOkAsync<RespuestaPull>($"/api/v1/sync/pull?desde={sinRuta.Cursor}");
        cambio.RutaCambiada.ShouldBeTrue();
        cambio.Cursor.ShouldBe(0);
        cambio.Ruta!.Id.ShouldBe(ruta.Id);

        var completa = await app.GetOkAsync<RespuestaPull>($"/api/v1/sync/pull?desde=0&rutaId={ruta.Id}");
        completa.Cambios.Clientes.ShouldContain(c => c.Id == ruta.ClienteGenericoId);
    }

    [Fact]
    [Trait("Regla", "RN-SYN-09")]
    public async Task Operacion_SeAplicaALaRutaDeSuFechaOperativa_YSinRutaSeRechaza()
    {
        var admin = await api.ClienteAsync();
        var (choferId, usuario) = await admin.CrearUsuarioChoferAsync();
        var rutaAyer = await CrearRutaAsync(choferId: choferId);
        var rutaHoy = await CrearRutaAsync();
        var hoy = await HoyAsync();
        await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", await CargaAsync(rutaAyer, choferId, fecha: hoy.AddDays(-1)));
        await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", await CargaAsync(rutaHoy, choferId));
        var (app, codigo, dispositivo) = await SesionAppAsync(usuario);

        var pull = await app.GetOkAsync<RespuestaPull>("/api/v1/sync/pull?desde=0");
        pull.Ruta!.Id.ShouldBe(rutaHoy.Id);
        var generico = pull.Cambios.Clientes.Single(c => c.EsGenerico);
        var salsa = pull.Cambios.Productos.Single(p => p.Sku == "RV-SALSA");
        var precio = pull.Cambios.Precios.First(p => p.ListaPrecioId == generico.ListaPrecioId && p.ProductoId == salsa.Id);

        // Una venta de ayer que sube hoy va a la ruta de ayer (los genéricos de Detalle comparten lista).
        var ayer = DateTimeOffset.UtcNow.AddDays(-1);
        var push = await app.PostOkAsync<RespuestaPush>("/api/v1/sync/push", new
        {
            dispositivoId = dispositivo, loteId = Id.Nuevo(), versionApp = "1.0.0",
            operaciones = new[] { Venta($"{rutaAyer.Codigo}-{codigo}-000001", rutaAyer.ClienteGenericoId!.Value, precio, ayer) },
        });
        var resultado = push.Resultados.Single();
        resultado.Estado.ShouldBe("Aplicada", resultado.Codigo);
        var venta = await api.ConScopeAsync(sp => sp.GetRequiredService<IVentasService>().ObtenerVentaAsync(resultado.IdsServidor!["ventaId"], CancellationToken.None));
        venta!.RutaId.ShouldBe(rutaAyer.Id);

        // Sin carga ese día ni ruta predeterminada: SYN-006.
        var (sinRutaId, sinRuta) = await admin.CrearUsuarioChoferAsync();
        var (appSinRuta, codigoSinRuta, dispositivoSinRuta) = await SesionAppAsync(sinRuta);
        (await appSinRuta.GetOkAsync<RespuestaPull>("/api/v1/sync/pull?desde=0")).Ruta.ShouldBeNull();
        var rechazo = await appSinRuta.PostOkAsync<RespuestaPush>("/api/v1/sync/push", new
        {
            dispositivoId = dispositivoSinRuta, loteId = Id.Nuevo(), versionApp = "1.0.0",
            operaciones = new[] { Venta($"{rutaHoy.Codigo}-{codigoSinRuta}-000001", rutaHoy.ClienteGenericoId!.Value, precio, DateTimeOffset.UtcNow) },
        });
        rechazo.Resultados.Single().Codigo.ShouldBe("SYN-006");
        sinRutaId.ShouldNotBe(choferId);
    }

    [Fact]
    [Trait("Regla", "RN-APP-09")]
    public async Task Pull_TraeConsumoPromedioDelCliente_YElCanalDeLaRuta()
    {
        var admin = await api.ClienteAsync();
        var (choferId, usuario) = await admin.CrearUsuarioChoferAsync();
        var ruta = await CrearRutaAsync(choferId: choferId);
        var (app, codigo, dispositivo) = await SesionAppAsync(usuario);
        var pull = await app.GetOkAsync<RespuestaPull>("/api/v1/sync/pull?desde=0");
        pull.Cambios.Canales!.Single().Id.ShouldBe(ruta.CanalId);
        pull.Cambios.ConsumosClientes!.ShouldBeEmpty();

        var generico = pull.Cambios.Clientes.Single(c => c.EsGenerico);
        var salsa = pull.Cambios.Productos.Single(p => p.Sku == "RV-SALSA");
        var precio = pull.Cambios.Precios.First(p => p.ListaPrecioId == generico.ListaPrecioId && p.ProductoId == salsa.Id);
        var operaciones = Enumerable.Range(1, 2).Select(i =>
        {
            var op = Venta($"{ruta.Codigo}-{codigo}-{i:000000}", generico.Id, precio, DateTimeOffset.UtcNow);
            return op;
        }).ToArray();
        foreach (var op in operaciones)
        {
            var push = await app.PostOkAsync<RespuestaPush>("/api/v1/sync/push", new
            {
                dispositivoId = dispositivo, loteId = Id.Nuevo(), versionApp = "1.0.0", operaciones = new[] { op },
            });
            push.Resultados.Single().Estado.ShouldBe("Aplicada", push.Resultados.Single().Codigo);
        }

        // Dos ventas de 1 unidad: el promedio es la cantidad base de una.
        var despues = await app.GetOkAsync<RespuestaPull>($"/api/v1/sync/pull?desde={pull.Cursor}&rutaId={ruta.Id}");
        var consumo = despues.Cambios.ConsumosClientes!.Single(c => c.ClienteId == generico.Id && c.ProductoId == salsa.Id);
        consumo.PromedioBase.ShouldBeGreaterThan(0);
        despues.Cambios.Canales!.Single().Id.ShouldBe(ruta.CanalId);
    }

    // ───────────── Migración ─────────────

    [Fact]
    [Trait("Regla", "RN-LOG-31")]
    public async Task Migracion_LeDaCanalALasRutasExistentes_YChoferALasCargas()
    {
        var admin = await api.ClienteAsync();
        var semilla = await admin.GetOkAsync<List<RutaPrueba>>("/api/v1/logistica/rutas");
        var r02 = semilla.Single(r => r.Codigo == "R02");
        var carga = await admin.PostOkAsync<EnvioPrueba>("/api/v1/logistica/envios", await CargaAsync(r02));

        await api.ConScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<LogisticaDbContext>();
            var migrador = db.GetService<IMigrator>();
            var anterior = db.Database.GetAppliedMigrations().Single(m => m.EndsWith("_V02MapeoBitacora", StringComparison.Ordinal));
            await migrador.MigrateAsync(anterior);
            await migrador.MigrateAsync();
            return true;
        });

        var rutas = await admin.GetOkAsync<List<RutaPrueba>>("/api/v1/logistica/rutas");
        rutas.Single(r => r.Codigo == "R01").CanalNombre.ShouldBe("Detalle");
        rutas.Single(r => r.Codigo == "R02").CanalNombre.ShouldBe("Autoservicio");
        rutas.Single(r => r.Codigo == "R03").CanalNombre.ShouldBe("Institucional");
        var migrada = await admin.GetOkAsync<EnvioPrueba>($"/api/v1/logistica/envios/{carga.Id}");
        migrada.ChoferId.ShouldBe(r02.VendedorId);
        migrada.VehiculoId.ShouldBe(r02.VehiculoId);
    }

    private sealed record UsuarioPrueba(Guid Id, string Usuario);

    private static StringContent Contenido(object cuerpo) =>
        new(System.Text.Json.JsonSerializer.Serialize(cuerpo, ExtensionesHttp.Json), System.Text.Encoding.UTF8, "application/json");
}
