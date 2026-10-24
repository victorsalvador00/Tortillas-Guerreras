using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Dominio;
using TG.Catalogos.Infraestructura;
using TG.Catalogos.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Resultados;

namespace TG.Catalogos.Aplicacion;

public sealed record ConversionDto(
    Guid UnidadId,
    string UnidadClave,
    string UnidadNombre,
    decimal FactorABase,
    bool UsoCompra,
    bool UsoVenta,
    bool UsoProduccion,
    bool PredeterminadaVenta,
    bool UsoTraslado,
    bool PredeterminadaTraslado);

public sealed record ProductoDto(
    Guid Id,
    string Sku,
    string Nombre,
    string? NombreCorto,
    string? Descripcion,
    TipoProducto Tipo,
    Guid UnidadBaseId,
    string UnidadBaseClave,
    bool EsComprable,
    bool EsProducible,
    bool EsVendible,
    bool ManejaLote,
    int? VidaUtilDias,
    string? ClaveProdServSat,
    string ObjetoImpuestoSat,
    decimal TasaIva,
    decimal? TasaIeps,
    string? CodigoBarras,
    decimal? StockMinimo,
    bool AplicaProductoACobro,
    int? DiasToleranciaDevolucion,
    bool Activo,
    IReadOnlyList<ConversionDto> Conversiones);

public sealed record ConversionRequest(
    Guid UnidadId,
    decimal FactorABase,
    bool UsoCompra,
    bool UsoVenta,
    bool UsoProduccion,
    bool PredeterminadaVenta,
    bool UsoTraslado = false,
    bool PredeterminadaTraslado = false);

public sealed record GuardarProductoRequest(
    string Sku,
    string Nombre,
    string? Descripcion,
    TipoProducto Tipo,
    Guid UnidadBaseId,
    bool? EsComprable,
    bool? EsProducible,
    bool? EsVendible,
    bool? ManejaLote,
    int? VidaUtilDias,
    string? ClaveProdServSat,
    string? ObjetoImpuestoSat,
    decimal TasaIva,
    decimal? TasaIeps,
    string? CodigoBarras,
    decimal? StockMinimo,
    IReadOnlyList<ConversionRequest>? Conversiones,
    string? NombreCorto = null,
    bool AplicaProductoACobro = false,
    int? DiasToleranciaDevolucion = null);

public sealed class GuardarProductoValidator : AbstractValidator<GuardarProductoRequest>
{
    public GuardarProductoValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(40).WithMessage("Captura el SKU (máximo 40 caracteres).");
        RuleFor(x => x.Nombre).NotEmpty().MaximumLength(150).WithMessage("Captura el nombre.");
        RuleFor(x => x.UnidadBaseId).NotEmpty().WithMessage("Elige la unidad base.");
        RuleFor(x => x.TasaIva).InclusiveBetween(0m, 1m).WithMessage("La tasa de IVA va de 0 a 1 (0.16 = 16 %).");
        RuleFor(x => x.TasaIeps).InclusiveBetween(0m, 1m).When(x => x.TasaIeps is not null)
            .WithMessage("La tasa de IEPS va de 0 a 1 (0.08 = 8 %).");
        RuleFor(x => x.VidaUtilDias).GreaterThan(0).When(x => x.VidaUtilDias is not null)
            .WithMessage("La vida útil debe ser mayor que cero.");
        RuleFor(x => x.StockMinimo).GreaterThanOrEqualTo(0).When(x => x.StockMinimo is not null);
        // RN-CAT-08.
        RuleFor(x => x.NombreCorto).MaximumLength(15).WithErrorCode("CAT-012")
            .WithMessage("El nombre corto admite máximo 15 caracteres.");
        // RN-CAT-10.
        RuleFor(x => x.DiasToleranciaDevolucion).GreaterThan(0).When(x => x.DiasToleranciaDevolucion is not null)
            .WithMessage("Los días de tolerancia deben ser mayores que cero.");
        RuleFor(x => x.DiasToleranciaDevolucion).Null().When(x => !x.AplicaProductoACobro)
            .WithMessage("Los días de tolerancia solo aplican a productos a cobro.");
        RuleForEach(x => x.Conversiones).ChildRules(c =>
            c.RuleFor(x => x.FactorABase).GreaterThan(0).WithMessage("El factor debe ser mayor que cero."));
        RuleFor(x => x.Conversiones)
            .Must(c => c is null || c.Count(x => x.PredeterminadaVenta) <= 1)
            .WithMessage("Solo una unidad puede ser la predeterminada de venta.")
            .Must(c => c is null || c.Count(x => x.PredeterminadaTraslado) <= 1)
            .WithMessage("Solo una unidad puede ser la predeterminada de traslado.")
            .Must(c => c is null || c.All(x => !x.PredeterminadaTraslado || x.UsoTraslado))
            .WithMessage("La unidad predeterminada de traslado debe tener uso de traslado.")
            .Must(c => c is null || c.Select(x => x.UnidadId).Distinct().Count() == c.Count)
            .WithMessage("Hay unidades repetidas en las conversiones.");
        RuleFor(x => x).Must(x => x.Conversiones is null || x.Conversiones.All(c => c.UnidadId != x.UnidadBaseId))
            .WithName("conversiones").WithMessage("La unidad base no lleva conversión (su factor es 1).");
    }
}

