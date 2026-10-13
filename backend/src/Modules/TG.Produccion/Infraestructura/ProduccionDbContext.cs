using Microsoft.EntityFrameworkCore;
using TG.Produccion.Dominio;
using TG.SharedKernel.Datos;

namespace TG.Produccion.Infraestructura;

public sealed class ProduccionDbContext(DbContextOptions<ProduccionDbContext> options) : DbContext(options)
{
    public const string Esquema = "produccion";

    public DbSet<Linea> Lineas => Set<Linea>();
    public DbSet<Receta> Recetas => Set<Receta>();
    public DbSet<OrdenProduccion> Ordenes => Set<OrdenProduccion>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Esquema);

        builder.Entity<Linea>(e =>
        {
            e.ToTable("lineas");
            e.HasIndex(l => l.Codigo).IsUnique().HasDatabaseName("ux_lineas_codigo");
            e.Property(l => l.Tipo).ComoTexto("lineas");
        });

        builder.Entity<Receta>(e =>
        {
            e.ToTable("recetas");
            e.Property(r => r.NumeroVersion).HasColumnName("version");
            e.HasIndex(r => new { r.ProductoId, r.NumeroVersion }).IsUnique().HasDatabaseName("ux_recetas_producto_version");
            // RN-PRO-01: una sola Vigente por producto.
            e.HasIndex(r => r.ProductoId).IsUnique().HasFilter("estado = 'Vigente'").HasDatabaseName("ux_recetas_producto_vigente");
            e.Property(r => r.Estado).ComoTexto("recetas");
            e.Property(r => r.RendimientoCantidad).Cantidad();
            e.Property(r => r.MermaEsperadaPct).Tasa();
            e.HasMany(r => r.Componentes).WithOne().HasForeignKey(c => c.RecetaId).OnDelete(DeleteBehavior.Cascade);
            e.ConcurrenciaXmin();
            e.Property(r => r.Version).HasColumnName("xmin");
        });

        builder.Entity<RecetaComponente>(e =>
        {
            e.ToTable("recetas_componentes");
            e.Property(c => c.FactorABase).HasPrecision(18, 6);
            e.Property(c => c.Cantidad).Cantidad();
            e.Property(c => c.CantidadBase).Cantidad();
        });

        builder.Entity<OrdenProduccion>(e =>
        {
            e.ToTable("ordenes_produccion");
            e.HasIndex(o => o.Folio).IsUnique().HasDatabaseName("ux_ordenes_produccion_folio");
            e.HasIndex(o => o.FechaOperativa).HasDatabaseName("ix_ordenes_produccion_fecha_operativa");
            e.Property(o => o.Turno).ComoTexto("ordenes_produccion");
            e.Property(o => o.Estado).ComoTexto("ordenes_produccion");
            e.Property(o => o.CantidadPlaneada).Cantidad();
            e.Property(o => o.CantidadProducida).Cantidad();
            e.Property(o => o.CostoTotal).Importe();
            e.Property(o => o.CostoUnitario).Costo();
            e.HasMany(o => o.Consumos).WithOne().HasForeignKey(c => c.OrdenProduccionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(o => o.Mermas).WithOne().HasForeignKey(m => m.OrdenProduccionId).OnDelete(DeleteBehavior.Cascade);
            e.ConcurrenciaXmin();
        });

        builder.Entity<OrdenConsumo>(e =>
        {
            e.ToTable("ordenes_produccion_consumos");
            e.Property(c => c.CantidadTeoricaBase).Cantidad();
            e.Property(c => c.CantidadRealBase).Cantidad();
            e.Property(c => c.CostoUnitario).Costo();
        });

        builder.Entity<OrdenMerma>(e =>
        {
            e.ToTable("ordenes_produccion_mermas");
            e.Property(m => m.CantidadBase).Cantidad();
        });

        builder.UsarIdsDeAplicacion();
    }
}
