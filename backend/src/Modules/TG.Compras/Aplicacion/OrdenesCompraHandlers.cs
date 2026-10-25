using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Compras.Dominio;
using TG.Compras.Infraestructura;
using TG.Inventario.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;

namespace TG.Compras.Aplicacion;

public sealed record PartidaOrdenCompraDto(
    Guid Id,
    Guid ProductoId,
    string Sku,
    string Producto,
    Guid UnidadId,
    string Unidad,
    string UnidadBase,
    decimal FactorABase,
    decimal Cantidad,
    decimal CantidadBase,
    decimal PrecioUnitario,
    decimal TasaIva,
    decimal Importe,
    decimal Iva,
    decimal CantidadRecibidaBase,
    bool ManejaLote);

public sealed record OrdenCompraDto(
    Guid Id,
    string Folio,
    Guid ProveedorId,
    string Proveedor,
    DateOnly Fecha,
    DateOnly? FechaEntregaEsperada,
    Guid AlmacenDestinoId,
    string AlmacenDestino,
    EstadoOrdenCompra Estado,
    decimal Subtotal,
    decimal Iva,
    decimal Total,
    string? Notas,
    Guid? AutorizadaPor,
    DateTimeOffset? AutorizadaEn,
    DateTimeOffset CreadoEn,
    IReadOnlyList<PartidaOrdenCompraDto> Partidas);

public sealed record OrdenCompraResumenDto(
    Guid Id,
    string Folio,
    Guid ProveedorId,
    string Proveedor,
    DateOnly Fecha,
    DateOnly? FechaEntregaEsperada,
    EstadoOrdenCompra Estado,
    decimal Total,
    int Partidas);

public sealed record PartidaOrdenCompraRequest(Guid ProductoId, Guid UnidadId, decimal Cantidad, decimal PrecioUnitario);

public sealed record GuardarOrdenCompraRequest(
    Guid ProveedorId,
    Guid AlmacenDestinoId,
    DateOnly? Fecha,
    DateOnly? FechaEntregaEsperada,
    string? Notas,
    IReadOnlyList<PartidaOrdenCompraRequest> Partidas);

public sealed class GuardarOrdenCompraValidator : AbstractValidator<GuardarOrdenCompraRequest>
{
    public GuardarOrdenCompraValidator()
    {
        RuleFor(x => x.ProveedorId).NotEmpty().WithMessage("Elige el proveedor.");
        RuleFor(x => x.AlmacenDestinoId).NotEmpty().WithMessage("Elige el almacén destino.");
        RuleFor(x => x.Partidas).NotNull();
        RuleForEach(x => x.Partidas).ChildRules(p =>
        {
            p.RuleFor(x => x.ProductoId).NotEmpty();
            p.RuleFor(x => x.UnidadId).NotEmpty();
            p.RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad debe ser mayor que cero.");
            p.RuleFor(x => x.PrecioUnitario).GreaterThanOrEqualTo(0).WithMessage("El precio no puede ser negativo.");
        });
    }
}

/// <summary>Consultas de órdenes de compra.</summary>
public sealed class ConsultasOrdenesCompraHandler(ComprasDbContext db, ICatalogosService catalogos, IInventarioService inventario)
{
    public async Task<Paginado<OrdenCompraResumenDto>> ListarAsync(EstadoOrdenCompra? estado, Guid? proveedorId, DateOnly? desde,
        DateOnly? hasta, string? buscar, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.OrdenesCompra.AsNoTracking();
        if (estado is not null)
        {
            consulta = consulta.Where(o => o.Estado == estado);
        }

        if (proveedorId is not null)
        {
            consulta = consulta.Where(o => o.ProveedorId == proveedorId);
        }

        if (desde is not null)
        {
            consulta = consulta.Where(o => o.Fecha >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(o => o.Fecha <= hasta);
        }

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var patron = $"%{buscar.Trim()}%";
            consulta = consulta.Where(o => EF.Functions.ILike(o.Folio, patron));
        }

        var proyeccion = from o in consulta
                         join p in db.Proveedores on o.ProveedorId equals p.Id
                         orderby o.Fecha descending, o.Folio descending
                         select new OrdenCompraResumenDto(o.Id, o.Folio, o.ProveedorId, p.RazonSocial, o.Fecha, o.FechaEntregaEsperada,
                             o.Estado, o.Total, o.Partidas.Count);
        return await proyeccion.PaginarAsync(pagina, tamano, ct);
    }

    public async Task<Result<OrdenCompraDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var orden = await db.OrdenesCompra.AsNoTracking().Include(o => o.Partidas).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (orden is null)
        {
            return ErroresCompras.OrdenNoEncontrada;
        }

