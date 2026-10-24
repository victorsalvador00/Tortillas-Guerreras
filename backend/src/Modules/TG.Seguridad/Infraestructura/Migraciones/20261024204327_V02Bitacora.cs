using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TG.Seguridad.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class V02Bitacora : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bitacora",
                schema: "seguridad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    esquema = table.Column<string>(type: "text", nullable: false),
                    tabla = table.Column<string>(type: "text", nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accion = table.Column<string>(type: "text", nullable: false),
                    cambios = table.Column<string>(type: "jsonb", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dispositivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origen = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bitacora", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bitacora_documento",
                schema: "seguridad",
                table: "bitacora",
                column: "documento_id");

            migrationBuilder.CreateIndex(
                name: "ix_bitacora_tabla_entidad",
                schema: "seguridad",
                table: "bitacora",
                columns: new[] { "tabla", "entidad_id", "fecha_hora" });

            migrationBuilder.CreateIndex(
                name: "ix_bitacora_usuario_fecha",
                schema: "seguridad",
                table: "bitacora",
                columns: new[] { "usuario_id", "fecha_hora" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bitacora",
                schema: "seguridad");
        }
    }
}
