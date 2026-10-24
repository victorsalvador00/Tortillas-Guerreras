using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Logistica.Aplicacion;

public sealed record PedidoSugeridoDto(Guid PedidoId, string Folio, string Cliente, DateOnly FechaEntrega);

/// <param name="PendienteBase">Cambios pendientes de los clientes que se visitan ese día (solo en una carga inicial nueva).</param>
public sealed record PartidaSugeridaDto(Guid ProductoId, string Sku, string Producto, string Unidad, decimal CantidadBase, decimal CambioFisicoBase,
    decimal PendienteBase);

/// <param name="EnvioId">Carga inicial <c>Preparado</c> de la ruta y fecha a la que se agregarían los pedidos.</param>
public sealed record CargaSugeridaDto(Guid RutaId, string RutaCodigo, string RutaNombre, Guid? EnvioId, string? EnvioFolio,
    IReadOnlyList<PedidoSugeridoDto> Pedidos, IReadOnlyList<PartidaSugeridaDto> Partidas);

public sealed record CargasSugeridasDto(DateOnly Fecha, IReadOnlyList<CargaSugeridaDto> Rutas);

public sealed record GenerarSugeridasRequest(DateOnly? Fecha, IReadOnlyList<Guid>? RutaIds);

public sealed record CargaGeneradaDto(Guid RutaId, string RutaCodigo, Guid EnvioId, string Folio, bool Nueva, int Pedidos);

public sealed record CargaOmitidaDto(Guid RutaId, string RutaCodigo, string Codigo, string Mensaje);

public sealed record GenerarSugeridasDto(IReadOnlyList<CargaGeneradaDto> Generadas, IReadOnlyList<CargaOmitidaDto> Omitidas);

