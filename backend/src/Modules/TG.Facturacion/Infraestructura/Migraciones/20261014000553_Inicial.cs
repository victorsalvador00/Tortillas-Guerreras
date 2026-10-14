using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Facturacion.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS facturacion;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS facturacion.folio_ingreso_seq;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS facturacion.folio_global_seq;");

            migrationBuilder.EnsureSchema(
                name: "facturacion");

            migrationBuilder.CreateTable(
                name: "emisor",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfc = table.Column<string>(type: "text", nullable: false),
                    razon_social = table.Column<string>(type: "text", nullable: false),
                    regimen_fiscal = table.Column<string>(type: "text", nullable: false),
                    codigo_postal_expedicion = table.Column<string>(type: "text", nullable: false),
                    serie_facturas = table.Column<string>(type: "text", nullable: false),
                    serie_global = table.Column<string>(type: "text", nullable: false),
                    serie_notas_credito = table.Column<string>(type: "text", nullable: false),
                    es_ejemplo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emisor", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "facturas",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    serie = table.Column<string>(type: "text", nullable: false),
                    folio = table.Column<string>(type: "text", nullable: false),
                    uuid_fiscal = table.Column<Guid>(type: "uuid", nullable: true),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receptor_rfc = table.Column<string>(type: "text", nullable: false),
                    receptor_nombre = table.Column<string>(type: "text", nullable: false),
                    receptor_regimen = table.Column<string>(type: "text", nullable: false),
                    receptor_cp = table.Column<string>(type: "text", nullable: false),
                    uso_cfdi = table.Column<string>(type: "text", nullable: false),
                    email_receptor = table.Column<string>(type: "text", nullable: true),
                    metodo_pago_sat = table.Column<string>(type: "text", nullable: false),
                    forma_pago_sat = table.Column<string>(type: "text", nullable: false),
                    periodicidad = table.Column<string>(type: "text", nullable: true),
                    meses = table.Column<string>(type: "text", nullable: true),
                    anio = table.Column<string>(type: "text", nullable: true),
                    fecha_operativa = table.Column<DateOnly>(type: "date", nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ieps = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    proveedor = table.Column<string>(type: "text", nullable: false),
                    xml_url = table.Column<string>(type: "text", nullable: true),
                    pdf_url = table.Column<string>(type: "text", nullable: true),
                    error_detalle = table.Column<string>(type: "text", nullable: true),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    timbrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_cancelacion = table.Column<string>(type: "text", nullable: true),
                    cancelada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturas", x => x.id);
                    table.CheckConstraint("ck_facturas_estado", "estado IN ('Pendiente', 'EnviadaProveedor', 'Timbrada', 'Error', 'Cancelada')");
                    table.CheckConstraint("ck_facturas_tipo", "tipo IN ('Ingreso', 'Global', 'NotaCredito')");
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    siguiente_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                    table.CheckConstraint("ck_outbox_estado", "estado IN ('Pendiente', 'EnProceso', 'Procesado', 'Error')");
                    table.CheckConstraint("ck_outbox_tipo", "tipo IN ('TimbrarFactura', 'CancelarFactura')");
                });

            migrationBuilder.CreateTable(
                name: "solicitudes_autofactura",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folio_ticket = table.Column<string>(type: "text", nullable: false),
                    venta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rfc = table.Column<string>(type: "text", nullable: false),
                    razon_social = table.Column<string>(type: "text", nullable: false),
                    regimen_fiscal = table.Column<string>(type: "text", nullable: false),
                    codigo_postal = table.Column<string>(type: "text", nullable: false),
                    uso_cfdi = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    ip_origen = table.Column<string>(type: "text", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false),
                    motivo_rechazo = table.Column<string>(type: "text", nullable: true),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitudes_autofactura", x => x.id);
                    table.CheckConstraint("ck_solicitudes_autofactura_estado", "estado IN ('Recibida', 'Rechazada', 'EnProceso', 'Timbrada', 'Error')");
                });

            migrationBuilder.CreateTable(
                name: "facturas_conceptos",
                schema: "facturacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    producto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    no_identificacion = table.Column<string>(type: "text", nullable: true),
                    clave_prod_serv = table.Column<string>(type: "text", nullable: false),
                    clave_unidad = table.Column<string>(type: "text", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    valor_unitario = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    objeto_imp = table.Column<string>(type: "text", nullable: false),
                    tasa_iva = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    tasa_ieps = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    iva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ieps = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    orden = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturas_conceptos", x => x.id);
                    table.ForeignKey(
                        name: "fk_facturas_conceptos_facturas_factura_id",
                        column: x => x.factura_id,
                        principalSchema: "facturacion",
                        principalTable: "facturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "facturas_ventas",
                schema: "facturacion",
                columns: table => new
                {
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venta_folio = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturas_ventas", x => new { x.factura_id, x.venta_id });
                    table.ForeignKey(
                        name: "fk_facturas_ventas_facturas_factura_id",
                        column: x => x.factura_id,
                        principalSchema: "facturacion",
                        principalTable: "facturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_facturas_fecha_operativa",
                schema: "facturacion",
                table: "facturas",
                column: "fecha_operativa");

            migrationBuilder.CreateIndex(
                name: "ux_facturas_serie_folio",
                schema: "facturacion",
                table: "facturas",
                columns: new[] { "serie", "folio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_facturas_uuid_fiscal",
                schema: "facturacion",
                table: "facturas",
                column: "uuid_fiscal",
                unique: true,
                filter: "uuid_fiscal IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_facturas_conceptos_factura_id",
                schema: "facturacion",
                table: "facturas_conceptos",
                column: "factura_id");

            migrationBuilder.CreateIndex(
                name: "ix_facturas_ventas_venta_id",
                schema: "facturacion",
                table: "facturas_ventas",
                column: "venta_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_estado_siguiente",
                schema: "facturacion",
                table: "outbox",
                columns: new[] { "estado", "siguiente_intento_en" });

            migrationBuilder.CreateIndex(
                name: "ix_solicitudes_autofactura_folio_ticket",
                schema: "facturacion",
                table: "solicitudes_autofactura",
                column: "folio_ticket");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "emisor",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "facturas_conceptos",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "facturas_ventas",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "solicitudes_autofactura",
                schema: "facturacion");

            migrationBuilder.DropTable(
                name: "facturas",
                schema: "facturacion");
        }
    }
}
