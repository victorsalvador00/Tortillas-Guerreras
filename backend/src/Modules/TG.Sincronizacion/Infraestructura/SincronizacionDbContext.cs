using Microsoft.EntityFrameworkCore;
using TG.SharedKernel.Datos;
using TG.Sincronizacion.Dominio;

namespace TG.Sincronizacion.Infraestructura;

public sealed class SincronizacionDbContext(DbContextOptions<SincronizacionDbContext> options) : DbContext(options)
{
    public const string Esquema = "sync";

    public DbSet<Operacion> Operaciones => Set<Operacion>();
    public DbSet<LoteSync> Lotes => Set<LoteSync>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);

        modelBuilder.Entity<Operacion>(e =>
        {
            e.ToTable("operaciones");
            e.HasKey(o => o.OpId);
            e.Property(o => o.Estado).ComoTexto("operaciones");
            e.Property(o => o.Resultado).HasColumnType("jsonb");
            e.HasIndex(o => new { o.DispositivoId, o.RecibidaEn }).HasDatabaseName("ix_operaciones_dispositivo_recibida");
            e.HasIndex(o => o.Estado).HasDatabaseName("ix_operaciones_estado");
        });

        modelBuilder.Entity<LoteSync>(e =>
        {
            e.ToTable("lotes");
            e.HasIndex(l => new { l.DispositivoId, l.RecibidoEn }).HasDatabaseName("ix_lotes_dispositivo_recibido");
        });

        modelBuilder.UsarIdsDeAplicacion();
    }
}

/// <summary>Orden 90 (el último): lee de todos los módulos.</summary>
internal sealed class InicializadorSincronizacion : InicializadorModulo<SincronizacionDbContext>
{
    public override int Orden => 90;
}
