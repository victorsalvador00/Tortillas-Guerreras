using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Compras.Dominio;
using TG.Compras.Infraestructura;
using TG.SharedKernel.Resultados;

namespace TG.Compras.Aplicacion;

/// <summary>
/// Producto del catálogo de un proveedor. <c>Precio</c> es el pactado (opcional), sin IVA, por <c>Unidad</c> (de compra);
/// <c>PrecioBase</c> = Precio / factor. <c>UltimoPrecio</c> es lo pagado en la última recepción, en <c>UltimaUnidad</c>.
/// </summary>
public sealed record ProductoProveedorDto(
    Guid ProductoId,
    string Sku,
    string Producto,
    string Tipo,
    bool ProductoActivo,
    string UnidadBase,
    Guid UnidadId,
    string Unidad,
    decimal FactorABase,
    string? ClaveProveedor,
    decimal? Precio,
    decimal? PrecioBase,
    int DiasEntrega,
    bool Preferido,
    bool Activo,
    decimal? UltimoPrecio,
    string? UltimaUnidad,
    DateTimeOffset? UltimaCompraEn,
    decimal TasaIva);

/// <summary>Proveedor activo que vende un producto: primero el preferido, luego el más barato por unidad base (sin precio al final).</summary>
public sealed record OfertaProveedorDto(
    Guid ProveedorId,
    string Codigo,
    string Proveedor,
    int DiasCredito,
    string? ClaveProveedor,
    Guid UnidadId,
    string Unidad,
    decimal? Precio,
    decimal? PrecioBase,
    int DiasEntrega,
    bool Preferido);

public sealed record AgregarProductoProveedorRequest(
    Guid ProductoId,
    Guid UnidadId,
    string? ClaveProveedor,
    decimal? Precio,
    int DiasEntrega,
    bool Preferido);

/// <summary>Marcarlo preferido quita la preferencia del proveedor que la tenía; una relación inactiva nunca es preferida.</summary>
public sealed record ActualizarProductoProveedorRequest(
    Guid UnidadId,
    string? ClaveProveedor,
    decimal? Precio,
    int DiasEntrega,
    bool Preferido,
    bool Activo);

public static class ReglasProductoProveedor
{
    public const int ClaveProveedorMaximo = 50;
    public const int DiasEntregaMaximo = 365;
}

public sealed class AgregarProductoProveedorValidator : AbstractValidator<AgregarProductoProveedorRequest>
{
    public AgregarProductoProveedorValidator()
    {
        RuleFor(x => x.ProductoId).NotEmpty().WithMessage("Elige el producto.");
        RuleFor(x => x.UnidadId).NotEmpty().WithMessage("Elige la unidad de compra.");
        RuleFor(x => x.ClaveProveedor).MaximumLength(ReglasProductoProveedor.ClaveProveedorMaximo)
            .WithMessage($"La clave del proveedor admite máximo {ReglasProductoProveedor.ClaveProveedorMaximo} caracteres.");
        RuleFor(x => x.Precio).GreaterThanOrEqualTo(0).When(x => x.Precio is not null).WithMessage("El precio no puede ser negativo.");
        RuleFor(x => x.DiasEntrega).InclusiveBetween(0, ReglasProductoProveedor.DiasEntregaMaximo)
            .WithMessage($"Los días de entrega van de 0 a {ReglasProductoProveedor.DiasEntregaMaximo}.");
    }
}

public sealed class ActualizarProductoProveedorValidator : AbstractValidator<ActualizarProductoProveedorRequest>
{
    public ActualizarProductoProveedorValidator()
    {
        RuleFor(x => x.UnidadId).NotEmpty().WithMessage("Elige la unidad de compra.");
        RuleFor(x => x.ClaveProveedor).MaximumLength(ReglasProductoProveedor.ClaveProveedorMaximo)
            .WithMessage($"La clave del proveedor admite máximo {ReglasProductoProveedor.ClaveProveedorMaximo} caracteres.");
        RuleFor(x => x.Precio).GreaterThanOrEqualTo(0).When(x => x.Precio is not null).WithMessage("El precio no puede ser negativo.");
        RuleFor(x => x.DiasEntrega).InclusiveBetween(0, ReglasProductoProveedor.DiasEntregaMaximo)
            .WithMessage($"Los días de entrega van de 0 a {ReglasProductoProveedor.DiasEntregaMaximo}.");
        RuleFor(x => x.Preferido).Must((x, preferido) => !preferido || x.Activo)
            .WithMessage("Un producto inactivo en el catálogo del proveedor no puede ser el preferido.");
    }
}

