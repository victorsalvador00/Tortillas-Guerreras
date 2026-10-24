using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Seguridad.Infraestructura;

namespace TG.Api.IntegrationTests.Seguridad;

/// <summary>Roles y permisos v0.2 (T-8.3). Base propia: una prueba baja y vuelve a subir la migración de Seguridad.</summary>
public sealed class PermisosV02Tests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Almacen_NoPuedeCerrarElDineroDeUnaLiquidacion()
    {
        var almacen = await api.ClienteAsync("almacen");
        var respuesta = await almacen.PostJsonAsync($"/api/v1/logistica/liquidaciones/{Guid.CreateVersion7()}/cerrar", new { });
        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("captura", new[] { "ventas.pedidos", "ventas.ver", "produccion.ver", "logistica.ver" })]
    [InlineData("liquidaciones", new[] { "logistica.ver", "logistica.liquidar", "logistica.cargos_chofer", "ventas.ver" })]
    [InlineData("almacen", new[] { "logistica.liquidar_producto" })]
    [InlineData("supervisor", new[] { "logistica.revisar_liquidacion", "logistica.cargos_chofer", "logistica.cambios_cancelar" })]
    [InlineData("vendedor1", new[] { "ventas.pedidos_app" })]
    public async Task RolesV02_TienenSusPermisos(string usuario, string[] permisos)
    {
        var cliente = await api.ClienteAsync(usuario);
        var perfil = await cliente.GetOkAsync<PerfilPrueba>("/api/v1/seguridad/auth/yo");
        permisos.Except(perfil.Permisos).ShouldBeEmpty();
    }

    [Fact]
    public async Task RolesNuevos_NoPuedenLiquidarProductoNiCaja_SinSuPermiso()
    {
        var captura = await api.ClienteAsync("captura");
        var perfil = await captura.GetOkAsync<PerfilPrueba>("/api/v1/seguridad/auth/yo");
        perfil.Permisos.ShouldNotContain("logistica.liquidar");
        perfil.Permisos.ShouldNotContain("logistica.liquidar_producto");
    }

    [Fact]
    public async Task Migracion_AsignaLosPermisosNuevosALosRolesExistentes()
    {
        await api.ConScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<SeguridadDbContext>();
            var migrador = db.GetService<IMigrator>();
            var anterior = db.Database.GetAppliedMigrations().Single(m => m.EndsWith("_V02Bitacora", StringComparison.Ordinal));

            // Estado de una base v0.1 ya en uso: roles existentes sin los permisos nuevos.
            await migrador.MigrateAsync(anterior);
            await db.Database.ExecuteSqlRawAsync("""
                DELETE FROM seguridad.roles_permisos rp USING seguridad.permisos p
                WHERE rp.permiso_id = p.id AND p.clave IN ('logistica.liquidar_producto', 'ventas.pedidos_app');
                """);

            await migrador.MigrateAsync();

            var asignados = await db.Database.SqlQueryRaw<string>("""
                SELECT r.normalized_name || ':' || p.clave AS "Value"
                FROM seguridad.roles_permisos rp
                JOIN seguridad.roles r ON r.id = rp.rol_id
                JOIN seguridad.permisos p ON p.id = rp.permiso_id
                WHERE p.clave IN ('logistica.liquidar_producto', 'ventas.pedidos_app')
                """).ToListAsync();
            asignados.ShouldContain("ALMACEN:logistica.liquidar_producto");
            asignados.ShouldContain("VENDEDOR:ventas.pedidos_app");
            return true;
        });
    }
}
