using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Inventario.Publico;
using TG.Produccion.Dominio;
using TG.Produccion.Infraestructura;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;

namespace TG.Produccion.Aplicacion;

/// <param name="AlmacenId">RN-PRO-11: almacén del que sale el componente.</param>
public sealed record ConsumoDto(Guid Id, Guid ProductoId, string Sku, string Producto, string Unidad,
    decimal CantidadTeoricaBase, decimal? CantidadRealBase, string? LoteConsumido, decimal CostoUnitario, Guid AlmacenId,
    string Almacen);

public sealed record MermaDto(Guid Id, Guid ProductoId, string Sku, string Producto, string Unidad, decimal CantidadBase,
    Guid MotivoMermaId, string Motivo, string? Notas, DateTimeOffset RegistradaEn);

public sealed record MovimientoOrdenDto(Guid Id, TipoMovimiento Tipo, Guid AlmacenId, Guid ProductoId, string Sku,
    string Producto, string Unidad, string Lote, decimal Cantidad, decimal CostoUnitario, decimal CostoTotal);

public sealed record OrdenDto(
    Guid Id,
    string Folio,
    DateOnly FechaOperativa,
    Turno Turno,
    Guid ProductoId,
    string Sku,
    string Producto,
    string Unidad,
    Guid RecetaId,
    string Receta,
    int RecetaVersion,
    Guid? LineaId,
    string? Linea,
    decimal CantidadPlaneada,
    decimal? CantidadProducida,
    Guid AlmacenConsumoId,
    string AlmacenConsumo,
    Guid AlmacenDestinoId,
    string AlmacenDestino,
    string Lote,
    EstadoOrden Estado,
    DateTimeOffset? LiberadaEn,
    DateTimeOffset? IniciadaEn,
    DateTimeOffset? TerminadaEn,
    decimal? CostoTotal,
    decimal? CostoUnitario,
    decimal MermaTotal,
    string? Notas,
    IReadOnlyList<ConsumoDto> Consumos,
    IReadOnlyList<MermaDto> Mermas,
    IReadOnlyList<MovimientoOrdenDto> Movimientos);

public sealed record PlanearOrdenRequest(Guid ProductoId, Guid? RecetaId, DateOnly? FechaOperativa, Turno Turno,
    Guid? LineaId, decimal CantidadPlaneada, Guid? AlmacenConsumoId, Guid? AlmacenDestinoId, string? Notas);

public sealed class PlanearOrdenValidator : AbstractValidator<PlanearOrdenRequest>
{
    public PlanearOrdenValidator()
    {
        RuleFor(x => x.ProductoId).NotEmpty().WithMessage("Elige el producto.");
        RuleFor(x => x.CantidadPlaneada).GreaterThan(0).WithMessage("La cantidad planeada debe ser mayor que cero.");
    }
}

/// <param name="CantidadRealBase">Nulo = deja lo capturado antes.</param>
/// <param name="AlmacenId">RN-PRO-11: cambia el almacén del que sale el componente; nulo = sin cambio.</param>
public sealed record ConsumoRealRequest(Guid ProductoId, decimal? CantidadRealBase, string? LoteConsumido, Guid? AlmacenId = null);

public sealed record CapturarConsumosRequest(IReadOnlyList<ConsumoRealRequest> Consumos);

public sealed class CapturarConsumosValidator : AbstractValidator<CapturarConsumosRequest>
{
    public CapturarConsumosValidator()
    {
        RuleFor(x => x.Consumos).NotEmpty();
        RuleForEach(x => x.Consumos).ChildRules(c =>
            c.RuleFor(x => x.CantidadRealBase).GreaterThanOrEqualTo(0).When(x => x.CantidadRealBase is not null)
                .WithMessage("El consumo no puede ser negativo."));
    }
}

public sealed record RegistrarMermaRequest(Guid ProductoId, decimal CantidadBase, Guid MotivoMermaId, string? Notas);

public sealed class RegistrarMermaValidator : AbstractValidator<RegistrarMermaRequest>
{
    public RegistrarMermaValidator()
    {
        RuleFor(x => x.ProductoId).NotEmpty();
        RuleFor(x => x.CantidadBase).GreaterThan(0).WithMessage("La merma debe ser mayor que cero.");
        RuleFor(x => x.MotivoMermaId).NotEmpty().WithMessage("Elige el motivo de la merma.");
    }
}

