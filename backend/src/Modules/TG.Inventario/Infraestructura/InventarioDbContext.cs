using Microsoft.EntityFrameworkCore;
using TG.Inventario.Dominio;
using TG.Inventario.Publico;
using TG.SharedKernel.Datos;

namespace TG.Inventario.Infraestructura;

public sealed class InventarioDbContext(DbContextOptions<InventarioDbContext> options) : DbContext(options)
{
    public const string Esquema = "inventario";

    public DbSet<Almacen> Almacenes => Set<Almacen>();
    public DbSet<Existencia> Existencias => Set<Existencia>();
    public DbSet<CostoProducto> CostosProducto => Set<CostoProducto>();
    public DbSet<Movimiento> Movimientos => Set<Movimiento>();
    public DbSet<Traspaso> Traspasos => Set<Traspaso>();
    public DbSet<ConteoFisico> Conteos => Set<ConteoFisico>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Esquema);

        builder.Entity<Almacen>(e =>
        {
            e.ToTable("almacenes");
            e.HasIndex(a => a.Codigo).IsUnique().HasDatabaseName("ux_almacenes_codigo");
            e.HasIndex(a => a.RutaId).IsUnique().HasFilter("ruta_id IS NOT NULL").HasDatabaseName("ux_almacenes_ruta_id");
            e.Property(a => a.Tipo).ComoTexto("almacenes");
        });

        builder.Entity<Existencia>(e =>
        {
            e.ToTable("existencias");
            e.HasKey(x => new { x.AlmacenId, x.ProductoId, x.Lote });
            e.Property(x => x.Cantidad).Cantidad();
            e.HasIndex(x => x.ProductoId).HasDatabaseName("ix_existencias_producto_id");
            e.ConcurrenciaXmin();
        });

        builder.Entity<CostoProducto>(e =>
        {
            e.ToTable("costos_producto");
            e.HasKey(c => c.ProductoId);
            e.Property(c => c.CostoPromedio).Costo();
        });

        builder.Entity<Movimiento>(e =>
        {
            e.ToTable("movimientos");
            e.Property(m => m.Tipo).ComoTexto("movimientos");
            e.Property(m => m.DocumentoTipo).ComoTexto("movimientos");
            e.Property(m => m.Cantidad).Cantidad();
            e.Property(m => m.CostoUnitario).Costo();
            e.Property(m => m.CostoTotal).Importe();
            e.Property(m => m.ExistenciaResultante).Cantidad();
            e.HasIndex(m => new { m.ProductoId, m.AlmacenId, m.FechaHora }).HasDatabaseName("ix_movimientos_producto_almacen_fecha");
            e.HasIndex(m => new { m.DocumentoTipo, m.DocumentoId }).HasDatabaseName("ix_movimientos_documento");
            e.HasIndex(m => m.FechaOperativa).HasDatabaseName("ix_movimientos_fecha_operativa");
            // Trazabilidad (RN-INV-14): movimientos de un lote; los productos sin lote guardan "" y no se indexan.
            e.HasIndex(m => new { m.Lote, m.ProductoId }).HasDatabaseName("ix_movimientos_lote").HasFilter("lote <> ''");
        });

        builder.Entity<Traspaso>(e =>
        {
            e.ToTable("traspasos");
            e.HasIndex(t => t.Folio).IsUnique().HasDatabaseName("ux_traspasos_folio");
            e.Property(t => t.Estado).ComoTexto("traspasos");
            e.HasMany(t => t.Partidas).WithOne().HasForeignKey(p => p.TraspasoId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TraspasoPartida>(e =>
        {
            e.ToTable("traspasos_partidas");
            e.Property(p => p.CantidadBase).Cantidad();
        });

        builder.Entity<ConteoFisico>(e =>
        {
            e.ToTable("conteos_fisicos");
            e.HasIndex(c => c.Folio).IsUnique().HasDatabaseName("ux_conteos_fisicos_folio");
            e.Property(c => c.Estado).ComoTexto("conteos_fisicos");
            e.HasMany(c => c.Partidas).WithOne().HasForeignKey(p => p.ConteoId).OnDelete(DeleteBehavior.Cascade);
            e.ConcurrenciaXmin();
        });

        builder.Entity<ConteoPartida>(e =>
        {
            e.ToTable("conteos_partidas");
            e.HasIndex(p => new { p.ConteoId, p.ProductoId, p.Lote }).IsUnique().HasDatabaseName("ux_conteos_partidas_producto_lote");
            e.Property(p => p.CantidadSistema).Cantidad();
            e.Property(p => p.CantidadContada).Cantidad();
            e.Property(p => p.Diferencia).Cantidad();
        });

        builder.UsarIdsDeAplicacion();
    }
}
