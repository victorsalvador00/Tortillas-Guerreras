using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Inventario.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Dominio;
using TG.Ventas.Infraestructura;
using TG.Ventas.Publico;

namespace TG.Ventas.Aplicacion;

// ───────────────────────────── Pedidos ─────────────────────────────

public sealed record PartidaPedidoRequest(Guid ProductoId, Guid UnidadId, decimal Cantidad);

public sealed record GuardarPedidoRequest(Guid ClienteId, DateOnly? FechaEntrega, string? OrdenCompraCliente, string? Notas,
    IReadOnlyList<PartidaPedidoRequest> Partidas);

public sealed record AsignarPedidoRequest(Guid RutaId, Guid? EnvioId);

public sealed class GuardarPedidoValidator : AbstractValidator<GuardarPedidoRequest>
{
    public GuardarPedidoValidator()
    {
        RuleFor(x => x.ClienteId).NotEmpty().WithMessage("Elige el cliente.");
        RuleFor(x => x.Partidas).NotEmpty().WithMessage("Agrega al menos una partida.");
        RuleForEach(x => x.Partidas).ChildRules(p =>
        {
            p.RuleFor(x => x.ProductoId).NotEmpty();
            p.RuleFor(x => x.UnidadId).NotEmpty();
            p.RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad debe ser mayor que cero.");
        });
    }
}

public sealed record PedidoPartidaDto(Guid Id, Guid ProductoId, string Sku, string Producto, Guid UnidadId, string Unidad,
    decimal Cantidad, decimal CantidadBase, decimal PrecioUnitario, decimal TasaIva, decimal? TasaIeps, decimal Importe,
    decimal CantidadEntregadaBase);

public sealed record PedidoDto(Guid Id, string Folio, Guid ClienteId, string Cliente, DateOnly Fecha, DateOnly? FechaEntrega,
    EstadoPedido Estado, Guid? RutaId, Guid? EnvioId, string? OrdenCompraCliente, string? Notas, decimal Subtotal, decimal Iva,
    decimal Ieps, decimal Total, IReadOnlyList<PedidoPartidaDto> Partidas);

