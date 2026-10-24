using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Logistica.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class V02CargosChofer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS logistica.folio_cch_seq;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS logistica.folio_csc_seq;");

            migrationBuilder.AddColumn<decimal>(
                name: "cantidad_a_cobro",
                schema: "logistica",
                table: "liquidaciones_partidas",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "precio_a_cobro",
                schema: "logistica",
                table: "liquidaciones_partidas",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cargos_chofer",
                schema: "logistica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "text", nullable: false),
                    chofer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    liquidacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cantidad_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    corte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cargos_chofer", x => x.id);
                    table.CheckConstraint("ck_cargos_chofer_estado", "estado IN ('Pendiente', 'Aplicado', 'Cancelado')");
                    table.CheckConstraint("ck_cargos_chofer_tipo", "tipo IN ('ProductoACobro', 'FaltanteProducto', 'FaltanteEfectivo', 'DiferenciaPrecio', 'Otro')");
                });

            migrationBuilder.CreateTable(
                name: "cortes_chofer",
                schema: "logistica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "text", nullable: false),
                    chofer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    desde = table.Column<DateOnly>(type: "date", nullable: false),
                    hasta = table.Column<DateOnly>(type: "date", nullable: false),
                    total_venta = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_cargos = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    cerrado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cortes_chofer", x => x.id);
                    table.CheckConstraint("ck_cortes_chofer_estado", "estado IN ('Abierto', 'Cerrado')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_cargos_chofer_chofer_estado",
                schema: "logistica",
                table: "cargos_chofer",
                columns: new[] { "chofer_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_cargos_chofer_liquidacion",
                schema: "logistica",
                table: "cargos_chofer",
                column: "liquidacion_id");

            migrationBuilder.CreateIndex(
                name: "ux_cargos_chofer_folio",
                schema: "logistica",
                table: "cargos_chofer",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_cortes_chofer_chofer_desde",
                schema: "logistica",
                table: "cortes_chofer",
                columns: new[] { "chofer_id", "desde" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_cortes_chofer_folio",
                schema: "logistica",
                table: "cortes_chofer",
                column: "folio",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS logistica.folio_cch_seq;");
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS logistica.folio_csc_seq;");

            migrationBuilder.DropTable(
                name: "cargos_chofer",
                schema: "logistica");

            migrationBuilder.DropTable(
                name: "cortes_chofer",
                schema: "logistica");

            migrationBuilder.DropColumn(
                name: "cantidad_a_cobro",
                schema: "logistica",
                table: "liquidaciones_partidas");

            migrationBuilder.DropColumn(
                name: "precio_a_cobro",
                schema: "logistica",
                table: "liquidaciones_partidas");
        }
    }
}
