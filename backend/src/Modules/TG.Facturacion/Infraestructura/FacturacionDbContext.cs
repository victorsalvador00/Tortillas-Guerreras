using Microsoft.EntityFrameworkCore;
using TG.Facturacion.Dominio;
using TG.SharedKernel.Datos;

namespace TG.Facturacion.Infraestructura;

public sealed class FacturacionDbContext(DbContextOptions<FacturacionDbContext> options) : DbContext(options)
{
    public const string Esquema = "facturacion";

    public DbSet<Emisor> Emisores => Set<Emisor>();
    public DbSet<Factura> Facturas => Set<Factura>();
    public DbSet<FacturaVenta> FacturasVentas => Set<FacturaVenta>();
    public DbSet<SolicitudAutofactura> Solicitudes => Set<SolicitudAutofactura>();
    public DbSet<MensajeOutbox> Outbox => Set<MensajeOutbox>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Esquema);

        builder.Entity<Emisor>(e => e.ToTable("emisor"));

        builder.Entity<Factura>(e =>
        {
            e.ToTable("facturas");
            e.Property(f => f.Tipo).ComoTexto("facturas");
            e.Property(f => f.Estado).ComoTexto("facturas");
            e.Property(f => f.Subtotal).Importe();
            e.Property(f => f.Iva).Importe();
            e.Property(f => f.Ieps).Importe();
            e.Property(f => f.Total).Importe();
            e.HasIndex(f => new { f.Serie, f.Folio }).IsUnique().HasDatabaseName("ux_facturas_serie_folio");
            e.HasIndex(f => f.UuidFiscal).IsUnique().HasFilter("uuid_fiscal IS NOT NULL").HasDatabaseName("ux_facturas_uuid_fiscal");
            e.HasIndex(f => f.FechaOperativa).HasDatabaseName("ix_facturas_fecha_operativa");
            e.HasMany(f => f.Conceptos).WithOne().HasForeignKey(c => c.FacturaId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(f => f.Ventas).WithOne().HasForeignKey(v => v.FacturaId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(f => f.Vigente);
            e.ConcurrenciaXmin();
        });

        builder.Entity<FacturaConcepto>(e =>
        {
            e.ToTable("facturas_conceptos");
            e.Property(c => c.Cantidad).Cantidad();
            e.Property(c => c.ValorUnitario).Precio();
            e.Property(c => c.Importe).Importe();
            e.Property(c => c.Iva).Importe();
            e.Property(c => c.Ieps).Importe();
            e.Property(c => c.TasaIva).Tasa();
            e.Property(c => c.TasaIeps).Tasa();
        });

        builder.Entity<FacturaVenta>(e =>
        {
            e.ToTable("facturas_ventas");
            e.HasKey(v => new { v.FacturaId, v.VentaId });
            e.HasIndex(v => v.VentaId).HasDatabaseName("ix_facturas_ventas_venta_id");
        });

        builder.Entity<SolicitudAutofactura>(e =>
        {
            e.ToTable("solicitudes_autofactura");
            e.Property(s => s.Estado).ComoTexto("solicitudes_autofactura");
            e.HasIndex(s => s.FolioTicket).HasDatabaseName("ix_solicitudes_autofactura_folio_ticket");
        });

        builder.Entity<MensajeOutbox>(e =>
        {
            e.ToTable("outbox");
            e.Property(o => o.Tipo).ComoTexto("outbox");
            e.Property(o => o.Estado).ComoTexto("outbox");
            e.Property(o => o.Payload).HasColumnType("jsonb");
            e.HasIndex(o => new { o.Estado, o.SiguienteIntentoEn }).HasDatabaseName("ix_outbox_estado_siguiente");
        });

        builder.UsarIdsDeAplicacion();
    }
}
