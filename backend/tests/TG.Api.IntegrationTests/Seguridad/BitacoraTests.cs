using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Catalogos.Aplicacion;
using TG.Catalogos.Dominio;
using TG.Seguridad.Infraestructura;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;

namespace TG.Api.IntegrationTests.Seguridad;

public sealed class BitacoraTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record Registro(Guid EntidadId, Guid? DocumentoId, string Tabla, string Accion, JsonElement Cambios,
        Guid? UsuarioId, string? Usuario, Guid? DispositivoId, string Origen);

    private sealed record Pagina(List<Registro> Items, int Total);

    private sealed record IdRespuesta(Guid Id);

    private sealed record PedidoRespuesta(Guid Id, List<IdRespuesta> Partidas);

    [Fact]
    [Trait("Regla", "RN-SEG-06")]
    public async Task Producto_AltaCambioYBajaLogica_QuedanEnLaBitacoraConUsuarioYCambios()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync();
        await admin.PutOkAsync<IdRespuesta>($"/api/v1/catalogos/productos/{producto.Id}", new
        {
            sku = producto.Sku, nombre = "Nombre corregido", tipo = "MateriaPrima", unidadBaseId = producto.UnidadBaseId, tasaIva = 0,
        });
        await admin.PostOkAsync<IdRespuesta>($"/api/v1/catalogos/productos/{producto.Id}/desactivar");

        var historial = await admin.GetOkAsync<Pagina>($"/api/v1/seguridad/bitacora?tabla=productos&entidadId={producto.Id}");

        historial.Items.Select(r => r.Accion).ShouldBe(["Baja", "Cambio", "Alta"]);
        historial.Items.ShouldAllBe(r => r.Tabla == "productos" && r.DocumentoId == producto.Id && r.Origen == "Web");
        historial.Items.ShouldAllBe(r => r.Usuario == "Administrador del sistema");
        var alta = historial.Items[2].Cambios;
        alta.GetProperty("sku").GetString().ShouldBe(producto.Sku);
        var cambio = historial.Items[1].Cambios.GetProperty("nombre");
        cambio[0].GetString().ShouldBe(producto.Nombre);
        cambio[1].GetString().ShouldBe("Nombre corregido");
        historial.Items[1].Cambios.TryGetProperty("actualizado_en", out _).ShouldBeFalse();
        historial.Items[0].Cambios.GetProperty("activo")[1].GetBoolean().ShouldBeFalse();
    }

    [Fact]
    [Trait("Regla", "RN-SEG-06")]
    public async Task PartidaDePedido_SeRegistraConElDocumentoDelPedido()
    {
        var admin = await api.ClienteAsync();
        var tortilla = await admin.ProductoAsync("PT-TORT-KG");
        var clientes = await admin.GetOkAsync<Pagina<IdRespuesta>>("/api/v1/ventas/clientes?tamano=1");
        var pedido = await admin.PostOkAsync<PedidoRespuesta>("/api/v1/ventas/pedidos", new
        {
            clienteId = clientes.Items[0].Id,
            partidas = new[] { new { productoId = tortilla.Id, unidadId = tortilla.UnidadBaseId, cantidad = 5 } },
        });

        var historial = await admin.GetOkAsync<Pagina>($"/api/v1/seguridad/bitacora?tabla=pedidos&documentoId={pedido.Id}");

        historial.Items.ShouldContain(r => r.Tabla == "pedidos" && r.EntidadId == pedido.Id && r.Accion == "Alta");
        historial.Items.ShouldContain(r => r.Tabla == "pedidos_partidas" && r.EntidadId == pedido.Partidas[0].Id
            && r.DocumentoId == pedido.Id);
    }

    [Fact]
    [Trait("Regla", "RN-SEG-06")]
    public async Task Rollback_NoDejaFilasDeBitacora()
    {
        var unidad = await (await api.ClienteAsync()).UnidadAsync("KG");
        var sku = DatosPrueba.Unico("RBK");
        var falla = Error.Conflicto("PRB-001", "Se revierte a propósito.");

        var productoId = await api.ConScopeAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var guardar = sp.GetRequiredService<GuardarProductoHandler>();
            Guid? id = null;
            var resultado = await uow.EjecutarAsync<bool>(async ct =>
            {
                var creado = await guardar.CrearAsync(new GuardarProductoRequest(sku, "Se revierte", null, TipoProducto.MateriaPrima,
                    unidad, null, null, null, null, null, null, null, 0m, null, null, null, []), ct);
                id = creado.Valor.Id;
                return falla;
            }, CancellationToken.None);
            resultado.EsExito.ShouldBeFalse();
            return id!.Value;
        });

        var filas = await api.ConScopeAsync(sp =>
            sp.GetRequiredService<SeguridadDbContext>().Bitacora.CountAsync(b => b.EntidadId == productoId));
        filas.ShouldBe(0);
    }

    [Fact]
    [Trait("Regla", "RN-SEG-06")]
    public async Task CambioDesdeLaApp_RegistraOrigenAppYDispositivo()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync();
        var usuarioId = Guid.CreateVersion7();
        var dispositivoId = Guid.CreateVersion7();

        await api.ConScopeAsync(async sp =>
        {
            // Mismo usuario actual que tendría una solicitud con token ligado a un dispositivo.
            sp.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
                    new Claim(UsuarioActualHttp.ClaimDispositivo, dispositivoId.ToString()),
                ], "prueba")),
            };
            var guardar = sp.GetRequiredService<GuardarProductoHandler>();
            var r = await guardar.ActualizarAsync(producto.Id, new GuardarProductoRequest(producto.Sku, "Desde la app", null,
                TipoProducto.MateriaPrima, producto.UnidadBaseId, null, null, null, null, null, null, null, 0m, null, null, null, []),
                CancellationToken.None);
            r.EsExito.ShouldBeTrue();
            return true;
        });

        var historial = await admin.GetOkAsync<Pagina>($"/api/v1/seguridad/bitacora?tabla=productos&entidadId={producto.Id}");
        var cambio = historial.Items.First(r => r.Accion == "Cambio");
        cambio.Origen.ShouldBe("App");
        cambio.DispositivoId.ShouldBe(dispositivoId);
        cambio.UsuarioId.ShouldBe(usuarioId);
    }

    [Fact]
    [Trait("Regla", "RN-SEG-07")]
    public async Task SinPermisoDeBitacora_SoloVeElHistorialDeUnDocumentoQuePuedeConsultar()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync();
        var vendedor = await api.ClienteAsync("vendedor1");

        (await vendedor.GetAsync("/api/v1/seguridad/bitacora")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vendedor.GetAsync($"/api/v1/seguridad/bitacora?tabla=usuarios&documentoId={Guid.CreateVersion7()}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var historial = await vendedor.GetOkAsync<Pagina>($"/api/v1/seguridad/bitacora?tabla=productos&documentoId={producto.Id}");
        historial.Items.ShouldContain(r => r.Accion == "Alta");
    }

    [Fact]
    [Trait("Regla", "RN-SEG-07")]
    public async Task Bitacora_NoTieneEndpointsDeEscritura()
    {
        var admin = await api.ClienteAsync();
        var respuesta = await admin.PostJsonAsync("/api/v1/seguridad/bitacora", new { });
        respuesta.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await admin.DeleteAsync($"/api/v1/seguridad/bitacora/{Guid.CreateVersion7()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private sealed record Pagina<T>(List<T> Items);
}
