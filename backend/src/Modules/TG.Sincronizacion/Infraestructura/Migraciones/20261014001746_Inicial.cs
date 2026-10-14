using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Sincronizacion.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La secuencia global la crea Catálogos; aquí solo se asegura si este módulo migra primero.
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS sync;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS sync.version_seq;");

            migrationBuilder.EnsureSchema(
                name: "sync");

            migrationBuilder.CreateTable(
                name: "lotes",
                schema: "sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispositivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recibido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_operaciones = table.Column<int>(type: "integer", nullable: false),
                    aplicadas = table.Column<int>(type: "integer", nullable: false),
                    rechazadas = table.Column<int>(type: "integer", nullable: false),
                    duplicadas = table.Column<int>(type: "integer", nullable: false),
                    version_app = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lotes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operaciones",
                schema: "sync",
                columns: table => new
                {
                    op_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispositivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    secuencia_dispositivo = table.Column<long>(type: "bigint", nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recibida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    resultado = table.Column<string>(type: "jsonb", nullable: true),
                    codigo = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operaciones", x => x.op_id);
                    table.CheckConstraint("ck_operaciones_estado", "estado IN ('Aplicada', 'Rechazada')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_lotes_dispositivo_recibido",
                schema: "sync",
                table: "lotes",
                columns: new[] { "dispositivo_id", "recibido_en" });

            migrationBuilder.CreateIndex(
                name: "ix_operaciones_dispositivo_recibida",
                schema: "sync",
                table: "operaciones",
                columns: new[] { "dispositivo_id", "recibida_en" });

            migrationBuilder.CreateIndex(
                name: "ix_operaciones_estado",
                schema: "sync",
                table: "operaciones",
                column: "estado");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lotes",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "operaciones",
                schema: "sync");
        }
    }
}
