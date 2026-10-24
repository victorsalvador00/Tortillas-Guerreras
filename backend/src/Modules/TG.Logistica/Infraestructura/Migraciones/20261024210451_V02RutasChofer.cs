using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Logistica.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class V02RutasChofer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "canal_id",
                schema: "logistica",
                table: "rutas",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "chofer_id",
                schema: "logistica",
                table: "liquidaciones",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "chofer_id",
                schema: "logistica",
                table: "envios",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "vehiculo_id",
                schema: "logistica",
                table: "envios",
                type: "uuid",
                nullable: true);

            // RN-LOG-31: canal más común de los clientes activos de la ruta (sin el genérico); si no tiene, Detalle.
            // RN-LOG-12/13: las cargas existentes toman el chofer y vehículo predeterminados de su ruta y la liquidación,
            // el chofer de sus cargas.
            migrationBuilder.Sql("""
                UPDATE logistica.rutas r SET canal_id = COALESCE(
                    (SELECT c.canal_id
                       FROM logistica.rutas_clientes rc
                       JOIN ventas.clientes c ON c.id = rc.cliente_id
                      WHERE rc.ruta_id = r.id AND rc.activo AND c.activo AND NOT c.es_generico
                      GROUP BY c.canal_id
                      ORDER BY count(*) DESC, c.canal_id
                      LIMIT 1),
                    (SELECT id FROM ventas.canales WHERE clave = 'DET'),
                    r.canal_id);
                ALTER TABLE logistica.rutas ALTER COLUMN canal_id DROP DEFAULT;

                UPDATE logistica.envios e SET chofer_id = r.vendedor_id, vehiculo_id = r.vehiculo_id
                  FROM logistica.rutas r
                 WHERE r.id = e.ruta_id;

                UPDATE logistica.liquidaciones l SET chofer_id =
                    (SELECT e.chofer_id FROM logistica.envios e
                      WHERE e.liquidacion_id = l.id AND e.chofer_id IS NOT NULL
                      ORDER BY e.creado_en
                      LIMIT 1);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_rutas_vendedor",
                schema: "logistica",
                table: "rutas",
                column: "vendedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_envios_chofer_fecha",
                schema: "logistica",
                table: "envios",
                columns: new[] { "chofer_id", "fecha_operativa" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_rutas_vendedor",
                schema: "logistica",
                table: "rutas");

            migrationBuilder.DropIndex(
                name: "ix_envios_chofer_fecha",
                schema: "logistica",
                table: "envios");

            migrationBuilder.DropColumn(
                name: "canal_id",
                schema: "logistica",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "chofer_id",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "chofer_id",
                schema: "logistica",
                table: "envios");

            migrationBuilder.DropColumn(
                name: "vehiculo_id",
                schema: "logistica",
                table: "envios");
        }
    }
}
