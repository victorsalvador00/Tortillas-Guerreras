using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Logistica.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class V02Liquidacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_liquidaciones_estado",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.AddColumn<decimal>(
                name: "cargado_inicial",
                schema: "logistica",
                table: "liquidaciones_partidas",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "importe_venta",
                schema: "logistica",
                table: "liquidaciones_partidas",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "recargado",
                schema: "logistica",
                table: "liquidaciones_partidas",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "diferencia_precios",
                schema: "logistica",
                table: "liquidaciones",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "formato",
                schema: "logistica",
                table: "liquidaciones",
                type: "text",
                nullable: false,
                defaultValue: "Completo");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "producto_confirmado_en",
                schema: "logistica",
                table: "liquidaciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "producto_confirmado_por",
                schema: "logistica",
                table: "liquidaciones",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "revisada_en",
                schema: "logistica",
                table: "liquidaciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "revisada_por",
                schema: "logistica",
                table: "liquidaciones",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_cheques",
                schema: "logistica",
                table: "liquidaciones",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_cobros",
                schema: "logistica",
                table: "liquidaciones",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_credito",
                schema: "logistica",
                table: "liquidaciones",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_gastos",
                schema: "logistica",
                table: "liquidaciones",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_transferencias",
                schema: "logistica",
                table: "liquidaciones",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_venta",
                schema: "logistica",
                table: "liquidaciones",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // Partidas anteriores a v0.2: todo lo cargado cuenta como carga inicial.
            migrationBuilder.Sql("UPDATE logistica.liquidaciones_partidas SET cargado_inicial = cargado;");

            migrationBuilder.CreateTable(
                name: "liquidaciones_gastos",
                schema: "logistica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    liquidacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concepto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    comprobante = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    autorizado = table.Column<bool>(type: "boolean", nullable: false),
                    capturado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    capturado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidaciones_gastos", x => x.id);
                    table.ForeignKey(
                        name: "fk_liquidaciones_gastos_liquidaciones_liquidacion_id",
                        column: x => x.liquidacion_id,
                        principalSchema: "logistica",
                        principalTable: "liquidaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_liquidaciones_estado",
                schema: "logistica",
                table: "liquidaciones",
                sql: "estado IN ('Abierta', 'Cerrada', 'ProductoConfirmado', 'Revisada')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_liquidaciones_formato",
                schema: "logistica",
                table: "liquidaciones",
                sql: "formato IN ('Completo', 'Simplificado')");

            migrationBuilder.CreateIndex(
                name: "ix_liquidaciones_gastos_liquidacion_id",
                schema: "logistica",
                table: "liquidaciones_gastos",
                column: "liquidacion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "liquidaciones_gastos",
                schema: "logistica");

            migrationBuilder.DropCheckConstraint(
                name: "ck_liquidaciones_estado",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropCheckConstraint(
                name: "ck_liquidaciones_formato",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "cargado_inicial",
                schema: "logistica",
                table: "liquidaciones_partidas");

            migrationBuilder.DropColumn(
                name: "importe_venta",
                schema: "logistica",
                table: "liquidaciones_partidas");

            migrationBuilder.DropColumn(
                name: "recargado",
                schema: "logistica",
                table: "liquidaciones_partidas");

            migrationBuilder.DropColumn(
                name: "diferencia_precios",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "formato",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "producto_confirmado_en",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "producto_confirmado_por",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "revisada_en",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "revisada_por",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "total_cheques",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "total_cobros",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "total_credito",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "total_gastos",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "total_transferencias",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.DropColumn(
                name: "total_venta",
                schema: "logistica",
                table: "liquidaciones");

            migrationBuilder.AddCheckConstraint(
                name: "ck_liquidaciones_estado",
                schema: "logistica",
                table: "liquidaciones",
                sql: "estado IN ('Abierta', 'Cerrada')");
        }
    }
}
