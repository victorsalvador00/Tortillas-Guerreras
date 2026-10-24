using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Ventas.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class V02GruposPrecios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_incidencias_tipo",
                schema: "ventas",
                table: "incidencias");

            migrationBuilder.AddColumn<decimal>(
                name: "precio_referencia",
                schema: "ventas",
                table: "ventas_partidas",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "grupo_cliente_id",
                schema: "ventas",
                table: "clientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "grupos_clientes",
                schema: "ventas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    canal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lista_precio_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version_sync = table.Column<long>(type: "bigint", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grupos_clientes", x => x.id);
                    table.ForeignKey(
                        name: "fk_grupos_clientes_canales_canal_id",
                        column: x => x.canal_id,
                        principalSchema: "ventas",
                        principalTable: "canales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_grupos_clientes_listas_precio_lista_precio_id",
                        column: x => x.lista_precio_id,
                        principalSchema: "ventas",
                        principalTable: "listas_precio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "precios_cliente",
                schema: "ventas",
                columns: table => new
                {
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version_sync = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_precios_cliente", x => new { x.cliente_id, x.producto_id, x.unidad_id });
                    table.ForeignKey(
                        name: "fk_precios_cliente_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalSchema: "ventas",
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "productos_exclusivos",
                schema: "ventas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    grupo_cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version_sync = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_productos_exclusivos", x => x.id);
                    table.CheckConstraint("ck_productos_exclusivos_cliente_o_grupo", "(cliente_id IS NULL) <> (grupo_cliente_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_productos_exclusivos_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalSchema: "ventas",
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_productos_exclusivos_grupos_clientes_grupo_cliente_id",
                        column: x => x.grupo_cliente_id,
                        principalSchema: "ventas",
                        principalTable: "grupos_clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_incidencias_tipo",
                schema: "ventas",
                table: "incidencias",
                sql: "tipo IN ('ExistenciaNegativaRuta', 'GpsFueraDeRango', 'GpsImpreciso', 'SinGps', 'PrecioDistinto', 'UbicacionPorAprobar', 'DiferenciaLiquidacion', 'CreditoExcedido', 'TotalDistinto', 'ProductoExclusivo')");

            migrationBuilder.CreateIndex(
                name: "ix_clientes_grupo_cliente_id",
                schema: "ventas",
                table: "clientes",
                column: "grupo_cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_grupos_clientes_canal_id",
                schema: "ventas",
                table: "grupos_clientes",
                column: "canal_id");

            migrationBuilder.CreateIndex(
                name: "ix_grupos_clientes_lista_precio_id",
                schema: "ventas",
                table: "grupos_clientes",
                column: "lista_precio_id");

            migrationBuilder.CreateIndex(
                name: "ix_grupos_clientes_version_sync",
                schema: "ventas",
                table: "grupos_clientes",
                column: "version_sync");

            migrationBuilder.CreateIndex(
                name: "ux_grupos_clientes_clave",
                schema: "ventas",
                table: "grupos_clientes",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_precios_cliente_version_sync",
                schema: "ventas",
                table: "precios_cliente",
                column: "version_sync");

            migrationBuilder.CreateIndex(
                name: "ix_productos_exclusivos_cliente_id",
                schema: "ventas",
                table: "productos_exclusivos",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_productos_exclusivos_grupo_cliente_id",
                schema: "ventas",
                table: "productos_exclusivos",
                column: "grupo_cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_productos_exclusivos_version_sync",
                schema: "ventas",
                table: "productos_exclusivos",
                column: "version_sync");

            migrationBuilder.CreateIndex(
                name: "ux_productos_exclusivos",
                schema: "ventas",
                table: "productos_exclusivos",
                columns: new[] { "producto_id", "cliente_id", "grupo_cliente_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddForeignKey(
                name: "fk_clientes_grupos_clientes_grupo_cliente_id",
                schema: "ventas",
                table: "clientes",
                column: "grupo_cliente_id",
                principalSchema: "ventas",
                principalTable: "grupos_clientes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_clientes_grupos_clientes_grupo_cliente_id",
                schema: "ventas",
                table: "clientes");

            migrationBuilder.DropTable(
                name: "precios_cliente",
                schema: "ventas");

            migrationBuilder.DropTable(
                name: "productos_exclusivos",
                schema: "ventas");

            migrationBuilder.DropTable(
                name: "grupos_clientes",
                schema: "ventas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_incidencias_tipo",
                schema: "ventas",
                table: "incidencias");

            migrationBuilder.DropIndex(
                name: "ix_clientes_grupo_cliente_id",
                schema: "ventas",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "precio_referencia",
                schema: "ventas",
                table: "ventas_partidas");

            migrationBuilder.DropColumn(
                name: "grupo_cliente_id",
                schema: "ventas",
                table: "clientes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_incidencias_tipo",
                schema: "ventas",
                table: "incidencias",
                sql: "tipo IN ('ExistenciaNegativaRuta', 'GpsFueraDeRango', 'GpsImpreciso', 'SinGps', 'PrecioDistinto', 'UbicacionPorAprobar', 'DiferenciaLiquidacion', 'CreditoExcedido', 'TotalDistinto')");
        }
    }
}
