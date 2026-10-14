using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Logistica.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS logistica;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS logistica.folio_env_seq;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS logistica.folio_ret_seq;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS logistica.folio_liq_seq;");

            migrationBuilder.EnsureSchema(
                name: "logistica");

            migrationBuilder.CreateTable(
                name: "envios",
                schema: "logistica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "text", nullable: false),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    ruta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    almacen_origen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    almacen_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    despachado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    liquidacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notas = table.Column<string>(type: "text", nullable: true),
                    version_sync = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_envios", x => x.id);
                    table.CheckConstraint("ck_envios_estado", "estado IN ('Preparado', 'EnRuta', 'Liquidado', 'Cancelado')");
                    table.CheckConstraint("ck_envios_tipo", "tipo IN ('CargaInicial', 'Recarga')");
                });

            migrationBuilder.CreateTable(
                name: "liquidaciones",
                schema: "logistica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "text", nullable: false),
                    ruta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    efectivo_esperado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    efectivo_entregado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    diferencia_efectivo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    cerrada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    cerrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cerrada_forzada = table.Column<bool>(type: "boolean", nullable: false),
                    notas = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidaciones", x => x.id);
                    table.CheckConstraint("ck_liquidaciones_estado", "estado IN ('Abierta', 'Cerrada')");
                });

            migrationBuilder.CreateTable(
                name: "retornos",
                schema: "logistica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "text", nullable: false),
                    op_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    ruta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    venta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_hora_dispositivo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dispositivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    latitud = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitud = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    precision_m = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false),
                    notas = table.Column<string>(type: "text", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retornos", x => x.id);
                    table.CheckConstraint("ck_retornos_estado", "estado IN ('Registrado', 'Cancelado')");
                    table.CheckConstraint("ck_retornos_tipo", "tipo IN ('DevolucionCliente', 'RechazoEntrega')");
                });

            migrationBuilder.CreateTable(
                name: "rutas",
                schema: "logistica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vehiculo_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_rutas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rutas_clientes",
                schema: "logistica",
                columns: table => new
                {
                    ruta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_visita = table.Column<short>(type: "smallint", nullable: false),
                    dias_visita = table.Column<short[]>(type: "smallint[]", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version_sync = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rutas_clientes", x => new { x.ruta_id, x.cliente_id });
                });

            migrationBuilder.CreateTable(
                name: "vehiculos",
                schema: "logistica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    placas = table.Column<string>(type: "text", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    capacidad_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehiculos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "envios_partidas",
                schema: "logistica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    envio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "text", nullable: false),
                    cantidad_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version_sync = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_envios_partidas", x => x.id);
                    table.ForeignKey(
                        name: "fk_envios_partidas_envios_envio_id",
                        column: x => x.envio_id,
                        principalSchema: "logistica",
                        principalTable: "envios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "liquidaciones_partidas",
                schema: "logistica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    liquidacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "text", nullable: false),
                    cargado = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vendido = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    devuelto_clientes_vendible = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    devuelto_clientes_no_vendible = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    sobrante_esperado = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    sobrante_contado = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    diferencia = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    destino_sobrante = table.Column<string>(type: "text", nullable: false),
                    motivo_merma_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidaciones_partidas", x => x.id);
                    table.CheckConstraint("ck_liquidaciones_partidas_destino_sobrante", "destino_sobrante IN ('Reintegrar', 'Merma', 'Reproceso')");
                    table.ForeignKey(
                        name: "fk_liquidaciones_partidas_liquidaciones_liquidacion_id",
                        column: x => x.liquidacion_id,
                        principalSchema: "logistica",
                        principalTable: "liquidaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retornos_partidas",
                schema: "logistica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    retorno_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "text", nullable: false),
                    cantidad_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    condicion = table.Column<string>(type: "text", nullable: false),
                    motivo_merma_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retornos_partidas", x => x.id);
                    table.CheckConstraint("ck_retornos_partidas_condicion", "condicion IN ('Vendible', 'NoVendible')");
                    table.ForeignKey(
                        name: "fk_retornos_partidas_retornos_retorno_id",
                        column: x => x.retorno_id,
                        principalSchema: "logistica",
                        principalTable: "retornos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_envios_ruta_fecha",
                schema: "logistica",
                table: "envios",
                columns: new[] { "ruta_id", "fecha_operativa" });

            migrationBuilder.CreateIndex(
                name: "ux_envios_folio",
                schema: "logistica",
                table: "envios",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_envios_partidas_envio_id",
                schema: "logistica",
                table: "envios_partidas",
                column: "envio_id");

            migrationBuilder.CreateIndex(
                name: "ux_liquidaciones_folio",
                schema: "logistica",
                table: "liquidaciones",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_liquidaciones_ruta_fecha",
                schema: "logistica",
                table: "liquidaciones",
                columns: new[] { "ruta_id", "fecha_operativa" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_liquidaciones_partidas_producto_lote",
                schema: "logistica",
                table: "liquidaciones_partidas",
                columns: new[] { "liquidacion_id", "producto_id", "lote" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_retornos_ruta_fecha",
                schema: "logistica",
                table: "retornos",
                columns: new[] { "ruta_id", "fecha_operativa" });

            migrationBuilder.CreateIndex(
                name: "ux_retornos_folio",
                schema: "logistica",
                table: "retornos",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_retornos_op_id",
                schema: "logistica",
                table: "retornos",
                column: "op_id",
                unique: true,
                filter: "op_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_retornos_partidas_retorno_id",
                schema: "logistica",
                table: "retornos_partidas",
                column: "retorno_id");

            migrationBuilder.CreateIndex(
                name: "ix_rutas_version_sync",
                schema: "logistica",
                table: "rutas",
                column: "version_sync");

            migrationBuilder.CreateIndex(
                name: "ux_rutas_codigo",
                schema: "logistica",
                table: "rutas",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rutas_clientes_ruta_version",
                schema: "logistica",
                table: "rutas_clientes",
                columns: new[] { "ruta_id", "version_sync" });

            migrationBuilder.CreateIndex(
                name: "ux_vehiculos_placas",
                schema: "logistica",
                table: "vehiculos",
                column: "placas",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "envios_partidas",
                schema: "logistica");

            migrationBuilder.DropTable(
                name: "liquidaciones_partidas",
                schema: "logistica");

            migrationBuilder.DropTable(
                name: "retornos_partidas",
                schema: "logistica");

            migrationBuilder.DropTable(
                name: "rutas",
                schema: "logistica");

            migrationBuilder.DropTable(
                name: "rutas_clientes",
                schema: "logistica");

            migrationBuilder.DropTable(
                name: "vehiculos",
                schema: "logistica");

            migrationBuilder.DropTable(
                name: "envios",
                schema: "logistica");

            migrationBuilder.DropTable(
                name: "liquidaciones",
                schema: "logistica");

            migrationBuilder.DropTable(
                name: "retornos",
                schema: "logistica");
        }
    }
}
