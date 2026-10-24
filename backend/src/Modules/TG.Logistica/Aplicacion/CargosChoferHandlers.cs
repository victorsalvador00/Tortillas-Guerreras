using System.Globalization;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Inventario.Publico;
using TG.Logistica.Dominio;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.Seguridad.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Logistica.Aplicacion;

public sealed record CargoChoferDto(
    Guid Id,
    string Folio,
    Guid ChoferId,
    string? Chofer,
    TipoCargoChofer Tipo,
    Guid? LiquidacionId,
    string? LiquidacionFolio,
    Guid? ProductoId,
    string? Sku,
    string? Producto,
    decimal? CantidadBase,
    decimal? PrecioUnitario,
    decimal Importe,
    DateOnly FechaOperativa,
    EstadoCargoChofer Estado,
    Guid? CorteId,
    string? Notas,
    DateTimeOffset? CanceladoEn,
    string? MotivoCancelacion);

public sealed record CrearCargoChoferRequest(TipoCargoChofer Tipo, decimal Importe, Guid? LiquidacionId, Guid? ChoferId,
    DateOnly? FechaOperativa, Guid? ProductoId, decimal? CantidadBase, string? Notas);

public sealed class CrearCargoChoferValidator : AbstractValidator<CrearCargoChoferRequest>
{
    public CrearCargoChoferValidator()
    {
        RuleFor(x => x.Tipo).Must(t => t is TipoCargoChofer.DiferenciaPrecio or TipoCargoChofer.Otro)
            .WithMessage("A mano solo se capturan cargos de diferencia de precio u otro.");
        RuleFor(x => x.Importe).NotEqual(0).WithMessage("El importe no puede ser cero.");
        RuleFor(x => x.Importe).GreaterThan(0).When(x => x.Tipo == TipoCargoChofer.DiferenciaPrecio)
            .WithMessage("La diferencia de precio se carga con importe positivo.");
        RuleFor(x => x.Notas).NotEmpty().When(x => x.Tipo == TipoCargoChofer.Otro).WithMessage("Un cargo «Otro» requiere nota.");
        RuleFor(x => x.Notas).MaximumLength(500);
        RuleFor(x => x).Must(x => x.LiquidacionId is not null || x.ChoferId is not null)
            .WithName("choferId").WithMessage("Indica el chofer o la liquidación.");
    }
}

public sealed record CancelarCargoChoferRequest(string Motivo);

public sealed record CargoTipoTotalDto(TipoCargoChofer Tipo, decimal Importe);

public sealed record CorteResumenDto(
    Guid ChoferId,
    string? Chofer,
    DateOnly Desde,
    DateOnly Hasta,
    Guid? CorteId,
    string? Folio,
    EstadoCorteChofer? Estado,
    decimal TotalVenta,
    IReadOnlyList<CargoTipoTotalDto> Cargos,
    decimal TotalCargos,
    int LiquidacionesAbiertas);

public sealed record VentaCorteDto(DateOnly Fecha, Guid ProductoId, string Sku, string Producto, string Unidad, decimal CantidadBase, decimal Importe);

public sealed record CorteDetalleDto(
    Guid Id,
    string Folio,
    Guid ChoferId,
    string? Chofer,
    DateOnly Desde,
    DateOnly Hasta,
    EstadoCorteChofer Estado,
    decimal TotalVenta,
    decimal TotalCargos,
    int LiquidacionesAbiertas,
    DateTimeOffset? CerradoEn,
    string? CerradoPor,
    IReadOnlyList<VentaCorteDto> Ventas,
    IReadOnlyList<CargoTipoTotalDto> CargosPorTipo,
    IReadOnlyList<CargoChoferDto> Cargos);

public sealed record AbrirCorteRequest(Guid ChoferId, DateOnly Fecha);

public sealed record ArchivoCorte(byte[] Contenido, string TipoContenido, string Nombre);

