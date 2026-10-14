using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Inventario.Publico;
using TG.Logistica.Dominio;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.Ventas.Publico;

namespace TG.Logistica.Aplicacion;

public sealed record LiquidacionPartidaDto(
    Guid ProductoId,
    string Sku,
    string Producto,
    string Unidad,
    string Lote,
    decimal Cargado,
    decimal Vendido,
    decimal DevueltoClientesVendible,
    decimal DevueltoClientesNoVendible,
    decimal SobranteEsperado,
    decimal? SobranteContado,
    decimal? Diferencia,
    DestinoSobrante DestinoSobrante,
    Guid? MotivoMermaId);

public sealed record LiquidacionDto(
    Guid Id,
    string Folio,
    Guid RutaId,
    string RutaCodigo,
    string RutaNombre,
    DateOnly FechaOperativa,
    EstadoLiquidacion Estado,
    int Ventas,
    decimal TotalVendido,
    decimal VentasContadoEfectivo,
    decimal CobrosEfectivo,
    decimal EfectivoEsperado,
    decimal? EfectivoEntregado,
    decimal? DiferenciaEfectivo,
    int OperacionesPendientes,
    DateTimeOffset? CerradaEn,
    Guid? CerradaPor,
    bool CerradaForzada,
    string? Notas,
    IReadOnlyList<LiquidacionPartidaDto> Partidas);

public sealed record LiquidacionResumenDto(
    Guid Id,
    string Folio,
    Guid RutaId,
    string RutaCodigo,
    DateOnly FechaOperativa,
    EstadoLiquidacion Estado,
    decimal EfectivoEsperado,
    decimal? EfectivoEntregado,
    decimal? DiferenciaEfectivo,
    DateTimeOffset? CerradaEn);

public sealed record ConteoPartidaRequest(Guid ProductoId, string? Lote, decimal SobranteContado, DestinoSobrante Destino,
    Guid? MotivoMermaId);

public sealed record CapturarConteoLiquidacionRequest(decimal? EfectivoEntregado, string? Notas, IReadOnlyList<ConteoPartidaRequest> Partidas);

public sealed class CapturarConteoLiquidacionValidator : AbstractValidator<CapturarConteoLiquidacionRequest>
{
    public CapturarConteoLiquidacionValidator()
    {
        RuleFor(x => x.EfectivoEntregado).GreaterThanOrEqualTo(0).When(x => x.EfectivoEntregado is not null)
            .WithMessage("El efectivo entregado no puede ser negativo.");
        RuleForEach(x => x.Partidas).ChildRules(p =>
        {
            p.RuleFor(x => x.ProductoId).NotEmpty();
            p.RuleFor(x => x.SobranteContado).GreaterThanOrEqualTo(0).WithMessage("El sobrante contado no puede ser negativo.");
        });
    }
}

public sealed record CerrarLiquidacionRequest(bool Forzar);

