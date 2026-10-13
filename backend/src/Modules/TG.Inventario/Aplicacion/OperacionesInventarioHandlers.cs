using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Inventario.Dominio;
using TG.Inventario.Infraestructura;
using TG.Inventario.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;

namespace TG.Inventario.Aplicacion;

public sealed record ResultadoDocumentoDto(Guid DocumentoId, string Folio, IReadOnlyList<MovimientoDto> Movimientos);

// ───────────────────────────── Ajustes ─────────────────────────────

public sealed record PartidaAjusteRequest(Guid ProductoId, string? Lote, decimal Cantidad, decimal? CostoUnitario,
    DateOnly? FechaCaducidad);

/// <param name="EsMerma">Las salidas se registran como <c>Merma</c> en lugar de <c>AjusteSalida</c>.</param>
public sealed record RegistrarAjusteRequest(Guid AlmacenId, Guid MotivoMermaId, string Notas, bool EsMerma,
    IReadOnlyList<PartidaAjusteRequest> Partidas);

public sealed class RegistrarAjusteValidator : AbstractValidator<RegistrarAjusteRequest>
{
    public RegistrarAjusteValidator()
    {
        RuleFor(x => x.AlmacenId).NotEmpty();
        // RN-INV-08: los ajustes manuales requieren motivo y nota.
        RuleFor(x => x.MotivoMermaId).NotEmpty().WithMessage("Elige el motivo del ajuste.");
        RuleFor(x => x.Notas).NotEmpty().WithMessage("Escribe una nota que explique el ajuste.");
        RuleFor(x => x.Partidas).NotEmpty().WithMessage("Agrega al menos una partida.");
        RuleForEach(x => x.Partidas).ChildRules(p =>
        {
            p.RuleFor(x => x.ProductoId).NotEmpty();
            p.RuleFor(x => x.Cantidad).NotEqual(0).WithMessage("La cantidad no puede ser cero.");
            p.RuleFor(x => x.CostoUnitario).GreaterThanOrEqualTo(0).When(x => x.CostoUnitario is not null);
        });
    }
}

public sealed record AjusteResumenDto(Guid DocumentoId, string Folio, DateOnly FechaOperativa, DateTimeOffset FechaHora,
    string AlmacenCodigo, int Partidas, decimal CostoTotal, string? Notas, Guid? MotivoMermaId);