public sealed class ConsultasProductosHandler(CatalogosDbContext db)
{
    public async Task<Paginado<ProductoDto>> ListarAsync(string? buscar, TipoProducto? tipo, bool? activo, bool? esVendible,
        bool? esComprable, bool? esProducible, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = Consulta();
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var patron = $"%{buscar.Trim()}%";
            consulta = consulta.Where(p => EF.Functions.ILike(p.Sku, patron) || EF.Functions.ILike(p.Nombre, patron)
                || (p.CodigoBarras != null && p.CodigoBarras == buscar.Trim()));
        }

        if (tipo is not null)
        {
            consulta = consulta.Where(p => p.Tipo == tipo);
        }

        if (activo is not null)
        {
            consulta = consulta.Where(p => p.Activo == activo);
        }

        if (esVendible is not null)
        {
            consulta = consulta.Where(p => p.EsVendible == esVendible);
        }

        if (esComprable is not null)
        {
            consulta = consulta.Where(p => p.EsComprable == esComprable);
        }

        if (esProducible is not null)
        {
            consulta = consulta.Where(p => p.EsProducible == esProducible);
        }

        var paginado = await consulta.OrderBy(p => p.Sku).PaginarAsync(pagina, tamano, ct);
        return paginado.Map(ADto);
    }

    public async Task<Result<ProductoDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var producto = await Consulta().FirstOrDefaultAsync(p => p.Id == id, ct);
        return producto is null ? ErroresCatalogos.ProductoNoEncontrado : ADto(producto);
    }

    private IQueryable<Producto> Consulta() =>
        db.Productos.AsNoTracking().Include(p => p.UnidadBase).Include(p => p.Conversiones).ThenInclude(c => c.Unidad);

    internal static ProductoDto ADto(Producto p) => new(
        p.Id, p.Sku, p.Nombre, p.NombreCorto, p.Descripcion, p.Tipo, p.UnidadBaseId, p.UnidadBase?.Clave ?? string.Empty, p.EsComprable,
        p.EsProducible, p.EsVendible, p.ManejaLote, p.VidaUtilDias, p.ClaveProdServSat, p.ObjetoImpuestoSat, p.TasaIva,
        p.TasaIeps, p.CodigoBarras, p.StockMinimo, p.AplicaProductoACobro, p.DiasToleranciaDevolucion, p.Activo,
        p.Conversiones.OrderBy(c => c.FactorABase).Select(c => new ConversionDto(c.UnidadId, c.Unidad?.Clave ?? string.Empty,
            c.Unidad?.Nombre ?? string.Empty, c.FactorABase, c.UsoCompra, c.UsoVenta, c.UsoProduccion,
            c.PredeterminadaVenta, c.UsoTraslado, c.PredeterminadaTraslado)).ToList());
}

