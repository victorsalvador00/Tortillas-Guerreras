using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Inventario.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class V02SalidaCambio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "inventario",
                table: "movimientos");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "inventario",
                table: "movimientos",
                sql: "tipo IN ('EntradaCompra', 'DevolucionProveedor', 'EntradaProduccion', 'SalidaConsumo', 'Merma', 'TraspasoSalida', 'TraspasoEntrada', 'Venta', 'RetornoCliente', 'AjusteEntrada', 'AjusteSalida', 'SalidaCambio')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "inventario",
                table: "movimientos");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "inventario",
                table: "movimientos",
                sql: "tipo IN ('EntradaCompra', 'DevolucionProveedor', 'EntradaProduccion', 'SalidaConsumo', 'Merma', 'TraspasoSalida', 'TraspasoEntrada', 'Venta', 'RetornoCliente', 'AjusteEntrada', 'AjusteSalida')");
        }
    }
}
