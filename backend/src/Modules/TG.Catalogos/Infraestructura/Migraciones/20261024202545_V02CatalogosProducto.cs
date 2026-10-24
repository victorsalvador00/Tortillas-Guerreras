using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Catalogos.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class V02CatalogosProducto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "predeterminada_traslado",
                schema: "catalogos",
                table: "productos_unidades",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "uso_traslado",
                schema: "catalogos",
                table: "productos_unidades",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "aplica_producto_a_cobro",
                schema: "catalogos",
                table: "productos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "dias_tolerancia_devolucion",
                schema: "catalogos",
                table: "productos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre_corto",
                schema: "catalogos",
                table: "productos",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_productos_nombre_corto",
                schema: "catalogos",
                table: "productos",
                column: "nombre_corto",
                unique: true,
                filter: "activo AND nombre_corto IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_productos_nombre_corto",
                schema: "catalogos",
                table: "productos");

            migrationBuilder.DropColumn(
                name: "predeterminada_traslado",
                schema: "catalogos",
                table: "productos_unidades");

            migrationBuilder.DropColumn(
                name: "uso_traslado",
                schema: "catalogos",
                table: "productos_unidades");

            migrationBuilder.DropColumn(
                name: "aplica_producto_a_cobro",
                schema: "catalogos",
                table: "productos");

            migrationBuilder.DropColumn(
                name: "dias_tolerancia_devolucion",
                schema: "catalogos",
                table: "productos");

            migrationBuilder.DropColumn(
                name: "nombre_corto",
                schema: "catalogos",
                table: "productos");
        }
    }
}
