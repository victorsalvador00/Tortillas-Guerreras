using System.Net;
using System.Text.Json.Nodes;
using TG.Api.IntegrationTests.Infraestructura;

namespace TG.Api.IntegrationTests.Tablero;

public sealed class TableroTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Tablero_DevuelveIndicadores_YBajoMinimoDeLaSemilla()
    {
        var admin = await api.ClienteAsync();
        var tablero = await admin.GetOkAsync<JsonObject>("/api/v1/tablero");

        tablero["bajoMinimo"]!.AsArray().Select(x => x!["sku"]!.GetValue<string>()).ShouldContain("MP-MAIZ-BCO");
        tablero["liquidacionesPendientes"].ShouldNotBeNull();
        tablero["mermaMes"]!["costoMerma"]!.GetValue<decimal>().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    [Trait("Regla", "RN-INV-08")]
    public async Task ReporteMermas_IncluyeMermasDeAlmacenPorMotivo_YAjustesEnTablero()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync();
        var mp = await admin.AlmacenAsync("MP");
        await admin.EntradaAsync(mp, producto.Id, 10, 4);
        var motivo = await admin.MotivoAsync("CADUCADO");
        (await admin.PostJsonAsync("/api/v1/inventario/ajustes", new
        {
            almacenId = mp, motivoMermaId = motivo, notas = "Bultos caducados", esMerma = true,
            partidas = new[] { new { productoId = producto.Id, cantidad = -2m } },
        })).EnsureSuccessStatusCode();

        var reporte = await admin.GetOkAsync<JsonObject>("/api/v1/reportes/mermas");
        var caducado = reporte["porMotivo"]!.AsArray().Single(x => x!["motivo"]!.GetValue<string>() == "Producto caducado");
        caducado!["costo"]!.GetValue<decimal>().ShouldBeGreaterThanOrEqualTo(8m);

        var tablero = await admin.GetOkAsync<JsonObject>("/api/v1/tablero");
        tablero["ajustesDelMes"]!.AsArray().ShouldContain(a => a!["notas"]!.GetValue<string>() == "Bultos caducados");
    }

    [Fact]
    public async Task ReportesVentasYContinuidad_Responden()
    {
        var admin = await api.ClienteAsync();
        var ventas = await admin.GetOkAsync<JsonObject>("/api/v1/reportes/ventas");
        ventas["porRuta"].ShouldNotBeNull();

        // Lunes 2 de noviembre de 2026: las rutas semilla tienen clientes programados.
        var continuidad = await admin.GetOkAsync<JsonObject>("/api/v1/reportes/continuidad?fecha=2026-11-02");
        continuidad["rutas"]!.AsArray().Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Reportes_SinPermiso_Recibe403()
    {
        var vendedor = await api.ClienteAsync("vendedor1");
        (await vendedor.GetAsync("/api/v1/tablero")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vendedor.GetAsync("/api/v1/reportes/ventas")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
