using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Catalogos.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class LoteMateriaPrimaSemiterminado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // [SUP-02] Materia prima y semiterminados pasan a manejar lote para la trazabilidad (RN-INV-14). Lo que ya
            // está en el kardex sin lote se queda así; las entradas nuevas piden lote.
            migrationBuilder.Sql(
                "UPDATE catalogos.productos SET maneja_lote = true WHERE tipo IN ('MateriaPrima', 'Semiterminado') AND NOT maneja_lote;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin vuelta atrás: no se sabe qué productos tenían la bandera apagada a propósito.
        }
    }
}