/// <summary>Alta y edición de productos con sus conversiones (RN-CAT-01 a 04).</summary>
public sealed class GuardarProductoHandler(CatalogosDbContext db, IConsultaInventarioProducto inventario,
    ConsultasProductosHandler consultas)
{
    public async Task<Result<ProductoDto>> CrearAsync(GuardarProductoRequest request, CancellationToken ct)
    {
        var sku = request.Sku.Trim().ToUpperInvariant();
        if (await db.Productos.AnyAsync(p => p.Sku == sku, ct))
        {
            return ErroresCatalogos.SkuDuplicado;
        }

        var validacion = await ValidarAsync(null, request, ct);
        if (!validacion.EsExito)
        {
            return validacion.Error!;
        }

        var producto = new Producto();
        Aplicar(producto, request, sku);
        db.Productos.Add(producto);
        await db.SaveChangesAsync(ct);
        return await consultas.ObtenerAsync(producto.Id, ct);
    }

    public async Task<Result<ProductoDto>> ActualizarAsync(Guid id, GuardarProductoRequest request, CancellationToken ct)
    {
        var producto = await db.Productos.Include(p => p.Conversiones).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (producto is null)
        {
            return ErroresCatalogos.ProductoNoEncontrado;
        }

        var sku = request.Sku.Trim().ToUpperInvariant();
        var cambiaSku = sku != producto.Sku;
        var cambiaUnidad = request.UnidadBaseId != producto.UnidadBaseId;
        if (cambiaSku && await db.Productos.AnyAsync(p => p.Sku == sku && p.Id != id, ct))
        {
            return ErroresCatalogos.SkuDuplicado;
        }

        if ((cambiaSku || cambiaUnidad) && await inventario.TieneMovimientosAsync(id, ct))
        {
            // RN-CAT-01 y RN-CAT-02.
            return cambiaSku ? ErroresCatalogos.SkuConMovimientos : ErroresCatalogos.UnidadBaseConMovimientos;
        }

        var validacion = await ValidarAsync(id, request, ct);
        if (!validacion.EsExito)
        {
            return validacion.Error!;
        }

        Aplicar(producto, request, sku);
        // Las conversiones bajan a la app con el producto: siempre se incrementa su version_sync.
        db.Entry(producto).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
        return await consultas.ObtenerAsync(producto.Id, ct);
    }

    private async Task<Result> ValidarAsync(Guid? id, GuardarProductoRequest request, CancellationToken ct)
    {
        var ids = (request.Conversiones ?? []).Select(c => c.UnidadId).Append(request.UnidadBaseId).Distinct().ToList();
        var unidades = await db.Unidades.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Clave, ct);
        if (unidades.Count != ids.Count)
        {
            return ErroresCatalogos.UnidadNoEncontrada;
        }

        // RN-CAT-08: único entre productos activos.
        var nombreCorto = NormalizarNombreCorto(request.NombreCorto);
        if (nombreCorto is not null
            && await db.Productos.AnyAsync(p => p.Activo && p.NombreCorto == nombreCorto && p.Id != id, ct))
        {
            return ErroresCatalogos.NombreCortoDuplicado;
        }

        // RN-CAT-09: la caja contiene un número entero de unidades de venta.
        var conversiones = request.Conversiones ?? [];
        var traslado = conversiones.FirstOrDefault(c => c.PredeterminadaTraslado);
        if (traslado is not null)
        {
            var factorVenta = conversiones.FirstOrDefault(c => c.PredeterminadaVenta)?.FactorABase ?? 1m;
            var paquetesPorCaja = traslado.FactorABase / factorVenta;
            if (paquetesPorCaja < 1 || paquetesPorCaja != decimal.Truncate(paquetesPorCaja))
            {
                return ErroresCatalogos.TrasladoNoEntero(unidades[traslado.UnidadId]);
            }
        }

        return Result.Exito();
    }

    private static string? NormalizarNombreCorto(string? nombreCorto) =>
        string.IsNullOrWhiteSpace(nombreCorto) ? null : nombreCorto.Trim().ToUpperInvariant();

    private static void Aplicar(Producto p, GuardarProductoRequest r, string sku)
    {
        var banderas = Producto.BanderasPorTipo(r.Tipo);
        p.Sku = sku;
        p.Nombre = r.Nombre.Trim();
        p.NombreCorto = NormalizarNombreCorto(r.NombreCorto);
        p.Descripcion = string.IsNullOrWhiteSpace(r.Descripcion) ? null : r.Descripcion.Trim();
        p.Tipo = r.Tipo;
        p.UnidadBaseId = r.UnidadBaseId;
        p.EsComprable = r.EsComprable ?? banderas.Comprable;
        p.EsProducible = r.EsProducible ?? banderas.Producible;
        p.EsVendible = r.EsVendible ?? banderas.Vendible;
        p.ManejaLote = r.ManejaLote ?? banderas.ManejaLote;
        p.VidaUtilDias = r.VidaUtilDias;
        p.ClaveProdServSat = string.IsNullOrWhiteSpace(r.ClaveProdServSat) ? null : r.ClaveProdServSat.Trim();
        p.ObjetoImpuestoSat = string.IsNullOrWhiteSpace(r.ObjetoImpuestoSat) ? "02" : r.ObjetoImpuestoSat.Trim();
        p.TasaIva = r.TasaIva;
        p.TasaIeps = r.TasaIeps is 0 ? null : r.TasaIeps;
        p.CodigoBarras = string.IsNullOrWhiteSpace(r.CodigoBarras) ? null : r.CodigoBarras.Trim();
        p.StockMinimo = r.StockMinimo;
        p.AplicaProductoACobro = r.AplicaProductoACobro;
        p.DiasToleranciaDevolucion = r.AplicaProductoACobro ? r.DiasToleranciaDevolucion : null;

        var nuevas = r.Conversiones ?? [];
        p.Conversiones.RemoveAll(c => nuevas.All(n => n.UnidadId != c.UnidadId));
        foreach (var n in nuevas)
        {
            var c = p.Conversiones.FirstOrDefault(x => x.UnidadId == n.UnidadId);
            if (c is null)
            {
                c = new ProductoUnidad { ProductoId = p.Id, UnidadId = n.UnidadId };
                p.Conversiones.Add(c);
            }

            c.FactorABase = n.FactorABase;
            c.UsoCompra = n.UsoCompra;
            c.UsoVenta = n.UsoVenta;
            c.UsoProduccion = n.UsoProduccion;
            c.PredeterminadaVenta = n.PredeterminadaVenta;
            c.UsoTraslado = n.UsoTraslado;
            c.PredeterminadaTraslado = n.PredeterminadaTraslado;
        }
    }
}

