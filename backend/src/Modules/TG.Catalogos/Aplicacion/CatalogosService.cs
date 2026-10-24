using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TG.Catalogos.Dominio;
using TG.Catalogos.Infraestructura;
using TG.Catalogos.Publico;
using TG.SharedKernel.Resultados;

namespace TG.Catalogos.Aplicacion;

public static class ErroresCatalogos
{
    public static readonly Error SkuDuplicado = Error.Campo("CAT-001", "sku", "Ya existe un producto con ese SKU.");

    public static readonly Error SkuConMovimientos =
        Error.Campo("CAT-001", "sku", "El SKU no se puede cambiar porque el producto tiene movimientos.");

    public static readonly Error UnidadBaseConMovimientos =
        Error.Campo("CAT-002", "unidadBaseId", "La unidad base no se puede cambiar porque el producto tiene movimientos.");

    public static Error SinConversion(string producto, string unidad) =>
        Error.Campo("CAT-003", "unidadId", $"El producto {producto} no tiene conversión de {unidad} a su unidad base.");

    public static Error UsoNoPermitido(string producto, string unidad, UsoUnidad uso) =>
        Error.Campo("CAT-003", "unidadId", $"La unidad {unidad} no está habilitada para {uso.ToString().ToLowerInvariant()} en {producto}.");

    public static readonly Error ProductoNoEncontrado = Error.NoEncontrado("CAT-004", "El producto no existe.");

    public static Error ConExistencia(decimal existencia) =>
        Error.Conflicto("CAT-005", $"No se puede desactivar: el producto tiene existencia ({existencia.ToString("0.####", CultureInfo.InvariantCulture)}).");

    public static Error Decimales(string unidad, short decimales) =>
        Error.Campo("CAT-006", "cantidad", decimales == 0
            ? $"La cantidad en {unidad} debe ser entera."
            : $"La cantidad en {unidad} admite máximo {decimales} decimales.");

    public static readonly Error UnidadNoEncontrada = Error.NoEncontrado("CAT-007", "La unidad no existe.");

    public static readonly Error MotivoNoEncontrado = Error.NoEncontrado("CAT-008", "El motivo de merma no existe.");

    public static readonly Error ParametroNoEncontrado = Error.NoEncontrado("CAT-009", "El parámetro no existe.");

    public static Error ParametroInvalido(string tipo) =>
        Error.Campo("CAT-009", "valor", $"El valor no es un {tipo} válido.");

    public static Error ClaveDuplicada(string campo = "clave") => Error.Campo("CAT-010", campo, "La clave ya existe.");

    public static Error TrasladoNoEntero(string unidad) =>
        Error.Campo("CAT-011", "conversiones", $"La unidad de traslado {unidad} debe contener un número entero de unidades de venta.");

    public static readonly Error NombreCortoDuplicado =
        Error.Campo("CAT-012", "nombreCorto", "Ya existe un producto activo con ese nombre corto.");
}

internal sealed class CatalogosService(CatalogosDbContext db, IMemoryCache cache) : ICatalogosService
{
    public async Task<ProductoInfo?> ObtenerProductoAsync(Guid id, CancellationToken ct)
    {
        var producto = await ConsultaProductos().FirstOrDefaultAsync(p => p.Id == id, ct);
        return producto is null ? null : AInfo(producto);
    }

    public async Task<IReadOnlyDictionary<Guid, ProductoInfo>> ObtenerProductosAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        var productos = await ConsultaProductos().Where(p => lista.Contains(p.Id)).ToListAsync(ct);
        return productos.ToDictionary(p => p.Id, AInfo);
    }

