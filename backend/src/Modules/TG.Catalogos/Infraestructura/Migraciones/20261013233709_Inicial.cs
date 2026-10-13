using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Catalogos.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Secuencia global de version_sync (05-sincronizacion-offline); la usan todos los módulos.
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS sync;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS sync.version_seq;");

            migrationBuilder.EnsureSchema(
                name: "catalogos");

            migrationBuilder.CreateTable(
                name: "motivos_merma",
                schema: "catalogos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    aplica_produccion = table.Column<bool>(type: "boolean", nullable: false),
                    aplica_almacen = table.Column<bool>(type: "boolean", nullable: false),
                    aplica_ruta = table.Column<bool>(type: "boolean", nullable: false),
                    aplica_retorno = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version_sync = table.Column<long>(type: "bigint", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_motivos_merma", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parametros",
                schema: "catalogos",
                columns: table => new
                {
                    clave = table.Column<string>(type: "text", nullable: false),
                    valor = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    version_sync = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parametros", x => x.clave);
                    table.CheckConstraint("ck_parametros_tipo", "tipo IN ('int', 'decimal', 'bool', 'text')");
                });

            migrationBuilder.CreateTable(
                name: "unidades_medida",
                schema: "catalogos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    clave_sat = table.Column<string>(type: "text", nullable: false),
                    decimales = table.Column<short>(type: "smallint", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unidades_medida", x => x.id);
                    table.CheckConstraint("ck_unidades_medida_tipo", "tipo IN ('Masa', 'Volumen', 'Pieza')");
                });

            migrationBuilder.CreateTable(
                name: "productos",
                schema: "catalogos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    unidad_base_id = table.Column<Guid>(type: "uuid", nullable: false),
                    es_comprable = table.Column<bool>(type: "boolean", nullable: false),
                    es_producible = table.Column<bool>(type: "boolean", nullable: false),
                    es_vendible = table.Column<bool>(type: "boolean", nullable: false),
                    maneja_lote = table.Column<bool>(type: "boolean", nullable: false),
                    vida_util_dias = table.Column<int>(type: "integer", nullable: true),
                    clave_prod_serv_sat = table.Column<string>(type: "text", nullable: true),
                    objeto_impuesto_sat = table.Column<string>(type: "text", nullable: false),
                    tasa_iva = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    tasa_ieps = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    codigo_barras = table.Column<string>(type: "text", nullable: true),
                    stock_minimo = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version_sync = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_productos", x => x.id);
                    table.CheckConstraint("ck_productos_tipo", "tipo IN ('MateriaPrima', 'Empaque', 'Semiterminado', 'Terminado', 'Reventa')");
                    table.ForeignKey(
                        name: "fk_productos_unidades_unidad_base_id",
                        column: x => x.unidad_base_id,
                        principalSchema: "catalogos",
                        principalTable: "unidades_medida",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "productos_unidades",
                schema: "catalogos",
                columns: table => new
                {
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factor_a_base = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    uso_compra = table.Column<bool>(type: "boolean", nullable: false),
                    uso_venta = table.Column<bool>(type: "boolean", nullable: false),
                    uso_produccion = table.Column<bool>(type: "boolean", nullable: false),
                    predeterminada_venta = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_productos_unidades", x => new { x.producto_id, x.unidad_id });
                    table.ForeignKey(
                        name: "fk_productos_unidades_productos_producto_id",
                        column: x => x.producto_id,
                        principalSchema: "catalogos",
                        principalTable: "productos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_productos_unidades_unidades_unidad_id",
                        column: x => x.unidad_id,
                        principalSchema: "catalogos",
                        principalTable: "unidades_medida",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_motivos_merma_clave",
                schema: "catalogos",
                table: "motivos_merma",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_productos_unidad_base_id",
                schema: "catalogos",
                table: "productos",
                column: "unidad_base_id");

            migrationBuilder.CreateIndex(
                name: "ix_productos_version_sync",
                schema: "catalogos",
                table: "productos",
                column: "version_sync");

            migrationBuilder.CreateIndex(
                name: "ux_productos_sku",
                schema: "catalogos",
                table: "productos",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_productos_unidades_unidad_id",
                schema: "catalogos",
                table: "productos_unidades",
                column: "unidad_id");

            migrationBuilder.CreateIndex(
                name: "ux_unidades_medida_clave",
                schema: "catalogos",
                table: "unidades_medida",
                column: "clave",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "motivos_merma",
                schema: "catalogos");

            migrationBuilder.DropTable(
                name: "parametros",
                schema: "catalogos");

            migrationBuilder.DropTable(
                name: "productos_unidades",
                schema: "catalogos");

            migrationBuilder.DropTable(
                name: "productos",
                schema: "catalogos");

            migrationBuilder.DropTable(
                name: "unidades_medida",
                schema: "catalogos");
        }
    }
}
