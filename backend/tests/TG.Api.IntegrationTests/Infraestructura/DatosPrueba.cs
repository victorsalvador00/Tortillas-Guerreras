using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace TG.Api.IntegrationTests.Infraestructura;

public sealed record IdCodigo(Guid Id, string Codigo);

public sealed record ProductoPrueba(Guid Id, string Sku, string Nombre, Guid UnidadBaseId, bool ManejaLote);

public sealed record MovimientoPrueba(Guid Id, string Tipo, Guid AlmacenId, Guid ProductoId, string Lote, decimal Cantidad,
    decimal CostoUnitario, decimal CostoTotal, decimal ExistenciaResultante, string DocumentoTipo, Guid DocumentoId,
    string DocumentoFolio);

public sealed record DocumentoPrueba(Guid DocumentoId, string Folio, IReadOnlyList<MovimientoPrueba> Movimientos);

public sealed record PaginaPrueba<T>(IReadOnlyList<T> Items, int Total);

/// <summary>Atajos para buscar datos semilla y crear datos de prueba aislados por prueba.</summary>
public static class DatosPrueba
{
    private static readonly ConcurrentDictionary<string, Guid> Cache = new();

    public static string Unico(string prefijo) => $"{prefijo}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    private sealed record ConId(Guid Id, string Clave = "");

    /// <summary>
    /// Usuario nuevo con rol Vendedor (contraseña de la semilla): un chofer propio evita choques de RN-LOG-13 entre pruebas.
    /// </summary>
    public static async Task<Guid> CrearChoferAsync(this HttpClient admin) => (await admin.CrearUsuarioChoferAsync()).Id;

    public static async Task<(Guid Id, string Usuario)> CrearUsuarioChoferAsync(this HttpClient admin)
    {
        var usuario = Unico("chofer").ToLowerInvariant();
        var creado = await admin.PostOkAsync<ConId>("/api/v1/seguridad/usuarios", new
        {
            usuario, nombreCompleto = $"Chofer {usuario}", email = $"{usuario}@tortillas.local", activo = true,
            roles = new[] { "Vendedor" }, contrasena = ApiFactory.Contrasena,
        });
        return (creado.Id, usuario);
    }

    public static async Task<Guid> CanalAsync(this HttpClient cliente, string clave) =>
        await CacheAsync(cliente, $"canal:{clave}", async () =>
            (await cliente.GetOkAsync<List<ConId>>("/api/v1/ventas/canales")).Single(c => c.Clave == clave).Id);

    public static async Task<Guid> UnidadAsync(this HttpClient cliente, string clave) =>
        await CacheAsync(cliente, $"u:{clave}", async () =>
        {
            var unidades = await cliente.GetOkAsync<List<IdCodigoClave>>("/api/v1/catalogos/unidades");
            return unidades.Single(u => u.Clave == clave).Id;
        });

    public static async Task<Guid> AlmacenAsync(this HttpClient cliente, string codigo)
    {
        var almacenes = await cliente.GetOkAsync<List<IdCodigo>>("/api/v1/inventario/almacenes");
        return almacenes.Single(a => a.Codigo == codigo).Id;
    }

    public static async Task<ProductoPrueba> ProductoAsync(this HttpClient cliente, string sku)
    {
        var pagina = await cliente.GetOkAsync<PaginaPrueba<ProductoPrueba>>($"/api/v1/catalogos/productos?buscar={sku}");
        return pagina.Items.Single(p => p.Sku == sku);
    }

    public static async Task<Guid> MotivoAsync(this HttpClient cliente, string clave) =>
        await CacheAsync(cliente, $"m:{clave}", async () =>
        {
            var motivos = await cliente.GetOkAsync<List<IdCodigoClave>>("/api/v1/catalogos/motivos-merma");
            return motivos.Single(m => m.Clave == clave).Id;
        });

    public static async Task<ProductoPrueba> CrearProductoAsync(this HttpClient cliente, string tipo = "MateriaPrima",
        string unidad = "KG", bool? manejaLote = null, bool? vendible = null, decimal tasaIva = 0m, decimal? tasaIeps = null,
        object[]? conversiones = null, string? sku = null, int? vidaUtil = null, string? claveSat = "01010101")
    {
        var unidadId = await cliente.UnidadAsync(unidad);
        var codigo = sku ?? Unico("PRB");
        return await cliente.PostOkAsync<ProductoPrueba>("/api/v1/catalogos/productos", new
        {
            sku = codigo,
            nombre = $"Producto de prueba {codigo}",
            tipo,
            unidadBaseId = unidadId,
            manejaLote,
            esVendible = vendible,
            tasaIva,
            tasaIeps,
            vidaUtilDias = vidaUtil,
            claveProdServSat = claveSat,
            conversiones = conversiones ?? [],
        });
    }

    /// <summary>Entrada por ajuste con costo (recalcula el promedio).</summary>
    public static async Task<DocumentoPrueba> EntradaAsync(this HttpClient cliente, Guid almacenId, Guid productoId,
        decimal cantidad, decimal costo, string? lote = null, DateOnly? caducidad = null)
    {
        var motivo = await cliente.MotivoAsync("OTRO");
        return await cliente.PostOkAsync<DocumentoPrueba>("/api/v1/inventario/ajustes", new
        {
            almacenId,
            motivoMermaId = motivo,
            notas = "Entrada de prueba",
            esMerma = false,
            partidas = new[] { new { productoId, lote, cantidad, costoUnitario = costo, fechaCaducidad = caducidad } },
        });
    }

    public static async Task<HttpResponseMessage> SalidaAsync(this HttpClient cliente, Guid almacenId, Guid productoId,
        decimal cantidad, string? lote = null)
    {
        var motivo = await cliente.MotivoAsync("OTRO");
        return await cliente.PostJsonAsync("/api/v1/inventario/ajustes", new
        {
            almacenId,
            motivoMermaId = motivo,
            notas = "Salida de prueba",
            esMerma = false,
            partidas = new[] { new { productoId, lote, cantidad = -cantidad } },
        });
    }

    public static async Task<decimal> ExistenciaAsync(this HttpClient cliente, Guid almacenId, Guid productoId, string? lote = null)
    {
        var pagina = await cliente.GetOkAsync<PaginaPrueba<JsonObject>>(
            $"/api/v1/inventario/existencias?almacenId={almacenId}&productoId={productoId}&soloConExistencia=false&tamano=200");
        return pagina.Items.Where(e => lote is null || e["lote"]!.GetValue<string>() == lote)
            .Sum(e => e["cantidad"]!.GetValue<decimal>());
    }

    /// <summary>Unidades y motivos salen de la misma plantilla: sus ids son iguales en todas las bases de prueba.</summary>
    private static async Task<Guid> CacheAsync(HttpClient _, string llave, Func<Task<Guid>> obtener)
    {
        if (Cache.TryGetValue(llave, out var id))
        {
            return id;
        }

        id = await obtener();
        Cache[llave] = id;
        return id;
    }

    private sealed record IdCodigoClave(Guid Id, string Clave);
}
