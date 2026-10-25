using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Compras.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class CatalogoProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "activo",
                schema: "compras",
                table: "proveedores_productos",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "actualizado_en",
                schema: "compras",
                table: "proveedores_productos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "actualizado_por",
                schema: "compras",
                table: "proveedores_productos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "clave_proveedor",
                schema: "compras",
                table: "proveedores_productos",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "creado_en",
                schema: "compras",
                table: "proveedores_productos",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "creado_por",
                schema: "compras",
                table: "proveedores_productos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "dias_entrega",
                schema: "compras",
                table: "proveedores_productos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "precio",
                schema: "compras",
                table: "proveedores_productos",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "preferido",
                schema: "compras",
                table: "proveedores_productos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ultima_unidad_id",
                schema: "compras",
                table: "proveedores_productos",
                type: "uuid",
                nullable: true);

            // Las relaciones existentes venían de recepciones (o de la semilla): su último precio pasa a ser el pactado.
            migrationBuilder.Sql("""
                UPDATE compras.proveedores_productos
                SET precio = ultimo_precio,
                    ultima_unidad_id = CASE WHEN ultima_compra_en IS NULL THEN NULL ELSE unidad_id END,
                    creado_en = coalesce(ultima_compra_en, now());
                """);

            migrationBuilder.CreateIndex(
                name: "ux_proveedores_productos_preferido",
                schema: "compras",
                table: "proveedores_productos",
                column: "producto_id",
                unique: true,
                filter: "preferido");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_proveedores_productos_preferido",
                schema: "compras",
                table: "proveedores_productos");

            migrationBuilder.DropColumn(
                name: "activo",
                schema: "compras",
                table: "proveedores_productos");

            migrationBuilder.DropColumn(
                name: "actualizado_en",
                schema: "compras",
                table: "proveedores_productos");

            migrationBuilder.DropColumn(
                name: "actualizado_por",
                schema: "compras",
                table: "proveedores_productos");

            migrationBuilder.DropColumn(
                name: "clave_proveedor",
                schema: "compras",
                table: "proveedores_productos");

            migrationBuilder.DropColumn(
                name: "creado_en",
                schema: "compras",
                table: "proveedores_productos");

            migrationBuilder.DropColumn(
                name: "creado_por",
                schema: "compras",
                table: "proveedores_productos");

            migrationBuilder.DropColumn(
                name: "dias_entrega",
                schema: "compras",
                table: "proveedores_productos");

            migrationBuilder.DropColumn(
                name: "precio",
                schema: "compras",
                table: "proveedores_productos");

            migrationBuilder.DropColumn(
                name: "preferido",
                schema: "compras",
                table: "proveedores_productos");

            migrationBuilder.DropColumn(
                name: "ultima_unidad_id",
                schema: "compras",
                table: "proveedores_productos");
        }
    }
}
