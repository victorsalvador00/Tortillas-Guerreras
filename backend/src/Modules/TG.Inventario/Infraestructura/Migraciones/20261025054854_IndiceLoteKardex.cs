using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Inventario.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class IndiceLoteKardex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_movimientos_lote",
                schema: "inventario",
                table: "movimientos",
                columns: new[] { "lote", "producto_id" },
                filter: "lote <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_movimientos_lote",
                schema: "inventario",
                table: "movimientos");
        }
    }
}
