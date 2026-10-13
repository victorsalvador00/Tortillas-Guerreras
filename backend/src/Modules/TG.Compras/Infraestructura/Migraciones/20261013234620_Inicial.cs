using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Compras.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS compras;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS compras.folio_oc_seq;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS compras.folio_rec_seq;");

            migrationBuilder.EnsureSchema(
                name: "compras");

            migrationBuilder.CreateTable(
                name: "proveedores",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "text", nullable: false),
                    razon_social = table.Column<string>(type: "text", nullable: false),
                    nombre_comercial = table.Column<string>(type: "text", nullable: true),
                    rfc = table.Column<string>(type: "text", nullable: true),
                    contacto = table.Column<string>(type: "text", nullable: true),
                    telefono = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    dias_credito = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proveedores", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ordenes_compra",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "text", nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_entrega_esperada = table.Column<DateOnly>(type: "date", nullable: true),
                    almacen_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notas = table.Column<string>(type: "text", nullable: true),
                    autorizada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    autorizada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ordenes_compra", x => x.id);
                    table.CheckConstraint("ck_ordenes_compra_estado", "estado IN ('Borrador', 'Autorizada', 'RecibidaParcial', 'Recibida', 'Cerrada', 'Cancelada')");
                    table.ForeignKey(
                        name: "fk_ordenes_compra_proveedores_proveedor_id",
                        column: x => x.proveedor_id,
                        principalSchema: "compras",
                        principalTable: "proveedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "proveedores_productos",
                schema: "compras",
                columns: table => new
                {
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ultimo_precio = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ultima_compra_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proveedores_productos", x => new { x.proveedor_id, x.producto_id });
                    table.ForeignKey(
                        name: "fk_proveedores_productos_proveedores_proveedor_id",
                        column: x => x.proveedor_id,
                        principalSchema: "compras",
                        principalTable: "proveedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ordenes_compra_partidas",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factor_a_base = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cantidad_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tasa_iva = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cantidad_recibida_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ordenes_compra_partidas", x => x.id);
                    table.ForeignKey(
                        name: "fk_ordenes_compra_partidas_ordenes_compra_orden_compra_id",
                        column: x => x.orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "ordenes_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recepciones",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "text", nullable: false),
                    orden_compra_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    factura_proveedor = table.Column<string>(type: "text", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false),
                    notas = table.Column<string>(type: "text", nullable: true),
                    cancelada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recepciones", x => x.id);
                    table.CheckConstraint("ck_recepciones_estado", "estado IN ('Registrada', 'Cancelada')");
                    table.ForeignKey(
                        name: "fk_recepciones_ordenes_compra_orden_compra_id",
                        column: x => x.orden_compra_id,
                        principalSchema: "compras",
                        principalTable: "ordenes_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recepciones_proveedores_proveedor_id",
                        column: x => x.proveedor_id,
                        principalSchema: "compras",
                        principalTable: "proveedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recepciones_partidas",
                schema: "compras",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recepcion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_compra_partida_id = table.Column<Guid>(type: "uuid", nullable: true),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factor_a_base = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cantidad_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    costo_unitario_base = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    lote = table.Column<string>(type: "text", nullable: true),
                    fecha_caducidad = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recepciones_partidas", x => x.id);
                    table.ForeignKey(
                        name: "fk_recepciones_partidas_recepciones_recepcion_id",
                        column: x => x.recepcion_id,
                        principalSchema: "compras",
                        principalTable: "recepciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_compra_proveedor_fecha",
                schema: "compras",
                table: "ordenes_compra",
                columns: new[] { "proveedor_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ux_ordenes_compra_folio",
                schema: "compras",
                table: "ordenes_compra",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_compra_partidas_orden_compra_id",
                schema: "compras",
                table: "ordenes_compra_partidas",
                column: "orden_compra_id");

            migrationBuilder.CreateIndex(
                name: "ux_proveedores_codigo",
                schema: "compras",
                table: "proveedores",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recepciones_orden_compra_id",
                schema: "compras",
                table: "recepciones",
                column: "orden_compra_id");

            migrationBuilder.CreateIndex(
                name: "ix_recepciones_proveedor_fecha",
                schema: "compras",
                table: "recepciones",
                columns: new[] { "proveedor_id", "fecha_operativa" });

            migrationBuilder.CreateIndex(
                name: "ux_recepciones_folio",
                schema: "compras",
                table: "recepciones",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recepciones_partidas_recepcion_id",
                schema: "compras",
                table: "recepciones_partidas",
                column: "recepcion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ordenes_compra_partidas",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "proveedores_productos",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "recepciones_partidas",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "recepciones",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "ordenes_compra",
                schema: "compras");

            migrationBuilder.DropTable(
                name: "proveedores",
                schema: "compras");
        }
    }
}