        var proveedor = await db.Proveedores.AsNoTracking().Where(p => p.Id == orden.ProveedorId).Select(p => p.RazonSocial)
            .FirstOrDefaultAsync(ct);
        var almacen = await inventario.ObtenerAlmacenAsync(orden.AlmacenDestinoId, ct);
        var productos = await catalogos.ObtenerProductosAsync(orden.Partidas.Select(p => p.ProductoId), ct);
        var unidades = await catalogos.ObtenerUnidadesAsync(ct);
        return new OrdenCompraDto(orden.Id, orden.Folio, orden.ProveedorId, proveedor ?? "?", orden.Fecha, orden.FechaEntregaEsperada,
            orden.AlmacenDestinoId, almacen?.Codigo ?? "?", orden.Estado, orden.Subtotal, orden.Iva, orden.Total, orden.Notas,
            orden.AutorizadaPor, orden.AutorizadaEn, orden.CreadoEn,
            orden.Partidas.OrderBy(p => p.Id).Select(p =>
            {
                var producto = productos.GetValueOrDefault(p.ProductoId);
                return new PartidaOrdenCompraDto(p.Id, p.ProductoId, producto?.Sku ?? "?", producto?.Nombre ?? "?", p.UnidadId,
                    unidades.GetValueOrDefault(p.UnidadId)?.Clave ?? "?", producto?.UnidadBaseClave ?? string.Empty, p.FactorABase,
                    p.Cantidad, p.CantidadBase, p.PrecioUnitario, p.TasaIva, p.Importe, p.Iva, p.CantidadRecibidaBase,
                    producto?.ManejaLote ?? false);
            }).ToList());
    }
}

/// <summary>Captura y edición en Borrador (RN-COM-01, RN-COM-02).</summary>
public sealed class GuardarOrdenCompraHandler(
    ComprasDbContext db,
    ICatalogosService catalogos,
    IInventarioService inventario,
    IGeneradorFolios folios,
    IFechaOperativa fechaOperativa,
    ConsultasOrdenesCompraHandler consultas)
{
    public async Task<Result<OrdenCompraDto>> CrearAsync(GuardarOrdenCompraRequest r, CancellationToken ct)
    {
        var orden = new OrdenCompra();
        var aplicado = await AplicarAsync(orden, r, ct);
        if (!aplicado.EsExito)
        {
            return aplicado.Error!;
        }

        orden.Folio = await folios.SiguienteAsync(ComprasDbContext.Esquema, "OC", ct);
        db.OrdenesCompra.Add(orden);
        await db.SaveChangesAsync(ct);
        return await consultas.ObtenerAsync(orden.Id, ct);
    }

    public async Task<Result<OrdenCompraDto>> ActualizarAsync(Guid id, GuardarOrdenCompraRequest r, CancellationToken ct)
    {
        var orden = await db.OrdenesCompra.Include(o => o.Partidas).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (orden is null)
        {
            return ErroresCompras.OrdenNoEncontrada;
        }

        if (orden.Estado != EstadoOrdenCompra.Borrador)
        {
            return ErroresCompras.NoEditable;
        }

        var anteriores = orden.Partidas.ToList();
        var aplicado = await AplicarAsync(orden, r, ct);
        if (!aplicado.EsExito)
        {
            return aplicado.Error!;
        }

        db.OrdenesCompraPartidas.RemoveRange(anteriores);
        await db.SaveChangesAsync(ct);
        return await consultas.ObtenerAsync(orden.Id, ct);
    }

    private async Task<Result> AplicarAsync(OrdenCompra orden, GuardarOrdenCompraRequest r, CancellationToken ct)
    {
        var proveedor = await db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == r.ProveedorId, ct);
        if (proveedor is null)
        {
            return ErroresCompras.ProveedorNoEncontrado;
        }

        if (!proveedor.Activo)
        {
            return ErroresCompras.ProveedorInactivo;
        }

        var almacen = await inventario.ObtenerAlmacenAsync(r.AlmacenDestinoId, ct);
        if (almacen is null || !almacen.Activo || almacen.Tipo == TipoAlmacen.Ruta)
        {
            return ErroresCompras.AlmacenInvalido;
        }

        var productos = await catalogos.ObtenerProductosAsync(r.Partidas.Select(p => p.ProductoId), ct);

        // RN-COM-12 [SUP-32]: con el parámetro activo solo se piden productos del catálogo activo del proveedor.
        var soloCatalogo = await catalogos.ObtenerParametroAsync(ClavesParametro.ComprasSoloCatalogoProveedor, true, ct);
        var catalogo = soloCatalogo
            ? (await db.ProveedoresProductos.AsNoTracking().Where(pp => pp.ProveedorId == r.ProveedorId && pp.Activo)
                .Select(pp => pp.ProductoId).ToListAsync(ct)).ToHashSet()
            : [];
        var partidas = new List<OrdenCompraPartida>();
        foreach (var p in r.Partidas)
        {
            if (!productos.TryGetValue(p.ProductoId, out var producto))
            {
                return ErroresCompras.ProductoNoEncontrado;
            }

            if (!producto.EsComprable || !producto.Activo)
            {
                return ErroresCompras.NoComprable(producto.Nombre);
            }

            if (soloCatalogo && !catalogo.Contains(p.ProductoId))
            {
                return ErroresCompras.FueraDeCatalogo(producto.Nombre, proveedor.RazonSocial);
            }

            var conversion = await catalogos.ConvertirABaseAsync(p.ProductoId, p.UnidadId, p.Cantidad, UsoUnidad.Compra, ct);
            if (!conversion.EsExito)
            {
                return conversion.Error!;
            }

            var (importe, iva) = ReglasCompras.Importes(p.Cantidad, p.PrecioUnitario, producto.TasaIva);
            partidas.Add(new OrdenCompraPartida
            {
                OrdenCompraId = orden.Id,
                ProductoId = p.ProductoId,
                UnidadId = p.UnidadId,
                FactorABase = conversion.Valor.FactorABase,
                Cantidad = p.Cantidad,
                CantidadBase = conversion.Valor.Base,
                PrecioUnitario = p.PrecioUnitario,
                TasaIva = producto.TasaIva,
                Importe = importe,
                Iva = iva,
            });
        }

        orden.ProveedorId = r.ProveedorId;
        orden.AlmacenDestinoId = r.AlmacenDestinoId;
        orden.Fecha = r.Fecha ?? (orden.Fecha == default ? fechaOperativa.Hoy() : orden.Fecha);
        orden.FechaEntregaEsperada = r.FechaEntregaEsperada;
        orden.Notas = string.IsNullOrWhiteSpace(r.Notas) ? null : r.Notas.Trim();
        orden.Partidas = partidas;
        orden.RecalcularTotales();
        return Result.Exito();
    }
}

