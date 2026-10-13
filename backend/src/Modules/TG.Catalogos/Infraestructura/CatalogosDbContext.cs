using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Dominio;
using TG.SharedKernel.Datos;

namespace TG.Catalogos.Infraestructura;

public sealed class CatalogosDbContext(DbContextOptions<CatalogosDbContext> options) : DbContext(options)
{
    public const string Esquema = "catalogos";

    public DbSet<UnidadMedida> Unidades => Set<UnidadMedida>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<ProductoUnidad> ProductosUnidades => Set<ProductoUnidad>();
    public DbSet<MotivoMerma> MotivosMerma => Set<MotivoMerma>();
    public DbSet<Parametro> Parametros => Set<Parametro>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Esquema);

        builder.Entity<UnidadMedida>(e =>
        {
            e.ToTable("unidades_medida");
            e.HasIndex(u => u.Clave).IsUnique().HasDatabaseName("ux_unidades_medida_clave");
            e.Property(u => u.Tipo).ComoTexto("unidades_medida");
        });

        builder.Entity<Producto>(e =>
        {
            e.ToTable("productos");
            e.HasIndex(p => p.Sku).IsUnique().HasDatabaseName("ux_productos_sku");
            e.HasIndex(p => p.VersionSync).HasDatabaseName("ix_productos_version_sync");
            e.Property(p => p.Tipo).ComoTexto("productos");
            e.Property(p => p.TasaIva).Tasa();
            e.Property(p => p.TasaIeps).Tasa();
            e.Property(p => p.StockMinimo).Cantidad();
            e.HasOne(p => p.UnidadBase).WithMany().HasForeignKey(p => p.UnidadBaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(p => p.Conversiones).WithOne().HasForeignKey(c => c.ProductoId).OnDelete(DeleteBehavior.Cascade);
            e.ConcurrenciaXmin();
        });

        builder.Entity<ProductoUnidad>(e =>
        {
            e.ToTable("productos_unidades");
            e.HasKey(c => new { c.ProductoId, c.UnidadId });
            e.Property(c => c.FactorABase).HasPrecision(18, 6);
            e.HasOne(c => c.Unidad).WithMany().HasForeignKey(c => c.UnidadId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MotivoMerma>(e =>
        {
            e.ToTable("motivos_merma");
            e.HasIndex(m => m.Clave).IsUnique().HasDatabaseName("ux_motivos_merma_clave");
        });

        builder.Entity<Parametro>(e =>
        {
            e.ToTable("parametros");
            e.HasKey(p => p.Clave);
            e.Property(p => p.Tipo).ComoTexto("parametros");
        });

        builder.UsarIdsDeAplicacion();
    }
}
