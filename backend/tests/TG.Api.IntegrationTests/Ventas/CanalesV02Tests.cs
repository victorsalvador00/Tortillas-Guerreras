using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Ventas.Infraestructura;
using TG.Ventas.Publico;

namespace TG.Api.IntegrationTests.Ventas;

/// <summary>Canales v0.2 (T-8.1). Base propia: la prueba de migración baja y vuelve a subir el esquema de Ventas.</summary>
public sealed class CanalesV02Tests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record CanalRespuesta(Guid Id, string Clave, string Nombre, string FormatoLiquidacion,
        bool AplicaProductoACobro, string PedidoPor, short Orden);

    [Fact]
    [Trait("Regla", "RN-LOG-19")]
    [Trait("Regla", "RN-VEN-16")]
    public async Task Semilla_CuatroCanales_ConFormatoProductoACobroYPedidoPor()
    {
        var admin = await api.ClienteAsync();
        var canales = await admin.GetOkAsync<List<CanalRespuesta>>("/api/v1/ventas/canales");

        canales.Select(c => c.Clave).ShouldBe([ClavesCanal.Autoservicio, ClavesCanal.Detalle, ClavesCanal.Institucional, ClavesCanal.Cedis]);
        canales.ShouldNotContain(c => c.Clave == ClavesCanal.DetalleAnterior);
        canales.Single(c => c.Clave == "DET").ShouldSatisfyAllConditions(
            c => c.FormatoLiquidacion.ShouldBe("Completo"), c => c.AplicaProductoACobro.ShouldBeTrue(),
            c => c.PedidoPor.ShouldBe("Ruta"));
        canales.Single(c => c.Clave == "CEDIS").ShouldSatisfyAllConditions(
            c => c.FormatoLiquidacion.ShouldBe("Simplificado"), c => c.AplicaProductoACobro.ShouldBeFalse(),
            c => c.PedidoPor.ShouldBe("Cliente"));
    }

    [Fact]
    public async Task Migracion_TradADet_ConservaIdYClientes()
    {
        await api.ConScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<VentasDbContext>();
            var migrador = db.GetService<IMigrator>();
            var anterior = db.Database.GetAppliedMigrations().Single(m => m.EndsWith("_Inicial", StringComparison.Ordinal));

            // Estado v0.1: el canal se llama TRAD.
            await migrador.MigrateAsync(anterior);
            var idTrad = await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM ventas.canales WHERE clave = 'TRAD'")
                .SingleAsync();
            var clientes = await ClientesDelCanalAsync(db, idTrad);
            clientes.ShouldBeGreaterThan(0);

            await migrador.MigrateAsync();

            var idDet = await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM ventas.canales WHERE clave = 'DET'")
                .SingleAsync();
            idDet.ShouldBe(idTrad);
            (await ClientesDelCanalAsync(db, idDet)).ShouldBe(clientes);
            var pedidoPor = await db.Database
                .SqlQueryRaw<string>("SELECT pedido_por AS \"Value\" FROM ventas.canales WHERE clave = 'DET'").SingleAsync();
            pedidoPor.ShouldBe("Ruta");
            return true;
        });
    }

    private static Task<int> ClientesDelCanalAsync(VentasDbContext db, Guid canalId) =>
        db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM ventas.clientes WHERE canal_id = {canalId}").SingleAsync();
}
