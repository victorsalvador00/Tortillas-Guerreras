using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Compras.Dominio;
using TG.Compras.Infraestructura;
using TG.Inventario.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;
using P = TG.SharedKernel.Seguridad.Permisos.Compras;

namespace TG.Compras.Aplicacion;

/// <param name="OrdenCompraPartidaId">Con orden: la partida que se recibe (producto, unidad y precio salen de ella).</param>
/// <param name="ProductoId">Compra directa: producto recibido.</param>
public sealed record PartidaRecepcionRequest(
    Guid? OrdenCompraPartidaId,
    Guid? ProductoId,
    Guid? UnidadId,
    decimal Cantidad,
    decimal? PrecioUnitario,
    DateOnly? FechaCaducidad);

/// <param name="OrdenCompraId"><c>null</c> = compra directa sin orden (permiso <c>compras.compra_directa</c>) [SUP-04].</param>
public sealed record RegistrarRecepcionRequest(
    Guid? OrdenCompraId,
    Guid? ProveedorId,
    Guid? AlmacenId,
    string? FacturaProveedor,
    string? Notas,
    IReadOnlyList<PartidaRecepcionRequest> Partidas);

public sealed class RegistrarRecepcionValidator : AbstractValidator<RegistrarRecepcionRequest>
{
    public RegistrarRecepcionValidator()
    {
        RuleFor(x => x.Partidas).NotEmpty().WithMessage("Agrega al menos una partida.");
        RuleFor(x => x.ProveedorId).NotEmpty().When(x => x.OrdenCompraId is null)
            .WithMessage("Elige el proveedor de la compra directa.");
        RuleFor(x => x.AlmacenId).NotEmpty().When(x => x.OrdenCompraId is null)
            .WithMessage("Elige el almacén que recibe.");
        RuleForEach(x => x.Partidas).ChildRules(p =>
        {
            p.RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad debe ser mayor que cero.");
            p.RuleFor(x => x.PrecioUnitario).GreaterThanOrEqualTo(0).When(x => x.PrecioUnitario is not null);
        });
        RuleForEach(x => x.Partidas).Must(p => p.OrdenCompraPartidaId is not null).When(x => x.OrdenCompraId is not null)
            .WithMessage("Indica la partida de la orden que recibes.");
        RuleForEach(x => x.Partidas)
            .Must(p => p.ProductoId is not null && p.UnidadId is not null && p.PrecioUnitario is not null)
            .When(x => x.OrdenCompraId is null)
            .WithMessage("En compra directa captura producto, unidad y precio.");
    }
}

public sealed record PartidaRecepcionDto(
    Guid Id,
    Guid? OrdenCompraPartidaId,
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
    decimal CostoUnitarioBase,
    string? Lote,
    DateOnly? FechaCaducidad);

public sealed record RecepcionDto(
    Guid Id,
    string Folio,
    Guid? OrdenCompraId,
    string? OrdenCompraFolio,
    Guid ProveedorId,
    string Proveedor,
    Guid AlmacenId,
    string Almacen,
    DateOnly FechaOperativa,
    string? FacturaProveedor,
    EstadoRecepcion Estado,
    string? Notas,
    decimal Subtotal,
    DateTimeOffset CreadoEn,
    DateTimeOffset? CanceladaEn,
    IReadOnlyList<PartidaRecepcionDto> Partidas);

public sealed record RecepcionResumenDto(
    Guid Id,
    string Folio,
    string? OrdenCompraFolio,
    Guid ProveedorId,
    string Proveedor,
    DateOnly FechaOperativa,
    string? FacturaProveedor,
    EstadoRecepcion Estado,
    int Partidas);

/// <summary>RN-COM-09: lotes de compra ya generados para un producto y prefijo.</summary>
internal static class LotesCompra
{
    public static Task<List<string>> ExistentesAsync(ComprasDbContext db, Guid productoId, string prefijo, CancellationToken ct) =>
        db.RecepcionesPartidas.AsNoTracking()
            .Where(x => x.ProductoId == productoId && x.Lote != null && x.Lote.StartsWith(prefijo + "-"))
            .Select(x => x.Lote!)
            .ToListAsync(ct);
}