/// <summary>RN-CAT-05: un producto con existencia distinta de cero no se puede desactivar.</summary>
public sealed class ActivacionProductoHandler(CatalogosDbContext db, IConsultaInventarioProducto inventario,
    ConsultasProductosHandler consultas)
{
    public async Task<Result<ProductoDto>> DesactivarAsync(Guid id, CancellationToken ct)
    {
        var producto = await db.Productos.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (producto is null)
        {
            return ErroresCatalogos.ProductoNoEncontrado;
        }

        var existencia = await inventario.ExistenciaTotalAsync(id, ct);
        if (existencia != 0)
        {
            return ErroresCatalogos.ConExistencia(existencia);
        }

        producto.Activo = false;
        await db.SaveChangesAsync(ct);
        return await consultas.ObtenerAsync(id, ct);
    }

    public async Task<Result<ProductoDto>> ActivarAsync(Guid id, CancellationToken ct)
    {
        var producto = await db.Productos.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (producto is null)
        {
            return ErroresCatalogos.ProductoNoEncontrado;
        }

        // RN-CAT-08: al reactivar, el nombre corto no debe chocar con otro activo.
        if (producto.NombreCorto is not null
            && await db.Productos.AnyAsync(p => p.Activo && p.NombreCorto == producto.NombreCorto && p.Id != id, ct))
        {
            return ErroresCatalogos.NombreCortoDuplicado;
        }

        producto.Activo = true;
        await db.SaveChangesAsync(ct);
        return await consultas.ObtenerAsync(id, ct);
    }
}
