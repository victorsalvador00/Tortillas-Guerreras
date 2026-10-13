using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Inventario.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS inventario;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS inventario.folio_aj_seq;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS inventario.folio_tr_seq;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS inventario.folio_cf_seq;");

            migrationBuilder.EnsureSchema(
                name: "inventario");

            migrationBuilder.CreateTable(
                name: "almacenes",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    ruta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    permite_negativo = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_almacenes", x => x.id);
                    table.CheckConstraint("ck_almacenes_tipo", "tipo IN ('MateriaPrima', 'ProductoTerminado', 'General', 'Ruta')");
                });

            migrationBuilder.CreateTable(
                name: "conteos_fisicos",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "text", nullable: false),
                    almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    notas = table.Column<string>(type: "text", nullable: true),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cerrado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conteos_fisicos", x => x.id);
                    table.CheckConstraint("ck_conteos_fisicos_estado", "estado IN ('Abierto', 'Cerrado', 'Cancelado')");
                });

            migrationBuilder.CreateTable(
                name: "costos_producto",
                schema: "inventario",
                columns: table => new
                {
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    costo_promedio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_costos_producto", x => x.producto_id);
                });

            migrationBuilder.CreateTable(
                name: "existencias",
                schema: "inventario",
                columns: table => new
                {
                    almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "text", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fecha_caducidad = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_entrada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_existencias", x => new { x.almacen_id, x.producto_id, x.lote });
                });

            migrationBuilder.CreateTable(
                name: "movimientos",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "text", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    costo_unitario = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    costo_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    existencia_resultante = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    documento_tipo = table.Column<string>(type: "text", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_folio = table.Column<string>(type: "text", nullable: false),
                    motivo_merma_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispositivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notas = table.Column<string>(type: "text", nullable: true),
                    fecha_caducidad = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimientos", x => x.id);
                    table.CheckConstraint("ck_movimientos_documento_tipo", "documento_tipo IN ('Recepcion', 'OrdenProduccion', 'Envio', 'Venta', 'Retorno', 'Liquidacion', 'Ajuste', 'Traspaso', 'ConteoFisico')");
                    table.CheckConstraint("ck_movimientos_tipo", "tipo IN ('EntradaCompra', 'DevolucionProveedor', 'EntradaProduccion', 'SalidaConsumo', 'Merma', 'TraspasoSalida', 'TraspasoEntrada', 'Venta', 'RetornoCliente', 'AjusteEntrada', 'AjusteSalida')");
                });

            migrationBuilder.CreateTable(
                name: "traspasos",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "text", nullable: false),
                    almacen_origen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    almacen_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    notas = table.Column<string>(type: "text", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_traspasos", x => x.id);
                    table.CheckConstraint("ck_traspasos_estado", "estado IN ('Registrado', 'Cancelado')");
                });

            migrationBuilder.CreateTable(
                name: "conteos_partidas",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conteo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "text", nullable: false),
                    cantidad_sistema = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cantidad_contada = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    diferencia = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    capturado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conteos_partidas", x => x.id);
                    table.ForeignKey(
                        name: "fk_conteos_partidas_conteos_fisicos_conteo_id",
                        column: x => x.conteo_id,
                        principalSchema: "inventario",
                        principalTable: "conteos_fisicos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "traspasos_partidas",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    traspaso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "text", nullable: false),
                    cantidad_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_traspasos_partidas", x => x.id);
                    table.ForeignKey(
                        name: "fk_traspasos_partidas_traspasos_traspaso_id",
                        column: x => x.traspaso_id,
                        principalSchema: "inventario",
                        principalTable: "traspasos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_almacenes_codigo",
                schema: "inventario",
                table: "almacenes",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_almacenes_ruta_id",
                schema: "inventario",
                table: "almacenes",
                column: "ruta_id",
                unique: true,
                filter: "ruta_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_conteos_fisicos_folio",
                schema: "inventario",
                table: "conteos_fisicos",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_conteos_partidas_producto_lote",
                schema: "inventario",
                table: "conteos_partidas",
                columns: new[] { "conteo_id", "producto_id", "lote" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_existencias_producto_id",
                schema: "inventario",
                table: "existencias",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_documento",
                schema: "inventario",
                table: "movimientos",
                columns: new[] { "documento_tipo", "documento_id" });

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_fecha_operativa",
                schema: "inventario",
                table: "movimientos",
                column: "fecha_operativa");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_producto_almacen_fecha",
                schema: "inventario",
                table: "movimientos",
                columns: new[] { "producto_id", "almacen_id", "fecha_hora" });

            migrationBuilder.CreateIndex(
                name: "ux_traspasos_folio",
                schema: "inventario",
                table: "traspasos",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_traspasos_partidas_traspaso_id",
                schema: "inventario",
                table: "traspasos_partidas",
                column: "traspaso_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "almacenes",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "conteos_partidas",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "costos_producto",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "existencias",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "movimientos",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "traspasos_partidas",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "conteos_fisicos",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "traspasos",
                schema: "inventario");
        }
    }
}
