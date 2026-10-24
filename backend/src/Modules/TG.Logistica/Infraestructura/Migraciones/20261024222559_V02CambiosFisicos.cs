using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Logistica.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class V02CambiosFisicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_retornos_tipo",
                schema: "logistica",
                table: "retornos");

            migrationBuilder.AddColumn<Guid>(
                name: "cambio_pendiente_id",
                schema: "logistica",
                table: "retornos_partidas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sentido",
                schema: "logistica",
                table: "retornos_partidas",
                type: "text",
                nullable: false,
                defaultValue: "Retirado");

            migrationBuilder.AddColumn<decimal>(
                name: "cambios_entregados",
                schema: "logistica",
                table: "liquidaciones_partidas",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "cambios_pendientes",
                schema: "logistica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ruta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cantidad_saldada_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    retorno_origen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    cancelado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version_sync = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cambios_pendientes", x => x.id);
                    table.CheckConstraint("ck_cambios_pendientes_estado", "estado IN ('Pendiente', 'Saldado', 'Cancelado')");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_retornos_partidas_sentido",
                schema: "logistica",
                table: "retornos_partidas",
                sql: "sentido IN ('Retirado', 'Entregado')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_retornos_tipo",
                schema: "logistica",
                table: "retornos",
                sql: "tipo IN ('DevolucionCliente', 'RechazoEntrega', 'CambioFisico')");

            migrationBuilder.CreateIndex(
                name: "ix_cambios_pendientes_cliente_estado",
                schema: "logistica",
                table: "cambios_pendientes",
                columns: new[] { "cliente_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_cambios_pendientes_version_sync",
                schema: "logistica",
                table: "cambios_pendientes",
                column: "version_sync");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cambios_pendientes",
                schema: "logistica");

            migrationBuilder.DropCheckConstraint(
                name: "ck_retornos_partidas_sentido",
                schema: "logistica",
                table: "retornos_partidas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_retornos_tipo",
                schema: "logistica",
                table: "retornos");

            migrationBuilder.DropColumn(
                name: "cambio_pendiente_id",
                schema: "logistica",
                table: "retornos_partidas");

            migrationBuilder.DropColumn(
                name: "sentido",
                schema: "logistica",
                table: "retornos_partidas");

            migrationBuilder.DropColumn(
                name: "cambios_entregados",
                schema: "logistica",
                table: "liquidaciones_partidas");

            migrationBuilder.AddCheckConstraint(
                name: "ck_retornos_tipo",
                schema: "logistica",
                table: "retornos",
                sql: "tipo IN ('DevolucionCliente', 'RechazoEntrega')");
        }
    }
}