/// <summary>Catálogo de productos por proveedor (RN-COM-10 a RN-COM-12).</summary>
public sealed class ProductosProveedorHandler(ComprasDbContext db, ICatalogosService catalogos)
{
    public async Task<Result<IReadOnlyList<ProductoProveedorDto>>> ListarAsync(Guid proveedorId, bool incluirInactivos, CancellationToken ct)
    {
        if (!await db.Proveedores.AnyAsync(p => p.Id == proveedorId, ct))
        {
            return ErroresCompras.ProveedorNoEncontrado;
        }

        var registros = await db.ProveedoresProductos.AsNoTracking()
            .Where(pp => pp.ProveedorId == proveedorId && (incluirInactivos || pp.Activo)).ToListAsync(ct);
        var productos = await catalogos.ObtenerProductosAsync(registros.Select(r => r.ProductoId), ct);
        var unidades = await catalogos.ObtenerUnidadesAsync(ct);
        return registros
            .Select(r => ADto(r, productos.GetValueOrDefault(r.ProductoId), unidades))
            .OrderBy(d => d.Producto).ToList();
    }

    public async Task<Result<IReadOnlyList<OfertaProveedorDto>>> OfertasAsync(Guid productoId, CancellationToken ct)
    {
        var productos = await catalogos.ObtenerProductosAsync([productoId], ct);
        if (!productos.TryGetValue(productoId, out var producto))
        {
            return ErroresCompras.ProductoNoEncontrado;
        }

        var ofertas = await (from pp in db.ProveedoresProductos.AsNoTracking()
                             join p in db.Proveedores.AsNoTracking() on pp.ProveedorId equals p.Id
                             where pp.ProductoId == productoId && pp.Activo && p.Activo
                             select new { pp, p }).ToListAsync(ct);
        var unidades = await catalogos.ObtenerUnidadesAsync(ct);
        return ofertas
            .Select(o =>
            {
                var factor = Factor(producto, o.pp.UnidadId);
                return new OfertaProveedorDto(o.p.Id, o.p.Codigo, o.p.RazonSocial, o.p.DiasCredito, o.pp.ClaveProveedor, o.pp.UnidadId,
                    unidades.GetValueOrDefault(o.pp.UnidadId)?.Clave ?? "?", o.pp.Precio, PrecioBase(o.pp.Precio, factor), o.pp.DiasEntrega,
                    o.pp.Preferido);
            })
            .OrderByDescending(o => o.Preferido).ThenBy(o => o.PrecioBase is null).ThenBy(o => o.PrecioBase).ThenBy(o => o.Proveedor).ToList();
    }

    public async Task<Result<ProductoProveedorDto>> AgregarAsync(Guid proveedorId, AgregarProductoProveedorRequest r, CancellationToken ct)
    {
        var proveedor = await db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == proveedorId, ct);
        if (proveedor is null)
        {
            return ErroresCompras.ProveedorNoEncontrado;
        }

        if (!proveedor.Activo)
        {
            return ErroresCompras.ProveedorInactivoCatalogo;
        }

        var validacion = await ValidarProductoAsync(r.ProductoId, r.UnidadId, ct);
        if (!validacion.EsExito)
        {
            return validacion.Error!;
        }

        if (await db.ProveedoresProductos.AnyAsync(pp => pp.ProveedorId == proveedorId && pp.ProductoId == r.ProductoId, ct))
        {
            return ErroresCompras.ProductoYaEnCatalogo(validacion.Valor.Nombre);
        }

        var registro = new ProveedorProducto { ProveedorId = proveedorId, ProductoId = r.ProductoId, Activo = true };
        Aplicar(registro, r.UnidadId, r.ClaveProveedor, r.Precio, r.DiasEntrega);
        db.ProveedoresProductos.Add(registro);
        if (r.Preferido)
        {
            await MarcarPreferidoAsync(registro, ct);
        }

