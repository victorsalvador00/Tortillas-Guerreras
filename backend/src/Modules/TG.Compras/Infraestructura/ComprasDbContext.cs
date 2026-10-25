using Microsoft.EntityFrameworkCore;
using TG.Compras.Dominio;
using TG.SharedKernel.Datos;

namespace TG.Compras.Infraestructura;

public sealed class ComprasDbContext(DbContextOptions<ComprasDbContext> options) : DbContext(options)
{
    public const string Esquema = "compras";

    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<ProveedorProducto> ProveedoresProductos => Set<ProveedorProducto>();
    public DbSet<OrdenCompra> OrdenesCompra => Set<OrdenCompra>();
    public DbSet<OrdenCompraPartida> OrdenesCompraPartidas => Set<OrdenCompraPartida>();
    public DbSet<Recepcion> Recepciones => Set<Recepcion>();
    public DbSet<RecepcionPartida> RecepcionesPartidas => Set<RecepcionPartida>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Esquema);

        builder.Entity<Proveedor>(e =>
        {
            e.ToTable("proveedores");
            e.HasIndex(p => p.Codigo).IsUnique().HasDatabaseName("ux_proveedores_codigo");
        });

        builder.Entity<ProveedorProducto>(e =>
        {
            e.ToTable("proveedores_productos");
            e.HasKey(p => new { p.ProveedorId, p.ProductoId });
            e.Property(p => p.UltimoPrecio).Precio();
            e.Property(p => p.Precio).Precio();
            e.Property(p => p.ClaveProveedor).HasMaxLength(50);
            e.Property(p => p.Activo).HasDefaultValue(true);
            e.HasIndex(p => p.ProductoId).IsUnique().HasFilter("preferido").HasDatabaseName("ux_proveedores_productos_preferido");
            e.HasOne<Proveedor>().WithMany().HasForeignKey(p => p.ProveedorId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OrdenCompra>(e =>
        {
            e.ToTable("ordenes_compra");
            e.HasIndex(o => o.Folio).IsUnique().HasDatabaseName("ux_ordenes_compra_folio");
            e.HasIndex(o => new { o.ProveedorId, o.Fecha }).HasDatabaseName("ix_ordenes_compra_proveedor_fecha");
            e.Property(o => o.Estado).ComoTexto("ordenes_compra");
            e.Property(o => o.Subtotal).Importe();
            e.Property(o => o.Iva).Importe();
            e.Property(o => o.Total).Importe();
            e.HasOne<Proveedor>().WithMany().HasForeignKey(o => o.ProveedorId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(o => o.Partidas).WithOne().HasForeignKey(p => p.OrdenCompraId).OnDelete(DeleteBehavior.Cascade);
            e.ConcurrenciaXmin();
        });

        builder.Entity<OrdenCompraPartida>(e =>
        {
            e.ToTable("ordenes_compra_partidas");
            e.Property(p => p.FactorABase).HasPrecision(18, 6);
            e.Property(p => p.Cantidad).Cantidad();
            e.Property(p => p.CantidadBase).Cantidad();
            e.Property(p => p.PrecioUnitario).Precio();
            e.Property(p => p.TasaIva).Tasa();
            e.Property(p => p.Importe).Importe();
            e.Property(p => p.Iva).Importe();
            e.Property(p => p.CantidadRecibidaBase).Cantidad();
        });

        builder.Entity<Recepcion>(e =>
        {
            e.ToTable("recepciones");
            e.HasIndex(r => r.Folio).IsUnique().HasDatabaseName("ux_recepciones_folio");
            e.HasIndex(r => r.OrdenCompraId).HasDatabaseName("ix_recepciones_orden_compra_id");
            e.HasIndex(r => new { r.ProveedorId, r.FechaOperativa }).HasDatabaseName("ix_recepciones_proveedor_fecha");
            e.Property(r => r.Estado).ComoTexto("recepciones");
            e.HasOne<Proveedor>().WithMany().HasForeignKey(r => r.ProveedorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrdenCompra>().WithMany().HasForeignKey(r => r.OrdenCompraId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.Partidas).WithOne().HasForeignKey(p => p.RecepcionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RecepcionPartida>(e =>
        {
            e.ToTable("recepciones_partidas");
            e.Property(p => p.FactorABase).HasPrecision(18, 6);
            e.Property(p => p.Cantidad).Cantidad();
            e.Property(p => p.CantidadBase).Cantidad();
            e.Property(p => p.PrecioUnitario).Precio();
            e.Property(p => p.CostoUnitarioBase).Costo();
        });

        builder.UsarIdsDeAplicacion();
    }
}