public sealed class PedidosHandler(
    VentasDbContext db,
    ICatalogosService catalogos,
    IInventarioService inventario,
    IGeneradorFolios folios,
    IFechaOperativa fechaOperativa)
{
    /// <param name="envioId">Pedidos que van en esa carga.</param>
    /// <param name="sinCarga">Solo pedidos que todavía no van en ninguna carga.</param>
    public async Task<Paginado<PedidoDto>> ListarAsync(EstadoPedido? estado, Guid? clienteId, Guid? rutaId, DateOnly? desde,
        DateOnly? hasta, Guid? envioId, bool? sinCarga, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Pedidos.AsNoTracking().Include(p => p.Partidas).AsQueryable();
        if (estado is not null)
        {
            consulta = consulta.Where(p => p.Estado == estado);
        }

        if (clienteId is not null)
        {
            consulta = consulta.Where(p => p.ClienteId == clienteId);
        }

        if (rutaId is not null)
        {
            consulta = consulta.Where(p => p.RutaId == rutaId);
        }

        if (envioId is not null)
        {
            consulta = consulta.Where(p => p.EnvioId == envioId);
        }

        if (sinCarga == true)
        {
            consulta = consulta.Where(p => p.EnvioId == null);
        }

        if (desde is not null)
        {
            consulta = consulta.Where(p => (p.FechaEntrega ?? p.Fecha) >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(p => (p.FechaEntrega ?? p.Fecha) <= hasta);
        }

        var paginado = await consulta.OrderByDescending(p => p.Fecha).ThenByDescending(p => p.Folio).PaginarAsync(pagina, tamano, ct);
        var dtos = await MapearAsync(paginado.Items.ToList(), ct);
        return new Paginado<PedidoDto>(dtos, paginado.Total, paginado.Pagina, paginado.Tamano);
    }

    public async Task<Result<PedidoDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var pedido = await db.Pedidos.AsNoTracking().Include(p => p.Partidas).FirstOrDefaultAsync(p => p.Id == id, ct);
        return pedido is null
            ? Error.NoEncontrado(ErroresVentas.PedidoNoEncontrado, "El pedido no existe.")
            : (await MapearAsync([pedido], ct))[0];
    }

    public async Task<Result<PedidoDto>> GuardarAsync(Guid? id, GuardarPedidoRequest r, CancellationToken ct)
    {
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ClienteId && c.Activo, ct);
        if (cliente is null)
        {
            return Error.NoEncontrado(ErroresVentas.ClienteNoEncontrado, "El cliente no existe o está inactivo.");
        }

        Pedido? pedido;
        if (id is null)
        {
            pedido = new Pedido
            {
                Folio = await folios.SiguienteAsync(VentasDbContext.Esquema, "PED", ct),
                Fecha = fechaOperativa.Hoy(),
            };
            db.Pedidos.Add(pedido);
        }
        else
        {
            pedido = await db.Pedidos.Include(p => p.Partidas).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (pedido is null)
            {
                return Error.NoEncontrado(ErroresVentas.PedidoNoEncontrado, "El pedido no existe.");
            }

            if (pedido.Estado != EstadoPedido.Capturado)
            {
                return Error.Conflicto(ErroresVentas.EstadoPedido, "Solo se edita un pedido capturado.");
            }

            db.RemoveRange(pedido.Partidas);
            pedido.Partidas.Clear();
        }

        var productos = await catalogos.ObtenerProductosAsync(r.Partidas.Select(p => p.ProductoId), ct);
        var ajenos = await db.ExclusivosAjenosAsync(cliente, r.Partidas.Select(p => p.ProductoId), ct);
        if (ajenos.Count > 0)
        {
            var nombre = productos.GetValueOrDefault(ajenos.First())?.Nombre ?? "El producto";
            return Error.Validacion(ErroresVentas.ProductoExclusivo, $"{nombre} es exclusivo de otros clientes; no se pide para {cliente.Nombre}.");
        }

        var hoy = fechaOperativa.Hoy();
        foreach (var p in r.Partidas)
        {
            if (!productos.TryGetValue(p.ProductoId, out var producto) || !producto.EsVendible || !producto.Activo)
            {
                return Error.Validacion(ErroresVentas.ProductoNoVendible, "El producto no existe o no es vendible.");
            }

            var conversion = await catalogos.ConvertirABaseAsync(p.ProductoId, p.UnidadId, p.Cantidad, UsoUnidad.Venta, ct);
            if (!conversion.EsExito)
            {
                return conversion.Error!;
            }

            var precio = await db.VigenteAsync(cliente, p.ProductoId, p.UnidadId, hoy, ct);
            if (precio is null)
            {
                return Error.Validacion(ErroresVentas.SinPrecio, $"{producto.Nombre} no tiene precio en la lista del cliente.");
            }

            var (importe, _, _) = CalculoImportes.Partida(p.Cantidad, precio.Value, producto.TasaIva, producto.TasaIeps);
            var partida = new PedidoPartida
            {
                PedidoId = pedido.Id, ProductoId = p.ProductoId, UnidadId = p.UnidadId, FactorABase = conversion.Valor.FactorABase,
                Cantidad = p.Cantidad, CantidadBase = conversion.Valor.Base, PrecioUnitario = precio.Value,
                TasaIva = producto.TasaIva, TasaIeps = producto.TasaIeps, Importe = importe,
            };
            pedido.Partidas.Add(partida);
        }

        pedido.ClienteId = r.ClienteId;
        pedido.FechaEntrega = r.FechaEntrega;
        pedido.OrdenCompraCliente = string.IsNullOrWhiteSpace(r.OrdenCompraCliente) ? null : r.OrdenCompraCliente.Trim();
        pedido.Notas = string.IsNullOrWhiteSpace(r.Notas) ? null : r.Notas.Trim();
        Totales(pedido);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(pedido.Id, ct);
    }

    /// <summary>RN-VEN-13: al confirmar, el pedido toma los precios vigentes del cliente y desde ahí quedan fijos.</summary>
    public async Task<Result<PedidoDto>> ConfirmarAsync(Guid id, CancellationToken ct)
    {
        var pedido = await db.Pedidos.Include(p => p.Partidas).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pedido is null)
        {
            return Error.NoEncontrado(ErroresVentas.PedidoNoEncontrado, "El pedido no existe.");
        }

        if (pedido.Estado != EstadoPedido.Capturado)
        {
            return Error.Conflicto(ErroresVentas.EstadoPedido, $"La acción no aplica a un pedido {pedido.Estado}.");
        }

        var cliente = await db.Clientes.AsNoTracking().FirstAsync(c => c.Id == pedido.ClienteId, ct);
        var hoy = fechaOperativa.Hoy();
        foreach (var partida in pedido.Partidas)
        {
            var precio = await db.VigenteAsync(cliente, partida.ProductoId, partida.UnidadId, hoy, ct);
            if (precio is null)
            {
                return Error.Validacion(ErroresVentas.SinPrecio, "Un producto del pedido ya no tiene precio para el cliente.");
            }

            if (precio != partida.PrecioUnitario)
            {
                partida.PrecioUnitario = precio.Value;
                partida.Importe = CalculoImportes.Partida(partida.Cantidad, partida.PrecioUnitario, partida.TasaIva, partida.TasaIeps).Importe;
            }
        }

        Totales(pedido);
        pedido.Estado = EstadoPedido.Confirmado;
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    public Task<Result<PedidoDto>> CancelarAsync(Guid id, CancellationToken ct) =>
        CambiarAsync(id, [EstadoPedido.Capturado, EstadoPedido.Confirmado, EstadoPedido.Asignado], p => p.Estado = EstadoPedido.Cancelado, ct);

    public async Task<Result<PedidoDto>> AsignarAsync(Guid id, AsignarPedidoRequest r, CancellationToken ct)
    {
        if (await inventario.AlmacenDeRutaAsync(r.RutaId, ct) is null)
        {
            return Error.Validacion(ErroresVentas.RutaSinAlmacen, "La ruta no existe o no tiene almacén.");
        }

        return await CambiarAsync(id, [EstadoPedido.Confirmado, EstadoPedido.Asignado], p =>
        {
            p.RutaId = r.RutaId;
            p.EnvioId = r.EnvioId;
            p.Estado = EstadoPedido.Asignado;
        }, ct);
    }

    private async Task<Result<PedidoDto>> CambiarAsync(Guid id, EstadoPedido[] desde, Action<Pedido> cambio, CancellationToken ct)
    {
        var pedido = await db.Pedidos.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pedido is null)
        {
            return Error.NoEncontrado(ErroresVentas.PedidoNoEncontrado, "El pedido no existe.");
        }

        if (!desde.Contains(pedido.Estado))
        {
            return Error.Conflicto(ErroresVentas.EstadoPedido, $"La acción no aplica a un pedido {pedido.Estado}.");
        }

        cambio(pedido);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    private static void Totales(Pedido pedido)
    {
        var calculos = pedido.Partidas.Select(p => CalculoImportes.Partida(p.Cantidad, p.PrecioUnitario, p.TasaIva, p.TasaIeps)).ToList();
        pedido.Subtotal = calculos.Sum(c => c.Importe);
        pedido.Ieps = calculos.Sum(c => c.Ieps);
        pedido.Iva = calculos.Sum(c => c.Iva);
        pedido.Total = pedido.Subtotal + pedido.Ieps + pedido.Iva;
    }

    private async Task<IReadOnlyList<PedidoDto>> MapearAsync(List<Pedido> pedidos, CancellationToken ct)
    {
        var clienteIds = pedidos.Select(p => p.ClienteId).Distinct().ToList();
        var clientes = await db.Clientes.AsNoTracking().Where(c => clienteIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        var productos = await catalogos.ObtenerProductosAsync(pedidos.SelectMany(p => p.Partidas).Select(p => p.ProductoId), ct);
        var unidades = await catalogos.ObtenerUnidadesAsync(ct);
        return pedidos.Select(p => new PedidoDto(p.Id, p.Folio, p.ClienteId, clientes.GetValueOrDefault(p.ClienteId) ?? "?", p.Fecha,
            p.FechaEntrega, p.Estado, p.RutaId, p.EnvioId, p.OrdenCompraCliente, p.Notas, p.Subtotal, p.Iva, p.Ieps, p.Total,
            p.Partidas.Select(x => new PedidoPartidaDto(x.Id, x.ProductoId, productos.GetValueOrDefault(x.ProductoId)?.Sku ?? "?",
                productos.GetValueOrDefault(x.ProductoId)?.Nombre ?? "?", x.UnidadId, unidades.GetValueOrDefault(x.UnidadId)?.Clave ?? "?",
                x.Cantidad, x.CantidadBase, x.PrecioUnitario, x.TasaIva, x.TasaIeps, x.Importe, x.CantidadEntregadaBase)).ToList()))
            .ToList();
    }

    internal static PedidoInfo AInfo(Pedido p) => new(p.Id, p.Folio, p.ClienteId, p.Fecha, p.FechaEntrega, p.Estado, p.RutaId,
        p.EnvioId, p.OrdenCompraCliente, p.Subtotal, p.Iva, p.Ieps, p.Total, p.VersionSync,
        p.Partidas.Select(x => new PedidoPartidaInfo(x.Id, x.ProductoId, x.UnidadId, x.FactorABase, x.Cantidad, x.CantidadBase,
            x.PrecioUnitario, x.TasaIva, x.TasaIeps, x.Importe, x.CantidadEntregadaBase)).ToList());
}

