using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Inventario.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class TipoAlmacenEmpaquetado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_almacenes_tipo",
                schema: "inventario",
                table: "almacenes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_almacenes_tipo",
                schema: "inventario",
                table: "almacenes",
                sql: "tipo IN ('MateriaPrima', 'ProductoTerminado', 'General', 'Ruta', 'ProductoSemiterminado', 'Subalmacen', 'Empaquetado')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_almacenes_tipo",
                schema: "inventario",
                table: "almacenes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_almacenes_tipo",
                schema: "inventario",
                table: "almacenes",
                sql: "tipo IN ('MateriaPrima', 'ProductoTerminado', 'General', 'Ruta', 'ProductoSemiterminado', 'Subalmacen')");
        }
    }
}