/// <summary>Autorizar (RN-COM-03), cancelar (solo sin recepciones) y cerrar con faltante.</summary>
public sealed class EstadoOrdenCompraHandler(ComprasDbContext db, IUsuarioActual usuario, TimeProvider reloj,
    ConsultasOrdenesCompraHandler consultas)
{
    public async Task<Result<OrdenCompraDto>> AutorizarAsync(Guid id, CancellationToken ct)
    {
        var orden = await db.OrdenesCompra.Include(o => o.Partidas).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (orden is null)
        {
            return ErroresCompras.OrdenNoEncontrada;
        }

        if (orden.Estado != EstadoOrdenCompra.Borrador)
        {
            return ErroresCompras.TransicionInvalida("Solo se autoriza una orden en Borrador.");
        }

        if (orden.Partidas.Count == 0)
        {
            return ErroresCompras.SinPartidas;
        }

        orden.Estado = EstadoOrdenCompra.Autorizada;
        orden.AutorizadaPor = usuario.Id;
        orden.AutorizadaEn = reloj.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await consultas.ObtenerAsync(id, ct);
    }

    public async Task<Result<OrdenCompraDto>> CancelarAsync(Guid id, CancellationToken ct)
    {
        var orden = await db.OrdenesCompra.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (orden is null)
        {
            return ErroresCompras.OrdenNoEncontrada;
        }

        if (orden.Estado is not (EstadoOrdenCompra.Borrador or EstadoOrdenCompra.Autorizada))
        {
            return ErroresCompras.TransicionInvalida("Solo se cancela una orden en Borrador o Autorizada sin recepciones.");
        }

        if (await db.Recepciones.AnyAsync(r => r.OrdenCompraId == id && r.Estado == EstadoRecepcion.Registrada, ct))
        {
            return ErroresCompras.TransicionInvalida("La orden tiene recepciones; ciérrala con faltante en lugar de cancelarla.");
        }

        orden.Estado = EstadoOrdenCompra.Cancelada;
        await db.SaveChangesAsync(ct);
        return await consultas.ObtenerAsync(id, ct);
    }

    public async Task<Result<OrdenCompraDto>> CerrarAsync(Guid id, CancellationToken ct)
    {
        var orden = await db.OrdenesCompra.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (orden is null)
        {
            return ErroresCompras.OrdenNoEncontrada;
        }

        if (orden.Estado != EstadoOrdenCompra.RecibidaParcial)
        {
            return ErroresCompras.TransicionInvalida("Solo se cierra con faltante una orden recibida parcialmente.");
        }

        orden.Estado = EstadoOrdenCompra.Cerrada;
        await db.SaveChangesAsync(ct);
        return await consultas.ObtenerAsync(id, ct);
    }
}