// ───────────────────────────── Ventas (web) ─────────────────────────────

public sealed record RegistrarVentaWebRequest(Guid ClienteId, Guid AlmacenId, CondicionPago CondicionPago, FormaPago FormaPago,
    IReadOnlyList<PartidaPedidoRequest> Partidas, Guid? RutaId, DateOnly? FechaOperativa);

public sealed class RegistrarVentaWebValidator : AbstractValidator<RegistrarVentaWebRequest>
{
    public RegistrarVentaWebValidator()
    {
        RuleFor(x => x.ClienteId).NotEmpty().WithMessage("Elige el cliente.");
        RuleFor(x => x.AlmacenId).NotEmpty().WithMessage("Elige el almacén.");
        RuleFor(x => x.Partidas).NotEmpty().WithMessage("Agrega al menos una partida.");
        RuleForEach(x => x.Partidas).ChildRules(p =>
            p.RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad debe ser mayor que cero."));
    }
}

public sealed record CancelarVentaRequest(string Motivo);

public sealed class CancelarVentaValidator : AbstractValidator<CancelarVentaRequest>
{
    public CancelarVentaValidator() => RuleFor(x => x.Motivo).NotEmpty().WithMessage("Escribe el motivo de la cancelación.");
}

public sealed record VentaPartidaDto(Guid Id, Guid ProductoId, string Sku, string Producto, Guid UnidadId, string Unidad,
    decimal Cantidad, decimal CantidadBase, string Lote, decimal PrecioUnitario, decimal TasaIva, decimal? TasaIeps,
    decimal Importe, decimal Ieps, decimal Iva);

public sealed record VentaDto(Guid Id, string Folio, OrigenVenta Origen, TipoVenta Tipo, Guid? PedidoId, Guid ClienteId,
    string Cliente, Guid? RutaId, Guid AlmacenId, Guid? DispositivoId, Guid VendedorId, DateTimeOffset? FechaHoraDispositivo,
    DateTimeOffset FechaHoraServidor, DateOnly FechaOperativa, CondicionPago CondicionPago, FormaPago FormaPago,
    decimal Subtotal, decimal Ieps, decimal Iva, decimal Total, decimal? TotalApp, decimal? Latitud, decimal? Longitud,
    decimal? PrecisionM, decimal? DistanciaClienteM, bool RequiereRevision, EstadoVenta Estado, string? MotivoCancelacion,
    Guid? FacturaId, IReadOnlyList<VentaPartidaDto> Partidas);

public sealed class VentasWebHandler(
    VentasDbContext db,
    VentasService servicio,
    IInventarioService inventario,
    ICatalogosService catalogos,
    IFechaOperativa fechaOperativa,
    IUsuarioActual usuario,
    IUnitOfWork unitOfWork,
    TimeProvider reloj)
{
    public async Task<Paginado<VentaDto>> ListarAsync(DateOnly? desde, DateOnly? hasta, Guid? rutaId, Guid? clienteId,
        EstadoVenta? estado, bool? requiereRevision, OrigenVenta? origen, string? folio, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Ventas.AsNoTracking().Include(v => v.Partidas).AsQueryable();
        if (desde is not null)
        {
            consulta = consulta.Where(v => v.FechaOperativa >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(v => v.FechaOperativa <= hasta);
        }

        if (rutaId is not null)
        {
            consulta = consulta.Where(v => v.RutaId == rutaId);
        }

        if (clienteId is not null)
        {
            consulta = consulta.Where(v => v.ClienteId == clienteId);
        }

        if (estado is not null)
        {
            consulta = consulta.Where(v => v.Estado == estado);
        }

        if (requiereRevision is not null)
        {
            consulta = consulta.Where(v => v.RequiereRevision == requiereRevision);
        }

        if (origen is not null)
        {
            consulta = consulta.Where(v => v.Origen == origen);
        }

        if (!string.IsNullOrWhiteSpace(folio))
        {
            consulta = consulta.Where(v => EF.Functions.ILike(v.Folio, $"%{folio.Trim()}%"));
        }

        var paginado = await consulta.OrderByDescending(v => v.FechaHoraServidor).PaginarAsync(pagina, tamano, ct);
        var dtos = await MapearAsync(paginado.Items.ToList(), ct);
        return new Paginado<VentaDto>(dtos, paginado.Total, paginado.Pagina, paginado.Tamano);
    }

    public async Task<Result<VentaDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var venta = await db.Ventas.AsNoTracking().Include(v => v.Partidas).FirstOrDefaultAsync(v => v.Id == id, ct);
        return venta is null
            ? Error.NoEncontrado(ErroresVentas.VentaNoEncontrada, "La venta no existe.")
            : (await MapearAsync([venta], ct))[0];
    }

    public async Task<Result<VentaDto>> RegistrarAsync(RegistrarVentaWebRequest r, CancellationToken ct)
    {
        var resultado = await servicio.RegistrarVentaAsync(new RegistrarVentaComando(OrigenVenta.Web, r.ClienteId, r.AlmacenId,
            usuario.Id ?? Guid.Empty, r.FechaOperativa ?? fechaOperativa.Hoy(), r.CondicionPago, r.FormaPago,
            r.Partidas.Select(p => new PartidaVentaComando(p.ProductoId, p.UnidadId, p.Cantidad)).ToList(), RutaId: r.RutaId), ct);
        return resultado.EsExito ? await ObtenerAsync(resultado.Valor.VentaId, ct) : resultado.Error!;
    }

    /// <summary>RN-VEN-07: mismo día operativo, no facturada; genera el movimiento contrario (+) sobre la venta.</summary>
    public async Task<Result<VentaDto>> CancelarAsync(Guid id, CancelarVentaRequest r, CancellationToken ct)
    {
        var resultado = await unitOfWork.EjecutarAsync(async t =>
        {
            var venta = await db.Ventas.FirstOrDefaultAsync(v => v.Id == id, t);
            if (venta is null)
            {
                return Error.NoEncontrado(ErroresVentas.VentaNoEncontrada, "La venta no existe.");
            }

            if (venta.Estado == EstadoVenta.Cancelada)
            {
                return Error.Conflicto(ErroresVentas.VentaCancelada, "La venta ya está cancelada.");
            }

            if (venta.FacturaId is not null)
            {
                return Error.Conflicto(ErroresVentas.VentaFacturada, "La venta está facturada: cancela primero la factura.");
            }

            if (venta.FechaOperativa != fechaOperativa.Hoy())
            {
                return Error.Conflicto(ErroresVentas.FueraDeDia, "Solo se cancelan ventas del mismo día operativo.");
            }

            var originales = await inventario.MovimientosDeDocumentoAsync(DocumentoInventario.Venta, venta.Id, t);
            var lineas = originales.Where(m => m.Tipo == TipoMovimiento.Venta)
                .Select(m => new LineaMovimiento(TipoMovimiento.RetornoCliente, m.AlmacenId, m.ProductoId, m.Lote, -m.Cantidad,
                    m.CostoUnitario, Notas: "Cancelación de venta")).ToList();
            var movimientos = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(DocumentoInventario.Venta,
                venta.Id, venta.Folio, fechaOperativa.Hoy(), usuario.Id ?? Guid.Empty, lineas, Notas: r.Motivo.Trim()), t);
            if (!movimientos.EsExito)
            {
                return movimientos.Error!;
            }

            venta.Estado = EstadoVenta.Cancelada;
            venta.MotivoCancelacion = r.Motivo.Trim();
            venta.CanceladaEn = reloj.GetUtcNow();
            venta.CanceladaPor = usuario.Id;
            await db.SaveChangesAsync(t);
            return Result<Guid>.Exito(venta.Id);
        }, ct);

        return resultado.EsExito ? await ObtenerAsync(id, ct) : resultado.Error!;
    }

