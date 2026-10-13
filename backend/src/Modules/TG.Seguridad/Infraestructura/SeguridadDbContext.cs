using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TG.Seguridad.Dominio;
using TG.SharedKernel.Datos;

namespace TG.Seguridad.Infraestructura;

public sealed class SeguridadDbContext(DbContextOptions<SeguridadDbContext> options)
    : IdentityDbContext<Usuario, Rol, Guid>(options)
{
    public const string Esquema = "seguridad";

    public DbSet<Permiso> Permisos => Set<Permiso>();
    public DbSet<RolPermiso> RolesPermisos => Set<RolPermiso>();
    public DbSet<Dispositivo> Dispositivos => Set<Dispositivo>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Esquema);

        builder.Entity<Usuario>(e =>
        {
            e.ToTable("usuarios");
            e.Property(u => u.NombreCompleto).IsRequired();
        });
        builder.Entity<Rol>(e => e.ToTable("roles"));
        builder.Entity<IdentityUserRole<Guid>>(e => e.ToTable("usuarios_roles"));
        builder.Entity<IdentityUserClaim<Guid>>(e => e.ToTable("usuarios_claims"));
        builder.Entity<IdentityUserLogin<Guid>>(e => e.ToTable("usuarios_logins"));
        builder.Entity<IdentityUserToken<Guid>>(e => e.ToTable("usuarios_tokens"));
        builder.Entity<IdentityRoleClaim<Guid>>(e => e.ToTable("roles_claims"));

        builder.Entity<Permiso>(e =>
        {
            e.ToTable("permisos");
            e.HasIndex(p => p.Clave).IsUnique().HasDatabaseName("ux_permisos_clave");
        });

        builder.Entity<RolPermiso>(e =>
        {
            e.ToTable("roles_permisos");
            e.HasKey(rp => new { rp.RolId, rp.PermisoId });
            e.HasOne<Rol>().WithMany(r => r.Permisos).HasForeignKey(rp => rp.RolId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(rp => rp.Permiso).WithMany().HasForeignKey(rp => rp.PermisoId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Dispositivo>(e =>
        {
            e.ToTable("dispositivos");
            e.HasIndex(d => d.Codigo).IsUnique().HasDatabaseName("ux_dispositivos_codigo");
            e.Property(d => d.Plataforma).ComoTexto("dispositivos");
            e.Ignore(d => d.PorAprobar);
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasIndex(r => r.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
            e.HasIndex(r => r.UsuarioId).HasDatabaseName("ix_refresh_tokens_usuario_id");
        });
    }
}