public sealed class AjustesHandler(
    InventarioDbContext db,
    IInventarioService inventario,
    ICatalogosService catalogos,
    IGeneradorFolios folios,
    IFechaOperativa fechaOperativa,
    IUsuarioActual usuario,
    IUnitOfWork unitOfWork,
    ConsultasInventarioHandler consultas)
{
    public async Task<Result<ResultadoDocumentoDto>> RegistrarAsync(RegistrarAjusteRequest r, CancellationToken ct)
    {
        var motivo = await catalogos.ObtenerMotivoAsync(r.MotivoMermaId, ct);
        if (motivo is null || !motivo.Activo || !motivo.AplicaAlmacen)
        {
            return Error.Campo("INV-008", "motivoMermaId", "El motivo no existe o no aplica a ajustes de almacén.");
        }

        var documentoId = SharedKernel.Dominio.Id.Nuevo();
        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var folio = await folios.SiguienteAsync(InventarioDbContext.Esquema, "AJ", c);
            var lineas = r.Partidas.Select(p => new LineaMovimiento(
                p.Cantidad > 0 ? TipoMovimiento.AjusteEntrada : r.EsMerma ? TipoMovimiento.Merma : TipoMovimiento.AjusteSalida,
                r.AlmacenId, p.ProductoId, p.Lote, p.Cantidad, p.Cantidad > 0 ? p.CostoUnitario : null, motivo.Id,
                p.FechaCaducidad)).ToList();
            var movimientos = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.Ajuste, documentoId, folio, fechaOperativa.Hoy(), usuario.Id ?? Guid.Empty, lineas,
                Notas: r.Notas.Trim()), c);
            return movimientos.EsExito ? Result<string>.Exito(folio) : Result<string>.Falla(movimientos.Error!);
        }, ct);
        if (!resultado.EsExito)
        {
            return resultado.Error!;
        }

        return await DocumentoAsync(DocumentoInventario.Ajuste, documentoId, resultado.Valor, ct);
    }

    public async Task<IReadOnlyList<AjusteResumenDto>> ListarAsync(DateOnly? desde, DateOnly? hasta, Guid? almacenId,
        CancellationToken ct)
    {
        var consulta = db.Movimientos.AsNoTracking().Where(m => m.DocumentoTipo == DocumentoInventario.Ajuste);
        if (desde is not null)
        {
            consulta = consulta.Where(m => m.FechaOperativa >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(m => m.FechaOperativa <= hasta);
        }

        if (almacenId is not null)
        {
            consulta = consulta.Where(m => m.AlmacenId == almacenId);
        }

        var almacenes = await db.Almacenes.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Codigo, ct);
        var movimientos = await consulta.OrderByDescending(m => m.FechaHora).Take(2000).ToListAsync(ct);
        return movimientos.GroupBy(m => m.DocumentoId)
            .Select(g =>
            {
                var primero = g.First();
                return new AjusteResumenDto(g.Key, primero.DocumentoFolio, primero.FechaOperativa, primero.FechaHora,
                    almacenes.GetValueOrDefault(primero.AlmacenId) ?? "?", g.Count(), g.Sum(m => m.CostoTotal),
                    primero.Notas, primero.MotivoMermaId);
            })
            .Take(200)
            .ToList();
    }

    private async Task<Result<ResultadoDocumentoDto>> DocumentoAsync(DocumentoInventario tipo, Guid id, string folio, CancellationToken ct)
    {
        var movimientos = await consultas.KardexAsync(null, null, null, null, tipo, id, 1, 200, ct);
        return new ResultadoDocumentoDto(id, folio, movimientos.Items);
    }
}

// ───────────────────────────── Traspasos ─────────────────────────────

public sealed record PartidaTraspasoRequest(Guid ProductoId, string? Lote, decimal Cantidad);

public sealed record RegistrarTraspasoRequest(Guid AlmacenOrigenId, Guid AlmacenDestinoId, string? Notas,
    IReadOnlyList<PartidaTraspasoRequest> Partidas);

public sealed class RegistrarTraspasoValidator : AbstractValidator<RegistrarTraspasoRequest>
{
    public RegistrarTraspasoValidator()
    {
        RuleFor(x => x.AlmacenOrigenId).NotEmpty();
        RuleFor(x => x.AlmacenDestinoId).NotEmpty().NotEqual(x => x.AlmacenOrigenId)
            .WithMessage("El almacén destino debe ser distinto del origen.");
        RuleFor(x => x.Partidas).NotEmpty().WithMessage("Agrega al menos una partida.");
        RuleForEach(x => x.Partidas).ChildRules(p =>
        {
            p.RuleFor(x => x.ProductoId).NotEmpty();
            p.RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad debe ser mayor que cero.");
        });
    }
}

public sealed record TraspasoPartidaDto(Guid ProductoId, string Sku, string Producto, string Unidad, string Lote, decimal Cantidad);

public sealed record TraspasoDto(Guid Id, string Folio, Guid AlmacenOrigenId, string AlmacenOrigen, Guid AlmacenDestinoId,
    string AlmacenDestino, DateOnly FechaOperativa, EstadoTraspaso Estado, string? Notas, DateTimeOffset CreadoEn,
    IReadOnlyList<TraspasoPartidaDto> Partidas);