    public async Task<IReadOnlyList<Guid>> BuscarProductosAsync(string texto, CancellationToken ct)
    {
        var patron = $"%{texto.Trim()}%";
        return await db.Productos.AsNoTracking()
            .Where(p => EF.Functions.ILike(p.Sku, patron) || EF.Functions.ILike(p.Nombre, patron))
            .Select(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProductoInfo>> ListarProductosAsync(bool? vendibles, bool soloActivos, CancellationToken ct)
    {
        var consulta = ConsultaProductos();
        if (vendibles is { } v)
        {
            consulta = consulta.Where(p => p.EsVendible == v);
        }

        if (soloActivos)
        {
            consulta = consulta.Where(p => p.Activo);
        }

        return (await consulta.OrderBy(p => p.Nombre).ToListAsync(ct)).Select(AInfo).ToList();
    }

    public async Task<Result<CantidadBase>> ConvertirABaseAsync(Guid productoId, Guid unidadId, decimal cantidad,
        UsoUnidad? uso, CancellationToken ct)
    {
        var producto = await ConsultaProductos().FirstOrDefaultAsync(p => p.Id == productoId, ct);
        if (producto is null)
        {
            return ErroresCatalogos.ProductoNoEncontrado;
        }

        var unidad = unidadId == producto.UnidadBaseId
            ? producto.UnidadBase!
            : producto.Conversiones.FirstOrDefault(c => c.UnidadId == unidadId)?.Unidad;
        if (unidad is null)
        {
            var nombre = await db.Unidades.Where(u => u.Id == unidadId).Select(u => u.Clave).FirstOrDefaultAsync(ct);
            return ErroresCatalogos.SinConversion(producto.Nombre, nombre ?? "la unidad");
        }

        var conversion = producto.Conversiones.FirstOrDefault(c => c.UnidadId == unidadId);
        if (uso is { } u && conversion is not null)
        {
            var permitido = u switch
            {
                UsoUnidad.Compra => conversion.UsoCompra,
                UsoUnidad.Venta => conversion.UsoVenta,
                _ => conversion.UsoProduccion,
            };
            if (!permitido)
            {
                return ErroresCatalogos.UsoNoPermitido(producto.Nombre, unidad.Clave, u);
            }
        }

        if (!unidad.RespetaDecimales(cantidad))
        {
            return ErroresCatalogos.Decimales(unidad.Clave, unidad.Decimales);
        }

        var factor = conversion?.FactorABase ?? 1m;
        return new CantidadBase(productoId, unidadId, cantidad, factor, decimal.Round(cantidad * factor, 4, MidpointRounding.AwayFromZero));
    }

    public async Task<UnidadInfo?> ObtenerUnidadAsync(Guid id, CancellationToken ct) =>
        await db.Unidades.AsNoTracking().Where(u => u.Id == id)
            .Select(u => new UnidadInfo(u.Id, u.Clave, u.Nombre, u.ClaveSat, u.Decimales))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, UnidadInfo>> ObtenerUnidadesAsync(CancellationToken ct) =>
        await db.Unidades.AsNoTracking()
            .Select(u => new UnidadInfo(u.Id, u.Clave, u.Nombre, u.ClaveSat, u.Decimales))
            .ToDictionaryAsync(u => u.Id, ct);

    public async Task<MotivoMermaInfo?> ObtenerMotivoAsync(Guid id, CancellationToken ct)
    {
        var m = await db.MotivosMerma.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return m is null ? null : AInfo(m);
    }

    public async Task<MotivoMermaInfo?> ObtenerMotivoPorClaveAsync(string clave, CancellationToken ct)
    {
        var m = await db.MotivosMerma.AsNoTracking().FirstOrDefaultAsync(x => x.Clave == clave, ct);
        return m is null ? null : AInfo(m);
    }

    public async Task<IReadOnlyList<MotivoMermaInfo>> ListarMotivosAsync(CancellationToken ct) =>
        (await db.MotivosMerma.AsNoTracking().OrderBy(m => m.Nombre).ToListAsync(ct)).Select(AInfo).ToList();

    public async Task<T> ObtenerParametroAsync<T>(string clave, T predeterminado, CancellationToken ct)
    {
        var valor = await cache.GetOrCreateAsync(ClaveCache(clave), async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await db.Parametros.AsNoTracking().Where(p => p.Clave == clave).Select(p => p.Valor).FirstOrDefaultAsync(ct);
        });
        if (valor is null)
        {
            return predeterminado;
        }

        try
        {
            var tipo = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (tipo == typeof(bool))
            {
                return (T)(object)bool.Parse(valor);
            }

            return (T)Convert.ChangeType(valor, tipo, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return predeterminado;
        }
    }

    public async Task<IReadOnlyList<ParametroInfo>> ListarParametrosAsync(CancellationToken ct) =>
        await db.Parametros.AsNoTracking().OrderBy(p => p.Clave)
            .Select(p => new ParametroInfo(p.Clave, p.Valor, p.Tipo.ToString(), p.VersionSync))
            .ToListAsync(ct);

    internal static string ClaveCache(string clave) => "catalogos.parametro:" + clave;

    private IQueryable<Producto> ConsultaProductos() =>
        db.Productos.AsNoTracking().Include(p => p.UnidadBase).Include(p => p.Conversiones).ThenInclude(c => c.Unidad);

    internal static ProductoInfo AInfo(Producto p) => new(
        p.Id, p.Sku, p.Nombre, p.NombreCorto, p.Tipo.ToString(), p.UnidadBaseId, p.UnidadBase!.Clave, p.UnidadBase.ClaveSat,
        p.UnidadBase.Decimales, p.EsComprable, p.EsProducible, p.EsVendible, p.ManejaLote, p.VidaUtilDias,
        p.ClaveProdServSat, p.ObjetoImpuestoSat, p.TasaIva, p.TasaIeps, p.StockMinimo, p.AplicaProductoACobro,
        p.DiasToleranciaDevolucion, p.Activo, p.VersionSync,
        p.Conversiones.Select(c => new ConversionInfo(c.UnidadId, c.Unidad!.Clave, c.Unidad.Nombre, c.Unidad.ClaveSat,
            c.Unidad.Decimales, c.FactorABase, c.UsoCompra, c.UsoVenta, c.UsoProduccion, c.PredeterminadaVenta, c.UsoTraslado,
            c.PredeterminadaTraslado)).ToList());

    private static MotivoMermaInfo AInfo(MotivoMerma m) =>
        new(m.Id, m.Clave, m.Nombre, m.AplicaProduccion, m.AplicaAlmacen, m.AplicaRuta, m.AplicaRetorno, m.Activo, m.VersionSync);
}