public sealed record TerminarOrdenRequest(decimal CantidadProducida, string? Notas);

public sealed class TerminarOrdenValidator : AbstractValidator<TerminarOrdenRequest>
{
    // RN-PRO-09: cantidad producida obligatoria y mayor que cero.
    public TerminarOrdenValidator() =>
        RuleFor(x => x.CantidadProducida).GreaterThan(0).WithMessage("La cantidad producida debe ser mayor que cero.");
}

/// <summary>Órdenes de producción: planear, liberar, iniciar, capturar, terminar y cancelar (RN-PRO-02 a 10).</summary>
public sealed class OrdenesHandler(
    ProduccionDbContext db,
    ICatalogosService catalogos,
    IInventarioService inventario,
    IGeneradorFolios folios,
    IFechaOperativa fechaOperativa,
    IUsuarioActual usuario,
    IUnitOfWork unitOfWork,
    TimeProvider reloj)
{
    public async Task<IReadOnlyList<OrdenDto>> ListarAsync(DateOnly? fecha, DateOnly? desde, DateOnly? hasta, Turno? turno,
        Guid? lineaId, EstadoOrden? estado, Guid? productoId, CancellationToken ct)
    {
        var consulta = Consulta();
        if (fecha is not null)
        {
            consulta = consulta.Where(o => o.FechaOperativa == fecha);
        }

        if (desde is not null)
        {
            consulta = consulta.Where(o => o.FechaOperativa >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(o => o.FechaOperativa <= hasta);
        }

        if (turno is not null)
        {
            consulta = consulta.Where(o => o.Turno == turno);
        }

        if (lineaId is not null)
        {
            consulta = consulta.Where(o => o.LineaId == lineaId);
        }

        if (estado is not null)
        {
            consulta = consulta.Where(o => o.Estado == estado);
        }

        if (productoId is not null)
        {
            consulta = consulta.Where(o => o.ProductoId == productoId);
        }

        var ordenes = await consulta.OrderByDescending(o => o.FechaOperativa).ThenBy(o => o.Turno).ThenBy(o => o.Folio)
            .Take(500).ToListAsync(ct);
        return await MapearAsync(ordenes, conMovimientos: false, ct);
    }

    public async Task<Result<OrdenDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var orden = await Consulta().FirstOrDefaultAsync(o => o.Id == id, ct);
        return orden is null ? ErroresProduccion.OrdenNoEncontrada : (await MapearAsync([orden], conMovimientos: true, ct))[0];
    }

    public async Task<Result<OrdenDto>> PlanearAsync(PlanearOrdenRequest r, CancellationToken ct)
    {
        var orden = new OrdenProduccion
        {
            Folio = await folios.SiguienteAsync(ProduccionDbContext.Esquema, "OP", ct),
            ResponsableId = usuario.Id,
        };
        var aplicado = await AplicarAsync(orden, r, ct);
        if (!aplicado.EsExito)
        {
            return aplicado.Error!;
        }

        db.Ordenes.Add(orden);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(orden.Id, ct);
    }

    public async Task<Result<OrdenDto>> ActualizarAsync(Guid id, PlanearOrdenRequest r, CancellationToken ct)
    {
        var orden = await db.Ordenes.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (orden is null)
        {
            return ErroresProduccion.OrdenNoEncontrada;
        }

        if (orden.Estado != EstadoOrden.Planeada)
        {
            return ErroresProduccion.EstadoInvalido("Solo se edita una orden Planeada.");
        }

        var aplicado = await AplicarAsync(orden, r, ct);
        if (!aplicado.EsExito)
        {
            return aplicado.Error!;
        }

        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>
    /// Liberar fija la receta (si la planeada ya no es Vigente, toma la Vigente actual), genera el lote (RN-PRO-10) y
    /// los consumos teóricos (RN-PRO-02).
    /// </summary>
    public async Task<Result<OrdenDto>> LiberarAsync(Guid id, CancellationToken ct)
    {
        var orden = await db.Ordenes.Include(o => o.Consumos).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (orden is null)
        {
            return ErroresProduccion.OrdenNoEncontrada;
        }

        if (orden.Estado != EstadoOrden.Planeada)
        {
            return ErroresProduccion.EstadoInvalido("Solo se libera una orden Planeada.");
        }

        var receta = await db.Recetas.AsNoTracking().Include(r => r.Componentes).FirstOrDefaultAsync(r => r.Id == orden.RecetaId, ct);
        if (receta is null || receta.Estado != EstadoReceta.Vigente)
        {
            receta = await db.Recetas.AsNoTracking().Include(r => r.Componentes)
                .FirstOrDefaultAsync(r => r.ProductoId == orden.ProductoId && r.Estado == EstadoReceta.Vigente, ct);
            if (receta is null)
            {
                return ErroresProduccion.SinRecetaVigente;
            }
        }

        orden.RecetaId = receta.Id;
        orden.Lote = ReglasProduccion.Lote(orden.FechaOperativa, orden.Folio);
        orden.Estado = EstadoOrden.Liberada;
        orden.LiberadaEn = reloj.GetUtcNow();
        orden.Consumos.Clear();
        foreach (var componente in receta.Componentes.OrderBy(c => c.Orden))
        {
            // RN-PRO-11: el almacén predeterminado del componente, si sigue activo; si no, el de consumo de la orden.
            var almacen = componente.AlmacenId is { } predeterminado ? await AlmacenAsync(predeterminado, "MP", ct) : null;
            orden.Consumos.Add(new OrdenConsumo
            {
                OrdenProduccionId = orden.Id,
                ProductoId = componente.ProductoId,
                AlmacenId = almacen?.Id ?? orden.AlmacenConsumoId,
                CantidadTeoricaBase = ReglasProduccion.ConsumoTeorico(componente.CantidadBase, orden.CantidadPlaneada,
                    receta.RendimientoCantidad),
            });
        }

        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    public async Task<Result<OrdenDto>> IniciarAsync(Guid id, CancellationToken ct)
    {
        var orden = await db.Ordenes.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (orden is null)
        {
            return ErroresProduccion.OrdenNoEncontrada;
        }

        if (orden.Estado != EstadoOrden.Liberada)
        {
            return ErroresProduccion.EstadoInvalido("Solo se inicia una orden Liberada.");
        }

        orden.Estado = EstadoOrden.EnProceso;
        orden.IniciadaEn = reloj.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    public async Task<Result<OrdenDto>> CancelarAsync(Guid id, CancellationToken ct)
    {
        var orden = await db.Ordenes.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (orden is null)
        {
            return ErroresProduccion.OrdenNoEncontrada;
        }

        if (orden.Estado is not (EstadoOrden.Planeada or EstadoOrden.Liberada))
        {
            return ErroresProduccion.EstadoInvalido("Solo se cancela una orden Planeada o Liberada.");
        }

        orden.Estado = EstadoOrden.Cancelada;
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    public async Task<Result<OrdenDto>> CapturarConsumosAsync(Guid id, CapturarConsumosRequest r, CancellationToken ct)
    {
        var orden = await db.Ordenes.Include(o => o.Consumos).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (orden is null)
        {
            return ErroresProduccion.OrdenNoEncontrada;
        }

        if (orden.Estado is not (EstadoOrden.Liberada or EstadoOrden.EnProceso))
        {
            return ErroresProduccion.EstadoInvalido("Los consumos se capturan con la orden Liberada o En proceso.");
        }

        var productos = await catalogos.ObtenerProductosAsync(r.Consumos.Select(c => c.ProductoId), ct);
        foreach (var captura in r.Consumos)
        {
            var consumo = orden.Consumos.FirstOrDefault(c => c.ProductoId == captura.ProductoId);
            if (consumo is null || !productos.TryGetValue(captura.ProductoId, out var producto))
            {
                return ErroresProduccion.ComponenteAjeno;
            }

            if (captura.CantidadRealBase is { } real && decimal.Round(real, producto.UnidadBaseDecimales) != real)
            {
                return Error.Campo("CAT-006", "cantidadRealBase",
                    $"La cantidad de {producto.Nombre} admite máximo {producto.UnidadBaseDecimales} decimales.");
            }

            // RN-PRO-11.
            if (captura.AlmacenId is { } almacenId)
            {
                if (await AlmacenAsync(almacenId, "MP", ct) is null)
                {
                    return ErroresProduccion.AlmacenInvalido("almacenId");
                }

                consumo.AlmacenId = almacenId;
            }

            if (captura.CantidadRealBase is not null)
            {
                consumo.CantidadRealBase = captura.CantidadRealBase;
                consumo.LoteConsumido = producto.ManejaLote && !string.IsNullOrWhiteSpace(captura.LoteConsumido)
                    ? captura.LoteConsumido.Trim().ToUpperInvariant()
                    : null;
            }
        }

        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>RN-PRO-07: la merma de producción requiere un motivo con <c>aplica_produccion</c>.</summary>
    public async Task<Result<OrdenDto>> RegistrarMermaAsync(Guid id, RegistrarMermaRequest r, CancellationToken ct)
    {
        var orden = await db.Ordenes.Include(o => o.Consumos).Include(o => o.Mermas).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (orden is null)
        {
            return ErroresProduccion.OrdenNoEncontrada;
        }

        if (orden.Estado is not (EstadoOrden.Liberada or EstadoOrden.EnProceso))
        {
            return ErroresProduccion.EstadoInvalido("Las mermas se registran con la orden Liberada o En proceso.");
        }

        if (r.ProductoId != orden.ProductoId && orden.Consumos.All(c => c.ProductoId != r.ProductoId))
        {
            return ErroresProduccion.ComponenteAjeno;
        }

        var motivo = await catalogos.ObtenerMotivoAsync(r.MotivoMermaId, ct);
        if (motivo is null || !motivo.Activo || !motivo.AplicaProduccion)
        {
            return ErroresProduccion.MotivoNoAplica;
        }

        orden.Mermas.Add(new OrdenMerma
        {
            OrdenProduccionId = orden.Id,
            ProductoId = r.ProductoId,
            CantidadBase = r.CantidadBase,
            MotivoMermaId = r.MotivoMermaId,
            Notas = string.IsNullOrWhiteSpace(r.Notas) ? null : r.Notas.Trim(),
            RegistradaEn = reloj.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>
    /// RN-PRO-03 a 06 y 10. En una transacción: <c>SalidaConsumo</c> por componente (consumo real o teórico recalculado),
    /// <c>EntradaProduccion</c> del lote de la orden y <c>Merma</c> por cada merma registrada.
    /// <para>
    /// La merma del producto de la orden sale del mismo lote después de entrar: se registra entrada por
    /// <c>producida + merma</c> y salida por la merma, de modo que el lote queda con la cantidad producida (buena).
    /// La merma de un componente sale del almacén de consumo (FEFO). El costo de la merma del producto se absorbe en el
    /// costo unitario (RN-PRO-05: consumos / producida).
    /// </para>
    /// </summary>
    public async Task<Result<OrdenDto>> TerminarAsync(Guid id, TerminarOrdenRequest r, CancellationToken ct)
    {
        if (r.CantidadProducida <= 0)
        {
            return ErroresProduccion.CantidadProducidaInvalida;
        }

        var permitirSinExistencia = await catalogos.ObtenerParametroAsync(ClavesParametro.ProduccionConsumoSinExistencia, false, ct);
        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var orden = await db.Ordenes.Include(o => o.Consumos).Include(o => o.Mermas).FirstOrDefaultAsync(o => o.Id == id, c);
            if (orden is null)
            {
                return Result.Falla(ErroresProduccion.OrdenNoEncontrada);
            }

            if (orden.Estado != EstadoOrden.EnProceso)
            {
                return Result.Falla(ErroresProduccion.EstadoInvalido("Solo se termina una orden En proceso."));
            }

            var producto = await catalogos.ObtenerProductoAsync(orden.ProductoId, c);
            if (producto is null)
            {
                return Result.Falla(ErroresProduccion.ProductoNoProducible);
            }

            if (decimal.Round(r.CantidadProducida, producto.UnidadBaseDecimales) != r.CantidadProducida)
            {
                return Result.Falla(Error.Campo("CAT-006", "cantidadProducida",
                    $"La cantidad admite máximo {producto.UnidadBaseDecimales} decimales."));
            }

            var usuarioId = usuario.Id ?? Guid.Empty;

            // 1. Consumos.
            foreach (var consumo in orden.Consumos)
            {
                consumo.CantidadRealBase = ReglasProduccion.ConsumoReal(consumo.CantidadRealBase, consumo.CantidadTeoricaBase,
                    orden.CantidadPlaneada, r.CantidadProducida);
            }

            var salidas = orden.Consumos.Where(x => x.CantidadRealBase > 0)
                .Select(x => new LineaMovimiento(TipoMovimiento.SalidaConsumo, x.AlmacenId, x.ProductoId,
                    x.LoteConsumido, -x.CantidadRealBase!.Value))
                .ToList();
            var movConsumo = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.OrdenProduccion, orden.Id, orden.Folio, orden.FechaOperativa, usuarioId, salidas,
                PermitirNegativo: permitirSinExistencia), c);
            if (!movConsumo.EsExito)
            {
                return Result.Falla(MapearError(movConsumo.Error!));
            }

            foreach (var consumo in orden.Consumos)
            {
                var propios = movConsumo.Valor.Where(m => m.ProductoId == consumo.ProductoId).ToList();
                var cantidad = -propios.Sum(m => m.Cantidad);
                consumo.CostoUnitario = cantidad == 0 ? 0 : Dinero.RedondearCosto(-propios.Sum(m => m.CostoTotal) / cantidad);
            }

            var costoConsumos = -movConsumo.Valor.Sum(m => m.CostoTotal);
            var costoUnitario = ReglasProduccion.CostoUnitario(costoConsumos, r.CantidadProducida);

            // 2. Entrada del producto y mermas.
            var mermaProducto = orden.Mermas.Where(m => m.ProductoId == orden.ProductoId).Sum(m => m.CantidadBase);
            var caducidad = ReglasProduccion.Caducidad(orden.FechaOperativa, producto.VidaUtilDias);
            var lote = producto.ManejaLote ? orden.Lote : null;
            var lineas = new List<LineaMovimiento>
            {
                new(TipoMovimiento.EntradaProduccion, orden.AlmacenDestinoId, orden.ProductoId, lote,
                    r.CantidadProducida + mermaProducto, costoUnitario, FechaCaducidad: caducidad),
            };
            foreach (var merma in orden.Mermas)
            {
                lineas.Add(merma.ProductoId == orden.ProductoId
                    ? new LineaMovimiento(TipoMovimiento.Merma, orden.AlmacenDestinoId, merma.ProductoId, lote,
                        -merma.CantidadBase, costoUnitario, merma.MotivoMermaId, Notas: merma.Notas)
                    : new LineaMovimiento(TipoMovimiento.Merma, AlmacenDelComponente(orden, merma.ProductoId), merma.ProductoId, null,
                        -merma.CantidadBase, null, merma.MotivoMermaId, Notas: merma.Notas));
            }

            var movProducto = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                DocumentoInventario.OrdenProduccion, orden.Id, orden.Folio, orden.FechaOperativa, usuarioId, lineas,
                PermitirNegativo: permitirSinExistencia), c);
            if (!movProducto.EsExito)
            {
                return Result.Falla(MapearError(movProducto.Error!));
            }

            orden.CantidadProducida = r.CantidadProducida;
            orden.CostoUnitario = costoUnitario;
            orden.CostoTotal = Dinero.Redondear(costoConsumos);
            orden.Estado = EstadoOrden.Terminada;
            orden.TerminadaEn = reloj.GetUtcNow();
            if (!string.IsNullOrWhiteSpace(r.Notas))
            {
                orden.Notas = string.IsNullOrWhiteSpace(orden.Notas) ? r.Notas.Trim() : $"{orden.Notas}\n{r.Notas.Trim()}";
            }

            await db.SaveChangesAsync(c);
            return Result.Exito();
        }, ct);

        return resultado.EsExito ? await ObtenerAsync(id, ct) : resultado.Error!;
    }

    /// <summary>RN-PRO-06: la falta de existencia de un componente se informa como <c>PRO-002</c>.</summary>
    private static Error MapearError(Error error) =>
        error.Codigo == ErroresInventario.ExistenciaInsuficiente
            ? error with { Codigo = ErroresProduccion.SinExistencia }
            : error;

    private async Task<Result> AplicarAsync(OrdenProduccion orden, PlanearOrdenRequest r, CancellationToken ct)
    {
        var producto = await catalogos.ObtenerProductoAsync(r.ProductoId, ct);
        if (producto is null || !producto.EsProducible || !producto.Activo)
        {
            return ErroresProduccion.ProductoNoProducible;
        }

        if (decimal.Round(r.CantidadPlaneada, producto.UnidadBaseDecimales) != r.CantidadPlaneada)
        {
            return Error.Campo("CAT-006", "cantidadPlaneada", $"La cantidad admite máximo {producto.UnidadBaseDecimales} decimales.");
        }

        Receta? receta;
        if (r.RecetaId is { } recetaId)
        {
            receta = await db.Recetas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == recetaId, ct);
            if (receta is null || receta.ProductoId != r.ProductoId || receta.Estado != EstadoReceta.Vigente)
            {
                return Error.Campo("PRO-003", "recetaId", "La receta no existe, no es del producto o no está Vigente.");
            }
        }
        else
        {
            receta = await db.Recetas.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ProductoId == r.ProductoId && x.Estado == EstadoReceta.Vigente, ct);
            if (receta is null)
            {
                return ErroresProduccion.SinRecetaVigente;
            }
        }

        if (r.LineaId is { } lineaId && !await db.Lineas.AnyAsync(l => l.Id == lineaId && l.Activo, ct))
        {
            return ErroresProduccion.LineaNoEncontrada;
        }

        // Por defecto se consume de MP. El destino es PT para terminados; los semiterminados (masa) entran a MP para
        // que las órdenes del siguiente nivel los consuman de su almacén de consumo (multinivel por inventario, [SUP-12]).
        var consumo = await AlmacenAsync(r.AlmacenConsumoId, "MP", ct);
        if (consumo is null)
        {
            return ErroresProduccion.AlmacenInvalido("almacenConsumoId");
        }

        var destino = await AlmacenAsync(r.AlmacenDestinoId, producto.Tipo == "Semiterminado" ? "MP" : "PT", ct);
        if (destino is null)
        {
            return ErroresProduccion.AlmacenInvalido("almacenDestinoId");
        }

        orden.ProductoId = r.ProductoId;
        orden.RecetaId = receta.Id;
        orden.FechaOperativa = r.FechaOperativa ?? fechaOperativa.Hoy();
        orden.Turno = r.Turno;
        orden.LineaId = r.LineaId;
        orden.CantidadPlaneada = r.CantidadPlaneada;
        orden.AlmacenConsumoId = consumo.Id;
        orden.AlmacenDestinoId = destino.Id;
        orden.Notas = string.IsNullOrWhiteSpace(r.Notas) ? null : r.Notas.Trim();
        return Result.Exito();
    }

    /// <summary>RN-PRO-11: la merma de un componente sale del mismo almacén que su consumo.</summary>
    private static Guid AlmacenDelComponente(OrdenProduccion orden, Guid productoId) =>
        orden.Consumos.FirstOrDefault(c => c.ProductoId == productoId)?.AlmacenId ?? orden.AlmacenConsumoId;

    private async Task<AlmacenInfo?> AlmacenAsync(Guid? id, string codigoPredeterminado, CancellationToken ct)
    {
        var almacen = id is { } valor
            ? await inventario.ObtenerAlmacenAsync(valor, ct)
            : await inventario.ObtenerAlmacenPorCodigoAsync(codigoPredeterminado, ct);
        return almacen is { Activo: true } && almacen.Tipo != TipoAlmacen.Ruta ? almacen : null;
    }

    private IQueryable<OrdenProduccion> Consulta() =>
        db.Ordenes.AsNoTracking().Include(o => o.Consumos).Include(o => o.Mermas);

    private async Task<IReadOnlyList<OrdenDto>> MapearAsync(List<OrdenProduccion> ordenes, bool conMovimientos, CancellationToken ct)
    {
        var recetaIds = ordenes.Select(o => o.RecetaId).Distinct().ToList();
        var recetas = await db.Recetas.AsNoTracking().Where(r => recetaIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => (r.Nombre, r.NumeroVersion), ct);
        var lineas = await db.Lineas.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Nombre, ct);
        var almacenes = (await inventario.ListarAlmacenesAsync(ct)).ToDictionary(a => a.Id, a => a.Codigo);
        var movimientos = new Dictionary<Guid, IReadOnlyList<MovimientoRegistrado>>();
        if (conMovimientos)
        {
            foreach (var orden in ordenes)
            {
                movimientos[orden.Id] = await inventario.MovimientosDeDocumentoAsync(DocumentoInventario.OrdenProduccion, orden.Id, ct);
            }
        }

        var productoIds = ordenes.Select(o => o.ProductoId)
            .Concat(ordenes.SelectMany(o => o.Consumos).Select(c => c.ProductoId))
            .Concat(ordenes.SelectMany(o => o.Mermas).Select(m => m.ProductoId))
            .Concat(movimientos.Values.SelectMany(m => m).Select(m => m.ProductoId));
        var productos = await catalogos.ObtenerProductosAsync(productoIds, ct);
        var motivos = (await catalogos.ListarMotivosAsync(ct)).ToDictionary(m => m.Id, m => m.Nombre);

        string Sku(Guid p) => productos.GetValueOrDefault(p)?.Sku ?? "?";
        string Nombre(Guid p) => productos.GetValueOrDefault(p)?.Nombre ?? "?";
        string Unidad(Guid p) => productos.GetValueOrDefault(p)?.UnidadBaseClave ?? string.Empty;

        return ordenes.Select(o =>
        {
            var receta = recetas.GetValueOrDefault(o.RecetaId);
            return new OrdenDto(o.Id, o.Folio, o.FechaOperativa, o.Turno, o.ProductoId, Sku(o.ProductoId), Nombre(o.ProductoId),
                Unidad(o.ProductoId), o.RecetaId, receta.Nombre ?? "?", receta.NumeroVersion, o.LineaId,
                o.LineaId is { } l ? lineas.GetValueOrDefault(l) : null, o.CantidadPlaneada, o.CantidadProducida,
                o.AlmacenConsumoId, almacenes.GetValueOrDefault(o.AlmacenConsumoId) ?? "?", o.AlmacenDestinoId,
                almacenes.GetValueOrDefault(o.AlmacenDestinoId) ?? "?", o.Lote, o.Estado, o.LiberadaEn, o.IniciadaEn,
                o.TerminadaEn, o.CostoTotal, o.CostoUnitario, o.Mermas.Sum(m => m.CantidadBase), o.Notas,
                o.Consumos.Select(c => new ConsumoDto(c.Id, c.ProductoId, Sku(c.ProductoId), Nombre(c.ProductoId),
                    Unidad(c.ProductoId), c.CantidadTeoricaBase, c.CantidadRealBase, c.LoteConsumido, c.CostoUnitario,
                    c.AlmacenId, almacenes.GetValueOrDefault(c.AlmacenId) ?? "?")).ToList(),
                o.Mermas.OrderBy(m => m.RegistradaEn).Select(m => new MermaDto(m.Id, m.ProductoId, Sku(m.ProductoId),
                    Nombre(m.ProductoId), Unidad(m.ProductoId), m.CantidadBase, m.MotivoMermaId,
                    motivos.GetValueOrDefault(m.MotivoMermaId) ?? "?", m.Notas, m.RegistradaEn)).ToList(),
                (movimientos.GetValueOrDefault(o.Id) ?? []).Select(m => new MovimientoOrdenDto(m.Id, m.Tipo, m.AlmacenId,
                    m.ProductoId, Sku(m.ProductoId), Nombre(m.ProductoId), Unidad(m.ProductoId), m.Lote, m.Cantidad, m.CostoUnitario,
                    m.CostoTotal))
                    .ToList());
        }).ToList();
    }
}