/// <summary>Alta de cargos automáticos (RN-LOG-26) y su valuación [SUP-20]; lo usa la liquidación dentro de su transacción.</summary>
public sealed class ServicioCargosChofer(
    LogisticaDbContext db,
    ICatalogosService catalogos,
    IInventarioService inventario,
    IVentasService ventas,
    IGeneradorFolios folios)
{
    /// <summary>[SUP-20] <c>ListaCanal</c>: precio sin impuestos del genérico de la ruta (lista del canal); sin precio o <c>Costo</c>: costo promedio.</summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> PreciosAsync(Ruta ruta, IReadOnlyCollection<Guid> productoIds, DateOnly fecha,
        CancellationToken ct)
    {
        var precios = new Dictionary<Guid, decimal>();
        if (productoIds.Count == 0)
        {
            return precios;
        }

        var modo = await catalogos.ObtenerParametroAsync(ClavesParametro.PrecioProductoACobro, "ListaCanal", ct);
        if (string.Equals(modo, "ListaCanal", StringComparison.OrdinalIgnoreCase)
            && await ventas.ClienteGenericoDeRutaAsync(ruta.Codigo, ct) is { } generico)
        {
            foreach (var (productoId, precio) in await ventas.PreciosBaseClienteAsync(generico.Id, productoIds, fecha, ct))
            {
                precios[productoId] = precio;
            }
        }

        var faltan = productoIds.Where(p => !precios.ContainsKey(p)).ToList();
        if (faltan.Count > 0)
        {
            foreach (var (productoId, costo) in await inventario.ObtenerCostosPromedioAsync(faltan, ct))
            {
                precios[productoId] = costo;
            }
        }

        return precios;
    }

    public async Task<CargoChofer> AgregarAsync(Guid choferId, TipoCargoChofer tipo, Guid? liquidacionId, DateOnly fecha, decimal importe,
        Guid? productoId, decimal? cantidad, decimal? precio, string? notas, CancellationToken ct)
    {
        var cargo = new CargoChofer
        {
            Folio = await folios.SiguienteAsync(LogisticaDbContext.Esquema, "CCH", ct),
            ChoferId = choferId,
            Tipo = tipo,
            LiquidacionId = liquidacionId,
            FechaOperativa = fecha,
            Importe = Dinero.Redondear(importe),
            ProductoId = productoId,
            CantidadBase = cantidad,
            PrecioUnitario = precio,
            Notas = notas,
        };
        db.CargosChofer.Add(cargo);
        return cargo;
    }
}