    private async Task<IReadOnlyList<VentaDto>> MapearAsync(List<Venta> ventas, CancellationToken ct)
    {
        var clienteIds = ventas.Select(v => v.ClienteId).Distinct().ToList();
        var clientes = await db.Clientes.AsNoTracking().Where(c => clienteIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        var productos = await catalogos.ObtenerProductosAsync(ventas.SelectMany(v => v.Partidas).Select(p => p.ProductoId), ct);
        var unidades = await catalogos.ObtenerUnidadesAsync(ct);
        return ventas.Select(v => new VentaDto(v.Id, v.Folio, v.Origen, v.Tipo, v.PedidoId, v.ClienteId,
            clientes.GetValueOrDefault(v.ClienteId) ?? "?", v.RutaId, v.AlmacenId, v.DispositivoId, v.VendedorId,
            v.FechaHoraDispositivo, v.FechaHoraServidor, v.FechaOperativa, v.CondicionPago, v.FormaPago, v.Subtotal, v.Ieps, v.Iva,
            v.Total, v.TotalApp, v.Latitud, v.Longitud, v.PrecisionM, v.DistanciaClienteM, v.RequiereRevision, v.Estado,
            v.MotivoCancelacion, v.FacturaId,
            v.Partidas.Select(p => new VentaPartidaDto(p.Id, p.ProductoId, productos.GetValueOrDefault(p.ProductoId)?.Sku ?? "?",
                productos.GetValueOrDefault(p.ProductoId)?.Nombre ?? "?", p.UnidadId, unidades.GetValueOrDefault(p.UnidadId)?.Clave ?? "?",
                p.Cantidad, p.CantidadBase, p.Lote, p.PrecioUnitario, p.TasaIva, p.TasaIeps, p.Importe, p.Ieps, p.Iva)).ToList()))
            .ToList();
    }
}

// ───────────────────────────── Cobros ─────────────────────────────

public sealed record RegistrarCobroRequest(Guid ClienteId, FormaPago FormaPago, decimal Importe, IReadOnlyList<Guid>? VentaIds,
    Guid? RutaId);

public sealed record CobroAplicacionDto(Guid VentaId, string Folio, decimal Importe);

public sealed record CobroDto(Guid Id, string Folio, Guid ClienteId, string Cliente, Guid? RutaId, DateOnly FechaOperativa,
    FormaPago FormaPago, decimal Importe, EstadoCobro Estado, IReadOnlyList<CobroAplicacionDto> Aplicaciones);

public sealed class CobrosHandler(VentasDbContext db, VentasService servicio, IFechaOperativa fechaOperativa)
{
    public async Task<Paginado<CobroDto>> ListarAsync(Guid? clienteId, Guid? rutaId, DateOnly? desde, DateOnly? hasta,
        int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Cobros.AsNoTracking().Include(c => c.Aplicaciones).AsQueryable();
        if (clienteId is not null)
        {
            consulta = consulta.Where(c => c.ClienteId == clienteId);
        }

        if (rutaId is not null)
        {
            consulta = consulta.Where(c => c.RutaId == rutaId);
        }

        if (desde is not null)
        {
            consulta = consulta.Where(c => c.FechaOperativa >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(c => c.FechaOperativa <= hasta);
        }

        var paginado = await consulta.OrderByDescending(c => c.CreadoEn).PaginarAsync(pagina, tamano, ct);
        var items = paginado.Items.ToList();
        var clienteIds = items.Select(c => c.ClienteId).Distinct().ToList();
        var clientes = await db.Clientes.AsNoTracking().Where(c => clienteIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        var ventaIds = items.SelectMany(c => c.Aplicaciones).Select(a => a.VentaId).Distinct().ToList();
        var folios = await db.Ventas.AsNoTracking().Where(v => ventaIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Folio, ct);
        return new Paginado<CobroDto>(items.Select(c => new CobroDto(c.Id, c.Folio, c.ClienteId,
                clientes.GetValueOrDefault(c.ClienteId) ?? "?", c.RutaId, c.FechaOperativa, c.FormaPago, c.Importe, c.Estado,
                c.Aplicaciones.Select(a => new CobroAplicacionDto(a.VentaId, folios.GetValueOrDefault(a.VentaId) ?? "?", a.Importe)).ToList()))
            .ToList(), paginado.Total, paginado.Pagina, paginado.Tamano);
    }

    public Task<Result<CobroRegistrado>> RegistrarAsync(RegistrarCobroRequest r, CancellationToken ct) =>
        servicio.RegistrarCobroAsync(new RegistrarCobroComando(r.ClienteId, fechaOperativa.Hoy(), r.FormaPago, r.Importe,
            RutaId: r.RutaId, VentaIds: r.VentaIds), ct);
}

// ───────────────────────────── Incidencias ─────────────────────────────

public sealed record IncidenciaDto(Guid Id, TipoIncidencia Tipo, string EntidadTipo, Guid EntidadId, string? Referencia,
    JsonElement? Detalle, EstadoIncidencia Estado, DateTimeOffset CreadoEn, Guid? RevisadaPor, DateTimeOffset? RevisadaEn,
    string? NotasRevision);

public sealed record RevisarIncidenciaRequest(string? Notas);

public sealed class IncidenciasHandler(VentasDbContext db, IUsuarioActual usuario, TimeProvider reloj)
{
    public async Task<Paginado<IncidenciaDto>> ListarAsync(EstadoIncidencia? estado, TipoIncidencia? tipo, Guid? entidadId,
        DateOnly? desde, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Incidencias.AsNoTracking();
        if (estado is not null)
        {
            consulta = consulta.Where(i => i.Estado == estado);
        }

        if (tipo is not null)
        {
            consulta = consulta.Where(i => i.Tipo == tipo);
        }

        if (entidadId is not null)
        {
            consulta = consulta.Where(i => i.EntidadId == entidadId);
        }

        if (desde is not null)
        {
            var inicio = new DateTimeOffset(desde.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            consulta = consulta.Where(i => i.CreadoEn >= inicio);
        }

        var paginado = await consulta.OrderByDescending(i => i.CreadoEn).PaginarAsync(pagina, tamano, ct);
        var items = paginado.Items.ToList();
        var ventaIds = items.Where(i => i.EntidadTipo == "Venta").Select(i => i.EntidadId).ToList();
        var clienteIds = items.Where(i => i.EntidadTipo == "Cliente").Select(i => i.EntidadId).ToList();
        var folios = await db.Ventas.AsNoTracking().Where(v => ventaIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Folio, ct);
        var clientes = await db.Clientes.AsNoTracking().Where(c => clienteIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        return new Paginado<IncidenciaDto>(items.Select(i => new IncidenciaDto(i.Id, i.Tipo, i.EntidadTipo, i.EntidadId,
                folios.GetValueOrDefault(i.EntidadId) ?? clientes.GetValueOrDefault(i.EntidadId),
                i.Detalle is null ? null : JsonDocument.Parse(i.Detalle).RootElement.Clone(), i.Estado, i.CreadoEn, i.RevisadaPor,
                i.RevisadaEn, i.NotasRevision)).ToList(),
            paginado.Total, paginado.Pagina, paginado.Tamano);
    }

    public async Task<Result> RevisarAsync(Guid id, RevisarIncidenciaRequest r, CancellationToken ct)
    {
        var incidencia = await db.Incidencias.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (incidencia is null)
        {
            return Error.NoEncontrado(ErroresVentas.NoEncontrado, "La incidencia no existe.");
        }

        incidencia.Estado = EstadoIncidencia.Revisada;
        incidencia.RevisadaPor = usuario.Id;
        incidencia.RevisadaEn = reloj.GetUtcNow();
        incidencia.NotasRevision = string.IsNullOrWhiteSpace(r.Notas) ? null : r.Notas.Trim();
        await db.SaveChangesAsync(ct);
        return Result.Exito();
    }
}
