using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Ventas.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS ventas;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS ventas.folio_vta_seq;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS ventas.folio_ped_seq;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS ventas.folio_cob_seq;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS ventas.folio_cli_seq;");

            migrationBuilder.EnsureSchema(
                name: "ventas");

            migrationBuilder.CreateTable(
                name: "canales",
                schema: "ventas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    requiere_factura_default = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_canales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cobros",
                schema: "ventas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    op_id = table.Column<Guid>(type: "uuid", nullable: true),
                    folio = table.Column<string>(type: "text", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ruta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dispositivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    forma_pago = table.Column<string>(type: "text", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobros", x => x.id);
                    table.CheckConstraint("ck_cobros_estado", "estado IN ('Registrado', 'Cancelado')");
                    table.CheckConstraint("ck_cobros_forma_pago", "forma_pago IN ('Efectivo', 'Transferencia', 'Credito')");
                });

            migrationBuilder.CreateTable(
                name: "incidencias",
                schema: "ventas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    entidad_tipo = table.Column<string>(type: "text", nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    detalle = table.Column<string>(type: "jsonb", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false),
                    revisada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    revisada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notas_revision = table.Column<string>(type: "text", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidencias", x => x.id);
                    table.CheckConstraint("ck_incidencias_estado", "estado IN ('Abierta', 'Revisada')");
                    table.CheckConstraint("ck_incidencias_tipo", "tipo IN ('ExistenciaNegativaRuta', 'GpsFueraDeRango', 'GpsImpreciso', 'SinGps', 'PrecioDistinto', 'UbicacionPorAprobar', 'DiferenciaLiquidacion', 'CreditoExcedido', 'TotalDistinto')");
                });

            migrationBuilder.CreateTable(
                name: "listas_precio",
                schema: "ventas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    canal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    version_sync = table.Column<long>(type: "bigint", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listas_precio", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cobros_aplicaciones",
                schema: "ventas",
                columns: table => new
                {
                    cobro_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobros_aplicaciones", x => new { x.cobro_id, x.venta_id });
                    table.ForeignKey(
                        name: "fk_cobros_aplicaciones_cobros_cobro_id",
                        column: x => x.cobro_id,
                        principalSchema: "ventas",
                        principalTable: "cobros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clientes",
                schema: "ventas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    canal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lista_precio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    condicion_pago = table.Column<string>(type: "text", nullable: false),
                    dias_credito = table.Column<int>(type: "integer", nullable: false),
                    limite_credito = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    requiere_factura = table.Column<bool>(type: "boolean", nullable: false),
                    rfc = table.Column<string>(type: "text", nullable: true),
                    razon_social = table.Column<string>(type: "text", nullable: true),
                    regimen_fiscal = table.Column<string>(type: "text", nullable: true),
                    codigo_postal_fiscal = table.Column<string>(type: "text", nullable: true),
                    uso_cfdi = table.Column<string>(type: "text", nullable: true),
                    email_facturacion = table.Column<string>(type: "text", nullable: true),
                    telefono = table.Column<string>(type: "text", nullable: true),
                    calle = table.Column<string>(type: "text", nullable: true),
                    colonia = table.Column<string>(type: "text", nullable: true),
                    municipio = table.Column<string>(type: "text", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: true),
                    codigo_postal = table.Column<string>(type: "text", nullable: true),
                    latitud = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitud = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    ubicacion_aprobada = table.Column<bool>(type: "boolean", nullable: false),
                    latitud_propuesta = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitud_propuesta = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    es_generico = table.Column<bool>(type: "boolean", nullable: false),
                    ruta_generico = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("pk_clientes", x => x.id);
                    table.CheckConstraint("ck_clientes_condicion_pago", "condicion_pago IN ('Contado', 'Credito')");
                    table.ForeignKey(
                        name: "fk_clientes_canales_canal_id",
                        column: x => x.canal_id,
                        principalSchema: "ventas",
                        principalTable: "canales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_clientes_listas_precio_lista_precio_id",
                        column: x => x.lista_precio_id,
                        principalSchema: "ventas",
                        principalTable: "listas_precio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "listas_precio_partidas",
                schema: "ventas",
                columns: table => new
                {
                    lista_precio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    version_sync = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listas_precio_partidas", x => new { x.lista_precio_id, x.producto_id, x.unidad_id });
                    table.ForeignKey(
                        name: "fk_listas_precio_partidas_listas_precio_lista_precio_id",
                        column: x => x.lista_precio_id,
                        principalSchema: "ventas",
                        principalTable: "listas_precio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pedidos",
                schema: "ventas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio = table.Column<string>(type: "text", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_entrega = table.Column<DateOnly>(type: "date", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false),
                    envio_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ruta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    orden_compra_cliente = table.Column<string>(type: "text", nullable: true),
                    notas = table.Column<string>(type: "text", nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ieps = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version_sync = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos", x => x.id);
                    table.CheckConstraint("ck_pedidos_estado", "estado IN ('Capturado', 'Confirmado', 'Asignado', 'Entregado', 'EntregadoParcial', 'Cancelado')");
                    table.ForeignKey(
                        name: "fk_pedidos_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalSchema: "ventas",
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ventas",
                schema: "ventas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    op_id = table.Column<Guid>(type: "uuid", nullable: true),
                    folio = table.Column<string>(type: "text", nullable: false),
                    origen = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ruta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    almacen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispositivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_hora_dispositivo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_hora_servidor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    condicion_pago = table.Column<string>(type: "text", nullable: false),
                    forma_pago = table.Column<string>(type: "text", nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ieps = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_app = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    latitud = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitud = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    precision_m = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    distancia_cliente_m = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    requiere_revision = table.Column<bool>(type: "boolean", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    motivo_cancelacion = table.Column<string>(type: "text", nullable: true),
                    cancelada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ventas", x => x.id);
                    table.CheckConstraint("ck_ventas_condicion_pago", "condicion_pago IN ('Contado', 'Credito')");
                    table.CheckConstraint("ck_ventas_estado", "estado IN ('Registrada', 'Cancelada')");
                    table.CheckConstraint("ck_ventas_forma_pago", "forma_pago IN ('Efectivo', 'Transferencia', 'Credito')");
                    table.CheckConstraint("ck_ventas_origen", "origen IN ('App', 'Web')");
                    table.CheckConstraint("ck_ventas_tipo", "tipo IN ('VentaRuta', 'EntregaPedido')");
                    table.ForeignKey(
                        name: "fk_ventas_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalSchema: "ventas",
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pedidos_partidas",
                schema: "ventas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factor_a_base = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cantidad_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tasa_iva = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    tasa_ieps = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cantidad_entregada_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos_partidas", x => x.id);
                    table.ForeignKey(
                        name: "fk_pedidos_partidas_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalSchema: "ventas",
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ventas_partidas",
                schema: "ventas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_partida_id = table.Column<Guid>(type: "uuid", nullable: true),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factor_a_base = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cantidad_base = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    lote = table.Column<string>(type: "text", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tasa_iva = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    tasa_ieps = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ieps = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ventas_partidas", x => x.id);
                    table.ForeignKey(
                        name: "fk_ventas_partidas_ventas_venta_id",
                        column: x => x.venta_id,
                        principalSchema: "ventas",
                        principalTable: "ventas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_canales_clave",
                schema: "ventas",
                table: "canales",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clientes_canal_id",
                schema: "ventas",
                table: "clientes",
                column: "canal_id");

            migrationBuilder.CreateIndex(
                name: "ix_clientes_lista_precio_id",
                schema: "ventas",
                table: "clientes",
                column: "lista_precio_id");

            migrationBuilder.CreateIndex(
                name: "ix_clientes_version_sync",
                schema: "ventas",
                table: "clientes",
                column: "version_sync");

            migrationBuilder.CreateIndex(
                name: "ux_clientes_codigo",
                schema: "ventas",
                table: "clientes",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_clientes_ruta_generico",
                schema: "ventas",
                table: "clientes",
                column: "ruta_generico",
                unique: true,
                filter: "ruta_generico IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_cobros_folio",
                schema: "ventas",
                table: "cobros",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_cobros_op_id",
                schema: "ventas",
                table: "cobros",
                column: "op_id",
                unique: true,
                filter: "op_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_entidad",
                schema: "ventas",
                table: "incidencias",
                columns: new[] { "entidad_tipo", "entidad_id" });

            migrationBuilder.CreateIndex(
                name: "ix_incidencias_estado_tipo",
                schema: "ventas",
                table: "incidencias",
                columns: new[] { "estado", "tipo" });

            migrationBuilder.CreateIndex(
                name: "ix_listas_precio_version_sync",
                schema: "ventas",
                table: "listas_precio",
                column: "version_sync");

            migrationBuilder.CreateIndex(
                name: "ix_listas_precio_partidas_version_sync",
                schema: "ventas",
                table: "listas_precio_partidas",
                column: "version_sync");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_cliente_id",
                schema: "ventas",
                table: "pedidos",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_ruta_version_sync",
                schema: "ventas",
                table: "pedidos",
                columns: new[] { "ruta_id", "version_sync" });

            migrationBuilder.CreateIndex(
                name: "ux_pedidos_folio",
                schema: "ventas",
                table: "pedidos",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_partidas_pedido_id",
                schema: "ventas",
                table: "pedidos_partidas",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "ix_ventas_cliente_id",
                schema: "ventas",
                table: "ventas",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_ventas_factura_id",
                schema: "ventas",
                table: "ventas",
                column: "factura_id");

            migrationBuilder.CreateIndex(
                name: "ix_ventas_fecha_operativa",
                schema: "ventas",
                table: "ventas",
                column: "fecha_operativa");

            migrationBuilder.CreateIndex(
                name: "ix_ventas_ruta_fecha",
                schema: "ventas",
                table: "ventas",
                columns: new[] { "ruta_id", "fecha_operativa" });

            migrationBuilder.CreateIndex(
                name: "ux_ventas_folio",
                schema: "ventas",
                table: "ventas",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ventas_op_id",
                schema: "ventas",
                table: "ventas",
                column: "op_id",
                unique: true,
                filter: "op_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ventas_partidas_venta_id",
                schema: "ventas",
                table: "ventas_partidas",
                column: "venta_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cobros_aplicaciones",
                schema: "ventas");

            migrationBuilder.DropTable(
                name: "incidencias",
                schema: "ventas");

            migrationBuilder.DropTable(
                name: "listas_precio_partidas",
                schema: "ventas");

            migrationBuilder.DropTable(
                name: "pedidos_partidas",
                schema: "ventas");

            migrationBuilder.DropTable(
                name: "ventas_partidas",
                schema: "ventas");

            migrationBuilder.DropTable(
                name: "cobros",
                schema: "ventas");

            migrationBuilder.DropTable(
                name: "pedidos",
                schema: "ventas");

            migrationBuilder.DropTable(
                name: "ventas",
                schema: "ventas");

            migrationBuilder.DropTable(
                name: "clientes",
                schema: "ventas");

            migrationBuilder.DropTable(
                name: "canales",
                schema: "ventas");

            migrationBuilder.DropTable(
                name: "listas_precio",
                schema: "ventas");
        }
    }
}