public sealed class ConsultasRecepcionesHandler(
    ComprasDbContext db,
    ICatalogosService catalogos,
    IInventarioService inventario,
    IFechaOperativa fechaOperativa)
{
    /// <summary>
    /// RN-COM-09: vista previa de los lotes que generaría una recepción con estas partidas, en el mismo orden
    /// (<c>null</c> si el producto no maneja lote). No bloquea: otra recepción registrada antes puede tomar el consecutivo.
    /// </summary>
    public async Task<IReadOnlyList<string?>> ProponerLotesAsync(IReadOnlyList<Guid> productoIds, CancellationToken ct)
    {
        var productos = await catalogos.ObtenerProductosAsync(productoIds.Distinct(), ct);
        var hoy = fechaOperativa.Hoy();
        var lotes = new Dictionary<Guid, List<string>>();
        var propuestos = new List<string?>(productoIds.Count);
        foreach (var id in productoIds)
        {
            if (!productos.TryGetValue(id, out var producto) || !producto.ManejaLote)
            {
                propuestos.Add(null);
                continue;
            }

            var prefijo = ReglasCompras.PrefijoLote(producto.Sku, hoy);
            if (!lotes.TryGetValue(id, out var existentes))
            {
                lotes[id] = existentes = await LotesCompra.ExistentesAsync(db, id, prefijo, ct);
            }

            var lote = ReglasCompras.SiguienteLote(prefijo, existentes);
            existentes.Add(lote);
            propuestos.Add(lote);
        }

        return propuestos;
    }

    public async Task<Paginado<RecepcionResumenDto>> ListarAsync(EstadoRecepcion? estado, Guid? proveedorId, Guid? ordenCompraId,
        DateOnly? desde, DateOnly? hasta, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Recepciones.AsNoTracking();
        if (estado is not null)
        {
            consulta = consulta.Where(r => r.Estado == estado);
        }

        if (proveedorId is not null)
        {
            consulta = consulta.Where(r => r.ProveedorId == proveedorId);
        }

        if (ordenCompraId is not null)
        {
            consulta = consulta.Where(r => r.OrdenCompraId == ordenCompraId);
        }

        if (desde is not null)
        {
            consulta = consulta.Where(r => r.FechaOperativa >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(r => r.FechaOperativa <= hasta);
        }

        var proyeccion = from r in consulta
                         join p in db.Proveedores on r.ProveedorId equals p.Id
                         orderby r.FechaOperativa descending, r.Folio descending
                         select new RecepcionResumenDto(r.Id, r.Folio,
                             db.OrdenesCompra.Where(o => o.Id == r.OrdenCompraId).Select(o => o.Folio).FirstOrDefault(),
                             r.ProveedorId, p.RazonSocial, r.FechaOperativa, r.FacturaProveedor, r.Estado, r.Partidas.Count);
        return await proyeccion.PaginarAsync(pagina, tamano, ct);
    }

    public async Task<Result<RecepcionDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var r = await db.Recepciones.AsNoTracking().Include(x => x.Partidas).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null)
        {
            return ErroresCompras.RecepcionNoEncontrada;
        }

        var proveedor = await db.Proveedores.AsNoTracking().Where(p => p.Id == r.ProveedorId).Select(p => p.RazonSocial).FirstOrDefaultAsync(ct);
        var folioOrden = r.OrdenCompraId is null
            ? null
            : await db.OrdenesCompra.AsNoTracking().Where(o => o.Id == r.OrdenCompraId).Select(o => o.Folio).FirstOrDefaultAsync(ct);
        var almacen = await inventario.ObtenerAlmacenAsync(r.AlmacenId, ct);
        var productos = await catalogos.ObtenerProductosAsync(r.Partidas.Select(p => p.ProductoId), ct);
        var unidades = await catalogos.ObtenerUnidadesAsync(ct);
        return new RecepcionDto(r.Id, r.Folio, r.OrdenCompraId, folioOrden, r.ProveedorId, proveedor ?? "?", r.AlmacenId,
            almacen?.Codigo ?? "?", r.FechaOperativa, r.FacturaProveedor, r.Estado, r.Notas,
            r.Partidas.Sum(p => SharedKernel.Dominio.Dinero.Redondear(p.Cantidad * p.PrecioUnitario)), r.CreadoEn, r.CanceladaEn,
            r.Partidas.OrderBy(p => p.Id).Select(p =>
            {
                var producto = productos.GetValueOrDefault(p.ProductoId);
                return new PartidaRecepcionDto(p.Id, p.OrdenCompraPartidaId, p.ProductoId, producto?.Sku ?? "?", producto?.Nombre ?? "?",
                    p.UnidadId, unidades.GetValueOrDefault(p.UnidadId)?.Clave ?? "?", producto?.UnidadBaseClave ?? string.Empty,
                    p.FactorABase, p.Cantidad, p.CantidadBase, p.PrecioUnitario, p.CostoUnitarioBase, p.Lote, p.FechaCaducidad);
            }).ToList());
    }
}