/// <summary>Traspasos entre almacenes no-ruta: <c>TraspasoSalida</c> + <c>TraspasoEntrada</c> con el mismo costo (RN-INV-06).</summary>
public sealed class TraspasosHandler(
    InventarioDbContext db,
    IInventarioService inventario,
    ICatalogosService catalogos,
    IGeneradorFolios folios,
    IFechaOperativa fechaOperativa,
    IUsuarioActual usuario,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<TraspasoDto>> RegistrarAsync(RegistrarTraspasoRequest r, CancellationToken ct)
    {
        var almacenes = await db.Almacenes.AsNoTracking()
            .Where(a => a.Id == r.AlmacenOrigenId || a.Id == r.AlmacenDestinoId).ToListAsync(ct);
        if (almacenes.Count != 2)
        {
            return Error.NoEncontrado(ErroresInventario.AlmacenNoEncontrado, "Almacén origen o destino no existe.");
        }

        if (almacenes.Any(a => a.Tipo == TipoAlmacen.Ruta))
        {
            return Error.Validacion("INV-009", "Los traspasos son entre almacenes que no son de ruta; para rutas usa envíos.");
        }

        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var traspaso = new Traspaso
            {
                Folio = await folios.SiguienteAsync(InventarioDbContext.Esquema, "TR", c),
                AlmacenOrigenId = r.AlmacenOrigenId,
                AlmacenDestinoId = r.AlmacenDestinoId,
                FechaOperativa = fechaOperativa.Hoy(),
                Notas = string.IsNullOrWhiteSpace(r.Notas) ? null : r.Notas.Trim(),
            };
            var salida = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.Traspaso, traspaso.Id, traspaso.Folio, traspaso.FechaOperativa, usuario.Id ?? Guid.Empty,
                r.Partidas.Select(p => new LineaMovimiento(TipoMovimiento.TraspasoSalida, r.AlmacenOrigenId, p.ProductoId, p.Lote,
                    -p.Cantidad)).ToList(), Notas: traspaso.Notas), c);
            if (!salida.EsExito)
            {
                return Result<Guid>.Falla(salida.Error!);
            }

            // Las entradas replican los lotes que salieron (FEFO) con el mismo costo.
            var entrada = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.Traspaso, traspaso.Id, traspaso.Folio, traspaso.FechaOperativa, usuario.Id ?? Guid.Empty,
                salida.Valor.Select(m => new LineaMovimiento(TipoMovimiento.TraspasoEntrada, r.AlmacenDestinoId, m.ProductoId,
                    m.Lote, -m.Cantidad, m.CostoUnitario, FechaCaducidad: m.FechaCaducidad)).ToList(), Notas: traspaso.Notas), c);
            if (!entrada.EsExito)
            {
                return Result<Guid>.Falla(entrada.Error!);
            }

            traspaso.Partidas = salida.Valor.Select(m => new TraspasoPartida
            {
                TraspasoId = traspaso.Id, ProductoId = m.ProductoId, Lote = m.Lote, CantidadBase = -m.Cantidad,
            }).ToList();
            db.Traspasos.Add(traspaso);
            await db.SaveChangesAsync(c);
            return Result<Guid>.Exito(traspaso.Id);
        }, ct);

        return resultado.EsExito ? await ObtenerAsync(resultado.Valor, ct) : resultado.Error!;
    }

    public async Task<Result<TraspasoDto>> CancelarAsync(Guid id, CancellationToken ct)
    {
        var traspaso = await db.Traspasos.Include(t => t.Partidas).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (traspaso is null)
        {
            return Error.NoEncontrado("INV-010", "El traspaso no existe.");
        }

        if (traspaso.Estado == EstadoTraspaso.Cancelado)
        {
            return Error.Conflicto("INV-011", "El traspaso ya está cancelado.");
        }

        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            // RN-INV-01: la corrección es el movimiento contrario sobre el mismo documento.
            var lineas = traspaso.Partidas.SelectMany(p => new[]
            {
                new LineaMovimiento(TipoMovimiento.TraspasoSalida, traspaso.AlmacenDestinoId, p.ProductoId, p.Lote, -p.CantidadBase),
                new LineaMovimiento(TipoMovimiento.TraspasoEntrada, traspaso.AlmacenOrigenId, p.ProductoId, p.Lote, p.CantidadBase),
            }).ToList();
            var movimientos = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.Traspaso, traspaso.Id, traspaso.Folio, fechaOperativa.Hoy(), usuario.Id ?? Guid.Empty,
                lineas, Notas: "Cancelación de traspaso"), c);
            if (!movimientos.EsExito)
            {
                return Result.Falla(movimientos.Error!);
            }

            traspaso.Estado = EstadoTraspaso.Cancelado;
            await db.SaveChangesAsync(c);
            return Result.Exito();
        }, ct);

        return resultado.EsExito ? await ObtenerAsync(id, ct) : resultado.Error!;
    }

    public async Task<IReadOnlyList<TraspasoDto>> ListarAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var consulta = db.Traspasos.AsNoTracking().Include(t => t.Partidas).AsQueryable();
        if (desde is not null)
        {
            consulta = consulta.Where(t => t.FechaOperativa >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(t => t.FechaOperativa <= hasta);
        }

        var traspasos = await consulta.OrderByDescending(t => t.CreadoEn).Take(200).ToListAsync(ct);
        return await MapearAsync(traspasos, ct);
    }

    public async Task<Result<TraspasoDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var traspaso = await db.Traspasos.AsNoTracking().Include(t => t.Partidas).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (traspaso is null)
        {
            return Error.NoEncontrado("INV-010", "El traspaso no existe.");
        }

        return (await MapearAsync([traspaso], ct))[0];
    }

    private async Task<IReadOnlyList<TraspasoDto>> MapearAsync(List<Traspaso> traspasos, CancellationToken ct)
    {
        var almacenes = await db.Almacenes.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Codigo, ct);
        var productos = await catalogos.ObtenerProductosAsync(traspasos.SelectMany(t => t.Partidas).Select(p => p.ProductoId), ct);
        return traspasos.Select(t => new TraspasoDto(t.Id, t.Folio, t.AlmacenOrigenId, almacenes.GetValueOrDefault(t.AlmacenOrigenId) ?? "?",
            t.AlmacenDestinoId, almacenes.GetValueOrDefault(t.AlmacenDestinoId) ?? "?", t.FechaOperativa, t.Estado, t.Notas,
            t.CreadoEn,
            t.Partidas.Select(p =>
            {
                var producto = productos.GetValueOrDefault(p.ProductoId);
                return new TraspasoPartidaDto(p.ProductoId, producto?.Sku ?? "?", producto?.Nombre ?? "?",
                    producto?.UnidadBaseClave ?? string.Empty, p.Lote, p.CantidadBase);
            }).ToList())).ToList();
    }
}

