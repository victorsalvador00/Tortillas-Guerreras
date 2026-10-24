using Microsoft.EntityFrameworkCore;
using TG.Logistica.Dominio;
using TG.Logistica.Publico;
using TG.SharedKernel.Datos;

namespace TG.Logistica.Infraestructura;

public sealed class LogisticaDbContext(DbContextOptions<LogisticaDbContext> options) : DbContext(options)
{
    public const string Esquema = "logistica";

    public DbSet<Vehiculo> Vehiculos => Set<Vehiculo>();
    public DbSet<Ruta> Rutas => Set<Ruta>();
    public DbSet<RutaCliente> RutasClientes => Set<RutaCliente>();
    public DbSet<Envio> Envios => Set<Envio>();
    public DbSet<Retorno> Retornos => Set<Retorno>();
    public DbSet<Liquidacion> Liquidaciones => Set<Liquidacion>();
    public DbSet<CambioPendiente> CambiosPendientes => Set<CambioPendiente>();
    public DbSet<CargoChofer> CargosChofer => Set<CargoChofer>();
    public DbSet<CorteChofer> CortesChofer => Set<CorteChofer>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Esquema);

        builder.Entity<Vehiculo>(e =>
        {
            e.ToTable("vehiculos");
            e.HasIndex(v => v.Placas).IsUnique().HasDatabaseName("ux_vehiculos_placas");
            e.Property(v => v.CapacidadKg).Cantidad();
        });

        builder.Entity<Ruta>(e =>
        {
            e.ToTable("rutas");
            e.HasIndex(r => r.Codigo).IsUnique().HasDatabaseName("ux_rutas_codigo");
            e.HasIndex(r => r.VersionSync).HasDatabaseName("ix_rutas_version_sync");
            e.HasIndex(r => r.VendedorId).HasDatabaseName("ix_rutas_vendedor");
            e.ConcurrenciaXmin();
        });

        builder.Entity<RutaCliente>(e =>
        {
            e.ToTable("rutas_clientes");
            e.HasKey(rc => new { rc.RutaId, rc.ClienteId });
            e.HasIndex(rc => new { rc.RutaId, rc.VersionSync }).HasDatabaseName("ix_rutas_clientes_ruta_version");
        });

        builder.Entity<Envio>(e =>
        {
            e.ToTable("envios");
            e.HasIndex(x => x.Folio).IsUnique().HasDatabaseName("ux_envios_folio");
            e.HasIndex(x => new { x.RutaId, x.FechaOperativa }).HasDatabaseName("ix_envios_ruta_fecha");
            e.HasIndex(x => new { x.ChoferId, x.FechaOperativa }).HasDatabaseName("ix_envios_chofer_fecha");
            e.Property(x => x.Tipo).ComoTexto("envios");
            e.Property(x => x.Estado).ComoTexto("envios");
            e.HasMany(x => x.Partidas).WithOne().HasForeignKey(p => p.EnvioId).OnDelete(DeleteBehavior.Cascade);
            e.ConcurrenciaXmin();
        });

        builder.Entity<EnvioPartida>(e =>
        {
            e.ToTable("envios_partidas");
            e.Property(p => p.CantidadBase).Cantidad();
        });

        builder.Entity<Retorno>(e =>
        {
            e.ToTable("retornos");
            e.HasIndex(r => r.Folio).IsUnique().HasDatabaseName("ux_retornos_folio");
            e.HasIndex(r => r.OpId).IsUnique().HasFilter("op_id IS NOT NULL").HasDatabaseName("ux_retornos_op_id");
            e.HasIndex(r => new { r.RutaId, r.FechaOperativa }).HasDatabaseName("ix_retornos_ruta_fecha");
            e.Property(r => r.Tipo).ComoTexto("retornos");
            e.Property(r => r.Estado).ComoTexto("retornos");
            e.Property(r => r.Latitud).Coordenada();
            e.Property(r => r.Longitud).Coordenada();
            e.Property(r => r.PrecisionM).Metros();
            e.HasMany(r => r.Partidas).WithOne().HasForeignKey(p => p.RetornoId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RetornoPartida>(e =>
        {
            e.ToTable("retornos_partidas");
            e.Property(p => p.CantidadBase).Cantidad();
            e.Property(p => p.Condicion).ComoTexto("retornos_partidas");
            e.Property(p => p.Sentido).ComoTexto("retornos_partidas");
        });

        builder.Entity<CambioPendiente>(e =>
        {
            e.ToTable("cambios_pendientes");
            e.HasIndex(c => new { c.ClienteId, c.Estado }).HasDatabaseName("ix_cambios_pendientes_cliente_estado");
            e.HasIndex(c => c.VersionSync).HasDatabaseName("ix_cambios_pendientes_version_sync");
            e.Property(c => c.CantidadBase).Cantidad();
            e.Property(c => c.CantidadSaldadaBase).Cantidad();
            e.Property(c => c.Estado).ComoTexto("cambios_pendientes");
            e.Property(c => c.MotivoCancelacion).HasMaxLength(500);
            e.ConcurrenciaXmin();
        });

        builder.Entity<Liquidacion>(e =>
        {
            e.ToTable("liquidaciones");
            e.HasIndex(l => l.Folio).IsUnique().HasDatabaseName("ux_liquidaciones_folio");
            e.HasIndex(l => new { l.RutaId, l.FechaOperativa }).IsUnique().HasDatabaseName("ux_liquidaciones_ruta_fecha");
            e.Property(l => l.Estado).ComoTexto("liquidaciones");
            e.Property(l => l.Formato).ComoTexto("liquidaciones");
            e.Property(l => l.EfectivoEsperado).Importe();
            e.Property(l => l.TotalVenta).Importe();
            e.Property(l => l.TotalCredito).Importe();
            e.Property(l => l.TotalCobros).Importe();
            e.Property(l => l.TotalTransferencias).Importe();
            e.Property(l => l.TotalCheques).Importe();
            e.Property(l => l.TotalGastos).Importe();
            e.Property(l => l.DiferenciaPrecios).Importe();
            e.HasMany(l => l.Gastos).WithOne().HasForeignKey(g => g.LiquidacionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(l => l.EfectivoEntregado).Importe();
            e.Property(l => l.DiferenciaEfectivo).Importe();
            e.HasMany(l => l.Partidas).WithOne().HasForeignKey(p => p.LiquidacionId).OnDelete(DeleteBehavior.Cascade);
            e.ConcurrenciaXmin();
        });

        builder.Entity<LiquidacionPartida>(e =>
        {
            e.ToTable("liquidaciones_partidas");
            e.HasIndex(p => new { p.LiquidacionId, p.ProductoId, p.Lote }).IsUnique().HasDatabaseName("ux_liquidaciones_partidas_producto_lote");
            e.Property(p => p.Cargado).Cantidad();
            e.Property(p => p.CargadoInicial).Cantidad();
            e.Property(p => p.Recargado).Cantidad();
            e.Property(p => p.Vendido).Cantidad();
            e.Property(p => p.ImporteVenta).Importe();
            e.Property(p => p.CantidadACobro).Cantidad();
            e.Property(p => p.PrecioACobro).Precio();
            e.Ignore(p => p.CantidadACobroSugerida);
            e.Ignore(p => p.AplicaACobro);
            e.Property(p => p.DevueltoClientesVendible).Cantidad();
            e.Property(p => p.DevueltoClientesNoVendible).Cantidad();
            e.Property(p => p.CambiosEntregados).Cantidad();
            e.Property(p => p.SobranteEsperado).Cantidad();
            e.Property(p => p.SobranteContado).Cantidad();
            e.Property(p => p.Diferencia).Cantidad();
            e.Property(p => p.DestinoSobrante).ComoTexto("liquidaciones_partidas");
        });

        builder.Entity<CargoChofer>(e =>
        {
            e.ToTable("cargos_chofer");
            e.HasIndex(c => c.Folio).IsUnique().HasDatabaseName("ux_cargos_chofer_folio");
            e.HasIndex(c => new { c.ChoferId, c.Estado }).HasDatabaseName("ix_cargos_chofer_chofer_estado");
            e.HasIndex(c => c.LiquidacionId).HasDatabaseName("ix_cargos_chofer_liquidacion");
            e.Property(c => c.Tipo).ComoTexto("cargos_chofer");
            e.Property(c => c.Estado).ComoTexto("cargos_chofer");
            e.Property(c => c.CantidadBase).Cantidad();
            e.Property(c => c.PrecioUnitario).Precio();
            e.Property(c => c.Importe).Importe();
            e.Property(c => c.Notas).HasMaxLength(500);
            e.Property(c => c.MotivoCancelacion).HasMaxLength(500);
            e.ConcurrenciaXmin();
        });

        builder.Entity<CorteChofer>(e =>
        {
            e.ToTable("cortes_chofer");
            e.HasIndex(c => c.Folio).IsUnique().HasDatabaseName("ux_cortes_chofer_folio");
            e.HasIndex(c => new { c.ChoferId, c.Desde }).IsUnique().HasDatabaseName("ux_cortes_chofer_chofer_desde");
            e.Property(c => c.Estado).ComoTexto("cortes_chofer");
            e.Property(c => c.TotalVenta).Importe();
            e.Property(c => c.TotalCargos).Importe();
            e.ConcurrenciaXmin();
        });

        builder.Entity<LiquidacionGasto>(e =>
        {
            e.ToTable("liquidaciones_gastos");
            e.Property(g => g.Concepto).HasMaxLength(200);
            e.Property(g => g.Comprobante).HasMaxLength(200);
            e.Property(g => g.Importe).Importe();
        });

        builder.MapearBitacora();
        builder.UsarIdsDeAplicacion();
    }
}
