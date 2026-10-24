using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Seguridad.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class V02PermisosRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // v0.2 (T-8.3): la semilla solo asigna permisos al crear un rol; aquí se dan los nuevos a los roles que ya
            // existen. En una base nueva no hay roles todavía y la semilla los crea completos.
            migrationBuilder.Sql("""
                INSERT INTO seguridad.permisos (id, clave, descripcion) VALUES
                    (gen_random_uuid(), 'logistica.liquidar_producto', 'Confirmar el producto de la liquidación (almacén)'),
                    (gen_random_uuid(), 'logistica.revisar_liquidacion', 'Revisar la liquidación (supervisor)'),
                    (gen_random_uuid(), 'logistica.cargos_chofer', 'Administrar la cuenta y el corte de los choferes'),
                    (gen_random_uuid(), 'logistica.cambios_cancelar', 'Cancelar cambios pendientes de clientes'),
                    (gen_random_uuid(), 'ventas.pedidos_app', 'Capturar pedidos desde la app de ruta'),
                    (gen_random_uuid(), 'seguridad.bitacora', 'Consultar la bitácora de cambios')
                ON CONFLICT (clave) DO NOTHING;

                INSERT INTO seguridad.roles_permisos (rol_id, permiso_id)
                SELECT r.id, p.id
                FROM (VALUES
                    ('ALMACEN', 'logistica.liquidar_producto'),
                    ('SUPERVISOR', 'logistica.revisar_liquidacion'),
                    ('SUPERVISOR', 'logistica.cargos_chofer'),
                    ('SUPERVISOR', 'logistica.cambios_cancelar'),
                    ('VENDEDOR', 'ventas.pedidos_app')
                ) AS v(rol, permiso)
                JOIN seguridad.roles r ON r.normalized_name = v.rol
                JOIN seguridad.permisos p ON p.clave = v.permiso
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