/// <summary>
/// RN-LOG-14: cargas sugeridas del día desde los pedidos confirmados sin carga. Los cambios pendientes se agregan en T-8.9.
/// </summary>
public sealed class SugeridosHandler(
    LogisticaDbContext db,
    EnviosHandler envios,
    IVentasService ventas,
    ILogisticaService logistica,
    ICatalogosService catalogos,
    IFechaOperativa fechaOperativa)
{
    private sealed record PedidoRuta(Guid RutaId, PedidoParaCargaInfo Pedido);

    public async Task<CargasSugeridasDto> ObtenerAsync(DateOnly? fecha, CancellationToken ct)
    {
        var dia = fecha ?? fechaOperativa.Hoy();
        var porRuta = await PedidosPorRutaAsync(dia, ct);
        var rutas = await db.Rutas.AsNoTracking().Where(r => r.Activo).ToDictionaryAsync(r => r.Id, ct);
        var preparadas = await CargasPreparadasAsync(dia, ct);

        // RN-LOG-14: los cambios pendientes van en la primera carga inicial del día de la ruta (no se repiten al agregar
        // pedidos a una carga existente ni si ya salió otra carga inicial).
        var conCargaInicial = (await db.Envios.AsNoTracking()
                .Where(e => e.FechaOperativa == dia && e.Tipo == TipoEnvio.CargaInicial && e.Estado != EstadoEnvio.Cancelado)
                .Select(e => e.RutaId).Distinct().ToListAsync(ct)).ToHashSet();
        var pendientes = (await logistica.CambiosPendientesDelDiaAsync(dia, ct)).Where(p => !conCargaInicial.Contains(p.RutaId)).ToList();
        var productos = await catalogos.ObtenerProductosAsync(porRuta.SelectMany(p => p.Pedido.Partidas).Select(x => x.ProductoId)
            .Concat(pendientes.Select(p => p.ProductoId)), ct);
        var resultado = porRuta.Where(p => rutas.ContainsKey(p.RutaId)).GroupBy(p => p.RutaId)
            .Select(g =>
            {
                var ruta = rutas[g.Key];
                var carga = preparadas.TryGetValue(g.Key, out var c) ? c : ((Guid Id, string Folio)?)null;
                var pendientesRuta = pendientes.Where(p => p.RutaId == g.Key).ToList();
                var partidas = g.SelectMany(p => p.Pedido.Partidas).Select(x => (x.ProductoId, x.Tipo, Cantidad: x.PendienteBase, Pendiente: 0m))
                    .Concat(pendientesRuta.Select(p => (p.ProductoId, Tipo: TipoPartidaPedido.CambioFisico, Cantidad: p.RestanteBase, Pendiente: p.RestanteBase)))
                    .GroupBy(x => x.ProductoId)
                    .Select(x =>
                    {
                        var producto = productos.GetValueOrDefault(x.Key);
                        return new PartidaSugeridaDto(x.Key, producto?.Sku ?? "?", producto?.Nombre ?? "?", producto?.UnidadBaseClave ?? "",
                            x.Sum(y => y.Cantidad), x.Where(y => y.Tipo == TipoPartidaPedido.CambioFisico).Sum(y => y.Cantidad), x.Sum(y => y.Pendiente));
                    })
                    .OrderBy(x => x.Sku).ToList();
                return new CargaSugeridaDto(ruta.Id, ruta.Codigo, ruta.Nombre, carga?.Id, carga?.Folio,
                    g.Select(p => new PedidoSugeridoDto(p.Pedido.PedidoId, p.Pedido.Folio, p.Pedido.Cliente, p.Pedido.FechaEntrega)).ToList(),
                    partidas);
            })
            .OrderBy(c => c.RutaCodigo).ToList();
        return new CargasSugeridasDto(dia, resultado);
    }

    /// <summary>Una operación por ruta: si una falla (p. ej. ruta sin chofer, LOG-017) se omite y las demás siguen.</summary>
    public async Task<GenerarSugeridasDto> GenerarAsync(GenerarSugeridasRequest r, CancellationToken ct)
    {
        var sugeridas = await ObtenerAsync(r.Fecha, ct);
        var generadas = new List<CargaGeneradaDto>();
        var omitidas = new List<CargaOmitidaDto>();
        foreach (var s in sugeridas.Rutas.Where(s => r.RutaIds is not { Count: > 0 } || r.RutaIds.Contains(s.RutaId)))
        {
            var pedidoIds = s.Pedidos.Select(p => p.PedidoId).ToList();
            var nuevas = s.Partidas.Select(p => new EnvioPartidaRequest(p.ProductoId, p.CantidadBase, null, null)).ToList();
            Result<EnvioDto> guardado;
            if (s.EnvioId is { } envioId)
            {
                // Se suma a la carga existente: las partidas sin lote del mismo producto se acumulan.
                var actual = await db.Envios.AsNoTracking().Include(e => e.Partidas).FirstAsync(e => e.Id == envioId, ct);
                var partidas = actual.Partidas.Select(p => new EnvioPartidaRequest(p.ProductoId, p.CantidadBase, null, p.Lote == "" ? null : p.Lote))
                    .Concat(nuevas)
                    .GroupBy(p => (p.ProductoId, p.Lote))
                    .Select(g => new EnvioPartidaRequest(g.Key.ProductoId, g.Sum(p => p.Cantidad), null, g.Key.Lote))
                    .ToList();
                guardado = await envios.ActualizarAsync(envioId, new GuardarEnvioRequest(s.RutaId, TipoEnvio.CargaInicial, actual.FechaOperativa,
                    actual.AlmacenOrigenId, actual.Notas, partidas, pedidoIds), ct);
            }
            else
            {
                guardado = await envios.CrearAsync(new GuardarEnvioRequest(s.RutaId, TipoEnvio.CargaInicial, sugeridas.Fecha, null,
                    "Carga sugerida", nuevas, pedidoIds), ct);
            }

            if (guardado.EsExito)
            {
                generadas.Add(new CargaGeneradaDto(s.RutaId, s.RutaCodigo, guardado.Valor.Id, guardado.Valor.Folio, s.EnvioId is null, pedidoIds.Count));
            }
            else
            {
                omitidas.Add(new CargaOmitidaDto(s.RutaId, s.RutaCodigo, guardado.Error!.Codigo, guardado.Error.Mensaje));
            }
        }

        return new GenerarSugeridasDto(generadas, omitidas);
    }

    /// <summary>La ruta del pedido es la asignada o, si no tiene, la ruta activa del cliente.</summary>
    private async Task<List<PedidoRuta>> PedidosPorRutaAsync(DateOnly dia, CancellationToken ct)
    {
        var pedidos = await ventas.PedidosParaCargaAsync(dia, ct);
        var sinRuta = pedidos.Where(p => p.RutaId is null).Select(p => p.ClienteId).Distinct().ToList();
        var rutasClientes = await logistica.RutasDeClientesAsync(sinRuta, ct);
        return pedidos.Select(p => new PedidoRuta(p.RutaId ?? rutasClientes.GetValueOrDefault(p.ClienteId), p))
            .Where(p => p.RutaId != Guid.Empty).ToList();
    }

    private async Task<Dictionary<Guid, (Guid Id, string Folio)>> CargasPreparadasAsync(DateOnly dia, CancellationToken ct) =>
        (await db.Envios.AsNoTracking()
            .Where(e => e.FechaOperativa == dia && e.Tipo == TipoEnvio.CargaInicial && e.Estado == EstadoEnvio.Preparado)
            .OrderBy(e => e.Folio).Select(e => new { e.RutaId, e.Id, e.Folio }).ToListAsync(ct))
        .GroupBy(e => e.RutaId).ToDictionary(g => g.Key, g => (g.First().Id, g.First().Folio));
}
