using Microsoft.EntityFrameworkCore;
using TG.SharedKernel.Datos;
using TG.Ventas.Dominio;

namespace TG.Ventas.Infraestructura;

public sealed class VentasDbContext(DbContextOptions<VentasDbContext> options) : DbContext(options)
{
    public const string Esquema = "ventas";

    public DbSet<Canal> Canales => Set<Canal>();
    public DbSet<ListaPrecio> ListasPrecio => Set<ListaPrecio>();
    public DbSet<ListaPrecioPartida> ListasPrecioPartidas => Set<ListaPrecioPartida>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<GrupoCliente> GruposClientes => Set<GrupoCliente>();
    public DbSet<PrecioCliente> PreciosClientes => Set<PrecioCliente>();
    public DbSet<ProductoExclusivo> ProductosExclusivos => Set<ProductoExclusivo>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<Venta> Ventas => Set<Venta>();
    public DbSet<VentaPartida> VentasPartidas => Set<VentaPartida>();
    public DbSet<Cobro> Cobros => Set<Cobro>();
    public DbSet<Incidencia> Incidencias => Set<Incidencia>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Esquema);

        builder.Entity<Canal>(e =>
        {
            e.ToTable("canales");
            e.HasIndex(c => c.Clave).IsUnique().HasDatabaseName("ux_canales_clave");
            e.Property(c => c.FormatoLiquidacion).ComoTexto("canales");
            e.Property(c => c.PedidoPor).ComoTexto("canales");
        });

        builder.Entity<ListaPrecio>(e =>
        {
            e.ToTable("listas_precio");
            e.HasMany(l => l.Partidas).WithOne().HasForeignKey(p => p.ListaPrecioId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => l.VersionSync).HasDatabaseName("ix_listas_precio_version_sync");
        });

        builder.Entity<ListaPrecioPartida>(e =>
        {
            e.ToTable("listas_precio_partidas");
            e.HasKey(p => new { p.ListaPrecioId, p.ProductoId, p.UnidadId });
            e.Property(p => p.PrecioUnitario).Precio();
            e.HasIndex(p => p.VersionSync).HasDatabaseName("ix_listas_precio_partidas_version_sync");
        });

        builder.Entity<GrupoCliente>(e =>
        {
            e.ToTable("grupos_clientes");
            e.HasIndex(g => g.Clave).IsUnique().HasDatabaseName("ux_grupos_clientes_clave");
            e.HasIndex(g => g.VersionSync).HasDatabaseName("ix_grupos_clientes_version_sync");
            e.HasOne<Canal>().WithMany().HasForeignKey(g => g.CanalId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ListaPrecio>().WithMany().HasForeignKey(g => g.ListaPrecioId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PrecioCliente>(e =>
        {
            e.ToTable("precios_cliente");
            e.HasKey(p => new { p.ClienteId, p.ProductoId, p.UnidadId });
            e.Property(p => p.PrecioUnitario).Precio();
            e.HasIndex(p => p.VersionSync).HasDatabaseName("ix_precios_cliente_version_sync");
            e.HasOne<Cliente>().WithMany().HasForeignKey(p => p.ClienteId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProductoExclusivo>(e =>
        {
            e.ToTable("productos_exclusivos", t => t.HasCheckConstraint("ck_productos_exclusivos_cliente_o_grupo",
                "(cliente_id IS NULL) <> (grupo_cliente_id IS NULL)"));
            e.HasIndex(p => new { p.ProductoId, p.ClienteId, p.GrupoClienteId }).IsUnique().AreNullsDistinct(false)
                .HasDatabaseName("ux_productos_exclusivos");
            e.HasIndex(p => p.VersionSync).HasDatabaseName("ix_productos_exclusivos_version_sync");
            e.HasOne<Cliente>().WithMany().HasForeignKey(p => p.ClienteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<GrupoCliente>().WithMany().HasForeignKey(p => p.GrupoClienteId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Cliente>(e =>
        {
            e.ToTable("clientes");
            e.HasOne<GrupoCliente>().WithMany().HasForeignKey(c => c.GrupoClienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => c.Codigo).IsUnique().HasDatabaseName("ux_clientes_codigo");
            e.HasIndex(c => c.RutaGenerico).IsUnique().HasFilter("ruta_generico IS NOT NULL").HasDatabaseName("ux_clientes_ruta_generico");
            e.HasIndex(c => c.VersionSync).HasDatabaseName("ix_clientes_version_sync");
            e.Property(c => c.CondicionPago).ComoTexto("clientes");
            e.Property(c => c.LimiteCredito).Importe();
            e.Property(c => c.Latitud).Coordenada();
            e.Property(c => c.Longitud).Coordenada();
            e.Property(c => c.LatitudPropuesta).Coordenada();
            e.Property(c => c.LongitudPropuesta).Coordenada();
            e.Ignore(c => c.Direccion);
            e.HasOne<Canal>().WithMany().HasForeignKey(c => c.CanalId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ListaPrecio>().WithMany().HasForeignKey(c => c.ListaPrecioId).OnDelete(DeleteBehavior.Restrict);
            e.ConcurrenciaXmin();
        });

        builder.Entity<Pedido>(e =>
        {
            e.ToTable("pedidos");
            e.HasIndex(p => p.Folio).IsUnique().HasDatabaseName("ux_pedidos_folio");
            e.HasIndex(p => new { p.RutaId, p.VersionSync }).HasDatabaseName("ix_pedidos_ruta_version_sync");
            e.Property(p => p.Estado).ComoTexto("pedidos");
            e.Property(p => p.Subtotal).Importe();
            e.Property(p => p.Iva).Importe();
            e.Property(p => p.Ieps).Importe();
            e.Property(p => p.Total).Importe();
            e.HasMany(p => p.Partidas).WithOne().HasForeignKey(p => p.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Cliente>().WithMany().HasForeignKey(p => p.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.ConcurrenciaXmin();
        });

        builder.Entity<PedidoPartida>(e =>
        {
            e.ToTable("pedidos_partidas");
            e.Property(p => p.FactorABase).HasPrecision(18, 6);
            e.Property(p => p.Cantidad).Cantidad();
            e.Property(p => p.CantidadBase).Cantidad();
            e.Property(p => p.PrecioUnitario).Precio();
            e.Property(p => p.TasaIva).Tasa();
            e.Property(p => p.TasaIeps).Tasa();
            e.Property(p => p.Importe).Importe();
            e.Property(p => p.CantidadEntregadaBase).Cantidad();
        });

        builder.Entity<Venta>(e =>
        {
            e.ToTable("ventas");
            e.HasIndex(v => v.Folio).IsUnique().HasDatabaseName("ux_ventas_folio");
            e.HasIndex(v => v.OpId).IsUnique().HasFilter("op_id IS NOT NULL").HasDatabaseName("ux_ventas_op_id");
            e.HasIndex(v => new { v.RutaId, v.FechaOperativa }).HasDatabaseName("ix_ventas_ruta_fecha");
            e.HasIndex(v => v.FechaOperativa).HasDatabaseName("ix_ventas_fecha_operativa");
            e.HasIndex(v => v.FacturaId).HasDatabaseName("ix_ventas_factura_id");
            e.Property(v => v.Origen).ComoTexto("ventas");
            e.Property(v => v.Tipo).ComoTexto("ventas");
            e.Property(v => v.CondicionPago).ComoTexto("ventas");
            e.Property(v => v.FormaPago).ComoTexto("ventas");
            e.Property(v => v.Estado).ComoTexto("ventas");
            e.Property(v => v.Subtotal).Importe();
            e.Property(v => v.Iva).Importe();
            e.Property(v => v.Ieps).Importe();
            e.Property(v => v.Total).Importe();
            e.Property(v => v.TotalApp).Importe();
            e.Property(v => v.Latitud).Coordenada();
            e.Property(v => v.Longitud).Coordenada();
            e.Property(v => v.PrecisionM).Metros();
            e.Property(v => v.DistanciaClienteM).Metros();
            e.HasMany(v => v.Partidas).WithOne().HasForeignKey(p => p.VentaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Cliente>().WithMany().HasForeignKey(v => v.ClienteId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<VentaPartida>(e =>
        {
            e.ToTable("ventas_partidas");
            e.Property(p => p.FactorABase).HasPrecision(18, 6);
            e.Property(p => p.Cantidad).Cantidad();
            e.Property(p => p.CantidadBase).Cantidad();
            e.Property(p => p.PrecioUnitario).Precio();
            e.Property(p => p.PrecioReferencia).Precio();
            e.Property(p => p.TasaIva).Tasa();
            e.Property(p => p.TasaIeps).Tasa();
            e.Property(p => p.Importe).Importe();
            e.Property(p => p.Iva).Importe();
            e.Property(p => p.Ieps).Importe();
        });

        builder.Entity<Cobro>(e =>
        {
            e.ToTable("cobros");
            e.HasIndex(c => c.Folio).IsUnique().HasDatabaseName("ux_cobros_folio");
            e.HasIndex(c => c.OpId).IsUnique().HasFilter("op_id IS NOT NULL").HasDatabaseName("ux_cobros_op_id");
            e.Property(c => c.FormaPago).ComoTexto("cobros");
            e.Property(c => c.Estado).ComoTexto("cobros");
            e.Property(c => c.Importe).Importe();
            e.HasMany(c => c.Aplicaciones).WithOne().HasForeignKey(a => a.CobroId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CobroAplicacion>(e =>
        {
            e.ToTable("cobros_aplicaciones");
            e.HasKey(a => new { a.CobroId, a.VentaId });
            e.Property(a => a.Importe).Importe();
        });

        builder.Entity<Incidencia>(e =>
        {
            e.ToTable("incidencias");
            e.Property(i => i.Tipo).ComoTexto("incidencias");
            e.Property(i => i.Estado).ComoTexto("incidencias");
            e.Property(i => i.Detalle).HasColumnType("jsonb");
            e.HasIndex(i => new { i.Estado, i.Tipo }).HasDatabaseName("ix_incidencias_estado_tipo");
            e.HasIndex(i => new { i.EntidadTipo, i.EntidadId }).HasDatabaseName("ix_incidencias_entidad");
        });

        builder.MapearBitacora();
        builder.UsarIdsDeAplicacion();
    }
}
