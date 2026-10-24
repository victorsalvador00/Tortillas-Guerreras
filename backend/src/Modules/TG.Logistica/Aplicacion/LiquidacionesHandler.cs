using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Inventario.Publico;
using TG.Logistica.Dominio;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.Seguridad.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;
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
    decimal CargadoInicial,
    decimal Recargado,
    decimal Vendido,
    decimal ImporteVenta,
    decimal DevueltoClientesVendible,
    decimal DevueltoClientesNoVendible,
    decimal SobranteEsperado,
    decimal CambiosEntregados,
    decimal? SobranteContado,
    decimal? Diferencia,
    DestinoSobrante DestinoSobrante,
    Guid? MotivoMermaId,
    bool AplicaACobro,
    decimal CantidadACobroSugerida,
    decimal? CantidadACobro,
    decimal? PrecioACobro);

public sealed record LiquidacionDto(
    Guid Id,
    string Folio,
    Guid RutaId,
    string RutaCodigo,
    string RutaNombre,
    DateOnly FechaOperativa,
    Guid? ChoferId,
    string? ChoferNombre,
    EstadoLiquidacion Estado,
    FormatoLiquidacion Formato,
    bool RequiereRevision,
    int Ventas,
    decimal TotalVendido,
    decimal VentasContadoEfectivo,
    decimal CobrosEfectivo,
    decimal TotalGastos,
    decimal EfectivoEsperado,
    decimal? EfectivoEntregado,
    decimal? DiferenciaEfectivo,
    decimal TotalCredito,
    decimal TotalCobros,
    decimal TotalTransferencias,
    decimal TotalCheques,
    decimal DiferenciaPrecios,
    int OperacionesPendientes,
    DateTimeOffset? ProductoConfirmadoEn,
    string? ProductoConfirmadoPor,
    DateTimeOffset? RevisadaEn,
    string? RevisadaPor,
    DateTimeOffset? CerradaEn,
    Guid? CerradaPorId,
    string? CerradaPor,
    bool CerradaForzada,
    string? Notas,
    IReadOnlyList<LiquidacionPartidaDto> Partidas,
    IReadOnlyList<LiquidacionGastoDto> Gastos,
    bool AplicaProductoACobro,
    IReadOnlyList<CargoChoferDto> Cargos);

public sealed record LiquidacionGastoDto(Guid Id, string Concepto, decimal Importe, string? Comprobante, bool Autorizado,
    DateTimeOffset CapturadoEn, string? CapturadoPor);

public sealed record GastoLiquidacionRequest(string Concepto, decimal Importe, string? Comprobante, bool Autorizado);

public sealed class GastoLiquidacionValidator : AbstractValidator<GastoLiquidacionRequest>
{
    public GastoLiquidacionValidator()
    {
        RuleFor(x => x.Concepto).NotEmpty().MaximumLength(200).WithMessage("Captura el concepto del gasto.");
        RuleFor(x => x.Importe).GreaterThan(0).WithMessage("El importe debe ser mayor que cero.");
        RuleFor(x => x.Comprobante).MaximumLength(200);
    }
}

public sealed record CreditoHojaDto(string Folio, string Cliente, decimal Total);

/// <summary>RN-LOG-22: lo que se imprime en la hoja de liquidación.</summary>
public sealed record HojaLiquidacionDto(LiquidacionDto Liquidacion, IReadOnlyList<CreditoHojaDto> Creditos, string? FirmaAlmacen,
    string? FirmaVendedor, string? FirmaSupervisor, string? FirmaLiquidaciones, DateTimeOffset GeneradaEn);

public sealed record LiquidacionResumenDto(
    Guid Id,
    string Folio,
    Guid RutaId,
    string RutaCodigo,
    DateOnly FechaOperativa,
    Guid? ChoferId,
    string? ChoferNombre,
    EstadoLiquidacion Estado,
    FormatoLiquidacion Formato,
    decimal EfectivoEsperado,
    decimal? EfectivoEntregado,
    decimal? DiferenciaEfectivo,
    DateTimeOffset? CerradaEn);

