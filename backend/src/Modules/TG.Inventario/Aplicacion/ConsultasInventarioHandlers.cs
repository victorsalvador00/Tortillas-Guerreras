using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Inventario.Dominio;
using TG.Inventario.Infraestructura;
using TG.Inventario.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Resultados;

namespace TG.Inventario.Aplicacion;

public sealed record AlmacenDto(Guid Id, string Codigo, string Nombre, TipoAlmacen Tipo, Guid? RutaId, bool PermiteNegativo, bool Activo);

public sealed record GuardarAlmacenRequest(string Codigo, string Nombre, TipoAlmacen Tipo, bool Activo);

public sealed class GuardarAlmacenValidator : AbstractValidator<GuardarAlmacenRequest>
{
    public GuardarAlmacenValidator()
    {
        RuleFor(x => x.Codigo).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Nombre).NotEmpty();
        RuleFor(x => x.Tipo).NotEqual(TipoAlmacen.Ruta)
            .WithMessage("Los almacenes de ruta se crean junto con la ruta (Logística).");
    }
}

public sealed class AlmacenesHandler(InventarioDbContext db)
{
    public async Task<IReadOnlyList<AlmacenDto>> ListarAsync(TipoAlmacen? tipo, bool? activo, CancellationToken ct)
    {
        var consulta = db.Almacenes.AsNoTracking();
        if (tipo is not null)
        {
            consulta = consulta.Where(a => a.Tipo == tipo);
        }

        if (activo is not null)
        {
            consulta = consulta.Where(a => a.Activo == activo);
        }

        return await consulta.OrderBy(a => a.Tipo == TipoAlmacen.Ruta).ThenBy(a => a.Codigo)
            .Select(a => new AlmacenDto(a.Id, a.Codigo, a.Nombre, a.Tipo, a.RutaId, a.PermiteNegativo, a.Activo))
            .ToListAsync(ct);
    }

    public async Task<Result<AlmacenDto>> CrearAsync(GuardarAlmacenRequest r, CancellationToken ct)
    {
        var codigo = r.Codigo.Trim().ToUpperInvariant();
        if (await db.Almacenes.AnyAsync(a => a.Codigo == codigo, ct))
        {
            return Error.Campo("INV-007", "codigo", "Ya existe un almacén con ese código.");
        }

        var almacen = new Almacen { Codigo = codigo, Nombre = r.Nombre.Trim(), Tipo = r.Tipo, Activo = r.Activo };
        db.Almacenes.Add(almacen);
        await db.SaveChangesAsync(ct);
        return ADto(almacen);
    }

    public async Task<Result<AlmacenDto>> ActualizarAsync(Guid id, GuardarAlmacenRequest r, CancellationToken ct)
    {
        var almacen = await db.Almacenes.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (almacen is null)
        {
            return Error.NoEncontrado(ErroresInventario.AlmacenNoEncontrado, "El almacén no existe.");
        }

        var codigo = r.Codigo.Trim().ToUpperInvariant();
        if (codigo != almacen.Codigo && await db.Almacenes.AnyAsync(a => a.Codigo == codigo, ct))
        {
            return Error.Campo("INV-007", "codigo", "Ya existe un almacén con ese código.");
        }

        if (almacen.Tipo == TipoAlmacen.Ruta)
        {
            // Solo se cambia el nombre de un almacén de ruta; tipo y código los gobierna la ruta.
            almacen.Nombre = r.Nombre.Trim();
        }
        else
        {
            almacen.Codigo = codigo;
            almacen.Nombre = r.Nombre.Trim();
            almacen.Tipo = r.Tipo;
            almacen.Activo = r.Activo;
        }

        await db.SaveChangesAsync(ct);
        return ADto(almacen);
    }

    private static AlmacenDto ADto(Almacen a) => new(a.Id, a.Codigo, a.Nombre, a.Tipo, a.RutaId, a.PermiteNegativo, a.Activo);
}

public sealed record ExistenciaDto(
    Guid AlmacenId,
    string AlmacenCodigo,
    Guid ProductoId,
    string Sku,
    string Producto,
    string Unidad,
    string Lote,
    decimal Cantidad,
    DateOnly? FechaCaducidad,
    decimal? StockMinimo);

public sealed record MovimientoDto(
    Guid Id,
    DateTimeOffset FechaHora,
    DateOnly FechaOperativa,
    TipoMovimiento Tipo,
    Guid AlmacenId,
    string AlmacenCodigo,
    Guid ProductoId,
    string Sku,
    string Producto,
    string Lote,
    decimal Cantidad,
    decimal CostoUnitario,
    decimal CostoTotal,
    decimal ExistenciaResultante,
    DocumentoInventario DocumentoTipo,
    Guid DocumentoId,
    string DocumentoFolio,
    Guid? MotivoMermaId,
    string? Notas);

public sealed record ValuacionDto(
    Guid ProductoId,
    string Sku,
    string Producto,
    string Tipo,
    string Unidad,
    decimal Existencia,
    decimal CostoPromedio,
    decimal Valor);

public sealed record ValuacionTotalDto(DateOnly Fecha, decimal Total, IReadOnlyList<ValuacionDto> Productos);

public sealed class ConsultasInventarioHandler(InventarioDbContext db, ICatalogosService catalogos)
{
    public async Task<Paginado<ExistenciaDto>> ExistenciasAsync(Guid? almacenId, Guid? productoId, string? buscar,
        bool? soloConExistencia, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Existencias.AsNoTracking();
        if (almacenId is not null)
        {
            consulta = consulta.Where(e => e.AlmacenId == almacenId);
        }

        if (productoId is not null)
        {
            consulta = consulta.Where(e => e.ProductoId == productoId);
        }

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var ids = await catalogos.BuscarProductosAsync(buscar, ct);
            consulta = consulta.Where(e => ids.Contains(e.ProductoId));
        }

