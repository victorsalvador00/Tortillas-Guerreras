using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Seguridad.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class PermisoTrazabilidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // T-8.16: permiso de trazabilidad para los roles que ya existen (la semilla solo lo asigna al crear el rol).
            migrationBuilder.Sql("""
                INSERT INTO seguridad.permisos (id, clave, descripcion) VALUES
                    (gen_random_uuid(), 'inventario.trazabilidad', 'Rastrear lotes de proveedor a cliente')
                ON CONFLICT (clave) DO NOTHING;

                INSERT INTO seguridad.roles_permisos (rol_id, permiso_id)
                SELECT r.id, p.id
                FROM (VALUES ('ADMINISTRADOR'), ('DIRECCION'), ('COMPRAS'), ('PRODUCCION'), ('ALMACEN')) AS v(rol)
                JOIN seguridad.roles r ON r.normalized_name = v.rol
                JOIN seguridad.permisos p ON p.clave = 'inventario.trazabilidad'
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
