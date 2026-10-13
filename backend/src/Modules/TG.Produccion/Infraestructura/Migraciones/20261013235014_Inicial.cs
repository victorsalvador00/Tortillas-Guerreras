using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Produccion.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS produccion;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS produccion.folio_op_seq;");

            migrationBuilder.EnsureSchema(
                name: "produccion");

            migrationBuilder.CreateTable(
                name: "lineas",
                schema: "produccion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lineas", x => x.id);
                    table.CheckConstraint("ck_lineas_tipo", "tipo IN ('Molino', 'Tortilladora', 'Freidora', 'Empaque', 'Otro')");
                });

            migrationBuilder.CreateTable(
                name: "ordenes_produccion",
                schema: "produccion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "text", nullable: false),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    turno = table.Column<string>(type: "text", nullable: false),
                    receta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cantidad_planeada = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cantidad_producida = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    almacen_consumo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    almacen_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    liberada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    iniciada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    terminada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responsable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    costo_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    costo_unitario = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    notas = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ordenes_produccion", x => x.id);
                    table.CheckConstraint("ck_ordenes_produccion_estado", "estado IN ('Planeada', 'Liberada', 'EnProceso', 'Terminada', 'Cancelada')");
                    table.CheckConstraint("ck_ordenes_produccion_turno", "turno IN ('Matutino', 'Vespertino', 'Nocturno')");
                });

            migrationBuilder.CreateTable(
                name: "recetas",
                schema: "produccion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    rendimiento_cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    merma_esperada_pct = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    vigente_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notas = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recetas", x => x.id);
                    table.CheckConstraint("ck_recetas_estado", "estado IN ('Borrador', 'Vigente', 'Obsoleta')");
                });

            migrationBuilder.CreateTable(
                name: "ordenes_produccion_consumos",
                schema: "produccion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_produccion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad_teorica_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cantidad_real_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    lote_consumido = table.Column<string>(type: "text", nullable: true),
                    costo_unitario = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ordenes_produccion_consumos", x => x.id);
                    table.ForeignKey(
                        name: "fk_ordenes_produccion_consumos_ordenes_produccion_orden_produc",
                        column: x => x.orden_produccion_id,
                        principalSchema: "produccion",
                        principalTable: "ordenes_produccion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ordenes_produccion_mermas",
                schema: "produccion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_produccion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    motivo_merma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notas = table.Column<string>(type: "text", nullable: true),
                    registrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ordenes_produccion_mermas", x => x.id);
                    table.ForeignKey(
                        name: "fk_ordenes_produccion_mermas_ordenes_produccion_orden_producci",
                        column: x => x.orden_produccion_id,
                        principalSchema: "produccion",
                        principalTable: "ordenes_produccion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recetas_componentes",
                schema: "produccion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factor_a_base = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cantidad_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    orden = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recetas_componentes", x => x.id);
                    table.ForeignKey(
                        name: "fk_recetas_componentes_recetas_receta_id",
                        column: x => x.receta_id,
                        principalSchema: "produccion",
                        principalTable: "recetas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_lineas_codigo",
                schema: "produccion",
                table: "lineas",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_produccion_fecha_operativa",
                schema: "produccion",
                table: "ordenes_produccion",
                column: "fecha_operativa");

            migrationBuilder.CreateIndex(
                name: "ux_ordenes_produccion_folio",
                schema: "produccion",
                table: "ordenes_produccion",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_produccion_consumos_orden_produccion_id",
                schema: "produccion",
                table: "ordenes_produccion_consumos",
                column: "orden_produccion_id");

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_produccion_mermas_orden_produccion_id",
                schema: "produccion",
                table: "ordenes_produccion_mermas",
                column: "orden_produccion_id");

            migrationBuilder.CreateIndex(
                name: "ux_recetas_producto_version",
                schema: "produccion",
                table: "recetas",
                columns: new[] { "producto_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_recetas_producto_vigente",
                schema: "produccion",
                table: "recetas",
                column: "producto_id",
                unique: true,
                filter: "estado = 'Vigente'");

            migrationBuilder.CreateIndex(
                name: "ix_recetas_componentes_receta_id",
                schema: "produccion",
                table: "recetas_componentes",
                column: "receta_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lineas",
                schema: "produccion");

            migrationBuilder.DropTable(
                name: "ordenes_produccion_consumos",
                schema: "produccion");

            migrationBuilder.DropTable(
                name: "ordenes_produccion_mermas",
                schema: "produccion");

            migrationBuilder.DropTable(
                name: "recetas_componentes",
                schema: "produccion");

            migrationBuilder.DropTable(
                name: "ordenes_produccion",
                schema: "produccion");

            migrationBuilder.DropTable(
                name: "recetas",
                schema: "produccion");
        }
    }
}
