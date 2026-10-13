using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TG.Seguridad.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "seguridad");

            migrationBuilder.CreateTable(
                name: "dispositivos",
                schema: "seguridad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "text", nullable: true),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plataforma = table.Column<string>(type: "text", nullable: false),
                    version_app = table.Column<string>(type: "text", nullable: true),
                    ultimo_sync_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_consecutivo = table.Column<int>(type: "integer", nullable: false),
                    operaciones_pendientes = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    solicitado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispositivos", x => x.id);
                    table.CheckConstraint("ck_dispositivos_plataforma", "plataforma IN ('android', 'ios')");
                });

            migrationBuilder.CreateTable(
                name: "permisos",
                schema: "seguridad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "text", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permisos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "seguridad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispositivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revocado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reemplazado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "seguridad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                schema: "seguridad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_completo = table.Column<string>(type: "text", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles_claims",
                schema: "seguridad",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_roles_claims_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "seguridad",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "roles_permisos",
                schema: "seguridad",
                columns: table => new
                {
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permiso_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles_permisos", x => new { x.rol_id, x.permiso_id });
                    table.ForeignKey(
                        name: "fk_roles_permisos_permisos_permiso_id",
                        column: x => x.permiso_id,
                        principalSchema: "seguridad",
                        principalTable: "permisos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_roles_permisos_roles_rol_id",
                        column: x => x.rol_id,
                        principalSchema: "seguridad",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuarios_claims",
                schema: "seguridad",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuarios_claims_usuarios_user_id",
                        column: x => x.user_id,
                        principalSchema: "seguridad",
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuarios_logins",
                schema: "seguridad",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_usuarios_logins_usuarios_user_id",
                        column: x => x.user_id,
                        principalSchema: "seguridad",
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuarios_roles",
                schema: "seguridad",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_usuarios_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "seguridad",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_usuarios_roles_usuarios_user_id",
                        column: x => x.user_id,
                        principalSchema: "seguridad",
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuarios_tokens",
                schema: "seguridad",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_usuarios_tokens_usuarios_user_id",
                        column: x => x.user_id,
                        principalSchema: "seguridad",
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_dispositivos_codigo",
                schema: "seguridad",
                table: "dispositivos",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_permisos_clave",
                schema: "seguridad",
                table: "permisos",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_usuario_id",
                schema: "seguridad",
                table: "refresh_tokens",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ux_refresh_tokens_token_hash",
                schema: "seguridad",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "seguridad",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_claims_role_id",
                schema: "seguridad",
                table: "roles_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_permisos_permiso_id",
                schema: "seguridad",
                table: "roles_permisos",
                column: "permiso_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "seguridad",
                table: "usuarios",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "seguridad",
                table: "usuarios",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_claims_user_id",
                schema: "seguridad",
                table: "usuarios_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_logins_user_id",
                schema: "seguridad",
                table: "usuarios_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_roles_role_id",
                schema: "seguridad",
                table: "usuarios_roles",
                column: "role_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispositivos",
                schema: "seguridad");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "seguridad");

            migrationBuilder.DropTable(
                name: "roles_claims",
                schema: "seguridad");

            migrationBuilder.DropTable(
                name: "roles_permisos",
                schema: "seguridad");

            migrationBuilder.DropTable(
                name: "usuarios_claims",
                schema: "seguridad");

            migrationBuilder.DropTable(
                name: "usuarios_logins",
                schema: "seguridad");

            migrationBuilder.DropTable(
                name: "usuarios_roles",
                schema: "seguridad");

            migrationBuilder.DropTable(
                name: "usuarios_tokens",
                schema: "seguridad");

            migrationBuilder.DropTable(
                name: "permisos",
                schema: "seguridad");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "seguridad");

            migrationBuilder.DropTable(
                name: "usuarios",
                schema: "seguridad");
        }
    }
}
