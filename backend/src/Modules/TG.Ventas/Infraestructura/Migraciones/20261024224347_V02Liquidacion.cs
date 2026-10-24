using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Ventas.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class V02Liquidacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ventas_forma_pago",
                schema: "ventas",
                table: "ventas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cobros_forma_pago",
                schema: "ventas",
                table: "cobros");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ventas_forma_pago",
                schema: "ventas",
                table: "ventas",
                sql: "forma_pago IN ('Efectivo', 'Transferencia', 'Credito', 'Cheque')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cobros_forma_pago",
                schema: "ventas",
                table: "cobros",
                sql: "forma_pago IN ('Efectivo', 'Transferencia', 'Credito', 'Cheque')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ventas_forma_pago",
                schema: "ventas",
                table: "ventas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cobros_forma_pago",
                schema: "ventas",
                table: "cobros");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ventas_forma_pago",
                schema: "ventas",
                table: "ventas",
                sql: "forma_pago IN ('Efectivo', 'Transferencia', 'Credito')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cobros_forma_pago",
                schema: "ventas",
                table: "cobros",
                sql: "forma_pago IN ('Efectivo', 'Transferencia', 'Credito')");
        }
    }
}
