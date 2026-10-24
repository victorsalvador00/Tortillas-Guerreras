using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Ventas.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class V03PedidosV02 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pedidos_cliente_id",
                schema: "ventas",
                table: "pedidos");

            migrationBuilder.AddColumn<decimal>(
                name: "cantidad_solicitada",
                schema: "ventas",
                table: "pedidos_partidas",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notas",
                schema: "ventas",
                table: "pedidos_partidas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo",
                schema: "ventas",
                table: "pedidos_partidas",
                type: "text",
                nullable: false,
                defaultValue: "Venta");

            migrationBuilder.AddColumn<Guid>(
                name: "capturado_por",
                schema: "ventas",
                table: "pedidos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmado_en",
                schema: "ventas",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "confirmado_por",
                schema: "ventas",
                table: "pedidos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "dispositivo_id",
                schema: "ventas",
                table: "pedidos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "es_ruta",
                schema: "ventas",
                table: "pedidos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "op_id",
                schema: "ventas",
                table: "pedidos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origen",
                schema: "ventas",
                table: "pedidos",
                type: "text",
                nullable: false,
                defaultValue: "Web");

            // Los pedidos existentes los capturó quien los creó (todos desde la web).
            migrationBuilder.Sql("UPDATE ventas.pedidos SET capturado_por = creado_por;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedidos_partidas_tipo",
                schema: "ventas",
                table: "pedidos_partidas",
                sql: "tipo IN ('Venta', 'CambioFisico')");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_fecha_entrega_estado",
                schema: "ventas",
                table: "pedidos",
                columns: new[] { "fecha_entrega", "estado" });

            migrationBuilder.CreateIndex(
                name: "ux_pedidos_op_id",
                schema: "ventas",
                table: "pedidos",
                column: "op_id",
                unique: true,
                filter: "op_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_pedidos_ruta_fecha_entrega",
                schema: "ventas",
                table: "pedidos",
                columns: new[] { "cliente_id", "fecha_entrega" },
                unique: true,
                filter: "es_ruta AND estado <> 'Cancelado'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedidos_origen",
                schema: "ventas",
                table: "pedidos",
                sql: "origen IN ('App', 'Web')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pedidos_partidas_tipo",
                schema: "ventas",
                table: "pedidos_partidas");

            migrationBuilder.DropIndex(
                name: "ix_pedidos_fecha_entrega_estado",
                schema: "ventas",
                table: "pedidos");

            migrationBuilder.DropIndex(
                name: "ux_pedidos_op_id",
                schema: "ventas",
                table: "pedidos");

            migrationBuilder.DropIndex(
                name: "ux_pedidos_ruta_fecha_entrega",
                schema: "ventas",
                table: "pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pedidos_origen",
                schema: "ventas",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "cantidad_solicitada",
                schema: "ventas",
                table: "pedidos_partidas");

            migrationBuilder.DropColumn(
                name: "notas",
                schema: "ventas",
                table: "pedidos_partidas");

            migrationBuilder.DropColumn(
                name: "tipo",
                schema: "ventas",
                table: "pedidos_partidas");

            migrationBuilder.DropColumn(
                name: "capturado_por",
                schema: "ventas",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "confirmado_en",
                schema: "ventas",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "confirmado_por",
                schema: "ventas",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "dispositivo_id",
                schema: "ventas",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "es_ruta",
                schema: "ventas",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "op_id",
                schema: "ventas",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "origen",
                schema: "ventas",
                table: "pedidos");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_cliente_id",
                schema: "ventas",
                table: "pedidos",
                column: "cliente_id");
        }
    }
}
