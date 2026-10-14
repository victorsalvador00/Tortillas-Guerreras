using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Inventario.Publico;
using TG.Logistica.Dominio;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Logistica.Aplicacion;

public sealed record EnvioPartidaRequest(Guid ProductoId, decimal Cantidad, Guid? UnidadId, string? Lote);

public sealed record GuardarEnvioRequest(
    Guid RutaId,
    TipoEnvio Tipo,
    DateOnly? FechaOperativa,
    Guid? AlmacenOrigenId,
    string? Notas,
    IReadOnlyList<EnvioPartidaRequest> Partidas,
    IReadOnlyList<Guid>? PedidoIds);

public sealed class GuardarEnvioValidator : AbstractValidator<GuardarEnvioRequest>
{
    public GuardarEnvioValidator()
    {
        RuleFor(x => x.RutaId).NotEmpty().WithMessage("Elige la ruta.");
        RuleFor(x => x.Partidas).NotEmpty().WithMessage("Agrega al menos un producto a la carga.");
        RuleForEach(x => x.Partidas).ChildRules(p =>
        {
            p.RuleFor(x => x.ProductoId).NotEmpty();
            p.RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad debe ser mayor que cero.");
        });
    }
}

public sealed record EnvioPartidaDto(Guid Id, Guid ProductoId, string Sku, string Producto, string Unidad, string Lote, decimal CantidadBase);

public sealed record EnvioDto(
    Guid Id,
    string Folio,
    DateOnly FechaOperativa,
    Guid RutaId,
    string RutaCodigo,
    TipoEnvio Tipo,
    Guid AlmacenOrigenId,
    string AlmacenOrigen,
    Guid AlmacenDestinoId,
    string AlmacenDestino,
    EstadoEnvio Estado,
    DateTimeOffset? DespachadoEn,
    Guid? LiquidacionId,
    string? Notas,
    DateTimeOffset CreadoEn,
    IReadOnlyList<EnvioPartidaDto> Partidas);

