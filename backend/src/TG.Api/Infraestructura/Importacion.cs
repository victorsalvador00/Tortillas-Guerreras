using System.Globalization;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Aplicacion;
using TG.Catalogos.Dominio;
using TG.Catalogos.Publico;
using TG.Compras.Aplicacion;
using TG.Inventario.Publico;
using TG.Logistica.Aplicacion;
using TG.Produccion.Aplicacion;
using TG.Seguridad.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Aplicacion;
using TG.Ventas.Publico;

namespace TG.Api.Infraestructura;

// Carga inicial por CSV (T-7.3; rutas, clientes por ruta, precios y grupos en T-8.14). Cada fila pasa por los handlers y servicios públicos de su módulo; las existencias
// entran solo por el kardex (regla 3). Todo el archivo va en una transacción: si una fila falla no se importa nada.

public sealed record ErrorImportacionDto(int Fila, string? Campo, string Mensaje);

public sealed record ResultadoImportacionDto(string Entidad, bool ValidarSolo, bool Aplicado, int Filas, int Creados,
    int Actualizados, IReadOnlyList<ErrorImportacionDto> Errores);

/// <summary>Lector CSV mínimo (RFC 4180): comillas, separador coma o punto y coma, UTF-8 con o sin BOM.</summary>
public sealed class ArchivoCsv
{
    public required IReadOnlyList<string> Encabezados { get; init; }
    public required IReadOnlyList<FilaCsv> Filas { get; init; }

    public static ArchivoCsv Leer(string texto)
    {
        texto = texto.TrimStart('﻿');
        var primeraLinea = texto.Split('\n', 2)[0];
        var separador = primeraLinea.Count(c => c == ';') > primeraLinea.Count(c => c == ',') ? ';' : ',';
        var registros = Partir(texto, separador);
        if (registros.Count == 0)
        {
            return new ArchivoCsv { Encabezados = [], Filas = [] };
        }

        var encabezados = registros[0].Select(Normalizar).ToList();
        var filas = new List<FilaCsv>();
        for (var i = 1; i < registros.Count; i++)
        {
            var valores = registros[i];
            if (valores.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var mapa = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var j = 0; j < encabezados.Count; j++)
            {
                mapa[encabezados[j]] = j < valores.Count ? valores[j].Trim() : string.Empty;
            }

            filas.Add(new FilaCsv(i + 1, mapa));
        }

        return new ArchivoCsv { Encabezados = encabezados, Filas = filas };
    }

    /// <summary>"Unidad base" → "unidad_base"; sin acentos ni mayúsculas.</summary>
    public static string Normalizar(string encabezado)
    {
        var sinAcentos = new string(encabezado.Trim().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return string.Join('_', sinAcentos.ToLowerInvariant().Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static List<List<string>> Partir(string texto, char separador)
    {
        var registros = new List<List<string>>();
        var actual = new List<string>();
        var campo = new StringBuilder();
        var enComillas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (enComillas)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"')
                {
                    campo.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    enComillas = false;
                }
                else
                {
                    campo.Append(c);
                }
            }
            else if (c == '"')
            {
                enComillas = true;
            }
            else if (c == separador)
            {
                actual.Add(campo.ToString());
                campo.Clear();
            }
            else if (c == '\n')
            {
                actual.Add(campo.ToString().TrimEnd('\r'));
                campo.Clear();
                registros.Add(actual);
                actual = [];
            }
            else
            {
                campo.Append(c);
            }
        }

        if (campo.Length > 0 || actual.Count > 0)
        {
            actual.Add(campo.ToString().TrimEnd('\r'));
            registros.Add(actual);
        }

        return registros;
    }
}

public sealed class FilaCsv(int numero, IReadOnlyDictionary<string, string> valores)
{
    public int Numero { get; } = numero;

    public string Texto(string columna) => valores.TryGetValue(columna, out var v) ? v : string.Empty;

    public string? Opcional(string columna) => string.IsNullOrWhiteSpace(Texto(columna)) ? null : Texto(columna);
}

/// <summary>Errores por fila y conversión de valores en español (sí/no, decimales con punto o coma).</summary>
internal sealed class BitacoraImportacion
{
    public List<ErrorImportacionDto> Errores { get; } = [];

    public void Agregar(int fila, string? campo, string mensaje) => Errores.Add(new ErrorImportacionDto(fila, campo, mensaje));

    public void Agregar(int fila, Error error)
    {
        if (error.Errores is { Count: > 0 })
        {
            foreach (var (campo, mensajes) in error.Errores)
            {
                foreach (var m in mensajes)
                {
                    Agregar(fila, campo, m);
                }
            }
        }
        else
        {
            Agregar(fila, null, error.Mensaje);
        }
    }

    public string Requerido(FilaCsv f, string columna)
    {
        var v = f.Texto(columna);
        if (string.IsNullOrWhiteSpace(v))
        {
            Agregar(f.Numero, columna, "Este dato es obligatorio.");
        }

        return v;
    }

