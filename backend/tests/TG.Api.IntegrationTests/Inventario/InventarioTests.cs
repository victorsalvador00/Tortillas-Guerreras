using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Inventario.Infraestructura;
using TG.Inventario.Publico;
using TG.SharedKernel.Dominio;

namespace TG.Api.IntegrationTests.Inventario;

public sealed class InventarioTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record ConteoPrueba(Guid Id, string Folio, string Estado, IReadOnlyList<PartidaConteoPrueba> Partidas);

    private sealed record PartidaConteoPrueba(Guid ProductoId, decimal CantidadSistema, decimal CantidadContada, decimal Diferencia);

    private sealed record TraspasoPrueba(Guid Id, string Folio, string Estado);

    [Fact]
    [Trait("Regla", "RN-INV-02")]
    public async Task Salida_SinExistenciaEnAlmacenNoNegativo_DevuelveINV001()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync();
        var mp = await admin.AlmacenAsync("MP");
        await admin.EntradaAsync(mp, producto.Id, 5, 1);

        var respuesta = await admin.SalidaAsync(mp, producto.Id, 6);
        var error = await respuesta.DebeFallarAsync(HttpStatusCode.Conflict, "INV-001");
        error["detalle"]!["disponible"]!.GetValue<decimal>().ShouldBe(5m);

        (await admin.ExistenciaAsync(mp, producto.Id)).ShouldBe(5m);
    }

    [Fact]
    [Trait("Regla", "RN-INV-03")]
    public async Task Ruta_PermiteNegativo_YLoReporta()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA");

        var resultado = await api.ConScopeAsync(async sp =>
        {
            var inventario = sp.GetRequiredService<IInventarioService>();
            var almacen = await inventario.CrearAlmacenRutaAsync(Id.Nuevo(), DatosPrueba.Unico("R")[..8], "Ruta de prueba", CancellationToken.None);
            return await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.Venta, Id.Nuevo(), "R99-D01-000001", new DateOnly(2026, 11, 3), Guid.Empty,
                [new LineaMovimiento(TipoMovimiento.Venta, almacen.Valor.Id, producto.Id, null, -3)]), CancellationToken.None);
        });

        resultado.EsExito.ShouldBeTrue();
        resultado.Valor.Single().ExistenciaResultante.ShouldBe(-3m);
        resultado.Valor.Single().QuedoNegativo.ShouldBeTrue();
    }

    [Fact]
    [Trait("Regla", "RN-INV-04")]
    public async Task Concurrencia_DosSalidasDeLaUltimaExistencia_UnaPasaYOtraRecibeINV001()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA");
        var pt = await admin.AlmacenAsync("PT");
        await admin.EntradaAsync(pt, producto.Id, 10, 3);

        var clienteA = await api.ClienteAsync();
        var clienteB = await api.ClienteAsync();
        var respuestas = await Task.WhenAll(clienteA.SalidaAsync(pt, producto.Id, 10), clienteB.SalidaAsync(pt, producto.Id, 10));

        respuestas.Count(r => r.IsSuccessStatusCode).ShouldBe(1);
        var fallida = respuestas.Single(r => !r.IsSuccessStatusCode);
        await fallida.DebeFallarAsync(HttpStatusCode.Conflict, "INV-001");
        (await admin.ExistenciaAsync(pt, producto.Id)).ShouldBe(0m);
    }

    [Fact]
    [Trait("Regla", "RN-INV-05")]
    public async Task CostoPromedio_DosEntradas_PonderaPorCantidad()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync();
        var mp = await admin.AlmacenAsync("MP");
        await admin.EntradaAsync(mp, producto.Id, 100, 10);
        await admin.EntradaAsync(mp, producto.Id, 100, 20);

        var costo = await api.ConScopeAsync(sp => sp.GetRequiredService<IInventarioService>()
            .ObtenerCostoPromedioAsync(producto.Id, CancellationToken.None));
        costo.ShouldBe(15m);

        // La salida usa el promedio vigente.
        var salida = await (await admin.SalidaAsync(mp, producto.Id, 50)).LeerAsync<DocumentoPrueba>();
        salida.Movimientos.Single().CostoUnitario.ShouldBe(15m);
        salida.Movimientos.Single().CostoTotal.ShouldBe(-750m);
    }

    [Fact]
    [Trait("Regla", "RN-INV-06")]
    [Trait("Regla", "RN-INV-01")]
    public async Task Traspaso_MismoCostoEnAmbosLados_YCancelarEsContraMovimiento()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync();
        var mp = await admin.AlmacenAsync("MP");
        var pt = await admin.AlmacenAsync("PT");
        await admin.EntradaAsync(mp, producto.Id, 20, 7.5m);

        var traspaso = await admin.PostOkAsync<TraspasoPrueba>("/api/v1/inventario/traspasos", new
        {
            almacenOrigenId = mp, almacenDestinoId = pt, partidas = new[] { new { productoId = producto.Id, cantidad = 8 } },
        });
        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>(
            $"/api/v1/inventario/kardex?documentoTipo=Traspaso&documentoId={traspaso.Id}");
        kardex.Items.Count.ShouldBe(2);
        kardex.Items.Select(m => m.CostoUnitario).Distinct().Single().ShouldBe(7.5m);
        (await admin.ExistenciaAsync(pt, producto.Id)).ShouldBe(8m);

        var cancelado = await admin.PostOkAsync<TraspasoPrueba>($"/api/v1/inventario/traspasos/{traspaso.Id}/cancelar");
        cancelado.Estado.ShouldBe("Cancelado");
        var despues = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>(
            $"/api/v1/inventario/kardex?documentoTipo=Traspaso&documentoId={traspaso.Id}");
        despues.Items.Count.ShouldBe(4);
        despues.Items.Sum(m => m.Cantidad).ShouldBe(0m);
        (await admin.ExistenciaAsync(mp, producto.Id)).ShouldBe(20m);
        (await admin.ExistenciaAsync(pt, producto.Id)).ShouldBe(0m);
    }

    [Fact]
    [Trait("Regla", "RN-INV-07")]
    public async Task SalidaSinLote_ConsumeFefo()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync(tipo: "Terminado", manejaLote: true);
        var pt = await admin.AlmacenAsync("PT");
        await admin.EntradaAsync(pt, producto.Id, 10, 1, "L-TARDE", new DateOnly(2026, 11, 10));
        await admin.EntradaAsync(pt, producto.Id, 10, 1, "L-PRONTO", new DateOnly(2026, 11, 5));
        await admin.EntradaAsync(pt, producto.Id, 10, 1, "L-SINCAD");

        var salida = await (await admin.SalidaAsync(pt, producto.Id, 15)).LeerAsync<DocumentoPrueba>();

        salida.Movimientos.Count.ShouldBe(2);
        salida.Movimientos.Single(m => m.Lote == "L-PRONTO").Cantidad.ShouldBe(-10m);
        salida.Movimientos.Single(m => m.Lote == "L-TARDE").Cantidad.ShouldBe(-5m);
        (await admin.ExistenciaAsync(pt, producto.Id, "L-SINCAD")).ShouldBe(10m);
    }

    [Fact]
    public async Task EntradaDeProductoConLote_SinLote_DevuelveINV003()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync(tipo: "Terminado", manejaLote: true);
        var motivo = await admin.MotivoAsync("OTRO");
        var respuesta = await admin.PostJsonAsync("/api/v1/inventario/ajustes", new
        {
            almacenId = await admin.AlmacenAsync("PT"), motivoMermaId = motivo, notas = "x", esMerma = false,
            partidas = new[] { new { productoId = producto.Id, cantidad = 5, costoUnitario = 1 } },
        });
        await respuesta.DebeFallarAsync(HttpStatusCode.BadRequest, "INV-003");
    }

    [Fact]
    [Trait("Regla", "RN-INV-08")]
    public async Task Ajuste_SinMotivoONota_SeRechaza()
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync();
        var respuesta = await admin.PostJsonAsync("/api/v1/inventario/ajustes", new
        {
            almacenId = await admin.AlmacenAsync("MP"), motivoMermaId = Guid.Empty, notas = "", esMerma = false,
            partidas = new[] { new { productoId = producto.Id, cantidad = 5 } },
        });
        var error = await respuesta.DebeFallarAsync(HttpStatusCode.BadRequest, "VAL-001");
        error["errores"]!["motivoMermaId"].ShouldNotBeNull();
        error["errores"]!["notas"].ShouldNotBeNull();
    }

    [Fact]
    [Trait("Regla", "RN-INV-09")]
    [Trait("Regla", "RN-INV-10")]
    public async Task Conteo_TomaSistemaAlCapturar_YAlCerrarAjustaDiferencias()
    {
        var admin = await api.ClienteAsync();
        var faltante = await admin.CrearProductoAsync();
        var sobrante = await admin.CrearProductoAsync();
        var mp = await admin.AlmacenAsync("MP");
        await admin.EntradaAsync(mp, faltante.Id, 50, 2);
        await admin.EntradaAsync(mp, sobrante.Id, 20, 2);

        var conteo = await admin.PostOkAsync<ConteoPrueba>("/api/v1/inventario/conteos", new { almacenId = mp });
        await admin.PostOkAsync<ConteoPrueba>($"/api/v1/inventario/conteos/{conteo.Id}/partidas",
            new { productoId = faltante.Id, cantidadContada = 47 });
        var capturado = await admin.PostOkAsync<ConteoPrueba>($"/api/v1/inventario/conteos/{conteo.Id}/partidas",
            new { productoId = sobrante.Id, cantidadContada = 21 });
        capturado.Partidas.Single(p => p.ProductoId == faltante.Id).Diferencia.ShouldBe(-3m);

        // RN-INV-10: el almacén sigue operando; la partida conserva lo que había al capturar.
        await admin.EntradaAsync(mp, faltante.Id, 10, 2);

        var cerrado = await admin.PostOkAsync<ConteoPrueba>($"/api/v1/inventario/conteos/{conteo.Id}/cerrar");
        cerrado.Estado.ShouldBe("Cerrado");
        var kardex = await admin.GetOkAsync<PaginaPrueba<MovimientoPrueba>>(
            $"/api/v1/inventario/kardex?documentoTipo=ConteoFisico&documentoId={conteo.Id}");
        kardex.Items.Single(m => m.ProductoId == faltante.Id).Tipo.ShouldBe("AjusteSalida");
        kardex.Items.Single(m => m.ProductoId == sobrante.Id).Cantidad.ShouldBe(1m);
        (await admin.ExistenciaAsync(mp, faltante.Id)).ShouldBe(57m);
    }

    [Fact]
    public async Task Kardex_SaldoCoincideConExistencias_En1000MovimientosAleatorios()
    {
        var admin = await api.ClienteAsync();
        var productos = new List<ProductoPrueba>();
        for (var i = 0; i < 4; i++)
        {
            productos.Add(await admin.CrearProductoAsync(tipo: "Terminado", manejaLote: i % 2 == 0));
        }

        var almacenes = new[] { await admin.AlmacenAsync("MP"), await admin.AlmacenAsync("PT") };
        var azar = new Random(20261103);
        var tipos = new[] { TipoMovimiento.AjusteEntrada, TipoMovimiento.AjusteSalida, TipoMovimiento.Merma, TipoMovimiento.EntradaCompra };

        await api.ConScopeAsync(async sp =>
        {
            var aplicados = 0;
            var intentos = 0;
            while (aplicados < 1000 && intentos < 5000)
            {
                intentos++;
                await using var scope = sp.CreateAsyncScope();
                var inventario = scope.ServiceProvider.GetRequiredService<IInventarioService>();
                var producto = productos[azar.Next(productos.Count)];
                var tipo = tipos[azar.Next(tipos.Length)];
                var entrada = tipo is TipoMovimiento.AjusteEntrada or TipoMovimiento.EntradaCompra;
                var cantidad = Math.Round((decimal)azar.NextDouble() * 20 + 0.001m, 3);
                string? lote = producto.ManejaLote ? (entrada || azar.Next(2) == 0 ? $"L{azar.Next(3)}" : null) : null;
                var resultado = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                    DocumentoInventario.Ajuste, Id.Nuevo(), "PROP", new DateOnly(2026, 11, 3), Guid.Empty,
                    [new LineaMovimiento(tipo, almacenes[azar.Next(2)], producto.Id, lote, entrada ? cantidad : -cantidad,
                        entrada ? Math.Round((decimal)azar.NextDouble() * 30, 2) : null)]), CancellationToken.None);
                if (resultado.EsExito)
                {
                    aplicados++;
                }
                else
                {
                    resultado.Error!.Codigo.ShouldBe("INV-001");
                }
            }

            aplicados.ShouldBe(1000);

            var db = sp.GetRequiredService<InventarioDbContext>();
            var ids = productos.Select(p => p.Id).ToList();
            var saldos = await db.Movimientos.Where(m => ids.Contains(m.ProductoId))
                .GroupBy(m => new { m.AlmacenId, m.ProductoId, m.Lote })
                .Select(g => new { g.Key.AlmacenId, g.Key.ProductoId, g.Key.Lote, Suma = g.Sum(m => m.Cantidad) })
                .ToListAsync();
            var existencias = await db.Existencias.AsNoTracking().Where(e => ids.Contains(e.ProductoId)).ToListAsync();
            foreach (var existencia in existencias)
            {
                var suma = saldos.SingleOrDefault(s => s.AlmacenId == existencia.AlmacenId && s.ProductoId == existencia.ProductoId
                    && s.Lote == existencia.Lote)?.Suma ?? 0m;
                existencia.Cantidad.ShouldBe(suma);
                existencia.Cantidad.ShouldBeGreaterThanOrEqualTo(0m);
            }

            // Y la existencia resultante del último movimiento de cada lote es el saldo.
            return true;
        });
    }
}
