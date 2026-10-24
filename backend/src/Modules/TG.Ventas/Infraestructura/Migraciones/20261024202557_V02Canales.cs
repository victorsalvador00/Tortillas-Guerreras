using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Ventas.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class V02Canales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "aplica_producto_a_cobro",
                schema: "ventas",
                table: "canales",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "formato_liquidacion",
                schema: "ventas",
                table: "canales",
                type: "text",
                nullable: false,
                defaultValue: "Completo");

            migrationBuilder.AddColumn<short>(
                name: "orden",
                schema: "ventas",
                table: "canales",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<string>(
                name: "pedido_por",
                schema: "ventas",
                table: "canales",
                type: "text",
                nullable: false,
                defaultValue: "Cliente");

            // v0.2: TRAD pasa a DET conservando el id (los clientes y listas no se tocan); CEDIS lo crea la semilla.
            migrationBuilder.Sql("""
                UPDATE ventas.canales SET clave = 'DET', nombre = 'Detalle'
                WHERE clave = 'TRAD' AND NOT EXISTS (SELECT 1 FROM ventas.canales WHERE clave = 'DET');

                UPDATE ventas.canales c SET
                    formato_liquidacion = v.formato, aplica_producto_a_cobro = v.a_cobro, pedido_por = v.pedido_por,
                    orden = v.orden
                FROM (VALUES
                    ('AUTO', 'Completo', true, 'Cliente', 1),
                    ('DET', 'Completo', true, 'Ruta', 2),
                    ('INST', 'Simplificado', false, 'Cliente', 3),
                    ('CEDIS', 'Simplificado', false, 'Cliente', 4)
                ) AS v(clave, formato, a_cobro, pedido_por, orden)
                WHERE c.clave = v.clave;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_canales_formato_liquidacion",
                schema: "ventas",
                table: "canales",
                sql: "formato_liquidacion IN ('Completo', 'Simplificado')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_canales_pedido_por",
                schema: "ventas",
                table: "canales",
                sql: "pedido_por IN ('Cliente', 'Ruta')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE ventas.canales SET clave = 'TRAD', nombre = 'Tradicional' WHERE clave = 'DET';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_canales_formato_liquidacion",
                schema: "ventas",
                table: "canales");

            migrationBuilder.DropCheckConstraint(
                name: "ck_canales_pedido_por",
                schema: "ventas",
                table: "canales");

            migrationBuilder.DropColumn(
                name: "aplica_producto_a_cobro",
                schema: "ventas",
                table: "canales");

            migrationBuilder.DropColumn(
                name: "formato_liquidacion",
                schema: "ventas",
                table: "canales");

            migrationBuilder.DropColumn(
                name: "orden",
                schema: "ventas",
                table: "canales");

            migrationBuilder.DropColumn(
                name: "pedido_por",
                schema: "ventas",
                table: "canales");
        }
    }
}