/// <summary>Cuenta de choferes: consulta, alta manual (<c>DiferenciaPrecio</c>/<c>Otro</c>) y cancelación (RN-LOG-26/27).</summary>
public sealed class CargosChoferHandler(
    LogisticaDbContext db,
    ServicioCargosChofer servicio,
    ICatalogosService catalogos,
    ISeguridadService seguridad,
    IFechaOperativa fechaOperativa,
    IUsuarioActual usuario,
    TimeProvider reloj)
{
    public async Task<IReadOnlyList<CargoChoferDto>> ListarAsync(Guid? choferId, EstadoCargoChofer? estado, DateOnly? desde, DateOnly? hasta,
        Guid? liquidacionId, Guid? corteId, CancellationToken ct)
    {
        var consulta = db.CargosChofer.AsNoTracking();
        if (choferId is not null)
        {
            consulta = consulta.Where(c => c.ChoferId == choferId);
        }

        if (estado is not null)
        {
            consulta = consulta.Where(c => c.Estado == estado);
        }

        if (desde is not null)
        {
            consulta = consulta.Where(c => c.FechaOperativa >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(c => c.FechaOperativa <= hasta);
        }

        if (liquidacionId is not null)
        {
            consulta = consulta.Where(c => c.LiquidacionId == liquidacionId);
        }

        if (corteId is not null)
        {
            consulta = consulta.Where(c => c.CorteId == corteId);
        }

        var cargos = await consulta.OrderByDescending(c => c.FechaOperativa).ThenByDescending(c => c.Folio).Take(1000).ToListAsync(ct);
        return await ADtosAsync(db, catalogos, seguridad, cargos, ct);
    }

    public async Task<Result<CargoChoferDto>> CrearAsync(CrearCargoChoferRequest r, CancellationToken ct)
    {
        var choferId = r.ChoferId;
        var fecha = r.FechaOperativa ?? fechaOperativa.Hoy();
        if (r.LiquidacionId is { } liquidacionId)
        {
            var liquidacion = await db.Liquidaciones.AsNoTracking().FirstOrDefaultAsync(l => l.Id == liquidacionId, ct);
            if (liquidacion is null)
            {
                return Error.NoEncontrado(ErroresLogistica.LiquidacionNoEncontrada, "La liquidación no existe.");
            }

            if (liquidacion.ChoferId is null)
            {
                return Error.Validacion(ErroresLogistica.ChoferInvalido, "La liquidación no tiene chofer.");
            }

            choferId = liquidacion.ChoferId;
            fecha = liquidacion.FechaOperativa;
        }

        var cargo = await servicio.AgregarAsync(choferId!.Value, r.Tipo, r.LiquidacionId, fecha, r.Importe, r.ProductoId, r.CantidadBase,
            null, string.IsNullOrWhiteSpace(r.Notas) ? null : r.Notas.Trim(), ct);
        await db.SaveChangesAsync(ct);
        return (await ADtosAsync(db, catalogos, seguridad, [cargo], ct))[0];
    }

    /// <summary>RN-LOG-27: solo un cargo <c>Pendiente</c>; uno aplicado se compensa con un cargo «Otro» negativo.</summary>
    public async Task<Result<CargoChoferDto>> CancelarAsync(Guid id, CancelarCargoChoferRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Motivo))
        {
            return Error.Campo("VAL-001", "motivo", "Escribe el motivo de la cancelación.");
        }

        var cargo = await db.CargosChofer.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cargo is null)
        {
            return Error.NoEncontrado(ErroresLogistica.CargoNoEncontrado, "El cargo no existe.");
        }

        if (cargo.Estado != EstadoCargoChofer.Pendiente)
        {
            return Error.Conflicto(ErroresLogistica.CargoAplicado,
                cargo.Estado == EstadoCargoChofer.Aplicado
                    ? $"El cargo {cargo.Folio} ya se aplicó en un corte; compénsalo con un cargo «Otro» negativo."
                    : $"El cargo {cargo.Folio} ya está cancelado.");
        }

        cargo.Estado = EstadoCargoChofer.Cancelado;
        cargo.MotivoCancelacion = r.Motivo.Trim();
        cargo.CanceladoEn = reloj.GetUtcNow();
        cargo.CanceladoPor = usuario.Id;
        await db.SaveChangesAsync(ct);
        return (await ADtosAsync(db, catalogos, seguridad, [cargo], ct))[0];
    }

    internal static async Task<List<CargoChoferDto>> ADtosAsync(LogisticaDbContext db, ICatalogosService catalogos, ISeguridadService seguridad,
        IReadOnlyList<CargoChofer> cargos, CancellationToken ct)
    {
        var productos = await catalogos.ObtenerProductosAsync(cargos.Where(c => c.ProductoId != null).Select(c => c.ProductoId!.Value), ct);
        var choferes = await seguridad.NombresUsuariosAsync(cargos.Select(c => c.ChoferId).Distinct(), ct);
        var liquidacionIds = cargos.Where(c => c.LiquidacionId != null).Select(c => c.LiquidacionId!.Value).Distinct().ToList();
        var folios = await db.Liquidaciones.AsNoTracking().Where(l => liquidacionIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Folio, ct);
        return cargos.Select(c =>
        {
            var producto = c.ProductoId is { } p ? productos.GetValueOrDefault(p) : null;
            return new CargoChoferDto(c.Id, c.Folio, c.ChoferId, choferes.GetValueOrDefault(c.ChoferId), c.Tipo, c.LiquidacionId,
                c.LiquidacionId is { } l ? folios.GetValueOrDefault(l) : null, c.ProductoId, producto?.Sku, producto?.Nombre, c.CantidadBase,
                c.PrecioUnitario, c.Importe, c.FechaOperativa, c.Estado, c.CorteId, c.Notas, c.CanceladoEn, c.MotivoCancelacion);
        }).ToList();
    }
}