        await db.SaveChangesAsync(ct);
        return ADto(registro, validacion.Valor, await catalogos.ObtenerUnidadesAsync(ct));
    }

    public async Task<Result<ProductoProveedorDto>> ActualizarAsync(Guid proveedorId, Guid productoId, ActualizarProductoProveedorRequest r,
        CancellationToken ct)
    {
        var proveedor = await db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == proveedorId, ct);
        if (proveedor is null)
        {
            return ErroresCompras.ProveedorNoEncontrado;
        }

        // RN-COM-11: los productos de un proveedor inactivo no se editan hasta reactivarlo.
        if (!proveedor.Activo)
        {
            return ErroresCompras.ProveedorInactivoCatalogo;
        }

        var registro = await db.ProveedoresProductos.FirstOrDefaultAsync(pp => pp.ProveedorId == proveedorId && pp.ProductoId == productoId, ct);
        if (registro is null)
        {
            return ErroresCompras.ProductoNoEnCatalogoProveedor;
        }

        var validacion = await ValidarProductoAsync(productoId, r.UnidadId, ct, exigirComprable: r.Activo);
        if (!validacion.EsExito)
        {
            return validacion.Error!;
        }

        Aplicar(registro, r.UnidadId, r.ClaveProveedor, r.Precio, r.DiasEntrega);
        registro.Activo = r.Activo;
        if (r.Preferido && r.Activo)
        {
            await MarcarPreferidoAsync(registro, ct);
        }
        else
        {
            registro.Preferido = false;
        }

        await db.SaveChangesAsync(ct);
        return ADto(registro, validacion.Valor, await catalogos.ObtenerUnidadesAsync(ct));
    }

    /// <summary>RN-COM-10: producto comprable y unidad base o con conversión de compra.</summary>
    private async Task<Result<ProductoInfo>> ValidarProductoAsync(Guid productoId, Guid unidadId, CancellationToken ct, bool exigirComprable = true)
    {
        var productos = await catalogos.ObtenerProductosAsync([productoId], ct);
        if (!productos.TryGetValue(productoId, out var producto))
        {
            return ErroresCompras.ProductoNoEncontrado;
        }

        if (exigirComprable && (!producto.EsComprable || !producto.Activo))
        {
            return ErroresCompras.NoComprable(producto.Nombre);
        }

        var conversion = await catalogos.ConvertirABaseAsync(productoId, unidadId, 1, UsoUnidad.Compra, ct);
        return conversion.EsExito ? producto : conversion.Error!;
    }

    /// <summary>RN-COM-11: un solo proveedor preferido por producto; marcar uno desmarca el anterior.</summary>
    private async Task MarcarPreferidoAsync(ProveedorProducto registro, CancellationToken ct)
    {
        var anteriores = await db.ProveedoresProductos
            .Where(pp => pp.ProductoId == registro.ProductoId && pp.Preferido && pp.ProveedorId != registro.ProveedorId).ToListAsync(ct);
        foreach (var anterior in anteriores)
        {
            anterior.Preferido = false;
        }

        // El índice único parcial exige que el desmarcado se guarde antes que el nuevo preferido.
        if (anteriores.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        registro.Preferido = true;
    }

    private static void Aplicar(ProveedorProducto registro, Guid unidadId, string? clave, decimal? precio, int diasEntrega)
    {
        registro.UnidadId = unidadId;
        registro.ClaveProveedor = string.IsNullOrWhiteSpace(clave) ? null : clave.Trim();
        registro.Precio = precio;
        registro.DiasEntrega = diasEntrega;
    }

    private static decimal Factor(ProductoInfo? producto, Guid unidadId) =>
        producto is null || producto.UnidadBaseId == unidadId
            ? 1m
            : producto.Conversiones.FirstOrDefault(c => c.UnidadId == unidadId)?.FactorABase ?? 1m;

    private static decimal? PrecioBase(decimal? precio, decimal factor) =>
        precio is null ? null : factor == 0 ? precio : Math.Round(precio.Value / factor, 4);

    private static ProductoProveedorDto ADto(ProveedorProducto r, ProductoInfo? producto, IReadOnlyDictionary<Guid, UnidadInfo> unidades)
    {
        var factor = Factor(producto, r.UnidadId);
        return new ProductoProveedorDto(r.ProductoId, producto?.Sku ?? "?", producto?.Nombre ?? "?", producto?.Tipo ?? "?",
            producto?.Activo ?? false, producto?.UnidadBaseClave ?? "?", r.UnidadId, unidades.GetValueOrDefault(r.UnidadId)?.Clave ?? "?",
            factor, r.ClaveProveedor, r.Precio, PrecioBase(r.Precio, factor), r.DiasEntrega, r.Preferido, r.Activo,
            r.UltimaCompraEn is null ? null : r.UltimoPrecio,
            r.UltimaUnidadId is { } u ? unidades.GetValueOrDefault(u)?.Clave : null, r.UltimaCompraEn, producto?.TasaIva ?? 0m);
    }
}