/// <summary>
/// Recepción total o parcial contra orden, o compra directa (RN-COM-01, 04, 05, 06, 08, 09). Todo en una transacción:
/// recepción, entradas al kardex, avance de la orden y último precio del proveedor.
/// </summary>
public sealed class RegistrarRecepcionHandler(
    ComprasDbContext db,
    ICatalogosService catalogos,
    IInventarioService inventario,
    IGeneradorFolios folios,
    IFechaOperativa fechaOperativa,
    IUsuarioActual usuario,
    IUnitOfWork unitOfWork,
    TimeProvider reloj,
    ConsultasRecepcionesHandler consultas)
{
    public async Task<Result<RecepcionDto>> EjecutarAsync(RegistrarRecepcionRequest r, CancellationToken ct)
    {
        if (r.OrdenCompraId is null && !usuario.TienePermiso(P.CompraDirecta))
        {
            return ErroresCompras.SinPermisoCompraDirecta;
        }

        var resultado = await unitOfWork.EjecutarAsync(c => RegistrarAsync(r, c), ct);
        return resultado.EsExito ? await consultas.ObtenerAsync(resultado.Valor, ct) : resultado.Error!;
    }

    /// <summary>
    /// RN-COM-09: el sistema genera el lote de compra. El bloqueo hace que dos recepciones simultáneas del mismo producto
    /// y día tomen consecutivos distintos; las partidas de esta recepción aún sin guardar también cuentan.
    /// </summary>
    private async Task<string> SiguienteLoteAsync(ProductoInfo producto, Recepcion recepcion, CancellationToken ct)
    {
        var prefijo = ReglasCompras.PrefijoLote(producto.Sku, recepcion.FechaOperativa);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({"lote-compra:" + prefijo}))", ct);
        var existentes = await LotesCompra.ExistentesAsync(db, producto.Id, prefijo, ct);
        existentes.AddRange(recepcion.Partidas.Where(x => x.ProductoId == producto.Id && x.Lote is not null).Select(x => x.Lote!));
        return ReglasCompras.SiguienteLote(prefijo, existentes);
    }

    private async Task<Result<Guid>> RegistrarAsync(RegistrarRecepcionRequest r, CancellationToken ct)
    {
        OrdenCompra? orden = null;
        Guid proveedorId;
        Guid almacenId;
        if (r.OrdenCompraId is { } ordenId)
        {
            orden = await db.OrdenesCompra.Include(o => o.Partidas).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
            if (orden is null)
            {
                return ErroresCompras.OrdenNoEncontrada;
            }

            if (orden.Estado is not (EstadoOrdenCompra.Autorizada or EstadoOrdenCompra.RecibidaParcial))
            {
                return ErroresCompras.OrdenNoRecibible;
            }

            proveedorId = orden.ProveedorId;
            almacenId = r.AlmacenId ?? orden.AlmacenDestinoId;
        }
        else
        {
            proveedorId = r.ProveedorId!.Value;
            almacenId = r.AlmacenId!.Value;
            var proveedor = await db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == proveedorId, ct);
            if (proveedor is null)
            {
                return ErroresCompras.ProveedorNoEncontrado;
            }

            if (!proveedor.Activo)
            {
                return ErroresCompras.ProveedorInactivo;
            }
        }

        var almacen = await inventario.ObtenerAlmacenAsync(almacenId, ct);
        if (almacen is null || !almacen.Activo || almacen.Tipo == TipoAlmacen.Ruta)
        {
            return ErroresCompras.AlmacenInvalido;
        }

        var tolerancia = await catalogos.ObtenerParametroAsync(ClavesParametro.ComprasTolerancia, 0.05m, ct);
        var productoIds = r.Partidas.Select(p => p.ProductoId ?? orden?.Partidas.FirstOrDefault(x => x.Id == p.OrdenCompraPartidaId)?.ProductoId ?? Guid.Empty);
        var productos = await catalogos.ObtenerProductosAsync(productoIds, ct);

        var recepcion = new Recepcion
        {
            OrdenCompraId = orden?.Id,
            ProveedorId = proveedorId,
            AlmacenId = almacenId,
            FechaOperativa = fechaOperativa.Hoy(),
            FacturaProveedor = string.IsNullOrWhiteSpace(r.FacturaProveedor) ? null : r.FacturaProveedor.Trim(),
            Notas = string.IsNullOrWhiteSpace(r.Notas) ? null : r.Notas.Trim(),
        };

        foreach (var p in r.Partidas)
        {
            OrdenCompraPartida? partidaOrden = null;
            Guid productoId;
            Guid unidadId;
            decimal precio;
            if (orden is not null)
            {
                partidaOrden = orden.Partidas.FirstOrDefault(x => x.Id == p.OrdenCompraPartidaId);
                if (partidaOrden is null)
                {
                    return ErroresCompras.PartidaOrdenInvalida;
                }

                productoId = partidaOrden.ProductoId;
                unidadId = partidaOrden.UnidadId;
                precio = partidaOrden.PrecioUnitario;
            }
            else
            {
                productoId = p.ProductoId!.Value;
                unidadId = p.UnidadId!.Value;
                precio = p.PrecioUnitario!.Value;
            }

            if (!productos.TryGetValue(productoId, out var producto))
            {
                return ErroresCompras.ProductoNoEncontrado;
            }

            // RN-COM-01.
            if (!producto.EsComprable)
            {
                return ErroresCompras.NoComprable(producto.Nombre);
            }

            var conversion = await catalogos.ConvertirABaseAsync(productoId, unidadId, p.Cantidad,
                orden is null ? UsoUnidad.Compra : null, ct);
            if (!conversion.EsExito)
            {
                return conversion.Error!;
            }

            var cantidadBase = conversion.Valor.Base;
            if (partidaOrden is not null)
            {
                // RN-COM-04: tolerancia sobre lo pedido.
                if (ReglasCompras.ExcedeTolerancia(partidaOrden.CantidadBase, partidaOrden.CantidadRecibidaBase, cantidadBase, tolerancia))
                {
                    var maximo = SharedKernel.Dominio.Dinero.RedondearCantidad(partidaOrden.CantidadBase * (1 + tolerancia))
                        - partidaOrden.CantidadRecibidaBase;
                    return ErroresCompras.ExcedeTolerancia(producto.Nombre, maximo);
                }

                partidaOrden.CantidadRecibidaBase += cantidadBase;
            }

            recepcion.Partidas.Add(new RecepcionPartida
            {
                RecepcionId = recepcion.Id,
                OrdenCompraPartidaId = partidaOrden?.Id,
                ProductoId = productoId,
                UnidadId = unidadId,
                FactorABase = conversion.Valor.FactorABase,
                Cantidad = p.Cantidad,
                CantidadBase = cantidadBase,
                PrecioUnitario = precio,
                CostoUnitarioBase = ReglasCompras.CostoUnitarioBase(precio, conversion.Valor.FactorABase),
                Lote = producto.ManejaLote ? await SiguienteLoteAsync(producto, recepcion, ct) : null,
                FechaCaducidad = p.FechaCaducidad,
            });
        }

        recepcion.Folio = await folios.SiguienteAsync(ComprasDbContext.Esquema, "REC", ct);

        // RN-COM-05 y RN-COM-06: EntradaCompra al costo sin IVA; el kardex recalcula el promedio.
        var movimientos = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
            DocumentoInventario.Recepcion, recepcion.Id, recepcion.Folio, recepcion.FechaOperativa, usuario.Id ?? Guid.Empty,
            recepcion.Partidas.Select(p => new LineaMovimiento(TipoMovimiento.EntradaCompra, almacenId, p.ProductoId, p.Lote,
                p.CantidadBase, p.CostoUnitarioBase, FechaCaducidad: p.FechaCaducidad)).ToList(), Notas: recepcion.Notas), ct);
        if (!movimientos.EsExito)
        {
            return movimientos.Error!;
        }

        if (orden is not null)
        {
            orden.Estado = ReglasCompras.EstadoPorRecepciones(orden.Partidas);
        }

        // RN-COM-08: último precio por proveedor y producto; si no estaba en su catálogo (compra directa) se agrega con
        // la unidad y el precio recibidos.
        var ahora = reloj.GetUtcNow();
        foreach (var grupo in recepcion.Partidas.GroupBy(p => p.ProductoId))
        {
            var ultima = grupo.Last();
            var registro = await db.ProveedoresProductos.FirstOrDefaultAsync(pp => pp.ProveedorId == proveedorId && pp.ProductoId == grupo.Key, ct);
            if (registro is null)
            {
                registro = new ProveedorProducto
                {
                    ProveedorId = proveedorId, ProductoId = grupo.Key, UnidadId = ultima.UnidadId, Precio = ultima.PrecioUnitario,
                };
                db.ProveedoresProductos.Add(registro);
            }

            registro.UltimaUnidadId = ultima.UnidadId;
            registro.UltimoPrecio = ultima.PrecioUnitario;
            registro.UltimaCompraEn = ahora;
        }

        db.Recepciones.Add(recepcion);
        await db.SaveChangesAsync(ct);
        return recepcion.Id;
    }
}