/// <summary>Corte semanal por chofer (RN-LOG-28 a 30).</summary>
public sealed class CortesChoferHandler(
    LogisticaDbContext db,
    ICatalogosService catalogos,
    IVentasService ventas,
    ISeguridadService seguridad,
    IGeneradorFolios folios,
    IUsuarioActual usuario,
    TimeProvider reloj)
{
    private async Task<(DateOnly Desde, DateOnly Hasta)> SemanaAsync(DateOnly fecha, CancellationToken ct)
    {
        var dia = await catalogos.ObtenerParametroAsync(ClavesParametro.DiaInicioSemanaCorte, 1, ct);
        var desde = ReglasLogistica.InicioSemana(fecha, dia);
        return (desde, desde.AddDays(6));
    }

    /// <summary>
    /// Resumen de la semana: un renglón por chofer con ventas, cargos o corte. Corte abierto (o sin corte): cargos
    /// <c>Pendiente</c> sin corte con fecha ≤ hasta (incluye atrasados, RN-LOG-29); cerrado: lo aplicado.
    /// </summary>
    public async Task<IReadOnlyList<CorteResumenDto>> ResumenAsync(DateOnly fecha, Guid? choferId, CancellationToken ct)
    {
        var (desde, hasta) = await SemanaAsync(fecha, ct);
        var cortes = await db.CortesChofer.AsNoTracking().Where(c => c.Desde == desde && (choferId == null || c.ChoferId == choferId))
            .ToListAsync(ct);
        var vendido = await ventas.VentasDeVendedoresAsync(desde, hasta, choferId is { } ch ? [ch] : null, ct);
        var cerrados = cortes.Where(c => c.Estado == EstadoCorteChofer.Cerrado).Select(c => c.Id).ToList();
        var cargos = await db.CargosChofer.AsNoTracking()
            .Where(c => (c.Estado == EstadoCargoChofer.Pendiente && c.CorteId == null && c.FechaOperativa <= hasta)
                || (c.CorteId != null && cerrados.Contains(c.CorteId.Value)))
            .Where(c => choferId == null || c.ChoferId == choferId)
            .ToListAsync(ct);
        var abiertas = await db.Liquidaciones.AsNoTracking()
            .Where(l => l.ChoferId != null && l.FechaOperativa >= desde && l.FechaOperativa <= hasta && l.Estado != EstadoLiquidacion.Cerrada)
            .GroupBy(l => l.ChoferId!.Value).Select(g => new { ChoferId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.ChoferId, x => x.Total, ct);

        var choferes = cortes.Select(c => c.ChoferId).Concat(vendido.Select(v => v.VendedorId)).Concat(cargos.Select(c => c.ChoferId))
            .Where(c => c != Guid.Empty).Distinct().ToList();
        var nombres = await seguridad.NombresUsuariosAsync(choferes, ct);
        return choferes.Select(id =>
        {
            var corte = cortes.FirstOrDefault(c => c.ChoferId == id);
            var cerrado = corte is { Estado: EstadoCorteChofer.Cerrado };
            var propios = cargos.Where(c => c.ChoferId == id && (cerrado ? c.CorteId == corte!.Id : c.CorteId == null)).ToList();
            var venta = cerrado ? corte!.TotalVenta : vendido.Where(v => v.VendedorId == id).Sum(v => v.Importe);
            return new CorteResumenDto(id, nombres.GetValueOrDefault(id), desde, hasta, corte?.Id, corte?.Folio, corte?.Estado, venta,
                PorTipo(propios), propios.Sum(c => c.Importe), abiertas.GetValueOrDefault(id));
        }).OrderBy(r => r.Chofer).ToList();
    }

    /// <summary>Obtiene o crea el corte <c>Abierto</c> del chofer para la semana de la fecha.</summary>
    public async Task<Result<CorteDetalleDto>> AbrirAsync(AbrirCorteRequest r, CancellationToken ct)
    {
        if (r.ChoferId == Guid.Empty)
        {
            return Error.Campo("VAL-001", "choferId", "Indica el chofer.");
        }

        var (desde, hasta) = await SemanaAsync(r.Fecha, ct);
        var corte = await db.CortesChofer.FirstOrDefaultAsync(c => c.ChoferId == r.ChoferId && c.Desde == desde, ct);
        if (corte is null)
        {
            corte = new CorteChofer
            {
                Folio = await folios.SiguienteAsync(LogisticaDbContext.Esquema, "CSC", ct),
                ChoferId = r.ChoferId,
                Desde = desde,
                Hasta = hasta,
            };
            db.CortesChofer.Add(corte);
            await db.SaveChangesAsync(ct);
        }

        return await ObtenerAsync(corte.Id, ct);
    }

    public async Task<Result<CorteDetalleDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var corte = await db.CortesChofer.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (corte is null)
        {
            return Error.NoEncontrado(ErroresLogistica.CorteNoEncontrado, "El corte no existe.");
        }

        var cerrado = corte.Estado == EstadoCorteChofer.Cerrado;
        var cargos = await CargosDelCorte(corte).ToListAsync(ct);
        var vendido = await ventas.VentasDeVendedoresAsync(corte.Desde, corte.Hasta, [corte.ChoferId], ct);
        var productos = await catalogos.ObtenerProductosAsync(vendido.Select(v => v.ProductoId), ct);
        var abiertas = await LiquidacionesAbiertasAsync(corte, ct);
        var nombres = await seguridad.NombresUsuariosAsync(new[] { corte.ChoferId, corte.CerradoPor ?? Guid.Empty }.Where(x => x != Guid.Empty), ct);
        var dtos = await CargosChoferHandler.ADtosAsync(db, catalogos, seguridad, cargos, ct);
        return new CorteDetalleDto(corte.Id, corte.Folio, corte.ChoferId, nombres.GetValueOrDefault(corte.ChoferId), corte.Desde, corte.Hasta,
            corte.Estado, cerrado ? corte.TotalVenta : vendido.Sum(v => v.Importe), cerrado ? corte.TotalCargos : cargos.Sum(c => c.Importe),
            abiertas, corte.CerradoEn, corte.CerradoPor is { } u ? nombres.GetValueOrDefault(u) : null,
            vendido.Select(v =>
            {
                var producto = productos.GetValueOrDefault(v.ProductoId);
                return new VentaCorteDto(v.Fecha, v.ProductoId, producto?.Sku ?? "?", producto?.Nombre ?? "?", producto?.UnidadBaseClave ?? string.Empty,
                    v.CantidadBase, v.Importe);
            }).OrderBy(v => v.Fecha).ThenBy(v => v.Sku).ToList(),
            PorTipo(cargos), dtos.OrderBy(c => c.FechaOperativa).ThenBy(c => c.Folio).ToList());
    }

    /// <summary>RN-LOG-28: aplica los cargos pendientes (≤ hasta, incluidos atrasados) si las liquidaciones del periodo están cerradas.</summary>
    public async Task<Result<CorteDetalleDto>> CerrarAsync(Guid id, CancellationToken ct)
    {
        var corte = await db.CortesChofer.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (corte is null)
        {
            return Error.NoEncontrado(ErroresLogistica.CorteNoEncontrado, "El corte no existe.");
        }

        if (corte.Estado == EstadoCorteChofer.Cerrado)
        {
            return Error.Conflicto(ErroresLogistica.TransicionInvalida, $"El corte {corte.Folio} ya está cerrado.");
        }

        var abiertas = await LiquidacionesAbiertasAsync(corte, ct);
        if (abiertas > 0)
        {
            return Error.Conflicto(ErroresLogistica.CorteConLiquidacionesAbiertas,
                $"El chofer tiene {abiertas} liquidación(es) sin cerrar en la semana; ciérralas antes del corte.");
        }

        var cargos = await CargosDelCorte(corte).ToListAsync(ct);
        foreach (var cargo in cargos)
        {
            cargo.Estado = EstadoCargoChofer.Aplicado;
            cargo.CorteId = corte.Id;
        }

        corte.TotalCargos = cargos.Sum(c => c.Importe);
        corte.TotalVenta = (await ventas.VentasDeVendedoresAsync(corte.Desde, corte.Hasta, [corte.ChoferId], ct)).Sum(v => v.Importe);
        corte.Estado = EstadoCorteChofer.Cerrado;
        corte.CerradoEn = reloj.GetUtcNow();
        corte.CerradoPor = usuario.Id;
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>RN-LOG-30: CSV con ventas por día y producto, cargos y total a descontar.</summary>
    public async Task<Result<ArchivoCorte>> ExportarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await ObtenerAsync(id, ct);
        if (!resultado.EsExito)
        {
            return resultado.Error!;
        }

        var c = resultado.Valor;
        var csv = new StringBuilder();
        static string Campo(string? valor) =>
            valor is null ? string.Empty : valor.Contains(',') || valor.Contains('"') ? $"\"{valor.Replace("\"", "\"\"")}\"" : valor;
        static string Numero(decimal? valor) => valor?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
        csv.AppendLine($"Corte,{Campo(c.Folio)},Chofer,{Campo(c.Chofer)},Desde,{c.Desde:yyyy-MM-dd},Hasta,{c.Hasta:yyyy-MM-dd},Estado,{c.Estado}");
        csv.AppendLine("Seccion,Fecha,Folio,Tipo,Sku,Producto,Cantidad,Importe,Notas");
        foreach (var v in c.Ventas)
        {
            csv.AppendLine($"Venta,{v.Fecha:yyyy-MM-dd},,,{Campo(v.Sku)},{Campo(v.Producto)},{Numero(v.CantidadBase)},{Numero(v.Importe)},");
        }

        foreach (var g in c.Cargos)
        {
            csv.AppendLine($"Cargo,{g.FechaOperativa:yyyy-MM-dd},{Campo(g.Folio)},{g.Tipo},{Campo(g.Sku)},{Campo(g.Producto)},{Numero(g.CantidadBase)},{Numero(g.Importe)},{Campo(g.Notas)}");
        }

        csv.AppendLine($"Total venta,,,,,,,{Numero(c.TotalVenta)},");
        csv.AppendLine($"Total a descontar,,,,,,,{Numero(c.TotalCargos)},");
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return new ArchivoCorte(bytes, "text/csv", $"{c.Folio}.csv");
    }

    private IQueryable<CargoChofer> CargosDelCorte(CorteChofer corte) =>
        corte.Estado == EstadoCorteChofer.Cerrado
            ? db.CargosChofer.Where(c => c.CorteId == corte.Id)
            : db.CargosChofer.Where(c => c.ChoferId == corte.ChoferId && c.Estado == EstadoCargoChofer.Pendiente && c.CorteId == null
                && c.FechaOperativa <= corte.Hasta);

    private Task<int> LiquidacionesAbiertasAsync(CorteChofer corte, CancellationToken ct) =>
        db.Liquidaciones.CountAsync(l => l.ChoferId == corte.ChoferId && l.FechaOperativa >= corte.Desde && l.FechaOperativa <= corte.Hasta
            && l.Estado != EstadoLiquidacion.Cerrada, ct);

    private static List<CargoTipoTotalDto> PorTipo(IEnumerable<CargoChofer> cargos) =>
        cargos.GroupBy(c => c.Tipo).OrderBy(g => g.Key).Select(g => new CargoTipoTotalDto(g.Key, g.Sum(c => c.Importe))).ToList();
}