// ───────────────────────────── Conteos físicos ─────────────────────────────

public sealed record AbrirConteoRequest(Guid AlmacenId, string? Notas);

public sealed record CapturarConteoRequest(Guid ProductoId, string? Lote, decimal CantidadContada);

public sealed class CapturarConteoValidator : AbstractValidator<CapturarConteoRequest>
{
    public CapturarConteoValidator()
    {
        RuleFor(x => x.ProductoId).NotEmpty();
        RuleFor(x => x.CantidadContada).GreaterThanOrEqualTo(0).WithMessage("La cantidad contada no puede ser negativa.");
    }
}

public sealed record ConteoPartidaDto(Guid Id, Guid ProductoId, string Sku, string Producto, string Unidad, string Lote,
    decimal CantidadSistema, decimal CantidadContada, decimal Diferencia, DateTimeOffset CapturadoEn);

public sealed record ConteoDto(Guid Id, string Folio, Guid AlmacenId, string Almacen, DateOnly FechaOperativa,
    EstadoConteo Estado, string? Notas, DateTimeOffset CreadoEn, DateTimeOffset? CerradoEn,
    IReadOnlyList<ConteoPartidaDto> Partidas);

/// <summary>Conteo físico: abrir, capturar (RN-INV-10) y cerrar con ajustes por diferencia (RN-INV-09).</summary>
public sealed class ConteosHandler(
    InventarioDbContext db,
    IInventarioService inventario,
    ICatalogosService catalogos,
    IGeneradorFolios folios,
    IFechaOperativa fechaOperativa,
    IUsuarioActual usuario,
    IUnitOfWork unitOfWork,
    TimeProvider reloj)
{
    public async Task<Result<ConteoDto>> AbrirAsync(AbrirConteoRequest r, CancellationToken ct)
    {
        var almacen = await db.Almacenes.AsNoTracking().FirstOrDefaultAsync(a => a.Id == r.AlmacenId, ct);
        if (almacen is null)
        {
            return Error.NoEncontrado(ErroresInventario.AlmacenNoEncontrado, "El almacén no existe.");
        }

        var conteo = new ConteoFisico
        {
            Folio = await folios.SiguienteAsync(InventarioDbContext.Esquema, "CF", ct),
            AlmacenId = r.AlmacenId,
            FechaOperativa = fechaOperativa.Hoy(),
            Notas = string.IsNullOrWhiteSpace(r.Notas) ? null : r.Notas.Trim(),
        };
        db.Conteos.Add(conteo);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(conteo.Id, ct);
    }

    /// <summary>RN-INV-10: la cantidad del sistema se toma al capturar cada partida; el almacén sigue operando.</summary>
    public async Task<Result<ConteoDto>> CapturarAsync(Guid id, CapturarConteoRequest r, CancellationToken ct)
    {
        var conteo = await db.Conteos.Include(c => c.Partidas).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conteo is null)
        {
            return Error.NoEncontrado("INV-012", "El conteo no existe.");
        }

        if (conteo.Estado != EstadoConteo.Abierto)
        {
            return Error.Conflicto("INV-013", "El conteo ya no está abierto.");
        }

        var producto = await catalogos.ObtenerProductoAsync(r.ProductoId, ct);
        if (producto is null)
        {
            return Error.NoEncontrado(ErroresInventario.ProductoNoEncontrado, "El producto no existe.");
        }

        var lote = producto.ManejaLote ? (r.Lote ?? string.Empty).Trim().ToUpperInvariant() : string.Empty;
        if (producto.ManejaLote && lote.Length == 0)
        {
            return Error.Campo(ErroresInventario.LoteRequerido, "lote", $"{producto.Nombre} maneja lote: indica el lote contado.");
        }

        if (decimal.Round(r.CantidadContada, producto.UnidadBaseDecimales) != r.CantidadContada)
        {
            return Error.Campo("CAT-006", "cantidadContada", $"La cantidad admite máximo {producto.UnidadBaseDecimales} decimales.");
        }

        var sistema = await inventario.ObtenerExistenciaAsync(conteo.AlmacenId, r.ProductoId, lote, ct);
        var partida = conteo.Partidas.FirstOrDefault(p => p.ProductoId == r.ProductoId && p.Lote == lote);
        if (partida is null)
        {
            partida = new ConteoPartida { ConteoId = conteo.Id, ProductoId = r.ProductoId, Lote = lote };
            conteo.Partidas.Add(partida);
            db.Entry(partida).State = EntityState.Added;
        }

        partida.CantidadSistema = sistema;
        partida.CantidadContada = r.CantidadContada;
        partida.Diferencia = r.CantidadContada - sistema;
        partida.CapturadoEn = reloj.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>RN-INV-09: cada diferencia distinta de cero genera un ajuste con documento <c>ConteoFisico</c>.</summary>
    public async Task<Result<ConteoDto>> CerrarAsync(Guid id, CancellationToken ct)
    {
        var conteo = await db.Conteos.Include(c => c.Partidas).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conteo is null)
        {
            return Error.NoEncontrado("INV-012", "El conteo no existe.");
        }

        if (conteo.Estado != EstadoConteo.Abierto)
        {
            return Error.Conflicto("INV-013", "El conteo ya no está abierto.");
        }

        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var lineas = conteo.Partidas.Where(p => p.Diferencia != 0).Select(p => new LineaMovimiento(
                p.Diferencia > 0 ? TipoMovimiento.AjusteEntrada : TipoMovimiento.AjusteSalida,
                conteo.AlmacenId, p.ProductoId, p.Lote, p.Diferencia, Notas: $"Conteo {conteo.Folio}")).ToList();
            var movimientos = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.ConteoFisico, conteo.Id, conteo.Folio, fechaOperativa.Hoy(), usuario.Id ?? Guid.Empty, lineas), c);
            if (!movimientos.EsExito)
            {
                return Result.Falla(movimientos.Error!);
            }

            conteo.Estado = EstadoConteo.Cerrado;
            conteo.CerradoEn = reloj.GetUtcNow();
            conteo.CerradoPor = usuario.Id;
            await db.SaveChangesAsync(c);
            return Result.Exito();
        }, ct);

        return resultado.EsExito ? await ObtenerAsync(id, ct) : resultado.Error!;
    }

    public async Task<Result<ConteoDto>> CancelarAsync(Guid id, CancellationToken ct)
    {
        var conteo = await db.Conteos.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conteo is null)
        {
            return Error.NoEncontrado("INV-012", "El conteo no existe.");
        }

        if (conteo.Estado != EstadoConteo.Abierto)
        {
            return Error.Conflicto("INV-013", "El conteo ya no está abierto.");
        }

        conteo.Estado = EstadoConteo.Cancelado;
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    public async Task<IReadOnlyList<ConteoDto>> ListarAsync(EstadoConteo? estado, CancellationToken ct)
    {
        var consulta = db.Conteos.AsNoTracking().Include(c => c.Partidas).AsQueryable();
        if (estado is not null)
        {
            consulta = consulta.Where(c => c.Estado == estado);
        }

        var conteos = await consulta.OrderByDescending(c => c.CreadoEn).Take(200).ToListAsync(ct);
        return await MapearAsync(conteos, ct);
    }

    public async Task<Result<ConteoDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var conteo = await db.Conteos.AsNoTracking().Include(c => c.Partidas).FirstOrDefaultAsync(c => c.Id == id, ct);
        return conteo is null ? Error.NoEncontrado("INV-012", "El conteo no existe.") : (await MapearAsync([conteo], ct))[0];
    }

    private async Task<IReadOnlyList<ConteoDto>> MapearAsync(List<ConteoFisico> conteos, CancellationToken ct)
    {
        var almacenes = await db.Almacenes.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Codigo, ct);
        var productos = await catalogos.ObtenerProductosAsync(conteos.SelectMany(c => c.Partidas).Select(p => p.ProductoId), ct);
        return conteos.Select(c => new ConteoDto(c.Id, c.Folio, c.AlmacenId, almacenes.GetValueOrDefault(c.AlmacenId) ?? "?",
            c.FechaOperativa, c.Estado, c.Notas, c.CreadoEn, c.CerradoEn,
            c.Partidas.OrderBy(p => p.CapturadoEn).Select(p =>
            {
                var producto = productos.GetValueOrDefault(p.ProductoId);
                return new ConteoPartidaDto(p.Id, p.ProductoId, producto?.Sku ?? "?", producto?.Nombre ?? "?",
                    producto?.UnidadBaseClave ?? string.Empty, p.Lote, p.CantidadSistema, p.CantidadContada, p.Diferencia,
                    p.CapturadoEn);
            }).ToList())).ToList();
    }
}