/// <summary><c>CantidadACobro</c> (RN-LOG-24): <c>null</c> deja la capturada antes o la sugerida.</summary>
public sealed record ConteoPartidaRequest(Guid ProductoId, string? Lote, decimal SobranteContado, DestinoSobrante Destino,
    Guid? MotivoMermaId, decimal? CantidadACobro = null);

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
            p.RuleFor(x => x.CantidadACobro).GreaterThanOrEqualTo(0).When(x => x.CantidadACobro is not null)
                .WithMessage("La cantidad a cobro no puede ser negativa.");
        });
    }
}

/// <summary><c>Forzar</c> aplica al confirmar producto (RN-LOG-09); <c>EfectivoEntregado</c> al cerrar el dinero.</summary>
public sealed record CerrarLiquidacionRequest(bool Forzar = false, decimal? EfectivoEntregado = null);

public sealed record RegresarLiquidacionRequest(string Nota);

/// <summary>
/// Liquidación del día de una ruta por etapas (RN-LOG-20): almacén confirma el producto (RN-LOG-06/07, deja el almacén
/// de la ruta en cero), el supervisor revisa y liquidaciones cierra el dinero (RN-LOG-08). No regresa (RN-LOG-10).
/// </summary>
public sealed class LiquidacionesHandler(
    LogisticaDbContext db,
    IInventarioService inventario,
    ICatalogosService catalogos,
    IVentasService ventas,
    IIncidenciasService incidencias,
    IEstadoSincronizacionRuta estadoSync,
    ISeguridadService seguridad,
    IUsuarioActual usuario,
    IUnitOfWork unitOfWork,
    ServicioCargosChofer cargos,
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
        var choferes = await seguridad.NombresUsuariosAsync(paginado.Items.Where(l => l.ChoferId != null).Select(l => l.ChoferId!.Value), ct);
        return paginado.Map(l => new LiquidacionResumenDto(l.Id, l.Folio, l.RutaId, rutas.GetValueOrDefault(l.RutaId) ?? "?",
            l.FechaOperativa, l.ChoferId, l.ChoferId is { } ch ? choferes.GetValueOrDefault(ch) : null, l.Estado, l.Formato, l.EfectivoEsperado, l.EfectivoEntregado, l.DiferenciaEfectivo, l.CerradaEn));
    }

    /// <summary>
    /// Detalle: abierta, el producto se calcula al momento; desde <c>ProductoConfirmado</c>, lo guardado. El dinero se
    /// calcula al momento hasta cerrar.
    /// </summary>
    public async Task<Result<LiquidacionDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var liquidacion = await db.Liquidaciones.AsNoTracking().Include(l => l.Partidas).Include(l => l.Gastos)
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        return liquidacion is null ? NoEncontrada() : await ADtoAsync(liquidacion, ct);
    }

    private async Task<LiquidacionDto> ADtoAsync(Liquidacion liquidacion, CancellationToken ct)
    {
        var ruta = await db.Rutas.AsNoTracking().FirstAsync(r => r.Id == liquidacion.RutaId, ct);
        var resumen = await ventas.ResumenRutaDiaAsync(liquidacion.RutaId, liquidacion.FechaOperativa, ct);
        var abierta = liquidacion.Estado == EstadoLiquidacion.Abierta;
        var cerrada = liquidacion.Estado == EstadoLiquidacion.Cerrada;
        var partidas = abierta ? await CalcularPartidasAsync(liquidacion, ruta, resumen, ct) : liquidacion.Partidas;
        var gastos = GastosAutorizados(liquidacion);
        var efectivoEsperado = cerrada
            ? liquidacion.EfectivoEsperado
            : ReglasLogistica.EfectivoEsperado(resumen.VentasContadoEfectivo, resumen.CobrosEfectivo, gastos);
        var pendientes = abierta ? await estadoSync.OperacionesPendientesAsync(ruta.Id, liquidacion.FechaOperativa, ct) : 0;
        var productos = await catalogos.ObtenerProductosAsync(partidas.Select(p => p.ProductoId), ct);
        var nombres = await seguridad.NombresUsuariosAsync(new[]
        {
            liquidacion.ChoferId, liquidacion.ProductoConfirmadoPor, liquidacion.RevisadaPor, liquidacion.CerradaPor,
        }.Where(x => x is not null).Select(x => x!.Value).Concat(liquidacion.Gastos.Select(g => g.CapturadoPor)), ct);
        string? Nombre(Guid? usuarioId) => usuarioId is { } u ? nombres.GetValueOrDefault(u) : null;
        var requiereRevision = await catalogos.ObtenerParametroAsync(ClavesParametro.RequiereRevisionSupervisor, true, ct);
        return new LiquidacionDto(liquidacion.Id, liquidacion.Folio, ruta.Id, ruta.Codigo, ruta.Nombre, liquidacion.FechaOperativa,
            liquidacion.ChoferId, Nombre(liquidacion.ChoferId), liquidacion.Estado, liquidacion.Formato, requiereRevision,
            resumen.Ventas, cerrada ? liquidacion.TotalVenta ?? resumen.TotalVendido : resumen.TotalVendido,
            resumen.VentasContadoEfectivo, resumen.CobrosEfectivo, gastos, efectivoEsperado, liquidacion.EfectivoEntregado,
            liquidacion.EfectivoEntregado is { } e ? ReglasLogistica.DiferenciaEfectivo(e, efectivoEsperado) : null,
            cerrada ? liquidacion.TotalCredito ?? 0 : resumen.TotalCredito,
            cerrada ? liquidacion.TotalCobros ?? 0 : resumen.CobrosTotal,
            cerrada ? liquidacion.TotalTransferencias ?? 0 : resumen.VentasTransferencia + resumen.CobrosTransferencia,
            cerrada ? liquidacion.TotalCheques ?? 0 : resumen.VentasCheque,
            cerrada ? liquidacion.DiferenciaPrecios : resumen.DiferenciaPrecios,
            pendientes, liquidacion.ProductoConfirmadoEn, Nombre(liquidacion.ProductoConfirmadoPor), liquidacion.RevisadaEn,
            Nombre(liquidacion.RevisadaPor), liquidacion.CerradaEn, liquidacion.CerradaPor, Nombre(liquidacion.CerradaPor),
            liquidacion.CerradaForzada, liquidacion.Notas,
            partidas.OrderBy(p => productos.GetValueOrDefault(p.ProductoId)?.Sku).ThenBy(p => p.Lote).Select(p =>
            {
                var producto = productos.GetValueOrDefault(p.ProductoId);
                return new LiquidacionPartidaDto(p.ProductoId, producto?.Sku ?? "?", producto?.Nombre ?? "?",
                    producto?.UnidadBaseClave ?? string.Empty, p.Lote, p.Cargado, p.CargadoInicial, p.Recargado, p.Vendido,
                    p.ImporteVenta, p.DevueltoClientesVendible, p.DevueltoClientesNoVendible, p.SobranteEsperado, p.CambiosEntregados,
                    p.SobranteContado, p.Diferencia, p.DestinoSobrante, p.MotivoMermaId, p.AplicaACobro, p.CantidadACobroSugerida,
                    p.CantidadACobro, p.PrecioACobro);
            }).ToList(),
            liquidacion.Gastos.OrderBy(g => g.CapturadoEn).Select(g => new LiquidacionGastoDto(g.Id, g.Concepto, g.Importe, g.Comprobante,
                g.Autorizado, g.CapturadoEn, Nombre(g.CapturadoPor))).ToList(),
            await AplicaProductoACobroAsync(liquidacion, ruta, ct),
            await CargosChoferHandler.ADtosAsync(db, catalogos, seguridad,
                await db.CargosChofer.AsNoTracking().Where(c => c.LiquidacionId == liquidacion.Id).OrderBy(c => c.Folio).ToListAsync(ct), ct));
    }

    /// <summary>RN-LOG-19/24: solo formato <c>Completo</c> y canal con producto a cobro.</summary>
    private async Task<bool> AplicaProductoACobroAsync(Liquidacion liquidacion, Ruta ruta, CancellationToken ct) =>
        liquidacion.Formato == FormatoLiquidacion.Completo
        && (await ventas.ListarCanalesAsync(ct)).FirstOrDefault(c => c.Id == ruta.CanalId) is { AplicaProductoACobro: true };

    /// <summary>RN-LOG-22: datos de la hoja imprimible (formato, dinero, créditos por cliente y folio, gastos y firmas).</summary>
    public async Task<Result<HojaLiquidacionDto>> HojaAsync(Guid id, CancellationToken ct)
    {
        var liquidacion = await db.Liquidaciones.AsNoTracking().Include(l => l.Partidas).Include(l => l.Gastos)
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidacion is null)
        {
            return NoEncontrada();
        }

        var dto = await ADtoAsync(liquidacion, ct);
        var resumen = await ventas.ResumenRutaDiaAsync(liquidacion.RutaId, liquidacion.FechaOperativa, ct);
        return new HojaLiquidacionDto(dto, (resumen.Creditos ?? []).Select(c => new CreditoHojaDto(c.Folio, c.Cliente, c.Total)).ToList(),
            dto.ProductoConfirmadoPor, dto.ChoferNombre, dto.RevisadaPor, dto.CerradaPor, reloj.GetUtcNow());
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
            return Transicion(liquidacion.Estado, "capturar el conteo de");
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

            if (captura.CantidadACobro is { } aCobro)
            {
                if (!partida.AplicaACobro && aCobro != 0)
                {
                    return Error.Validacion(ErroresLogistica.ACobroInvalido,
                        "Esa partida no admite producto a cobro (formato simplificado, canal o producto sin producto a cobro, o sin devolución no vendible).");
                }

                if (aCobro > partida.DevueltoClientesNoVendible)
                {
                    return Error.Validacion(ErroresLogistica.ACobroInvalido,
                        $"La cantidad a cobro ({aCobro:0.####}) no puede ser mayor que lo devuelto no vendible ({partida.DevueltoClientesNoVendible:0.####}).");
                }

                partida.CantidadACobro = aCobro;
            }

            partida.SobranteContado = captura.SobranteContado;
            partida.Diferencia = ReglasLogistica.Diferencia(captura.SobranteContado, partida.SobranteEsperado);
            partida.DestinoSobrante = captura.Destino;
            partida.MotivoMermaId = captura.MotivoMermaId;
        }

        Guardar(liquidacion, calculadas);
        if (r.EfectivoEntregado is { } efectivo)
        {
            liquidacion.EfectivoEntregado = efectivo;
        }

        if (r.Notas is not null)
        {
            liquidacion.Notas = string.IsNullOrWhiteSpace(r.Notas) ? null : r.Notas.Trim();
        }

        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>
    /// RN-LOG-20 <c>Abierta → ProductoConfirmado</c>. RN-LOG-07: diferencias → ajuste con motivo "Faltante de ruta" e
    /// incidencia; sobrante → reintegro a PT o merma; el almacén de la ruta queda en cero. RN-LOG-09 pendientes de sincronizar.
    /// </summary>
    public async Task<Result<LiquidacionDto>> ConfirmarProductoAsync(Guid id, CerrarLiquidacionRequest r, CancellationToken ct)
    {
        var liquidacion = await db.Liquidaciones.Include(l => l.Partidas).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidacion is null)
        {
            return NoEncontrada();
        }

        if (liquidacion.Estado != EstadoLiquidacion.Abierta)
        {
            return Transicion(liquidacion.Estado, "confirmar el producto de");
        }

        var ruta = await db.Rutas.AsNoTracking().FirstAsync(x => x.Id == liquidacion.RutaId, ct);
        var pendientes = await estadoSync.OperacionesPendientesAsync(ruta.Id, liquidacion.FechaOperativa, ct);
        var forzada = false;
        if (pendientes > 0)
        {
            if (!r.Forzar || !usuario.TienePermiso(Permisos.Logistica.ForzarCierre))
            {
                return new Error(ErroresLogistica.PendientesSync,
                    $"El dispositivo de la ruta reportó {pendientes} operaciones sin sincronizar. Sincroniza antes de confirmar el producto o fuerza la confirmación con permiso.",
                    TipoError.Conflicto, Detalle: new { pendientes });
            }

            forzada = true;
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

            // 3. RN-LOG-26: cargos ProductoACobro y, si aplica [SUP-21], FaltanteProducto.
            var cargoFaltante = await catalogos.ObtenerParametroAsync(ClavesParametro.CargoFaltanteProducto, true, c);
            foreach (var p in partidas)
            {
                p.CantidadACobro = p.AplicaACobro ? p.CantidadACobro ?? p.CantidadACobroSugerida : 0;
            }

            var aCobrar = partidas.Where(p => p.CantidadACobro > 0).ToList();
            var faltantes = cargoFaltante ? partidas.Where(p => p.Diferencia < 0).ToList() : [];
            var precios = await cargos.PreciosAsync(ruta, aCobrar.Concat(faltantes).Select(p => p.ProductoId).Distinct().ToList(),
                liquidacion.FechaOperativa, c);
            foreach (var p in aCobrar)
            {
                p.PrecioACobro = precios.GetValueOrDefault(p.ProductoId);
                if (liquidacion.ChoferId is { } chofer)
                {
                    await cargos.AgregarAsync(chofer, TipoCargoChofer.ProductoACobro, liquidacion.Id, liquidacion.FechaOperativa,
                        ReglasLogistica.ImporteCargo(p.CantidadACobro!.Value, p.PrecioACobro.Value), p.ProductoId, p.CantidadACobro,
                        p.PrecioACobro, p.Lote.Length > 0 ? $"Lote {p.Lote}" : null, c);
                }
            }

            foreach (var p in faltantes.Where(_ => liquidacion.ChoferId is not null))
            {
                var precio = precios.GetValueOrDefault(p.ProductoId);
                await cargos.AgregarAsync(liquidacion.ChoferId!.Value, TipoCargoChofer.FaltanteProducto, liquidacion.Id, liquidacion.FechaOperativa,
                    ReglasLogistica.ImporteCargo(-p.Diferencia!.Value, precio), p.ProductoId, -p.Diferencia, precio,
                    p.Lote.Length > 0 ? $"Lote {p.Lote}" : null, c);
            }

            // 4. Producto confirmado (V2-08: desde aquí el sobrante reintegrado está disponible en PT).
            Guardar(liquidacion, partidas);
            liquidacion.Estado = EstadoLiquidacion.ProductoConfirmado;
            liquidacion.TotalVenta = resumen.TotalVendido;
            liquidacion.ProductoConfirmadoEn = reloj.GetUtcNow();
            liquidacion.ProductoConfirmadoPor = usuario.Id;
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

    /// <summary>RN-LOG-20 <c>ProductoConfirmado → Revisada</c> (supervisor).</summary>
    public async Task<Result<LiquidacionDto>> RevisarAsync(Guid id, CancellationToken ct)
    {
        var liquidacion = await db.Liquidaciones.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidacion is null)
        {
            return NoEncontrada();
        }

        if (liquidacion.Estado != EstadoLiquidacion.ProductoConfirmado)
        {
            return Transicion(liquidacion.Estado, "revisar");
        }

        liquidacion.Estado = EstadoLiquidacion.Revisada;
        liquidacion.RevisadaEn = reloj.GetUtcNow();
        liquidacion.RevisadaPor = usuario.Id;
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>RN-LOG-21 <c>Revisada → ProductoConfirmado</c> con nota; el producto ya no cambia (RN-LOG-10).</summary>
    public async Task<Result<LiquidacionDto>> RegresarAsync(Guid id, RegresarLiquidacionRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Nota))
        {
            return Error.Campo("VAL-001", "nota", "Escribe por qué se regresa la liquidación.");
        }

        var liquidacion = await db.Liquidaciones.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidacion is null)
        {
            return NoEncontrada();
        }

        if (liquidacion.Estado != EstadoLiquidacion.Revisada)
        {
            return Transicion(liquidacion.Estado, "regresar");
        }

        var nota = $"Regresada a revisión: {r.Nota.Trim()}";
        liquidacion.Notas = liquidacion.Notas is null ? nota : $"{liquidacion.Notas}\n{nota}";
        liquidacion.Estado = EstadoLiquidacion.ProductoConfirmado;
        liquidacion.RevisadaEn = null;
        liquidacion.RevisadaPor = null;
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>
    /// RN-LOG-20 cierre del dinero (<c>Revisada</c>, o <c>ProductoConfirmado</c> sin revisión del supervisor). RN-LOG-08:
    /// efectivo esperado = contado en efectivo + cobros en efectivo − gastos autorizados; diferencia ≠ 0 → incidencia.
    /// Los totales de Ventas quedan como foto de solo lectura.
    /// </summary>
    public async Task<Result<LiquidacionDto>> CerrarAsync(Guid id, CerrarLiquidacionRequest r, CancellationToken ct)
    {
        var liquidacion = await db.Liquidaciones.Include(l => l.Gastos).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidacion is null)
        {
            return NoEncontrada();
        }

        var requiereRevision = await catalogos.ObtenerParametroAsync(ClavesParametro.RequiereRevisionSupervisor, true, ct);
        if (!ReglasLogistica.PuedeCerrar(liquidacion.Estado, requiereRevision))
        {
            return Transicion(liquidacion.Estado, "cerrar");
        }

        if (r.EfectivoEntregado is { } capturado)
        {
            if (capturado < 0)
            {
                return Error.Campo("VAL-001", "efectivoEntregado", "El efectivo entregado no puede ser negativo.");
            }

            liquidacion.EfectivoEntregado = capturado;
        }

        if (liquidacion.EfectivoEntregado is not { } entregado)
        {
            return Error.Campo(ErroresLogistica.SinConteo, "efectivoEntregado", "Captura el efectivo entregado antes de cerrar.");
        }

        var ruta = await db.Rutas.AsNoTracking().FirstAsync(x => x.Id == liquidacion.RutaId, ct);
        var resumen = await ventas.ResumenRutaDiaAsync(liquidacion.RutaId, liquidacion.FechaOperativa, ct);
        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            liquidacion.TotalGastos = GastosAutorizados(liquidacion);
            liquidacion.EfectivoEsperado = ReglasLogistica.EfectivoEsperado(resumen.VentasContadoEfectivo, resumen.CobrosEfectivo,
                liquidacion.TotalGastos);
            liquidacion.DiferenciaEfectivo = ReglasLogistica.DiferenciaEfectivo(entregado, liquidacion.EfectivoEsperado);
            liquidacion.TotalVenta ??= resumen.TotalVendido;
            liquidacion.TotalCredito = resumen.TotalCredito;
            liquidacion.TotalCobros = resumen.CobrosTotal;
            liquidacion.TotalTransferencias = resumen.VentasTransferencia + resumen.CobrosTransferencia;
            liquidacion.TotalCheques = resumen.VentasCheque;
            liquidacion.DiferenciaPrecios = resumen.DiferenciaPrecios;
            if (liquidacion.DiferenciaEfectivo != 0)
            {
                await incidencias.CrearAsync(TipoIncidencia.DiferenciaLiquidacion, "Liquidacion", liquidacion.Id, new
                {
                    liquidacion.Folio,
                    ruta = ruta.Codigo,
                    efectivoEsperado = liquidacion.EfectivoEsperado,
                    efectivoEntregado = liquidacion.EfectivoEntregado,
                    gastos = liquidacion.TotalGastos,
                    diferencia = liquidacion.DiferenciaEfectivo,
                }, c);
            }

            // RN-LOG-08/26: el faltante de efectivo se carga al chofer [SUP-21]; el sobrante solo es incidencia.
            if (liquidacion.DiferenciaEfectivo < 0 && liquidacion.ChoferId is { } chofer
                && await catalogos.ObtenerParametroAsync(ClavesParametro.CargoFaltanteEfectivo, true, c))
            {
                await cargos.AgregarAsync(chofer, TipoCargoChofer.FaltanteEfectivo, liquidacion.Id, liquidacion.FechaOperativa,
                    -liquidacion.DiferenciaEfectivo.Value, null, null, null, null, c);
            }

            liquidacion.Estado = EstadoLiquidacion.Cerrada;
            liquidacion.CerradaEn = reloj.GetUtcNow();
            liquidacion.CerradaPor = usuario.Id;
            await db.SaveChangesAsync(c);
            return Result.Exito();
        }, ct);

        return resultado.EsExito ? await ObtenerAsync(id, ct) : resultado.Error!;
    }

    /// <summary>Gastos de ruta: se capturan o corrigen hasta cerrar el dinero.</summary>
    public async Task<Result<IReadOnlyList<LiquidacionGastoDto>>> GastosAsync(Guid id, CancellationToken ct)
    {
        var resultado = await ObtenerAsync(id, ct);
        return resultado.EsExito ? Result<IReadOnlyList<LiquidacionGastoDto>>.Exito(resultado.Valor.Gastos) : resultado.Error!;
    }

    public async Task<Result<LiquidacionDto>> AgregarGastoAsync(Guid id, GastoLiquidacionRequest r, CancellationToken ct) =>
        await EditarGastoAsync(id, null, r, ct);

    public async Task<Result<LiquidacionDto>> ActualizarGastoAsync(Guid id, Guid gastoId, GastoLiquidacionRequest r, CancellationToken ct) =>
        await EditarGastoAsync(id, gastoId, r, ct);

    private async Task<Result<LiquidacionDto>> EditarGastoAsync(Guid id, Guid? gastoId, GastoLiquidacionRequest r, CancellationToken ct)
    {
        var liquidacion = await db.Liquidaciones.Include(l => l.Gastos).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (liquidacion is null)
        {
            return NoEncontrada();
        }

        if (liquidacion.Estado == EstadoLiquidacion.Cerrada)
        {
            return Cerrada();
        }

        LiquidacionGasto? gasto;
        if (gastoId is null)
        {
            gasto = new LiquidacionGasto
            {
                LiquidacionId = liquidacion.Id, CapturadoPor = usuario.Id ?? Guid.Empty, CapturadoEn = reloj.GetUtcNow(),
            };
            liquidacion.Gastos.Add(gasto);
            db.Entry(gasto).State = EntityState.Added;
        }
        else
        {
            gasto = liquidacion.Gastos.FirstOrDefault(g => g.Id == gastoId);
            if (gasto is null)
            {
                return Error.NoEncontrado(ErroresLogistica.LiquidacionNoEncontrada, "El gasto no existe en esta liquidación.");
            }
        }

        gasto.Concepto = r.Concepto.Trim();
        gasto.Importe = Dinero.Redondear(r.Importe);
        gasto.Comprobante = string.IsNullOrWhiteSpace(r.Comprobante) ? null : r.Comprobante.Trim();
        gasto.Autorizado = r.Autorizado;
        liquidacion.TotalGastos = GastosAutorizados(liquidacion);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    private static decimal GastosAutorizados(Liquidacion liquidacion) =>
        liquidacion.Gastos.Where(g => g.Autorizado).Sum(g => g.Importe);

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
                    CantidadACobro = guardada?.CantidadACobro,
                };
                mapa[llave] = p;
            }

            return p;
        }

        var cargas = await db.Envios.AsNoTracking().Include(e => e.Partidas)
            .Where(e => e.RutaId == ruta.Id && e.FechaOperativa == liquidacion.FechaOperativa
                && (e.Estado == EstadoEnvio.EnRuta || e.Estado == EstadoEnvio.Liquidado))
            .ToListAsync(ct);
        foreach (var carga in cargas)
        {
            foreach (var p in carga.Partidas)
            {
                var partida = Partida(p.ProductoId, p.Lote);
                partida.Cargado += p.CantidadBase;
                if (carga.Tipo == TipoEnvio.CargaInicial)
                {
                    partida.CargadoInicial += p.CantidadBase;
                }
                else
                {
                    partida.Recargado += p.CantidadBase;
                }
            }
        }

        foreach (var v in resumen.Vendido)
        {
            Partida(v.ProductoId, v.Lote).Vendido += v.CantidadBase;
        }

        var retornos = await db.Retornos.AsNoTracking().Include(r => r.Partidas)
            .Where(r => r.RutaId == ruta.Id && r.FechaOperativa == liquidacion.FechaOperativa && r.Estado == EstadoRetorno.Registrado
                && (r.Tipo == TipoRetorno.DevolucionCliente || r.Tipo == TipoRetorno.CambioFisico))
            .ToListAsync(ct);
        foreach (var p in retornos.SelectMany(r => r.Partidas))
        {
            var partida = Partida(p.ProductoId, p.Lote);
            if (p.Sentido == SentidoRetorno.Entregado)
            {
                partida.CambiosEntregados += p.CantidadBase;
            }
            else if (p.Condicion == CondicionRetorno.Vendible)
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

        // Columna "Total $": el importe vendido del producto se reparte entre sus lotes según lo vendido.
        foreach (var importe in resumen.ImportePorProducto ?? [])
        {
            var lotes = mapa.Values.Where(p => p.ProductoId == importe.ProductoId && p.Vendido > 0).OrderBy(p => p.Lote).ToList();
            if (lotes.Count == 0)
            {
                lotes = [Partida(importe.ProductoId, string.Empty)];
            }

            var partes = ReglasLogistica.Repartir(importe.Importe, lotes.Count == 1 ? [1m] : lotes.Select(p => p.Vendido).ToList());
            for (var k = 0; k < lotes.Count; k++)
            {
                lotes[k].ImporteVenta = partes[k];
            }
        }

        foreach (var p in mapa.Values)
        {
            p.SobranteEsperado = ReglasLogistica.SobranteEsperado(p.Cargado, p.Vendido, p.DevueltoClientesVendible, p.CambiosEntregados);
            p.Diferencia = p.SobranteContado is { } contado ? ReglasLogistica.Diferencia(contado, p.SobranteEsperado) : null;
        }

        await SugerirACobroAsync(liquidacion, ruta, mapa.Values.ToList(), retornos, ct);
        return mapa.Values.ToList();
    }

    /// <summary>
    /// RN-LOG-24: partidas que admiten producto a cobro y sugerencia cuando la antigüedad del lote supera la tolerancia del
    /// producto (o <c>logistica.dias_tolerancia_devolucion</c>) [SUP-19].
    /// </summary>
    private async Task SugerirACobroAsync(Liquidacion liquidacion, Ruta ruta, List<LiquidacionPartida> partidas, List<Retorno> retornos,
        CancellationToken ct)
    {
        var candidatas = partidas.Where(p => p.DevueltoClientesNoVendible > 0).ToList();
        if (candidatas.Count == 0 || !await AplicaProductoACobroAsync(liquidacion, ruta, ct))
        {
            return;
        }

        var productos = await catalogos.ObtenerProductosAsync(candidatas.Select(p => p.ProductoId), ct);
        var tolerancia = await catalogos.ObtenerParametroAsync(ClavesParametro.DiasToleranciaDevolucion, 11, ct);
        Dictionary<Llave, DateOnly?>? caducidades = null;
        foreach (var p in candidatas)
        {
            if (productos.GetValueOrDefault(p.ProductoId) is not { AplicaProductoACobro: true } producto)
            {
                continue;
            }

            p.AplicaACobro = true;
            DateOnly? caducidad = null;
            if (ReglasLogistica.AntiguedadLote(p.Lote, null, null, liquidacion.FechaOperativa) is null)
            {
                // Sin fecha en el lote: la caducidad viene del kardex de los retornos.
                if (caducidades is null)
                {
                    caducidades = [];
                    foreach (var retorno in retornos)
                    {
                        foreach (var m in await inventario.MovimientosDeDocumentoAsync(DocumentoInventario.Retorno, retorno.Id, ct))
                        {
                            caducidades[new Llave(m.ProductoId, m.Lote)] = m.FechaCaducidad ?? caducidades.GetValueOrDefault(new Llave(m.ProductoId, m.Lote));
                        }
                    }
                }

                caducidad = caducidades.GetValueOrDefault(new Llave(p.ProductoId, p.Lote));
            }

            var antiguedad = ReglasLogistica.AntiguedadLote(p.Lote, caducidad, producto.VidaUtilDias, liquidacion.FechaOperativa);
            p.CantidadACobroSugerida = ReglasLogistica.ACobroSugerido(p.DevueltoClientesNoVendible, antiguedad,
                producto.DiasToleranciaDevolucion ?? tolerancia);
        }
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
            guardada.CargadoInicial = nueva.CargadoInicial;
            guardada.Recargado = nueva.Recargado;
            guardada.Vendido = nueva.Vendido;
            guardada.ImporteVenta = nueva.ImporteVenta;
            guardada.DevueltoClientesVendible = nueva.DevueltoClientesVendible;
            guardada.DevueltoClientesNoVendible = nueva.DevueltoClientesNoVendible;
            guardada.CambiosEntregados = nueva.CambiosEntregados;
            guardada.SobranteEsperado = nueva.SobranteEsperado;
            guardada.SobranteContado = nueva.SobranteContado;
            guardada.Diferencia = nueva.Diferencia;
            guardada.DestinoSobrante = nueva.DestinoSobrante;
            guardada.MotivoMermaId = nueva.MotivoMermaId;
            guardada.CantidadACobro = nueva.CantidadACobro;
            guardada.PrecioACobro = nueva.PrecioACobro;
            guardada.AplicaACobro = nueva.AplicaACobro;
            guardada.CantidadACobroSugerida = nueva.CantidadACobroSugerida;
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

    /// <summary>RN-LOG-20: transición que no corresponde a la etapa actual (cerrada conserva LOG-007).</summary>
    private static Error Transicion(EstadoLiquidacion estado, string accion) =>
        estado == EstadoLiquidacion.Cerrada
            ? Cerrada()
            : Error.Conflicto(ErroresLogistica.TransicionInvalida, $"No se puede {accion} una liquidación en etapa {estado}.");
}
