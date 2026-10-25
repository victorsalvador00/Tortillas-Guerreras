using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Produccion.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class AlmacenPorComponente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "almacen_id",
                schema: "produccion",
                table: "recetas_componentes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "almacen_id",
                schema: "produccion",
                table: "ordenes_produccion_consumos",
                type: "uuid",
                nullable: true);

            // RN-PRO-11: los consumos existentes salían del almacén de consumo de su orden.
            migrationBuilder.Sql("""
                UPDATE produccion.ordenes_produccion_consumos c
                SET almacen_id = o.almacen_consumo_id
                FROM produccion.ordenes_produccion o
                WHERE o.id = c.orden_produccion_id;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "almacen_id",
                schema: "produccion",
                table: "ordenes_produccion_consumos",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "almacen_id",
                schema: "produccion",
                table: "recetas_componentes");

            migrationBuilder.DropColumn(
                name: "almacen_id",
                schema: "produccion",
                table: "ordenes_produccion_consumos");
        }
    }
}