    public decimal? Decimal(FilaCsv f, string columna, bool requerido = false)
    {
        var v = f.Texto(columna);
        if (string.IsNullOrWhiteSpace(v))
        {
            if (requerido)
            {
                Agregar(f.Numero, columna, "Este dato es obligatorio.");
            }

            return null;
        }

        if (decimal.TryParse(v.Replace(',', '.').Replace("$", string.Empty, StringComparison.Ordinal).Replace("%", string.Empty, StringComparison.Ordinal),
                NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        Agregar(f.Numero, columna, $"'{v}' no es un número válido.");
        return null;
    }

    public int? Entero(FilaCsv f, string columna)
    {
        var d = Decimal(f, columna);
        if (d is null)
        {
            return null;
        }

        if (d != decimal.Truncate(d.Value))
        {
            Agregar(f.Numero, columna, "Debe ser un número entero.");
            return null;
        }

        return (int)d.Value;
    }

    public bool? Booleano(FilaCsv f, string columna)
    {
        var v = ArchivoCsv.Normalizar(f.Texto(columna));
        return v switch
        {
            "" => null,
            "si" or "s" or "1" or "true" or "verdadero" or "x" => true,
            "no" or "n" or "0" or "false" or "falso" => false,
            _ => Invalido(),
        };

        bool? Invalido()
        {
            Agregar(f.Numero, columna, "Usa 'sí' o 'no'.");
            return null;
        }
    }

    public DateOnly? Fecha(FilaCsv f, string columna)
    {
        var v = f.Texto(columna);
        if (string.IsNullOrWhiteSpace(v))
        {
            return null;
        }

        if (DateOnly.TryParseExact(v, ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            return fecha;
        }

        Agregar(f.Numero, columna, "Usa el formato AAAA-MM-DD o DD/MM/AAAA.");
        return null;
    }

    public TEnum? Enum<TEnum>(FilaCsv f, string columna, IReadOnlyDictionary<string, TEnum> valores) where TEnum : struct
    {
        var v = ArchivoCsv.Normalizar(f.Texto(columna)).Replace("_", string.Empty, StringComparison.Ordinal);
        if (valores.TryGetValue(v, out var e))
        {
            return e;
        }

        Agregar(f.Numero, columna, $"Valor no válido; usa: {string.Join(", ", valores.Keys)}.");
        return null;
    }
}

internal sealed class ImportadorCsv(
    IUnitOfWork unitOfWork,
    IServiceProvider servicios,
    ICatalogosService catalogos,
    ILogger<ImportadorCsv> log)
{
    private sealed record Conteo(int Creados, int Actualizados);

    /// <summary>Error interno para revertir la vista previa o un archivo con errores.</summary>
    private static readonly Error Revertir = new("IMP-000", "Revertir importación");

    public async Task<ResultadoImportacionDto> EjecutarAsync(string entidad, IFormFile? archivo, bool validarSolo, CancellationToken ct)
    {
        if (archivo is null || archivo.Length == 0)
        {
            return new ResultadoImportacionDto(entidad, validarSolo, false, 0, 0, 0,
                [new ErrorImportacionDto(0, "archivo", "Elige un archivo CSV.")]);
        }

        string texto;
        using (var lector = new StreamReader(archivo.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            texto = await lector.ReadToEndAsync(ct);
        }

        var csv = ArchivoCsv.Leer(texto);
        var bitacora = new BitacoraImportacion();
        var faltantes = PlantillasImportacion.Columnas(entidad).Where(c => c.Obligatoria && !csv.Encabezados.Contains(c.Nombre))
            .Select(c => c.Nombre).ToList();
        if (faltantes.Count > 0)
        {
            return new ResultadoImportacionDto(entidad, validarSolo, false, csv.Filas.Count, 0, 0,
                faltantes.Select(c => new ErrorImportacionDto(1, c, "Falta la columna en el encabezado.")).ToList());
        }

        if (csv.Filas.Count == 0)
        {
            return new ResultadoImportacionDto(entidad, validarSolo, false, 0, 0, 0,
                [new ErrorImportacionDto(0, null, "El archivo no tiene filas de datos.")]);
        }

        var conteo = new Conteo(0, 0);
        Result<bool> resultado;
        try
        {
            resultado = await unitOfWork.EjecutarAsync(async c =>
            {
                conteo = entidad switch
                {
                    "productos" => await ProductosAsync(csv, bitacora, c),
                    "clientes" => await ClientesAsync(csv, bitacora, c),
                    "proveedores" => await ProveedoresAsync(csv, bitacora, c),
                    "recetas" => await RecetasAsync(csv, bitacora, c),
                    "existencias" => await ExistenciasAsync(csv, bitacora, c),
                    "grupos_clientes" => await GruposClientesAsync(csv, bitacora, c),
                    "rutas" => await RutasAsync(csv, bitacora, c),
                    "rutas_clientes" => await RutasClientesAsync(csv, bitacora, c),
                    "precios" => await PreciosAsync(csv, bitacora, c),
                    _ => throw new ArgumentOutOfRangeException(nameof(entidad)),
                };
                return bitacora.Errores.Count > 0 || validarSolo ? Result<bool>.Falla(Revertir) : Result<bool>.Exito(true);
            }, ct);
        }
        catch (DbUpdateException ex)
        {
            log.LogWarning(ex, "Importación de {Entidad} rechazada por la base de datos", entidad);
            bitacora.Agregar(0, null, "La base de datos rechazó un registro (dato duplicado o inválido). No se importó nada.");
            resultado = Result<bool>.Falla(Revertir);
        }

        return new ResultadoImportacionDto(entidad, validarSolo, resultado.EsExito, csv.Filas.Count, conteo.Creados, conteo.Actualizados,
            bitacora.Errores.OrderBy(e => e.Fila).ToList());
    }

    private async Task<bool> ValidarAsync<T>(T request, int fila, BitacoraImportacion bitacora, CancellationToken ct)
    {
        if (servicios.GetService<IValidator<T>>() is not { } validador)
        {
            return true;
        }

        var r = await validador.ValidateAsync(request, ct);
        foreach (var e in r.Errors)
        {
            bitacora.Agregar(fila, e.PropertyName, e.ErrorMessage);
        }

        return r.IsValid;
    }

    private async Task<Guid?> ProductoPorSkuAsync(string sku, CancellationToken ct) => (await ProductoInfoPorSkuAsync(sku, ct))?.Id;

    private async Task<ProductoInfo?> ProductoInfoPorSkuAsync(string sku, CancellationToken ct)
    {
        var ids = await catalogos.BuscarProductosAsync(sku, ct);
        var productos = await catalogos.ObtenerProductosAsync(ids, ct);
        return productos.Values.FirstOrDefault(p => string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Guid?> ClientePorCodigoAsync(string codigo, CancellationToken ct)
    {
        var encontrados = await servicios.GetRequiredService<ClientesHandler>().ListarAsync(codigo, null, null, null, null, 1, 20, ct);
        return encontrados.Items.FirstOrDefault(c => string.Equals(c.Codigo, codigo, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    // ───────────────────────────── Productos ─────────────────────────────

    private static readonly Dictionary<string, TipoProducto> Tipos = new()
    {
        ["materiaprima"] = TipoProducto.MateriaPrima,
        ["empaque"] = TipoProducto.Empaque,
        ["semiterminado"] = TipoProducto.Semiterminado,
        ["terminado"] = TipoProducto.Terminado,
        ["reventa"] = TipoProducto.Reventa,
    };

    private async Task<Conteo> ProductosAsync(ArchivoCsv csv, BitacoraImportacion b, CancellationToken ct)
    {
        var unidades = (await servicios.GetRequiredService<UnidadesHandler>().ListarAsync(ct))
            .ToDictionary(u => u.Clave, u => u.Id, StringComparer.OrdinalIgnoreCase);
        var guardar = servicios.GetRequiredService<GuardarProductoHandler>();
        int creados = 0, actualizados = 0;
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in csv.Filas)
        {
            var antes = b.Errores.Count;
            var sku = b.Requerido(f, "sku").ToUpperInvariant();
            if (sku.Length > 0 && !vistos.Add(sku))
            {
                b.Agregar(f.Numero, "sku", "El SKU está repetido en el archivo.");
            }

            var nombre = b.Requerido(f, "nombre");
            var tipo = b.Enum(f, "tipo", Tipos);
            var claveBase = b.Requerido(f, "unidad_base");
            Guid unidadBase = Guid.Empty;
            if (claveBase.Length > 0 && !unidades.TryGetValue(claveBase, out unidadBase))
            {
                b.Agregar(f.Numero, "unidad_base", $"No existe la unidad '{claveBase}'.");
            }

            var conversiones = Conversiones(f, b, unidades);
            var iva = b.Decimal(f, "iva_pct") ?? 0m;
            var ieps = b.Decimal(f, "ieps_pct");
            var request = new GuardarProductoRequest(sku, nombre, f.Opcional("descripcion"), tipo ?? TipoProducto.MateriaPrima, unidadBase,
                b.Booleano(f, "es_comprable"), b.Booleano(f, "es_producible"), b.Booleano(f, "es_vendible"), b.Booleano(f, "maneja_lote"),
                b.Entero(f, "vida_util_dias"), f.Opcional("clave_sat"), null, iva / 100m, ieps is null ? null : ieps / 100m,
                f.Opcional("codigo_barras"), b.Decimal(f, "stock_minimo"), conversiones, f.Opcional("nombre_corto"),
                b.Booleano(f, "producto_a_cobro") ?? false);
            if (b.Errores.Count > antes || !await ValidarAsync(request, f.Numero, b, ct))
            {
                continue;
            }

            var existente = await ProductoPorSkuAsync(sku, ct);
            var r = existente is { } id ? await guardar.ActualizarAsync(id, request, ct) : await guardar.CrearAsync(request, ct);
            if (!r.EsExito)
            {
                b.Agregar(f.Numero, r.Error!);
            }
            else if (existente is null)
            {
                creados++;
            }
            else
            {
                actualizados++;
            }
        }

        return new Conteo(creados, actualizados);
    }

    /// <summary>
    /// Formato: <c>TON=1000:compra|PAQ=1:venta*|CAJ=24:traslado</c> (usos compra, venta, produccion, traslado;
    /// * = predeterminada de venta; la primera de traslado es la predeterminada de traslado, RN-CAT-09).
    /// </summary>
    private static List<ConversionRequest> Conversiones(FilaCsv f, BitacoraImportacion b, Dictionary<string, Guid> unidades)
    {
        var lista = new List<ConversionRequest>();
        foreach (var parte in f.Texto("conversiones").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var predeterminada = parte.EndsWith('*');
            var limpio = parte.TrimEnd('*');
            var (definicion, usos) = limpio.Split(':', 2) is [var d, var us] ? (d, us) : (limpio, "compra");
            var claveFactor = definicion.Split('=', 2);
            if (claveFactor.Length != 2 || !unidades.TryGetValue(claveFactor[0].Trim(), out var unidad) ||
                !decimal.TryParse(claveFactor[1].Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var factor))
            {
                b.Agregar(f.Numero, "conversiones", $"Conversión '{parte}' no válida; usa UNIDAD=factor:usos (p. ej. TON=1000:compra).");
                continue;
            }

            var u = usos.ToLowerInvariant();
            var traslado = u.Contains("traslado", StringComparison.Ordinal);
            lista.Add(new ConversionRequest(unidad, factor, u.Contains("compra", StringComparison.Ordinal),
                u.Contains("venta", StringComparison.Ordinal), u.Contains("produccion", StringComparison.Ordinal) || u.Contains("producción", StringComparison.Ordinal),
                predeterminada, traslado, traslado && !lista.Any(c => c.PredeterminadaTraslado)));
        }

        return lista;
    }

    // ───────────────────────────── Clientes ─────────────────────────────

    private static readonly Dictionary<string, CondicionPago> Condiciones = new()
    {
        ["contado"] = CondicionPago.Contado,
        ["credito"] = CondicionPago.Credito,
    };

    private async Task<Conteo> ClientesAsync(ArchivoCsv csv, BitacoraImportacion b, CancellationToken ct)
    {
        var canalesExistentes = await servicios.GetRequiredService<CanalesHandler>().ListarAsync(ct);
        var canales = canalesExistentes.ToDictionary(c => c.Clave, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var listas = await servicios.GetRequiredService<ListasPrecioHandler>().ListarAsync(ct);
        var grupos = (await servicios.GetRequiredService<GruposClientesHandler>().ListarAsync(null, ct))
            .ToDictionary(g => g.Clave, StringComparer.OrdinalIgnoreCase);
        var clientes = servicios.GetRequiredService<ClientesHandler>();
        int creados = 0, actualizados = 0;
        foreach (var f in csv.Filas)
        {
            var antes = b.Errores.Count;
            var nombre = b.Requerido(f, "nombre");
            GrupoClienteDto? grupo = null;
            if (f.Opcional("grupo") is { } claveGrupo && !grupos.TryGetValue(claveGrupo.Trim(), out grupo))
            {
                b.Agregar(f.Numero, "grupo", $"No existe el grupo '{claveGrupo}'; impórtalo antes en grupos_clientes.");
            }

            // TRAD es la clave anterior de DET (v0.2).
            var claveCanal = b.Requerido(f, "canal");
            Guid canal = Guid.Empty;
            if (claveCanal.Length > 0 && !canales.TryGetValue(ClavesCanal.Normalizar(claveCanal), out canal))
            {
                b.Agregar(f.Numero, "canal",
                    $"No existe el canal '{claveCanal}' ({string.Join(", ", canalesExistentes.Select(c => c.Clave))}).");
            }

            // RN-VEN-12: sin lista explícita, el cliente de un grupo hereda la del grupo; si no, la del canal.
            var nombreLista = f.Opcional("lista_precio");
            var lista = nombreLista is not null
                ? listas.FirstOrDefault(l => string.Equals(l.Nombre, nombreLista, StringComparison.OrdinalIgnoreCase))
                : grupo?.ListaPrecioId is { } listaGrupo
                    ? listas.FirstOrDefault(l => l.Id == listaGrupo)
                    : listas.FirstOrDefault(l => l.CanalId == canal && l.Activo);
            if (lista is null && canal != Guid.Empty)
            {
                b.Agregar(f.Numero, "lista_precio", nombreLista is null ? "El canal no tiene lista de precios." : $"No existe la lista '{nombreLista}'.");
            }

            var condicion = f.Opcional("condicion_pago") is null ? CondicionPago.Contado : b.Enum(f, "condicion_pago", Condiciones);
            var request = new GuardarClienteRequest(f.Opcional("codigo")?.ToUpperInvariant(), nombre, canal, lista?.Id ?? Guid.Empty,
                condicion ?? CondicionPago.Contado, b.Entero(f, "dias_credito") ?? 0, b.Decimal(f, "limite_credito"),
                b.Booleano(f, "requiere_factura") ?? false, f.Opcional("rfc")?.ToUpperInvariant(), f.Opcional("razon_social"),
                f.Opcional("regimen_fiscal"), f.Opcional("cp_fiscal"), f.Opcional("uso_cfdi"), f.Opcional("email_facturacion"),
                f.Opcional("telefono"), f.Opcional("calle"), f.Opcional("colonia"), f.Opcional("municipio"), f.Opcional("estado"),
                f.Opcional("codigo_postal"), b.Decimal(f, "latitud"), b.Decimal(f, "longitud"), true, grupo?.Id);
            if (b.Errores.Count > antes || !await ValidarAsync(request, f.Numero, b, ct))
            {
                continue;
            }

            var existente = request.Codigo is { } codigo ? await ClientePorCodigoAsync(codigo, ct) : null;

            var r = await clientes.GuardarAsync(existente, request, ct);
            if (!r.EsExito)
            {
                b.Agregar(f.Numero, r.Error!);
            }
            else if (existente is null)
            {
                creados++;
            }
            else
            {
                actualizados++;
            }
        }

        return new Conteo(creados, actualizados);
    }

    // ───────────────────────────── Grupos de clientes (v0.2) ─────────────────────────────

    private async Task<Conteo> GruposClientesAsync(ArchivoCsv csv, BitacoraImportacion b, CancellationToken ct)
    {
        var canalesExistentes = await servicios.GetRequiredService<CanalesHandler>().ListarAsync(ct);
        var canales = canalesExistentes.ToDictionary(c => c.Clave, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var listas = await servicios.GetRequiredService<ListasPrecioHandler>().ListarAsync(ct);
        var handler = servicios.GetRequiredService<GruposClientesHandler>();
        var existentes = (await handler.ListarAsync(null, ct)).ToDictionary(g => g.Clave, g => g.Id, StringComparer.OrdinalIgnoreCase);
        int creados = 0, actualizados = 0;
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in csv.Filas)
        {
            var antes = b.Errores.Count;
            var clave = b.Requerido(f, "clave").Trim().ToUpperInvariant();
            if (clave.Length > 0 && !vistos.Add(clave))
            {
                b.Agregar(f.Numero, "clave", "La clave está repetida en el archivo.");
            }

            var nombre = b.Requerido(f, "nombre");
            var claveCanal = b.Requerido(f, "canal");
            Guid canal = Guid.Empty;
            if (claveCanal.Length > 0 && !canales.TryGetValue(ClavesCanal.Normalizar(claveCanal), out canal))
            {
                b.Agregar(f.Numero, "canal", $"No existe el canal '{claveCanal}' ({string.Join(", ", canalesExistentes.Select(c => c.Clave))}).");
            }

            Guid? lista = null;
            if (f.Opcional("lista_precio") is { } nombreLista)
            {
                lista = listas.FirstOrDefault(l => string.Equals(l.Nombre, nombreLista, StringComparison.OrdinalIgnoreCase))?.Id;
                if (lista is null)
                {
                    b.Agregar(f.Numero, "lista_precio", $"No existe la lista '{nombreLista}'.");
                }
            }

            var request = new GuardarGrupoClienteRequest(clave, nombre, canal, lista, b.Booleano(f, "activo") ?? true);
            if (b.Errores.Count > antes || !await ValidarAsync(request, f.Numero, b, ct))
            {
                continue;
            }

            Guid? existente = existentes.TryGetValue(clave, out var id) ? id : null;
            var r = await handler.GuardarAsync(existente, request, ct);
            if (!r.EsExito)
            {
                b.Agregar(f.Numero, r.Error!);
            }
            else if (existente is null)
            {
                creados++;
            }
            else
            {
                actualizados++;
            }
        }

        return new Conteo(creados, actualizados);
    }

    // ───────────────────────────── Rutas (v0.2) ─────────────────────────────

    private async Task<Conteo> RutasAsync(ArchivoCsv csv, BitacoraImportacion b, CancellationToken ct)
    {
        var canalesExistentes = await servicios.GetRequiredService<CanalesHandler>().ListarAsync(ct);
        var canales = canalesExistentes.ToDictionary(c => c.Clave, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var vehiculos = (await servicios.GetRequiredService<VehiculosHandler>().ListarAsync(null, ct))
            .ToDictionary(v => v.Placas, v => v.Id, StringComparer.OrdinalIgnoreCase);
        var seguridad = servicios.GetRequiredService<ISeguridadService>();
        var rutas = servicios.GetRequiredService<RutasHandler>();
        var existentes = (await rutas.ListarAsync(null, ct)).ToDictionary(r => r.Codigo, r => r.Id, StringComparer.OrdinalIgnoreCase);
        int creados = 0, actualizados = 0;
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in csv.Filas)
        {
            var antes = b.Errores.Count;
            var codigo = b.Requerido(f, "codigo").Trim().ToUpperInvariant();
            if (codigo.Length > 0 && !vistos.Add(codigo))
            {
                b.Agregar(f.Numero, "codigo", "El código está repetido en el archivo.");
            }

            var nombre = b.Requerido(f, "nombre");
            var claveCanal = b.Requerido(f, "canal");
            Guid canal = Guid.Empty;
            if (claveCanal.Length > 0 && !canales.TryGetValue(ClavesCanal.Normalizar(claveCanal), out canal))
            {
                b.Agregar(f.Numero, "canal", $"No existe el canal '{claveCanal}' ({string.Join(", ", canalesExistentes.Select(c => c.Clave))}).");
            }

            Guid? chofer = null;
            if (f.Opcional("chofer") is { } usuario)
            {
                chofer = await seguridad.UsuarioPorNombreAsync(usuario.Trim(), ct);
                if (chofer is null)
                {
                    b.Agregar(f.Numero, "chofer", $"No existe el usuario '{usuario}'.");
                }
            }

            Guid? vehiculo = null;
            if (f.Opcional("vehiculo") is { } placas)
            {
                if (vehiculos.TryGetValue(placas.Trim(), out var v))
                {
                    vehiculo = v;
                }
                else
                {
                    b.Agregar(f.Numero, "vehiculo", $"No existe el vehículo con placas '{placas}'.");
                }
            }

            var request = new GuardarRutaRequest(codigo, nombre, canal, chofer, vehiculo, b.Booleano(f, "activo") ?? true);
            if (b.Errores.Count > antes || !await ValidarAsync(request, f.Numero, b, ct))
            {
                continue;
            }

            // Una ruta nueva se crea con su almacén y su cliente genérico, igual que desde la web.
            Guid? existente = existentes.TryGetValue(codigo, out var id) ? id : null;
            var r = existente is { } rutaId ? await rutas.ActualizarAsync(rutaId, request, ct) : await rutas.CrearAsync(request, ct);
            if (!r.EsExito)
            {
                b.Agregar(f.Numero, r.Error!);
            }
            else if (existente is null)
            {
                creados++;
            }
            else
            {
                actualizados++;
            }
        }

        return new Conteo(creados, actualizados);
    }

    private static readonly Dictionary<string, short> Dias = new()
    {
        ["l"] = 1, ["lu"] = 1, ["lun"] = 1, ["lunes"] = 1,
        ["m"] = 2, ["ma"] = 2, ["mar"] = 2, ["martes"] = 2,
        ["x"] = 3, ["mi"] = 3, ["mie"] = 3, ["miercoles"] = 3,
        ["j"] = 4, ["ju"] = 4, ["jue"] = 4, ["jueves"] = 4,
        ["v"] = 5, ["vi"] = 5, ["vie"] = 5, ["viernes"] = 5,
        ["s"] = 6, ["sa"] = 6, ["sab"] = 6, ["sabado"] = 6,
        ["d"] = 7, ["do"] = 7, ["dom"] = 7, ["domingo"] = 7,
    };

    /// <summary>"L,X,V", "lunes|jueves" o "1,3,5" (1 = lunes); vacío = el valor por omisión de la ruta (lunes a sábado).</summary>
    private static List<short>? DiasVisita(FilaCsv f, BitacoraImportacion b)
    {
        var texto = f.Texto("dias");
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        var dias = new List<short>();
        foreach (var parte in texto.Split([',', '|', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var clave = ArchivoCsv.Normalizar(parte);
            if (short.TryParse(clave, out var n) && n is >= 1 and <= 7)
            {
                dias.Add(n);
            }
            else if (Dias.TryGetValue(clave, out var dia))
            {
                dias.Add(dia);
            }
            else
            {
                b.Agregar(f.Numero, "dias", $"Día '{parte}' no válido; usa L, M, X, J, V, S, D o 1 a 7.");
            }
        }

        return dias.Distinct().Order().ToList();
    }

    /// <summary>
    /// Clientes por ruta: cada ruta del archivo queda con los clientes que trae (los que no vienen se quitan de la ruta;
    /// el genérico siempre se queda). Valida canal (RN-LOG-31) en el handler de Logística.
    /// </summary>
    private async Task<Conteo> RutasClientesAsync(ArchivoCsv csv, BitacoraImportacion b, CancellationToken ct)
    {
        var rutasHandler = servicios.GetRequiredService<RutasHandler>();
        var rutas = (await rutasHandler.ListarAsync(null, ct)).ToDictionary(r => r.Codigo, r => r.Id, StringComparer.OrdinalIgnoreCase);
        var porRuta = new Dictionary<Guid, List<(int Fila, ClienteRutaRequest Cliente)>>();
        var codigos = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in csv.Filas)
        {
            var antes = b.Errores.Count;
            var codigoRuta = b.Requerido(f, "ruta").Trim();
            Guid ruta = Guid.Empty;
            if (codigoRuta.Length > 0 && !rutas.TryGetValue(codigoRuta, out ruta))
            {
                b.Agregar(f.Numero, "ruta", $"No existe la ruta '{codigoRuta}'; impórtala antes en rutas.");
            }

            var codigoCliente = b.Requerido(f, "cliente").Trim();
            Guid? cliente = null;
            if (codigoCliente.Length > 0)
            {
                if (!codigos.TryGetValue(codigoCliente, out cliente))
                {
                    codigos[codigoCliente] = cliente = await ClientePorCodigoAsync(codigoCliente, ct);
                }

                if (cliente is null)
                {
                    b.Agregar(f.Numero, "cliente", $"No existe el cliente con código '{codigoCliente}'.");
                }
            }

            var orden = b.Entero(f, "orden");
            if (orden is null && b.Errores.Count == antes)
            {
                b.Agregar(f.Numero, "orden", "Este dato es obligatorio.");
            }
            else if (orden is < 0 or > short.MaxValue)
            {
                b.Agregar(f.Numero, "orden", "El orden de visita debe ser 0 o mayor.");
            }

            var dias = DiasVisita(f, b);
            if (b.Errores.Count > antes)
            {
                continue;
            }

            var lista = porRuta.TryGetValue(ruta, out var l) ? l : porRuta[ruta] = [];
            if (lista.Any(x => x.Cliente.ClienteId == cliente!.Value))
            {
                b.Agregar(f.Numero, "cliente", "El cliente está repetido en la misma ruta.");
                continue;
            }

            lista.Add((f.Numero, new ClienteRutaRequest(cliente!.Value, (short)orden!.Value, dias)));
        }

        if (b.Errores.Count > 0)
        {
            return new Conteo(0, 0);
        }

        var asignados = 0;
        foreach (var (ruta, filas) in porRuta)
        {
            var request = new ClientesRutaRequest(filas.Select(x => x.Cliente).ToList());
            if (!await ValidarAsync(request, filas[0].Fila, b, ct))
            {
                continue;
            }

            var r = await rutasHandler.ReemplazarClientesAsync(ruta, request, ct);
            if (!r.EsExito)
            {
                b.Agregar(filas[0].Fila, r.Error!);
                continue;
            }

            asignados += filas.Count;
        }

        return new Conteo(0, asignados);
    }

    // ───────────────────────────── Precios (v0.2) ─────────────────────────────

    /// <summary>
    /// Precios de lista o especiales por cliente (RN-VEN-12). Se combinan con los existentes: lo que viene se agrega o
    /// actualiza y lo que no viene se queda.
    /// </summary>
    private async Task<Conteo> PreciosAsync(ArchivoCsv csv, BitacoraImportacion b, CancellationToken ct)
    {
        var unidades = (await servicios.GetRequiredService<UnidadesHandler>().ListarAsync(ct))
            .ToDictionary(u => u.Clave, u => u.Id, StringComparer.OrdinalIgnoreCase);
        var listasHandler = servicios.GetRequiredService<ListasPrecioHandler>();
        var listas = (await listasHandler.ListarAsync(ct)).ToDictionary(l => l.Nombre, l => l.Id, StringComparer.OrdinalIgnoreCase);
        var hoy = servicios.GetRequiredService<IFechaOperativa>().Hoy();
        var deLista = new Dictionary<Guid, List<(int Fila, PartidaListaRequest Precio)>>();
        var deCliente = new Dictionary<Guid, List<(int Fila, PrecioClienteRequest Precio)>>();
        var productos = new Dictionary<string, ProductoInfo?>(StringComparer.OrdinalIgnoreCase);
        var clientes = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in csv.Filas)
        {
            var antes = b.Errores.Count;
            var nombreLista = f.Opcional("lista");
            var codigoCliente = f.Opcional("cliente");
            if ((nombreLista is null) == (codigoCliente is null))
            {
                b.Agregar(f.Numero, "lista", "Indica la lista o el cliente (uno de los dos).");
            }

            Guid lista = Guid.Empty;
            if (nombreLista is not null && !listas.TryGetValue(nombreLista.Trim(), out lista))
            {
                b.Agregar(f.Numero, "lista", $"No existe la lista '{nombreLista}'.");
            }

            Guid? cliente = null;
            if (codigoCliente is not null)
            {
                if (!clientes.TryGetValue(codigoCliente.Trim(), out cliente))
                {
                    clientes[codigoCliente.Trim()] = cliente = await ClientePorCodigoAsync(codigoCliente.Trim(), ct);
                }

                if (cliente is null)
                {
                    b.Agregar(f.Numero, "cliente", $"No existe el cliente con código '{codigoCliente}'.");
                }
            }

            var sku = b.Requerido(f, "sku").Trim();
            ProductoInfo? producto = null;
            if (sku.Length > 0)
            {
                if (!productos.TryGetValue(sku, out producto))
                {
                    productos[sku] = producto = await ProductoInfoPorSkuAsync(sku, ct);
                }

                if (producto is null)
                {
                    b.Agregar(f.Numero, "sku", $"No existe el producto '{sku}'.");
                }
            }

            var unidad = producto?.UnidadBaseId ?? Guid.Empty;
            if (f.Opcional("unidad") is { } claveUnidad && !unidades.TryGetValue(claveUnidad.Trim(), out unidad))
            {
                b.Agregar(f.Numero, "unidad", $"No existe la unidad '{claveUnidad}'.");
            }

            var precio = b.Decimal(f, "precio", requerido: true);
            if (precio < 0)
            {
                b.Agregar(f.Numero, "precio", "El precio no puede ser negativo.");
            }

            var vigente = b.Fecha(f, "vigente_desde");
            if (vigente is not null && nombreLista is not null)
            {
                b.Agregar(f.Numero, "vigente_desde", "La vigencia solo aplica a precios de cliente.");
            }

            if (b.Errores.Count > antes)
            {
                continue;
            }

            if (cliente is { } clienteId)
            {
                var filas = deCliente.TryGetValue(clienteId, out var l) ? l : deCliente[clienteId] = [];
                if (filas.Any(x => x.Precio.ProductoId == producto!.Id && x.Precio.UnidadId == unidad))
                {
                    b.Agregar(f.Numero, "sku", "El producto con esa unidad está repetido para el cliente.");
                    continue;
                }

                filas.Add((f.Numero, new PrecioClienteRequest(producto!.Id, unidad, precio!.Value, vigente ?? hoy)));
            }
            else
            {
                var filas = deLista.TryGetValue(lista, out var l) ? l : deLista[lista] = [];
                if (filas.Any(x => x.Precio.ProductoId == producto!.Id && x.Precio.UnidadId == unidad))
                {
                    b.Agregar(f.Numero, "sku", "El producto con esa unidad está repetido en la lista.");
                    continue;
                }

                filas.Add((f.Numero, new PartidaListaRequest(producto!.Id, unidad, precio!.Value)));
            }
        }

        if (b.Errores.Count > 0)
        {
            return new Conteo(0, 0);
        }

        int creados = 0, actualizados = 0;
        foreach (var (lista, filas) in deLista)
        {
            var actual = await listasHandler.ObtenerAsync(lista, ct);
            var existentes = actual.Valor.Partidas.Select(p => new PartidaListaRequest(p.ProductoId, p.UnidadId, p.PrecioUnitario)).ToList();
            var nuevos = filas.Select(x => x.Precio).ToList();
            actualizados += nuevos.Count(n => existentes.Any(e => e.ProductoId == n.ProductoId && e.UnidadId == n.UnidadId));
            creados += nuevos.Count(n => !existentes.Any(e => e.ProductoId == n.ProductoId && e.UnidadId == n.UnidadId));
            var request = new PartidasListaRequest(
                [.. existentes.Where(e => !nuevos.Any(n => n.ProductoId == e.ProductoId && n.UnidadId == e.UnidadId)), .. nuevos]);
            if (!await ValidarAsync(request, filas[0].Fila, b, ct))
            {
                continue;
            }

            var r = await listasHandler.GuardarPartidasAsync(lista, request, ct);
            if (!r.EsExito)
            {
                b.Agregar(filas[0].Fila, r.Error!);
            }
        }

        var preciosCliente = servicios.GetRequiredService<PreciosClienteHandler>();
        foreach (var (cliente, filas) in deCliente)
        {
            var actual = await preciosCliente.ListarAsync(cliente, ct);
            var existentes = actual.Valor.Select(p => new PrecioClienteRequest(p.ProductoId, p.UnidadId, p.PrecioUnitario, p.VigenteDesde)).ToList();
            var nuevos = filas.Select(x => x.Precio).ToList();
            actualizados += nuevos.Count(n => existentes.Any(e => e.ProductoId == n.ProductoId && e.UnidadId == n.UnidadId));
            creados += nuevos.Count(n => !existentes.Any(e => e.ProductoId == n.ProductoId && e.UnidadId == n.UnidadId));
            var request = new PreciosClienteRequest(
                [.. existentes.Where(e => !nuevos.Any(n => n.ProductoId == e.ProductoId && n.UnidadId == e.UnidadId)), .. nuevos]);
            if (!await ValidarAsync(request, filas[0].Fila, b, ct))
            {
                continue;
            }

            var r = await preciosCliente.ReemplazarAsync(cliente, request, ct);
            if (!r.EsExito)
            {
                b.Agregar(filas[0].Fila, r.Error!);
            }
        }

        return new Conteo(creados, actualizados);
    }

    // ───────────────────────────── Proveedores ─────────────────────────────

    private async Task<Conteo> ProveedoresAsync(ArchivoCsv csv, BitacoraImportacion b, CancellationToken ct)
    {
        var proveedores = servicios.GetRequiredService<ProveedoresHandler>();
        int creados = 0, actualizados = 0;
        foreach (var f in csv.Filas)
        {
            var antes = b.Errores.Count;
            var codigo = b.Requerido(f, "codigo").ToUpperInvariant();
            var request = new GuardarProveedorRequest(codigo, b.Requerido(f, "razon_social"), f.Opcional("nombre_comercial"),
                f.Opcional("rfc")?.ToUpperInvariant(), f.Opcional("contacto"), f.Opcional("telefono"), f.Opcional("email"),
                b.Entero(f, "dias_credito") ?? 0);
            if (b.Errores.Count > antes || !await ValidarAsync(request, f.Numero, b, ct))
            {
                continue;
            }

            var encontrados = await proveedores.ListarAsync(codigo, null, 1, 20, ct);
            var existente = encontrados.Items.FirstOrDefault(p => p.Codigo == codigo)?.Id;
            var r = existente is { } id ? await proveedores.ActualizarAsync(id, request, ct) : await proveedores.CrearAsync(request, ct);
            if (!r.EsExito)
            {
                b.Agregar(f.Numero, r.Error!);
            }
            else if (existente is null)
            {
                creados++;
            }
            else
            {
                actualizados++;
            }
        }

        return new Conteo(creados, actualizados);
    }

    // ───────────────────────────── Recetas ─────────────────────────────

    /// <summary>Una fila por componente; las filas con el mismo producto y nombre forman una receta (nueva versión).</summary>
    private async Task<Conteo> RecetasAsync(ArchivoCsv csv, BitacoraImportacion b, CancellationToken ct)
    {
        var unidades = (await servicios.GetRequiredService<UnidadesHandler>().ListarAsync(ct))
            .ToDictionary(u => u.Clave, u => u.Id, StringComparer.OrdinalIgnoreCase);
        var recetas = servicios.GetRequiredService<RecetasHandler>();
        var creados = 0;
        foreach (var grupo in csv.Filas.GroupBy(f => (Sku: f.Texto("producto_sku").ToUpperInvariant(), Nombre: f.Texto("nombre"))))
        {
            var primera = grupo.First();
            var antes = b.Errores.Count;
            b.Requerido(primera, "producto_sku");
            b.Requerido(primera, "nombre");
            var productoId = grupo.Key.Sku.Length > 0 ? await ProductoPorSkuAsync(grupo.Key.Sku, ct) : null;
            if (grupo.Key.Sku.Length > 0 && productoId is null)
            {
                b.Agregar(primera.Numero, "producto_sku", $"No existe el producto '{grupo.Key.Sku}'.");
            }

            var rendimiento = b.Decimal(primera, "rendimiento", requerido: true) ?? 0m;
            var merma = b.Decimal(primera, "merma_esperada_pct") ?? 0m;
            var componentes = new List<ComponenteRequest>();
            foreach (var f in grupo)
            {
                var sku = b.Requerido(f, "componente_sku").ToUpperInvariant();
                var componente = sku.Length > 0 ? await ProductoPorSkuAsync(sku, ct) : null;
                if (sku.Length > 0 && componente is null)
                {
                    b.Agregar(f.Numero, "componente_sku", $"No existe el producto '{sku}'.");
                }

                Guid? unidad = null;
                if (f.Opcional("componente_unidad") is { } clave)
                {
                    if (unidades.TryGetValue(clave, out var u))
                    {
                        unidad = u;
                    }
                    else
                    {
                        b.Agregar(f.Numero, "componente_unidad", $"No existe la unidad '{clave}'.");
                    }
                }

                var cantidad = b.Decimal(f, "componente_cantidad", requerido: true) ?? 0m;
                if (componente is { } c)
                {
                    componentes.Add(new ComponenteRequest(c, unidad, cantidad));
                }
            }

            var request = new GuardarRecetaRequest(productoId ?? Guid.Empty, grupo.Key.Nombre, rendimiento, merma / 100m,
                primera.Opcional("notas"), componentes);
            if (b.Errores.Count > antes || !await ValidarAsync(request, primera.Numero, b, ct))
            {
                continue;
            }

            var creada = await recetas.CrearAsync(request, ct);
            if (!creada.EsExito)
            {
                b.Agregar(primera.Numero, creada.Error!);
                continue;
            }

            creados++;
            if (b.Booleano(primera, "publicar") == true)
            {
                var publicada = await recetas.PublicarAsync(creada.Valor.Id, ct);
                if (!publicada.EsExito)
                {
                    b.Agregar(primera.Numero, publicada.Error!);
                }
            }
        }

        return new Conteo(creados, 0);
    }

    // ───────────────────────────── Existencias iniciales ─────────────────────────────

    /// <summary>Entradas por kardex: <c>AjusteEntrada</c> con costo, documento Ajuste "Carga inicial" (regla 3).</summary>
    private async Task<Conteo> ExistenciasAsync(ArchivoCsv csv, BitacoraImportacion b, CancellationToken ct)
    {
        var inventario = servicios.GetRequiredService<IInventarioService>();
        var almacenes = (await inventario.ListarAlmacenesAsync(ct)).ToDictionary(a => a.Codigo, StringComparer.OrdinalIgnoreCase);
        var lineas = new List<LineaMovimiento>();
        var motivo = await catalogos.ObtenerMotivoPorClaveAsync(ClavesMotivoMerma.Otro, ct);
        foreach (var f in csv.Filas)
        {
            var antes = b.Errores.Count;
            var codigo = b.Requerido(f, "almacen");
            if (codigo.Length > 0 && !almacenes.ContainsKey(codigo))
            {
                b.Agregar(f.Numero, "almacen", $"No existe el almacén '{codigo}'.");
            }

            var sku = b.Requerido(f, "sku").ToUpperInvariant();
            var productoId = sku.Length > 0 ? await ProductoPorSkuAsync(sku, ct) : null;
            var producto = productoId is { } p ? await catalogos.ObtenerProductoAsync(p, ct) : null;
            if (sku.Length > 0 && producto is null)
            {
                b.Agregar(f.Numero, "sku", $"No existe el producto '{sku}'.");
            }

            var lote = f.Opcional("lote");
            if (producto is { ManejaLote: true } && lote is null)
            {
                b.Agregar(f.Numero, "lote", $"{producto.Nombre} maneja lote: captura el lote.");
            }

            var cantidad = b.Decimal(f, "cantidad", requerido: true);
            if (cantidad is <= 0)
            {
                b.Agregar(f.Numero, "cantidad", "La cantidad debe ser mayor que cero.");
            }
            else if (cantidad is { } c && producto is not null && decimal.Round(c, producto.UnidadBaseDecimales) != c)
            {
                b.Agregar(f.Numero, "cantidad", $"Máximo {producto.UnidadBaseDecimales} decimales en {producto.UnidadBaseClave}.");
            }

            var costo = b.Decimal(f, "costo_unitario", requerido: true);
            if (costo is < 0)
            {
                b.Agregar(f.Numero, "costo_unitario", "El costo no puede ser negativo.");
            }

            var caducidad = b.Fecha(f, "caducidad");
            if (b.Errores.Count == antes && producto is not null)
            {
                lineas.Add(new LineaMovimiento(TipoMovimiento.AjusteEntrada, almacenes[codigo].Id, producto.Id, lote, cantidad!.Value,
                    costo!.Value, motivo?.Id, caducidad, "Carga inicial"));
            }
        }

        if (b.Errores.Count > 0 || lineas.Count == 0)
        {
            return new Conteo(0, 0);
        }

        var folio = await servicios.GetRequiredService<IGeneradorFolios>().SiguienteAsync("inventario", "AJ", ct);
        var resultado = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
            DocumentoInventario.Ajuste, SharedKernel.Dominio.Id.Nuevo(), folio, servicios.GetRequiredService<IFechaOperativa>().Hoy(),
            servicios.GetRequiredService<IUsuarioActual>().Id ?? Guid.Empty, lineas, Notas: "Carga inicial"), ct);
        if (!resultado.EsExito)
        {
            b.Agregar(0, resultado.Error!);
            return new Conteo(0, 0);
        }

        return new Conteo(lineas.Count, 0);
    }
}

public sealed record ColumnaPlantilla(string Nombre, bool Obligatoria, string Descripcion);

/// <summary>Columnas de cada plantilla (encabezados en español, normalizados) y filas de ejemplo de la semilla.</summary>
public static class PlantillasImportacion
{
    public static readonly string[] Entidades =
        ["productos", "clientes", "proveedores", "recetas", "existencias", "grupos_clientes", "rutas", "rutas_clientes", "precios"];

    public static IReadOnlyList<ColumnaPlantilla> Columnas(string entidad) => entidad switch
    {
        "productos" =>
        [
            new("sku", true, "Clave única"), new("nombre", true, ""),
            new("tipo", true, "MateriaPrima, Empaque, Semiterminado, Terminado o Reventa"),
            new("unidad_base", true, "Clave de unidad (KG, PZA, PAQ…)"),
            new("conversiones", false, "UNIDAD=factor:usos separadas por |; usos compra, venta, produccion, traslado; * = predeterminada de venta"),
            new("es_comprable", false, "sí/no; vacío = según el tipo"), new("es_producible", false, ""), new("es_vendible", false, ""),
            new("maneja_lote", false, ""), new("vida_util_dias", false, ""), new("iva_pct", false, "16 = 16 %"),
            new("ieps_pct", false, "8 = 8 %"), new("clave_sat", false, "Clave de producto o servicio SAT"),
            new("codigo_barras", false, ""), new("stock_minimo", false, "En unidad base"), new("descripcion", false, ""),
            new("nombre_corto", false, "Máximo 15 caracteres (T-800, HNA…); vacío = SKU"),
            new("producto_a_cobro", false, "sí = la devolución fuera de tolerancia se cobra al chofer"),
        ],
        "clientes" =>
        [
            new("codigo", false, "Si existe, se actualiza; vacío = se asigna"), new("nombre", true, ""),
            new("canal", true, "AUTO, DET, INST o CEDIS (TRAD = DET)"), new("lista_precio", false, "Nombre; vacío = la del canal"),
            new("condicion_pago", false, "Contado o Credito"), new("dias_credito", false, ""), new("limite_credito", false, ""),
            new("requiere_factura", false, "sí/no"), new("rfc", false, ""), new("razon_social", false, ""),
            new("regimen_fiscal", false, "Clave SAT (601, 612…)"), new("cp_fiscal", false, ""), new("uso_cfdi", false, "G03…"),
            new("email_facturacion", false, ""), new("telefono", false, ""), new("calle", false, ""), new("colonia", false, ""),
            new("municipio", false, ""), new("estado", false, ""), new("codigo_postal", false, ""), new("latitud", false, ""),
            new("longitud", false, ""),
            new("grupo", false, "Clave del grupo (SANBORNS…); sin lista_precio hereda la del grupo"),
        ],
        "grupos_clientes" =>
        [
            new("clave", true, "Si existe, se actualiza"), new("nombre", true, ""), new("canal", true, "AUTO, DET, INST o CEDIS"),
            new("lista_precio", false, "Nombre; la heredan los clientes del grupo"), new("activo", false, "sí/no; vacío = sí"),
        ],
        "rutas" =>
        [
            new("codigo", true, "R51, R1…; si existe, se actualiza"), new("nombre", true, ""), new("canal", true, "AUTO, DET, INST o CEDIS"),
            new("chofer", false, "Usuario con rol Vendedor (chofer predeterminado)"), new("vehiculo", false, "Placas"),
            new("activo", false, "sí/no; vacío = sí"),
        ],
        "rutas_clientes" =>
        [
            new("ruta", true, "Código de la ruta; la ruta queda solo con los clientes del archivo (el genérico se queda)"),
            new("cliente", true, "Código del cliente (del mismo canal que la ruta)"), new("orden", true, "Orden de visita"),
            new("dias", false, "L,M,X,J,V,S,D o 1 a 7; vacío = lunes a sábado"),
        ],
        "precios" =>
        [
            new("lista", false, "Nombre de la lista (o cliente, no ambos)"), new("cliente", false, "Código del cliente: precio especial"),
            new("sku", true, ""), new("unidad", false, "Clave de unidad de venta; vacío = unidad base"),
            new("precio", true, "Sin impuestos"), new("vigente_desde", false, "Solo cliente; AAAA-MM-DD, vacío = hoy"),
        ],
        "proveedores" =>
        [
            new("codigo", true, "Si existe, se actualiza"), new("razon_social", true, ""), new("nombre_comercial", false, ""),
            new("rfc", false, ""), new("contacto", false, ""), new("telefono", false, ""), new("email", false, ""),
            new("dias_credito", false, ""),
        ],
        "recetas" =>
        [
            new("producto_sku", true, "Producto que se obtiene"), new("nombre", true, "Nombre de la receta"),
            new("rendimiento", true, "En unidad base del producto"), new("merma_esperada_pct", false, "3 = 3 %"),
            new("componente_sku", true, "Una fila por componente"), new("componente_unidad", false, "Vacío = unidad base"),
            new("componente_cantidad", true, ""), new("publicar", false, "sí = queda Vigente"), new("notas", false, ""),
        ],
        "existencias" =>
        [
            new("almacen", true, "Código (MP, PT…)"), new("sku", true, ""), new("lote", false, "Obligatorio si maneja lote"),
            new("caducidad", false, "AAAA-MM-DD"), new("cantidad", true, "En unidad base"), new("costo_unitario", true, "Sin IVA, por unidad base"),
        ],
        _ => [],
    };

    private static readonly Dictionary<string, string[][]> Ejemplos = new()
    {
        ["productos"] =
        [
            ["MP-MAIZ-BCO", "Maíz blanco", "MateriaPrima", "KG", "TON=1000:compra", "", "", "", "", "", "0", "", "", "", "2000", "", "", ""],
            ["PT-TORT-KG", "Tortilla de maíz", "Terminado", "KG", "PAQ=1:venta*|CAJ=24:traslado", "", "", "", "sí", "2", "0", "", "01010101", "", "100", "", "TORT", "sí"],
        ],
        ["clientes"] =
        [
            ["LUPITA", "Abarrotes Lupita", "DET", "", "Contado", "0", "", "no", "", "", "", "", "", "", "2291234567", "Av. Díaz Mirón 123",
                "Centro", "Veracruz", "Veracruz", "91700", "19.1800", "-96.1400", ""],
            ["SANB-CENTRO", "Sanborns Centro", "AUTO", "", "Credito", "30", "", "no", "", "", "", "", "", "", "", "Independencia 1050",
                "Centro", "Veracruz", "Veracruz", "91700", "", "", "SANBORNS"],
        ],
        ["grupos_clientes"] = [["SANBORNS", "Sanborns", "AUTO", "Lista Autoservicio", "sí"]],
        ["rutas"] = [["R90", "Ruta 90 Centro", "DET", "vendedor3", "XC-9012-C", "sí"]],
        ["rutas_clientes"] = [["R90", "LUPITA", "1", "L,X,V"]],
        ["precios"] =
        [
            ["Lista Detalle", "", "PT-TOSTADA", "", "18.50", ""],
            ["", "LUPITA", "PT-TOSTADA", "", "17.00", ""],
        ],
        ["proveedores"] = [["GRANOS", "Granos del Golfo S.A. de C.V.", "Granos del Golfo", "GGO010101AB1", "Juan Pérez", "2291112233", "ventas@granos.example", "15"]],
        ["recetas"] =
        [
            ["SEM-MASA", "Masa de maíz", "160", "2", "MP-MAIZ-BCO", "KG", "100", "sí", ""],
            ["SEM-MASA", "Masa de maíz", "160", "2", "MP-CAL", "KG", "1", "sí", ""],
        ],
        ["existencias"] =
        [
            ["MP", "MP-MAIZ-BCO", "", "", "5000", "6.50"],
            ["PT", "PT-TORT-KG", "261103-OP000001", "2026-11-05", "120", "9.80"],
        ],
    };

    public static string Csv(string entidad)
    {
        var columnas = Columnas(entidad);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', columnas.Select(c => c.Nombre)));
        foreach (var fila in Ejemplos.GetValueOrDefault(entidad) ?? [])
        {
            sb.AppendLine(string.Join(',', fila.Select(v => v.Contains(',', StringComparison.Ordinal) || v.Contains('"', StringComparison.Ordinal)
                ? $"\"{v.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
                : v)));
        }

        return sb.ToString();
    }
}

public static class ImportacionEndpoints
{
    private static readonly Dictionary<string, string> PermisoPorEntidad = new()
    {
        ["productos"] = Permisos.Catalogos.Editar,
        ["clientes"] = Permisos.Ventas.Clientes,
        ["proveedores"] = Permisos.Compras.Editar,
        ["recetas"] = Permisos.Produccion.Recetas,
        ["existencias"] = Permisos.Inventario.Ajustar,
        ["grupos_clientes"] = Permisos.Ventas.Configurar,
        ["rutas"] = Permisos.Logistica.Configurar,
        ["rutas_clientes"] = Permisos.Logistica.Configurar,
        ["precios"] = Permisos.Ventas.Configurar,
    };

    public static IEndpointRouteBuilder MapImportacionEndpoints(this IEndpointRouteBuilder api)
    {
        var grupo = api.MapGroup("/importacion").WithTags("Carga inicial");

        foreach (var (entidad, permiso) in PermisoPorEntidad)
        {
            grupo.MapPost($"/{entidad}", ([FromForm] IFormFile? archivo, bool? validarSolo, ImportadorCsv importador, CancellationToken ct) =>
                    importador.EjecutarAsync(entidad, archivo, validarSolo ?? false, ct))
                .RequireAuthorization(permiso)
                .DisableAntiforgery()
                .Accepts<IFormFile>("multipart/form-data")
                .WithName($"importar{string.Concat(entidad.Split('_').Select(p => char.ToUpperInvariant(p[0]) + p[1..]))}");
        }

        grupo.MapGet("/{entidad}/plantilla", Results<FileContentHttpResult, NotFound> (string entidad) =>
                PlantillasImportacion.Entidades.Contains(entidad)
                    ? TypedResults.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(PlantillasImportacion.Csv(entidad))).ToArray(),
                        "text/csv; charset=utf-8", $"plantilla-{entidad}.csv")
                    : TypedResults.NotFound())
            .RequireAuthorization()
            .WithName("descargarPlantillaImportacion");

        grupo.MapGet("/{entidad}/columnas", Results<Ok<IReadOnlyList<ColumnaPlantilla>>, NotFound> (string entidad) =>
                PlantillasImportacion.Entidades.Contains(entidad)
                    ? TypedResults.Ok(PlantillasImportacion.Columnas(entidad))
                    : TypedResults.NotFound())
            .RequireAuthorization()
            .WithName("columnasImportacion");
        return api;
    }
}