/// <summary>RN-COM-07: cancelar genera <c>AjusteSalida</c> por las mismas cantidades y lotes; falla si no alcanza.</summary>
public sealed class CancelarRecepcionHandler(
    ComprasDbContext db,
    IInventarioService inventario,
    IFechaOperativa fechaOperativa,
    IUsuarioActual usuario,
    IUnitOfWork unitOfWork,
    TimeProvider reloj,
    ConsultasRecepcionesHandler consultas)
{
    public async Task<Result<RecepcionDto>> EjecutarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var recepcion = await db.Recepciones.Include(r => r.Partidas).FirstOrDefaultAsync(r => r.Id == id, c);
            if (recepcion is null)
            {
                return Result.Falla(ErroresCompras.RecepcionNoEncontrada);
            }

            if (recepcion.Estado == EstadoRecepcion.Cancelada)
            {
                return Result.Falla(ErroresCompras.RecepcionCancelada);
            }

            var movimientos = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.Recepcion, recepcion.Id, recepcion.Folio, fechaOperativa.Hoy(), usuario.Id ?? Guid.Empty,
                recepcion.Partidas.Select(p => new LineaMovimiento(TipoMovimiento.AjusteSalida, recepcion.AlmacenId, p.ProductoId,
                    p.Lote ?? string.Empty, -p.CantidadBase, p.CostoUnitarioBase)).ToList(),
                Notas: $"Cancelación de recepción {recepcion.Folio}"), c);
            if (!movimientos.EsExito)
            {
                return Result.Falla(movimientos.Error!);
            }

            if (recepcion.OrdenCompraId is { } ordenId)
            {
                var orden = await db.OrdenesCompra.Include(o => o.Partidas).FirstAsync(o => o.Id == ordenId, c);
                foreach (var p in recepcion.Partidas.Where(p => p.OrdenCompraPartidaId is not null))
                {
                    var partida = orden.Partidas.First(x => x.Id == p.OrdenCompraPartidaId);
                    partida.CantidadRecibidaBase -= p.CantidadBase;
                }

                if (orden.Estado is EstadoOrdenCompra.RecibidaParcial or EstadoOrdenCompra.Recibida)
                {
                    orden.Estado = ReglasCompras.EstadoPorRecepciones(orden.Partidas);
                }
            }

            recepcion.Estado = EstadoRecepcion.Cancelada;
            recepcion.CanceladaEn = reloj.GetUtcNow();
            recepcion.CanceladaPor = usuario.Id;
            await db.SaveChangesAsync(c);
            return Result.Exito();
        }, ct);

        return resultado.EsExito ? await consultas.ObtenerAsync(id, ct) : resultado.Error!;
    }
}