        if (soloConExistencia ?? true)
        {
            consulta = consulta.Where(e => e.Cantidad != 0);
        }

        var paginado = await consulta.OrderBy(e => e.AlmacenId).ThenBy(e => e.ProductoId).ThenBy(e => e.Lote)
            .PaginarAsync(pagina, tamano, ct);
        var productos = await catalogos.ObtenerProductosAsync(paginado.Items.Select(e => e.ProductoId), ct);
        var almacenes = await db.Almacenes.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Codigo, ct);
        return paginado.Map(e =>
        {
            var p = productos.GetValueOrDefault(e.ProductoId);
            return new ExistenciaDto(e.AlmacenId, almacenes.GetValueOrDefault(e.AlmacenId) ?? "?", e.ProductoId,
                p?.Sku ?? "?", p?.Nombre ?? "?", p?.UnidadBaseClave ?? string.Empty, e.Lote, e.Cantidad, e.FechaCaducidad,
                p?.StockMinimo);
        });
    }

    public async Task<Paginado<MovimientoDto>> KardexAsync(Guid? productoId, Guid? almacenId, DateOnly? desde, DateOnly? hasta,
        DocumentoInventario? documentoTipo, Guid? documentoId, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Movimientos.AsNoTracking();
        if (productoId is not null)
        {
            consulta = consulta.Where(m => m.ProductoId == productoId);
        }

        if (almacenId is not null)
        {
            consulta = consulta.Where(m => m.AlmacenId == almacenId);
        }

        if (desde is not null)
        {
            consulta = consulta.Where(m => m.FechaOperativa >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(m => m.FechaOperativa <= hasta);
        }

        if (documentoTipo is not null)
        {
            consulta = consulta.Where(m => m.DocumentoTipo == documentoTipo);
        }

        if (documentoId is not null)
        {
            consulta = consulta.Where(m => m.DocumentoId == documentoId);
        }

        var paginado = await consulta.OrderByDescending(m => m.FechaHora).ThenByDescending(m => m.Id).PaginarAsync(pagina, tamano, ct);
        return await MapearAsync(paginado, ct);
    }

    public async Task<Paginado<MovimientoDto>> MapearAsync(Paginado<Movimiento> paginado, CancellationToken ct)
    {
        var productos = await catalogos.ObtenerProductosAsync(paginado.Items.Select(m => m.ProductoId), ct);
        var almacenes = await db.Almacenes.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Codigo, ct);
        return paginado.Map(m => ADto(m, productos, almacenes));
    }

    internal static MovimientoDto ADto(Movimiento m, IReadOnlyDictionary<Guid, ProductoInfo> productos, Dictionary<Guid, string> almacenes)
    {
        var p = productos.GetValueOrDefault(m.ProductoId);
        return new MovimientoDto(m.Id, m.FechaHora, m.FechaOperativa, m.Tipo, m.AlmacenId,
            almacenes.GetValueOrDefault(m.AlmacenId) ?? "?", m.ProductoId, p?.Sku ?? "?", p?.Nombre ?? "?", m.Lote, m.Cantidad,
            m.CostoUnitario, m.CostoTotal, m.ExistenciaResultante, m.DocumentoTipo, m.DocumentoId, m.DocumentoFolio,
            m.MotivoMermaId, m.Notas);
    }

    /// <summary>
    /// Valuación: existencia a la fecha (suma del kardex hasta ese día) por el costo promedio vigente [SUP-06]. Excluye
    /// almacenes de ruta si <paramref name="incluirRutas"/> es falso.
    /// </summary>
    public async Task<ValuacionTotalDto> ValuacionAsync(DateOnly fecha, bool incluirRutas, CancellationToken ct)
    {
        var rutas = await db.Almacenes.AsNoTracking().Where(a => a.Tipo == TipoAlmacen.Ruta).Select(a => a.Id).ToListAsync(ct);
        var consulta = db.Movimientos.AsNoTracking().Where(m => m.FechaOperativa <= fecha);
        if (!incluirRutas)
        {
            consulta = consulta.Where(m => !rutas.Contains(m.AlmacenId));
        }

        var saldos = await consulta.GroupBy(m => m.ProductoId)
            .Select(g => new { ProductoId = g.Key, Existencia = g.Sum(m => m.Cantidad) })
            .Where(x => x.Existencia != 0)
            .ToListAsync(ct);
        var ids = saldos.Select(s => s.ProductoId).ToList();
        var costos = await db.CostosProducto.AsNoTracking().Where(c => ids.Contains(c.ProductoId))
            .ToDictionaryAsync(c => c.ProductoId, c => c.CostoPromedio, ct);
        var productos = await catalogos.ObtenerProductosAsync(ids, ct);
        var lista = saldos.Select(s =>
            {
                var p = productos.GetValueOrDefault(s.ProductoId);
                var costo = costos.GetValueOrDefault(s.ProductoId);
                return new ValuacionDto(s.ProductoId, p?.Sku ?? "?", p?.Nombre ?? "?", p?.Tipo ?? string.Empty,
                    p?.UnidadBaseClave ?? string.Empty, s.Existencia, costo, Dinero.Redondear(s.Existencia * costo));
            })
            .OrderBy(v => v.Tipo).ThenBy(v => v.Sku).ToList();
        return new ValuacionTotalDto(fecha, lista.Sum(v => v.Valor), lista);
    }
}