/// <summary>
/// Liquidación del día de una ruta: cuadra producto (RN-LOG-06/07) y efectivo (RN-LOG-08) y deja el almacén de la
/// ruta en cero. No se reabre (RN-LOG-10).
/// </summary>
public sealed class LiquidacionesHandler(
    LogisticaDbContext db,
    IInventarioService inventario,
    ICatalogosService catalogos,
    IVentasService ventas,
    IIncidenciasService incidencias,
    IEstadoSincronizacionRuta estadoSync,
    IUsuarioActual usuario,
    IUnitOfWork unitOfWork,
    TimeProvider reloj)
{
    private sealed record Llave(Guid ProductoId, string Lote);

    public async Task<Paginado<LiquidacionResumenDto>> ListarAsync(Guid? rutaId, DateOnly? fecha, DateOnly? desde, DateOnly? hasta,
        EstadoLiquidacion? estado, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Liquidaciones.AsNoTracking();
        if (rutaId is not null)
        {
            consulta = consulta.Where(l => l.RutaId == rutaId);
        }

        if (fecha is not null)
        {
            consulta = consulta.Where(l => l.FechaOperativa == fecha);
        }

        if (desde is not null)
        {
            consulta = consulta.Where(l => l.FechaOperativa >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(l => l.FechaOperativa <= hasta);
        }

        if (estado is not null)
        {
            consulta = consulta.Where(l => l.Estado == estado);
        }

        var rutas = await db.Rutas.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Codigo, ct);
        var paginado = await consulta.OrderByDescending(l => l.FechaOperativa).ThenBy(l => l.RutaId).PaginarAsync(pagina, tamano, ct);
        return paginado.Map(l => new LiquidacionResumenDto(l.Id, l.Folio, l.RutaId, rutas.GetValueOrDefault(l.RutaId) ?? "?",
            l.FechaOperativa, l.Estado, l.EfectivoEsperado, l.EfectivoEntregado, l.DiferenciaEfectivo, l.CerradaEn));
    }

    /// <summary>Detalle: si está abierta se calcula al momento (ventas y retornos del día); cerrada, lo guardado.</summary>
    public async Task<Result<LiquidacionDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var liquidacion = await db.Liquidaciones.AsNoTracking().Include(l => l.Partidas).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidacion is null)
        {
            return NoEncontrada();
        }

        var ruta = await db.Rutas.AsNoTracking().FirstAsync(r => r.Id == liquidacion.RutaId, ct);
        var resumen = await ventas.ResumenRutaDiaAsync(liquidacion.RutaId, liquidacion.FechaOperativa, ct);
        var partidas = liquidacion.Estado == EstadoLiquidacion.Abierta
            ? await CalcularPartidasAsync(liquidacion, ruta, resumen, ct)
            : liquidacion.Partidas;
        var efectivoEsperado = liquidacion.Estado == EstadoLiquidacion.Abierta ? resumen.EfectivoEsperado : liquidacion.EfectivoEsperado;
        var pendientes = liquidacion.Estado == EstadoLiquidacion.Abierta ? await estadoSync.OperacionesPendientesAsync(ruta.Id, ct) : 0;
        var productos = await catalogos.ObtenerProductosAsync(partidas.Select(p => p.ProductoId), ct);
        return new LiquidacionDto(liquidacion.Id, liquidacion.Folio, ruta.Id, ruta.Codigo, ruta.Nombre, liquidacion.FechaOperativa,
            liquidacion.Estado, resumen.Ventas, resumen.TotalVendido, resumen.VentasContadoEfectivo, resumen.CobrosEfectivo,
            efectivoEsperado, liquidacion.EfectivoEntregado,
            liquidacion.EfectivoEntregado is { } e ? ReglasLogistica.DiferenciaEfectivo(e, efectivoEsperado) : null,
            pendientes, liquidacion.CerradaEn, liquidacion.CerradaPor, liquidacion.CerradaForzada, liquidacion.Notas,
            partidas.OrderBy(p => productos.GetValueOrDefault(p.ProductoId)?.Sku).ThenBy(p => p.Lote).Select(p =>
            {
                var producto = productos.GetValueOrDefault(p.ProductoId);
                return new LiquidacionPartidaDto(p.ProductoId, producto?.Sku ?? "?", producto?.Nombre ?? "?",
                    producto?.UnidadBaseClave ?? string.Empty, p.Lote, p.Cargado, p.Vendido, p.DevueltoClientesVendible,
                    p.DevueltoClientesNoVendible, p.SobranteEsperado, p.SobranteContado, p.Diferencia, p.DestinoSobrante, p.MotivoMermaId);
            }).ToList());
    }

    /// <summary>Guarda el sobrante contado y su destino por producto y lote, y el efectivo entregado.</summary>
    public async Task<Result<LiquidacionDto>> CapturarConteoAsync(Guid id, CapturarConteoLiquidacionRequest r, CancellationToken ct)
    {
        var liquidacion = await db.Liquidaciones.Include(l => l.Partidas).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidacion is null)
        {
            return NoEncontrada();
        }

        if (liquidacion.Estado != EstadoLiquidacion.Abierta)
        {
            return Cerrada();
        }

        var motivos = await ValidarMotivosAsync(r.Partidas, ct);
        if (!motivos.EsExito)
        {
            return motivos.Error!;
        }

        var ruta = await db.Rutas.AsNoTracking().FirstAsync(x => x.Id == liquidacion.RutaId, ct);
        var resumen = await ventas.ResumenRutaDiaAsync(liquidacion.RutaId, liquidacion.FechaOperativa, ct);
        var calculadas = await CalcularPartidasAsync(liquidacion, ruta, resumen, ct);
        foreach (var captura in r.Partidas)
        {
            var lote = (captura.Lote ?? string.Empty).Trim().ToUpperInvariant();
            var partida = calculadas.FirstOrDefault(p => p.ProductoId == captura.ProductoId && p.Lote == lote);
            if (partida is null)
            {
                partida = new LiquidacionPartida { LiquidacionId = liquidacion.Id, ProductoId = captura.ProductoId, Lote = lote };
                calculadas.Add(partida);
            }

            partida.SobranteContado = captura.SobranteContado;
            partida.Diferencia = ReglasLogistica.Diferencia(captura.SobranteContado, partida.SobranteEsperado);
            partida.DestinoSobrante = captura.Destino;
            partida.MotivoMermaId = captura.MotivoMermaId;
        }

        Guardar(liquidacion, calculadas);
        liquidacion.EfectivoEsperado = resumen.EfectivoEsperado;
        if (r.EfectivoEntregado is { } efectivo)
        {
            liquidacion.EfectivoEntregado = efectivo;
            liquidacion.DiferenciaEfectivo = ReglasLogistica.DiferenciaEfectivo(efectivo, resumen.EfectivoEsperado);
        }

        if (r.Notas is not null)
        {
            liquidacion.Notas = string.IsNullOrWhiteSpace(r.Notas) ? null : r.Notas.Trim();
        }

        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>
    /// RN-LOG-07: diferencias → ajuste con motivo "Faltante de ruta" e incidencia; sobrante → reintegro a PT o merma;
    /// el almacén de la ruta queda en cero. RN-LOG-08 efectivo. RN-LOG-09 pendientes de sincronizar.
    /// </summary>
    public async Task<Result<LiquidacionDto>> CerrarAsync(Guid id, CerrarLiquidacionRequest r, CancellationToken ct)
    {
        var liquidacion = await db.Liquidaciones.Include(l => l.Partidas).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidacion is null)
        {
            return NoEncontrada();
        }

        if (liquidacion.Estado != EstadoLiquidacion.Abierta)
        {
            return Cerrada();
        }

        var ruta = await db.Rutas.AsNoTracking().FirstAsync(x => x.Id == liquidacion.RutaId, ct);
        var pendientes = await estadoSync.OperacionesPendientesAsync(ruta.Id, ct);
        var forzada = false;
        if (pendientes > 0)
        {
            if (!r.Forzar || !usuario.TienePermiso(Permisos.Logistica.ForzarCierre))
            {
                return new Error(ErroresLogistica.PendientesSync,
                    $"El dispositivo de la ruta reportó {pendientes} operaciones sin sincronizar. Sincroniza antes de cerrar o fuerza el cierre con permiso.",
                    TipoError.Conflicto, Detalle: new { pendientes });
            }

            forzada = true;
        }

        if (liquidacion.EfectivoEntregado is null)
        {
            return Error.Campo(ErroresLogistica.SinConteo, "efectivoEntregado", "Captura el efectivo entregado antes de cerrar.");
        }

        var resumen = await ventas.ResumenRutaDiaAsync(liquidacion.RutaId, liquidacion.FechaOperativa, ct);
        var partidas = await CalcularPartidasAsync(liquidacion, ruta, resumen, ct);
        var sinContar = partidas.FirstOrDefault(p => p.SobranteContado is null && p.SobranteEsperado != 0);
        if (sinContar is not null)
        {
            var producto = await catalogos.ObtenerProductoAsync(sinContar.ProductoId, ct);
            return Error.Validacion(ErroresLogistica.SinConteo,
                $"Falta el sobrante contado de {producto?.Nombre ?? "un producto"}{(sinContar.Lote.Length > 0 ? $" lote {sinContar.Lote}" : string.Empty)}.");
        }

        foreach (var p in partidas)
        {
            p.SobranteContado ??= 0;
            p.Diferencia = ReglasLogistica.Diferencia(p.SobranteContado.Value, p.SobranteEsperado);
        }

        var faltante = await catalogos.ObtenerMotivoPorClaveAsync(ClavesMotivoMerma.FaltanteRuta, ct);
        var reproceso = await catalogos.ObtenerMotivoPorClaveAsync(ClavesMotivoMerma.Reproceso, ct);
        var otro = await catalogos.ObtenerMotivoPorClaveAsync(ClavesMotivoMerma.Otro, ct);
        var pt = await inventario.ObtenerAlmacenPorCodigoAsync("PT", ct);
        if (pt is null)
        {
            return Error.NoEncontrado(ErroresInventario.AlmacenNoEncontrado, "No existe el almacén PT para reintegrar sobrantes.");
        }

        var usuarioId = usuario.Id ?? Guid.Empty;
        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            // 1. Ajuste por diferencia contra la existencia real del lote (en el caso normal, la diferencia de RN-LOG-06),
            //    y salida del sobrante: traspaso a PT o merma.
            var existencias = (await inventario.ExistenciasDeAlmacenAsync(ruta.AlmacenId, c))
                .ToDictionary(e => new Llave(e.ProductoId, e.Lote), e => e.Cantidad);
            var lineas = new List<LineaMovimiento>();
            foreach (var p in partidas)
            {
                var contado = p.SobranteContado!.Value;
                var ajuste = contado - existencias.GetValueOrDefault(new Llave(p.ProductoId, p.Lote));
                if (ajuste != 0)
                {
                    lineas.Add(new LineaMovimiento(ajuste > 0 ? TipoMovimiento.AjusteEntrada : TipoMovimiento.AjusteSalida, ruta.AlmacenId,
                        p.ProductoId, p.Lote, ajuste, MotivoMermaId: faltante?.Id, Notas: "Diferencia de liquidación"));
                }

                if (contado > 0)
                {
                    lineas.Add(p.DestinoSobrante switch
                    {
                        DestinoSobrante.Reintegrar => new LineaMovimiento(TipoMovimiento.TraspasoSalida, ruta.AlmacenId, p.ProductoId, p.Lote, -contado,
                            Notas: "Reintegro de sobrante"),
                        DestinoSobrante.Reproceso => new LineaMovimiento(TipoMovimiento.Merma, ruta.AlmacenId, p.ProductoId, p.Lote, -contado,
                            MotivoMermaId: reproceso?.Id, Notas: "Sobrante a reproceso"),
                        _ => new LineaMovimiento(TipoMovimiento.Merma, ruta.AlmacenId, p.ProductoId, p.Lote, -contado,
                            MotivoMermaId: p.MotivoMermaId ?? otro?.Id, Notas: "Sobrante a merma"),
                    });
                }
            }

            var salidas = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.Liquidacion, liquidacion.Id, liquidacion.Folio, liquidacion.FechaOperativa, usuarioId, lineas), c);
            if (!salidas.EsExito)
            {
                return Result.Falla(salidas.Error!);
            }

            var reintegros = salidas.Valor.Where(m => m.Tipo == TipoMovimiento.TraspasoSalida).ToList();
            if (reintegros.Count > 0)
            {
                var entradas = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                    DocumentoInventario.Liquidacion, liquidacion.Id, liquidacion.Folio, liquidacion.FechaOperativa, usuarioId,
                    reintegros.Select(m => new LineaMovimiento(TipoMovimiento.TraspasoEntrada, pt.Id, m.ProductoId, m.Lote, -m.Cantidad,
                        m.CostoUnitario, FechaCaducidad: m.FechaCaducidad, Notas: $"Reintegro de ruta {ruta.Codigo}")).ToList()), c);
                if (!entradas.EsExito)
                {
                    return Result.Falla(entradas.Error!);
                }
            }

            var restante = await inventario.ExistenciasDeAlmacenAsync(ruta.AlmacenId, c);
            if (restante.Count > 0)
            {
                return Result.Falla(Error.Conflicto(ErroresLogistica.AlmacenNoQuedoEnCero,
                    "El almacén de la ruta no quedó en cero; revisa el conteo de todos los productos y lotes."));
            }

            // 2. Incidencias.
            var conDiferencia = partidas.Where(p => p.Diferencia != 0).ToList();
            if (conDiferencia.Count > 0)
            {
                await incidencias.CrearAsync(TipoIncidencia.DiferenciaLiquidacion, "Liquidacion", liquidacion.Id, new
                {
                    liquidacion.Folio,
                    ruta = ruta.Codigo,
                    fecha = liquidacion.FechaOperativa,
                    partidas = conDiferencia.Select(p => new
                    {
                        p.ProductoId, p.Lote, esperado = p.SobranteEsperado, contado = p.SobranteContado, diferencia = p.Diferencia,
                    }),
                }, c);
            }

            liquidacion.EfectivoEsperado = resumen.EfectivoEsperado;
            liquidacion.DiferenciaEfectivo = ReglasLogistica.DiferenciaEfectivo(liquidacion.EfectivoEntregado!.Value, resumen.EfectivoEsperado);
            if (liquidacion.DiferenciaEfectivo != 0)
            {
                await incidencias.CrearAsync(TipoIncidencia.DiferenciaLiquidacion, "Liquidacion", liquidacion.Id, new
                {
                    liquidacion.Folio,
                    ruta = ruta.Codigo,
                    efectivoEsperado = liquidacion.EfectivoEsperado,
                    efectivoEntregado = liquidacion.EfectivoEntregado,
                    diferencia = liquidacion.DiferenciaEfectivo,
                }, c);
            }

            // 3. Cierre.
            Guardar(liquidacion, partidas);
            liquidacion.Estado = EstadoLiquidacion.Cerrada;
            liquidacion.CerradaEn = reloj.GetUtcNow();
            liquidacion.CerradaPor = usuario.Id;
            liquidacion.CerradaForzada = forzada;
            var envios = await db.Envios.Where(e => e.RutaId == ruta.Id && e.FechaOperativa == liquidacion.FechaOperativa
                && e.Estado == EstadoEnvio.EnRuta).ToListAsync(c);
            foreach (var envio in envios)
            {
                envio.Estado = EstadoEnvio.Liquidado;
                envio.LiquidacionId = liquidacion.Id;
            }

            await db.SaveChangesAsync(c);
            return Result.Exito();
        }, ct);

        return resultado.EsExito ? await ObtenerAsync(id, ct) : resultado.Error!;
    }

    /// <summary>
    /// RN-LOG-06 por producto y lote: cargado (envíos del día), vendido (kardex de las ventas), devuelto vendible y no
    /// vendible (retornos). Incluye lo que haya en la camioneta aunque no se haya cargado hoy. Conserva lo ya contado.
    /// </summary>
    private async Task<List<LiquidacionPartida>> CalcularPartidasAsync(Liquidacion liquidacion, Ruta ruta, ResumenRutaDia resumen,
        CancellationToken ct)
    {
        var mapa = new Dictionary<Llave, LiquidacionPartida>();
        LiquidacionPartida Partida(Guid productoId, string lote)
        {
            var llave = new Llave(productoId, lote);
            if (!mapa.TryGetValue(llave, out var p))
            {
                var guardada = liquidacion.Partidas.FirstOrDefault(x => x.ProductoId == productoId && x.Lote == lote);
                p = new LiquidacionPartida
                {
                    LiquidacionId = liquidacion.Id,
                    ProductoId = productoId,
                    Lote = lote,
                    SobranteContado = guardada?.SobranteContado,
                    DestinoSobrante = guardada?.DestinoSobrante ?? DestinoSobrante.Reintegrar,
                    MotivoMermaId = guardada?.MotivoMermaId,
                };
                mapa[llave] = p;
            }

            return p;
        }

        var cargas = await db.Envios.AsNoTracking().Include(e => e.Partidas)
            .Where(e => e.RutaId == ruta.Id && e.FechaOperativa == liquidacion.FechaOperativa
                && (e.Estado == EstadoEnvio.EnRuta || e.Estado == EstadoEnvio.Liquidado))
            .ToListAsync(ct);
        foreach (var p in cargas.SelectMany(e => e.Partidas))
        {
            Partida(p.ProductoId, p.Lote).Cargado += p.CantidadBase;
        }

        foreach (var v in resumen.Vendido)
        {
            Partida(v.ProductoId, v.Lote).Vendido += v.CantidadBase;
        }

        var retornos = await db.Retornos.AsNoTracking().Include(r => r.Partidas)
            .Where(r => r.RutaId == ruta.Id && r.FechaOperativa == liquidacion.FechaOperativa && r.Estado == EstadoRetorno.Registrado
                && r.Tipo == TipoRetorno.DevolucionCliente)
            .ToListAsync(ct);
        foreach (var p in retornos.SelectMany(r => r.Partidas))
        {
            var partida = Partida(p.ProductoId, p.Lote);
            if (p.Condicion == CondicionRetorno.Vendible)
            {
                partida.DevueltoClientesVendible += p.CantidadBase;
            }
            else
            {
                partida.DevueltoClientesNoVendible += p.CantidadBase;
            }
        }

        foreach (var e in await inventario.ExistenciasDeAlmacenAsync(ruta.AlmacenId, ct))
        {
            Partida(e.ProductoId, e.Lote);
        }

        foreach (var p in mapa.Values)
        {
            p.SobranteEsperado = ReglasLogistica.SobranteEsperado(p.Cargado, p.Vendido, p.DevueltoClientesVendible);
            p.Diferencia = p.SobranteContado is { } contado ? ReglasLogistica.Diferencia(contado, p.SobranteEsperado) : null;
        }

        return mapa.Values.ToList();
    }

    /// <summary>Sustituye las partidas guardadas por las calculadas (las nuevas se insertan, las demás se actualizan).</summary>
    private void Guardar(Liquidacion liquidacion, List<LiquidacionPartida> calculadas)
    {
        foreach (var guardada in liquidacion.Partidas.ToList())
        {
            var nueva = calculadas.FirstOrDefault(p => p.ProductoId == guardada.ProductoId && p.Lote == guardada.Lote);
            if (nueva is null)
            {
                liquidacion.Partidas.Remove(guardada);
                db.Remove(guardada);
                continue;
            }

            guardada.Cargado = nueva.Cargado;
            guardada.Vendido = nueva.Vendido;
            guardada.DevueltoClientesVendible = nueva.DevueltoClientesVendible;
            guardada.DevueltoClientesNoVendible = nueva.DevueltoClientesNoVendible;
            guardada.SobranteEsperado = nueva.SobranteEsperado;
            guardada.SobranteContado = nueva.SobranteContado;
            guardada.Diferencia = nueva.Diferencia;
            guardada.DestinoSobrante = nueva.DestinoSobrante;
            guardada.MotivoMermaId = nueva.MotivoMermaId;
        }

        foreach (var nueva in calculadas.Where(n => liquidacion.Partidas.All(g => g.ProductoId != n.ProductoId || g.Lote != n.Lote)))
        {
            liquidacion.Partidas.Add(nueva);
            db.Entry(nueva).State = EntityState.Added;
        }
    }

    private async Task<Result> ValidarMotivosAsync(IReadOnlyList<ConteoPartidaRequest> partidas, CancellationToken ct)
    {
        foreach (var p in partidas.Where(p => p.Destino == DestinoSobrante.Merma && p.MotivoMermaId is not null))
        {
            var motivo = await catalogos.ObtenerMotivoAsync(p.MotivoMermaId!.Value, ct);
            if (motivo is null || !motivo.Activo || !motivo.AplicaRuta)
            {
                return Error.Campo(ErroresLogistica.MotivoInvalido, "motivoMermaId", "El motivo no existe o no aplica a rutas.");
            }
        }

        return Result.Exito();
    }

    private static Error NoEncontrada() => Error.NoEncontrado(ErroresLogistica.LiquidacionNoEncontrada, "La liquidación no existe.");

    private static Error Cerrada() =>
        Error.Conflicto(ErroresLogistica.LiquidacionCerrada, "La liquidación ya está cerrada; las correcciones se hacen con ajustes.");
}