/// <summary>Cargas de camioneta: Preparado → EnRuta (despacho con traspaso PT → ruta) → Liquidado; o Cancelado.</summary>
public sealed class EnviosHandler(
    LogisticaDbContext db,
    IInventarioService inventario,
    ICatalogosService catalogos,
    IVentasService ventas,
    IGeneradorFolios folios,
    IFechaOperativa fechaOperativa,
    IUsuarioActual usuario,
    IUnitOfWork unitOfWork,
    TimeProvider reloj)
{
    public async Task<Paginado<EnvioDto>> ListarAsync(DateOnly? fecha, Guid? rutaId, EstadoEnvio? estado, int? pagina, int? tamano,
        CancellationToken ct)
    {
        var consulta = db.Envios.AsNoTracking().Include(e => e.Partidas).AsQueryable();
        if (fecha is not null)
        {
            consulta = consulta.Where(e => e.FechaOperativa == fecha);
        }

        if (rutaId is not null)
        {
            consulta = consulta.Where(e => e.RutaId == rutaId);
        }

        if (estado is not null)
        {
            consulta = consulta.Where(e => e.Estado == estado);
        }

        var paginado = await consulta.OrderByDescending(e => e.FechaOperativa).ThenByDescending(e => e.Folio).PaginarAsync(pagina, tamano, ct);
        var mapeados = await MapearAsync(paginado.Items.ToList(), ct);
        return new Paginado<EnvioDto>(mapeados, paginado.Total, paginado.Pagina, paginado.Tamano);
    }

    public async Task<Result<EnvioDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var envio = await db.Envios.AsNoTracking().Include(e => e.Partidas).FirstOrDefaultAsync(e => e.Id == id, ct);
        return envio is null ? NoEncontrado() : (await MapearAsync([envio], ct))[0];
    }

    public async Task<Result<EnvioDto>> CrearAsync(GuardarEnvioRequest r, CancellationToken ct)
    {
        var ruta = await db.Rutas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.RutaId, ct);
        if (ruta is null || !ruta.Activo)
        {
            return Error.Campo(ErroresLogistica.RutaNoEncontrada, "rutaId", "La ruta no existe o está inactiva.");
        }

        var origen = await ResolverOrigenAsync(r.AlmacenOrigenId, ct);
        if (!origen.EsExito)
        {
            return origen.Error!;
        }

        var partidas = await ConvertirPartidasAsync(r.Partidas, ct);
        if (!partidas.EsExito)
        {
            return partidas.Error!;
        }

        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var envio = new Envio
            {
                Folio = await folios.SiguienteAsync(LogisticaDbContext.Esquema, "ENV", c),
                FechaOperativa = r.FechaOperativa ?? fechaOperativa.Hoy(),
                RutaId = ruta.Id,
                Tipo = r.Tipo,
                AlmacenOrigenId = origen.Valor.Id,
                AlmacenDestinoId = ruta.AlmacenId,
                Notas = Limpiar(r.Notas),
            };
            envio.Partidas = partidas.Valor.Select(p => new EnvioPartida
            {
                EnvioId = envio.Id, ProductoId = p.ProductoId, Lote = p.Lote, CantidadBase = p.Cantidad,
            }).ToList();
            db.Envios.Add(envio);
            await db.SaveChangesAsync(c);
            if (r.PedidoIds is { Count: > 0 })
            {
                var vinculo = await ventas.VincularPedidosEnvioAsync(r.PedidoIds, envio.Id, envio.RutaId, c);
                if (!vinculo.EsExito)
                {
                    return Result<Guid>.Falla(vinculo.Error!);
                }
            }

            return Result<Guid>.Exito(envio.Id);
        }, ct);
        return resultado.EsExito ? await ObtenerAsync(resultado.Valor, ct) : resultado.Error!;
    }

    public async Task<Result<EnvioDto>> ActualizarAsync(Guid id, GuardarEnvioRequest r, CancellationToken ct)
    {
        var envio = await db.Envios.Include(e => e.Partidas).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (envio is null)
        {
            return NoEncontrado();
        }

        if (envio.Estado != EstadoEnvio.Preparado)
        {
            return Error.Conflicto(ErroresLogistica.EstadoEnvio, "Solo se edita una carga en estado Preparado.");
        }

        if (r.RutaId != envio.RutaId)
        {
            return Error.Campo(ErroresLogistica.RutaNoEncontrada, "rutaId", "La ruta de una carga no se cambia; cancélala y crea otra.");
        }

        var origen = await ResolverOrigenAsync(r.AlmacenOrigenId ?? envio.AlmacenOrigenId, ct);
        if (!origen.EsExito)
        {
            return origen.Error!;
        }

        var partidas = await ConvertirPartidasAsync(r.Partidas, ct);
        if (!partidas.EsExito)
        {
            return partidas.Error!;
        }

        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            envio.Tipo = r.Tipo;
            envio.FechaOperativa = r.FechaOperativa ?? envio.FechaOperativa;
            envio.AlmacenOrigenId = origen.Valor.Id;
            envio.Notas = Limpiar(r.Notas);
            db.RemoveRange(envio.Partidas);
            envio.Partidas = partidas.Valor.Select(p => new EnvioPartida
            {
                EnvioId = envio.Id, ProductoId = p.ProductoId, Lote = p.Lote, CantidadBase = p.Cantidad,
            }).ToList();
            foreach (var p in envio.Partidas)
            {
                db.Entry(p).State = EntityState.Added;
            }

            await db.SaveChangesAsync(c);
            if (r.PedidoIds is { Count: > 0 })
            {
                var vinculo = await ventas.VincularPedidosEnvioAsync(r.PedidoIds, envio.Id, envio.RutaId, c);
                if (!vinculo.EsExito)
                {
                    return Result.Falla(vinculo.Error!);
                }
            }

            return Result.Exito();
        }, ct);
        return resultado.EsExito ? await ObtenerAsync(id, ct) : resultado.Error!;
    }

    /// <summary>
    /// RN-LOG-02: traspaso del almacén origen (FEFO, existencia suficiente) al de la ruta con los mismos lotes y costo
    /// (RN-INV-06). RN-LOG-03: la primera carga del día abre la liquidación de la ruta y fecha.
    /// </summary>
    public async Task<Result<EnvioDto>> DespacharAsync(Guid id, CancellationToken ct)
    {
        var envio = await db.Envios.Include(e => e.Partidas).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (envio is null)
        {
            return NoEncontrado();
        }

        if (envio.Estado != EstadoEnvio.Preparado)
        {
            return Error.Conflicto(ErroresLogistica.EstadoEnvio, "Solo se despacha una carga en estado Preparado.");
        }

        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var liquidacion = await db.Liquidaciones.FirstOrDefaultAsync(l => l.RutaId == envio.RutaId && l.FechaOperativa == envio.FechaOperativa, c);
            if (liquidacion is { Estado: EstadoLiquidacion.Cerrada })
            {
                return Result.Falla(Error.Conflicto(ErroresLogistica.LiquidacionCerrada,
                    "La liquidación de la ruta para ese día ya está cerrada; programa la carga para otro día."));
            }

            var salida = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.Envio, envio.Id, envio.Folio, envio.FechaOperativa, usuario.Id ?? Guid.Empty,
                envio.Partidas.Select(p => new LineaMovimiento(TipoMovimiento.TraspasoSalida, envio.AlmacenOrigenId, p.ProductoId,
                    p.Lote.Length == 0 ? null : p.Lote, -p.CantidadBase)).ToList(), Notas: $"Carga {envio.Folio}"), c);
            if (!salida.EsExito)
            {
                return Result.Falla(salida.Error!);
            }

            var entrada = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.Envio, envio.Id, envio.Folio, envio.FechaOperativa, usuario.Id ?? Guid.Empty,
                salida.Valor.Select(m => new LineaMovimiento(TipoMovimiento.TraspasoEntrada, envio.AlmacenDestinoId, m.ProductoId,
                    m.Lote, -m.Cantidad, m.CostoUnitario, FechaCaducidad: m.FechaCaducidad)).ToList(), Notas: $"Carga {envio.Folio}"), c);
            if (!entrada.EsExito)
            {
                return Result.Falla(entrada.Error!);
            }

            // Las partidas quedan con el lote real que salió (lo que baja a la app).
            db.RemoveRange(envio.Partidas);
            envio.Partidas = salida.Valor.GroupBy(m => new { m.ProductoId, m.Lote })
                .Select(g => new EnvioPartida { EnvioId = envio.Id, ProductoId = g.Key.ProductoId, Lote = g.Key.Lote, CantidadBase = -g.Sum(m => m.Cantidad) })
                .ToList();
            foreach (var p in envio.Partidas)
            {
                db.Entry(p).State = EntityState.Added;
            }

            if (liquidacion is null)
            {
                liquidacion = new Liquidacion
                {
                    Folio = await folios.SiguienteAsync(LogisticaDbContext.Esquema, "LIQ", c),
                    RutaId = envio.RutaId,
                    FechaOperativa = envio.FechaOperativa,
                };
                db.Liquidaciones.Add(liquidacion);
            }

            envio.Estado = EstadoEnvio.EnRuta;
            envio.DespachadoEn = reloj.GetUtcNow();
            envio.LiquidacionId = liquidacion.Id;
            await db.SaveChangesAsync(c);
            return Result.Exito();
        }, ct);
        return resultado.EsExito ? await ObtenerAsync(id, ct) : resultado.Error!;
    }

    public async Task<Result<EnvioDto>> CancelarAsync(Guid id, CancellationToken ct)
    {
        var envio = await db.Envios.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (envio is null)
        {
            return NoEncontrado();
        }

        if (envio.Estado != EstadoEnvio.Preparado)
        {
            return Error.Conflicto(ErroresLogistica.EstadoEnvio, "Solo se cancela una carga en estado Preparado.");
        }

        await unitOfWork.EjecutarAsync(async c =>
        {
            envio.Estado = EstadoEnvio.Cancelado;
            await db.SaveChangesAsync(c);
            await ventas.DesvincularPedidosEnvioAsync(envio.Id, c);
            return Result.Exito();
        }, ct);
        return await ObtenerAsync(id, ct);
    }

    private async Task<Result<AlmacenInfo>> ResolverOrigenAsync(Guid? almacenId, CancellationToken ct)
    {
        var almacen = almacenId is { } id
            ? await inventario.ObtenerAlmacenAsync(id, ct)
            : await inventario.ObtenerAlmacenPorCodigoAsync("PT", ct);
        if (almacen is null || !almacen.Activo)
        {
            return Error.Campo(ErroresInventario.AlmacenNoEncontrado, "almacenOrigenId", "El almacén de origen no existe o está inactivo.");
        }

        if (almacen.Tipo == TipoAlmacen.Ruta)
        {
            return Error.Campo(ErroresInventario.AlmacenNoEncontrado, "almacenOrigenId", "El origen de una carga no puede ser otra ruta.");
        }

        return almacen;
    }

    private sealed record PartidaBase(Guid ProductoId, string Lote, decimal Cantidad);

    private async Task<Result<List<PartidaBase>>> ConvertirPartidasAsync(IReadOnlyList<EnvioPartidaRequest> partidas, CancellationToken ct)
    {
        var productos = await catalogos.ObtenerProductosAsync(partidas.Select(p => p.ProductoId), ct);
        var resultado = new List<PartidaBase>();
        foreach (var p in partidas)
        {
            if (!productos.TryGetValue(p.ProductoId, out var producto) || !producto.Activo || !producto.EsVendible)
            {
                return Error.Campo(ErroresLogistica.ProductoInvalido, "partidas", "Uno de los productos no existe, no es vendible o está inactivo.");
            }

            var cantidad = p.Cantidad;
            if (p.UnidadId is { } unidad)
            {
                var conversion = await catalogos.ConvertirABaseAsync(p.ProductoId, unidad, p.Cantidad, UsoUnidad.Venta, ct);
                if (!conversion.EsExito)
                {
                    return conversion.Error!;
                }

                cantidad = conversion.Valor.Base;
            }

            var lote = producto.ManejaLote ? (p.Lote ?? string.Empty).Trim().ToUpperInvariant() : string.Empty;
            resultado.Add(new PartidaBase(p.ProductoId, lote, cantidad));
        }

        return resultado;
    }

    private async Task<IReadOnlyList<EnvioDto>> MapearAsync(List<Envio> envios, CancellationToken ct)
    {
        var productos = await catalogos.ObtenerProductosAsync(envios.SelectMany(e => e.Partidas).Select(p => p.ProductoId), ct);
        var almacenes = (await inventario.ListarAlmacenesAsync(ct)).ToDictionary(a => a.Id, a => a.Codigo);
        var rutas = await db.Rutas.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Codigo, ct);
        return envios.Select(e => new EnvioDto(e.Id, e.Folio, e.FechaOperativa, e.RutaId, rutas.GetValueOrDefault(e.RutaId) ?? "?", e.Tipo,
            e.AlmacenOrigenId, almacenes.GetValueOrDefault(e.AlmacenOrigenId) ?? "?", e.AlmacenDestinoId,
            almacenes.GetValueOrDefault(e.AlmacenDestinoId) ?? "?", e.Estado, e.DespachadoEn, e.LiquidacionId, e.Notas, e.CreadoEn,
            e.Partidas.OrderBy(p => productos.GetValueOrDefault(p.ProductoId)?.Sku).ThenBy(p => p.Lote).Select(p =>
            {
                var producto = productos.GetValueOrDefault(p.ProductoId);
                return new EnvioPartidaDto(p.Id, p.ProductoId, producto?.Sku ?? "?", producto?.Nombre ?? "?",
                    producto?.UnidadBaseClave ?? string.Empty, p.Lote, p.CantidadBase);
            }).ToList())).ToList();
    }

    private static Error NoEncontrado() => Error.NoEncontrado(ErroresLogistica.EnvioNoEncontrado, "La carga no existe.");

    private static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
