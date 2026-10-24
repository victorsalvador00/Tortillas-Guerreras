using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TG.Catalogos.Publico;
using TG.Inventario.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.Ventas.Dominio;
using TG.Ventas.Infraestructura;
using TG.Ventas.Publico;

namespace TG.Ventas.Aplicacion;

/// <summary>
/// Venta compartida web/app (RN-VEN-01 a 09), entrega de pedidos (RN-VEN-08), cobros [SUP-08] y consultas para
/// Logística y Facturación.
/// </summary>
public sealed class VentasService(
    VentasDbContext db,
    IUnitOfWork unitOfWork,
    IInventarioService inventario,
    ICatalogosService catalogos,
    IGeneradorFolios folios,
    TimeProvider reloj,
    PedidosHandler pedidos) : IVentasService
{
    internal sealed record Encabezado(
        OrigenVenta Origen,
        TipoVenta Tipo,
        Guid? PedidoId,
        Guid AlmacenId,
        Guid VendedorId,
        DateOnly FechaOperativa,
        CondicionPago Condicion,
        FormaPago Forma,
        Guid? VentaId,
        Guid? OpId,
        string? Folio,
        Guid? RutaId,
        Guid? DispositivoId,
        DateTimeOffset? FechaHoraDispositivo,
        decimal? TotalApp,
        GpsLectura? Gps);

    internal sealed record PartidaPreparada(Guid ProductoId, Guid UnidadId, decimal Cantidad, decimal Factor, decimal Base,
        decimal Precio, decimal TasaIva, decimal? TasaIeps, Guid? PedidoPartidaId, decimal? PrecioReferencia);

    internal sealed record IncidenciaPendiente(TipoIncidencia Tipo, object? Detalle);

    // ───────────────────────────── Registrar venta ─────────────────────────────

    public async Task<Result<VentaRegistrada>> RegistrarVentaAsync(RegistrarVentaComando c, CancellationToken ct)
    {
        if (c.Partidas.Count == 0 || c.Partidas.Any(p => p.Cantidad <= 0))
        {
            return Error.Validacion("VAL-001", "La venta requiere partidas con cantidad mayor que cero.");
        }

        var previa = await BuscarPreviaAsync(c.OpId, c.Folio, ct);
        if (previa is not null)
        {
            return previa;
        }

        var encabezado = new Encabezado(c.Origen, TipoVenta.VentaRuta, null, c.AlmacenId, c.VendedorId, c.FechaOperativa,
            c.CondicionPago, c.FormaPago, c.VentaId, c.OpId, c.Folio, c.RutaId, c.DispositivoId, c.FechaHoraDispositivo,
            c.TotalApp, c.Gps);

        return await ConReintentoIdempotenteAsync(c.OpId, c.Folio, () => unitOfWork.EjecutarAsync(async t =>
        {
            var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Id == c.ClienteId, t);
            if (cliente is null || !cliente.Activo)
            {
                return Error.NoEncontrado(ErroresVentas.ClienteNoEncontrado, "El cliente no existe o está inactivo.");
            }

            var incidencias = new List<IncidenciaPendiente>();
            var partidas = await PrepararPartidasAsync(c, cliente, incidencias, t);
            if (!partidas.EsExito)
            {
                return partidas.Error!;
            }

            return await CrearAsync(encabezado, cliente, partidas.Valor, incidencias, t);
        }, ct), ct);
    }

    private async Task<Result<List<PartidaPreparada>>> PrepararPartidasAsync(RegistrarVentaComando c, Cliente cliente,
        List<IncidenciaPendiente> incidencias, CancellationToken ct)
    {
        var productos = await catalogos.ObtenerProductosAsync(c.Partidas.Select(p => p.ProductoId), ct);

        // RN-VEN-14: en web se rechaza; la venta de la app ya se entregó y se aplica con incidencia [SUP-28].
        var ajenos = await db.ExclusivosAjenosAsync(cliente, c.Partidas.Select(p => p.ProductoId), ct);
        if (ajenos.Count > 0)
        {
            if (c.Origen != OrigenVenta.App)
            {
                var nombre = productos.GetValueOrDefault(ajenos.First())?.Nombre ?? "El producto";
                return Error.Validacion(ErroresVentas.ProductoExclusivo, $"{nombre} es exclusivo de otros clientes; no se vende a {cliente.Nombre}.");
            }

            incidencias.Add(new IncidenciaPendiente(TipoIncidencia.ProductoExclusivo, new { productoIds = ajenos }));
        }

        var resultado = new List<PartidaPreparada>();
        foreach (var p in c.Partidas)
        {
            if (!productos.TryGetValue(p.ProductoId, out var producto) || !producto.EsVendible || !producto.Activo)
            {
                return Error.Validacion(ErroresVentas.ProductoNoVendible, "El producto no existe, no es vendible o está inactivo.");
            }

            var conversion = await catalogos.ConvertirABaseAsync(p.ProductoId, p.UnidadId, p.Cantidad, UsoUnidad.Venta, ct);
            if (!conversion.EsExito)
            {
                return conversion.Error!;
            }

            // RN-VEN-01/12: precio especial vigente del cliente o, si no tiene, el de su lista.
            var vigente = await db.VigenteAsync(cliente, p.ProductoId, p.UnidadId, c.FechaOperativa, ct);
            decimal precio;
            if (c.Origen == OrigenVenta.App && p.PrecioUnitario is { } precioApp)
            {
                precio = precioApp;
                if (vigente != precioApp)
                {
                    incidencias.Add(new IncidenciaPendiente(TipoIncidencia.PrecioDistinto, new
                    {
                        productoId = p.ProductoId,
                        unidadId = p.UnidadId,
                        precioApp,
                        precioLista = vigente,
                    }));
                }
            }
            else if (vigente is { } precioLista)
            {
                precio = precioLista;
            }
            else
            {
                return Error.Validacion(ErroresVentas.SinPrecio,
                    $"{producto.Nombre} no tiene precio en la lista del cliente para la unidad elegida.");
            }

            resultado.Add(new PartidaPreparada(p.ProductoId, p.UnidadId, p.Cantidad, conversion.Valor.FactorABase,
                conversion.Valor.Base, precio, producto.TasaIva, producto.TasaIeps, null, vigente));
        }

        return resultado;
    }

    /// <summary>Crea la venta, sus movimientos <c>Venta</c> (−) y sus incidencias, en la transacción en curso.</summary>
    private async Task<Result<VentaRegistrada>> CrearAsync(Encabezado e, Cliente cliente, List<PartidaPreparada> partidas,
        List<IncidenciaPendiente> incidencias, CancellationToken ct)
    {
        if (e.Condicion == CondicionPago.Contado ? e.Forma == FormaPago.Credito : e.Forma != FormaPago.Credito)
        {
            return Error.Campo(ErroresVentas.FormaPagoInvalida, "formaPago",
                "La forma de pago no corresponde a la condición (contado: efectivo o transferencia; crédito: crédito).");
        }

        var venta = new Venta
        {
            Id = e.VentaId ?? Id.Nuevo(),
            OpId = e.OpId,
            Origen = e.Origen,
            Tipo = e.Tipo,
            PedidoId = e.PedidoId,
            ClienteId = cliente.Id,
            RutaId = e.RutaId,
            AlmacenId = e.AlmacenId,
            DispositivoId = e.DispositivoId,
            VendedorId = e.VendedorId,
            FechaHoraDispositivo = e.FechaHoraDispositivo,
            FechaHoraServidor = reloj.GetUtcNow(),
            FechaOperativa = e.FechaOperativa,
            CondicionPago = e.Condicion,
            FormaPago = e.Forma,
            TotalApp = e.TotalApp,
        };

        // RN-VEN-02: el servidor recalcula por partida.
        foreach (var p in partidas)
        {
            var (importe, ieps, iva) = CalculoImportes.Partida(p.Cantidad, p.Precio, p.TasaIva, p.TasaIeps);
            venta.Partidas.Add(new VentaPartida
            {
                VentaId = venta.Id, PedidoPartidaId = p.PedidoPartidaId, ProductoId = p.ProductoId, UnidadId = p.UnidadId,
                FactorABase = p.Factor, Cantidad = p.Cantidad, CantidadBase = p.Base, PrecioUnitario = p.Precio,
                PrecioReferencia = p.PrecioReferencia,
                TasaIva = p.TasaIva, TasaIeps = p.TasaIeps, Importe = importe, Ieps = ieps, Iva = iva,
            });
        }

        venta.Subtotal = venta.Partidas.Sum(p => p.Importe);
        venta.Ieps = venta.Partidas.Sum(p => p.Ieps);
        venta.Iva = venta.Partidas.Sum(p => p.Iva);
        venta.Total = venta.Subtotal + venta.Ieps + venta.Iva;
        if (CalculoImportes.TotalDifiere(venta.Total, e.TotalApp))
        {
            incidencias.Add(new IncidenciaPendiente(TipoIncidencia.TotalDistinto,
                new { totalApp = e.TotalApp, totalServidor = venta.Total }));
        }

        // RN-VEN-04 [SUP-08].
        if (e.Condicion == CondicionPago.Credito)
        {
            var saldo = await SaldoClienteAsync(cliente.Id, ct);
            var excedido = cliente.CondicionPago != CondicionPago.Credito
                || (cliente.LimiteCredito is { } limite && saldo + venta.Total > limite);
            if (excedido)
            {
                if (e.Origen == OrigenVenta.Web)
                {
                    return Error.Conflicto(ErroresVentas.Credito, cliente.CondicionPago != CondicionPago.Credito
                        ? "El cliente es de contado: no se le puede vender a crédito."
                        : "La venta excede el límite de crédito del cliente.");
                }

                incidencias.Add(new IncidenciaPendiente(TipoIncidencia.CreditoExcedido,
                    new { saldo, total = venta.Total, limite = cliente.LimiteCredito, condicionCliente = cliente.CondicionPago.ToString() }));
            }
        }

        if (e.Origen == OrigenVenta.App && !cliente.EsGenerico)
        {
            await AplicarGpsAsync(venta, cliente, e.Gps, incidencias, ct);
        }
        else if (e.Gps is { } gps)
        {
            venta.Latitud = gps.Latitud;
            venta.Longitud = gps.Longitud;
            venta.PrecisionM = gps.PrecisionM;
        }

        venta.Folio = e.Folio ?? await folios.SiguienteAsync(VentasDbContext.Esquema, "VTA", ct);

        // RN-VEN-03: un movimiento Venta (−) por partida en el almacén de la venta; FEFO por lote.
        var movimientos = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
            DocumentoInventario.Venta, venta.Id, venta.Folio, venta.FechaOperativa, venta.VendedorId,
            venta.Partidas.Select(p => new LineaMovimiento(TipoMovimiento.Venta, venta.AlmacenId, p.ProductoId, null, -p.CantidadBase))
                .ToList(), venta.DispositivoId), ct);
        if (!movimientos.EsExito)
        {
            return movimientos.Error!;
        }

        AsignarLotes(venta, movimientos.Valor);
        var negativos = movimientos.Valor.Where(m => m.QuedoNegativo).ToList();
        if (negativos.Count > 0)
        {
            // RN-INV-03 [SUP-10].
            incidencias.Add(new IncidenciaPendiente(TipoIncidencia.ExistenciaNegativaRuta, new
            {
                almacenId = venta.AlmacenId,
                productos = negativos.Select(m => new { m.ProductoId, m.Lote, existencia = m.ExistenciaResultante }),
            }));
        }

        db.Ventas.Add(venta);
        foreach (var incidencia in incidencias)
        {
            var entidad = incidencia.Tipo == TipoIncidencia.UbicacionPorAprobar ? ("Cliente", cliente.Id) : ("Venta", venta.Id);
            db.Incidencias.Add(NuevaIncidencia(incidencia.Tipo, entidad.Item1, entidad.Item2, incidencia.Detalle));
        }

        await db.SaveChangesAsync(ct);
        return new VentaRegistrada(venta.Id, venta.Folio, venta.Subtotal, venta.Ieps, venta.Iva, venta.Total,
            venta.RequiereRevision, incidencias.Select(i => i.Tipo).Distinct().ToList(), false);
    }

    /// <summary>RN-VEN-05 y 06: el GPS nunca rechaza la venta; solo marca revisión e incidencias.</summary>
    private async Task AplicarGpsAsync(Venta venta, Cliente cliente, GpsLectura? gps, List<IncidenciaPendiente> incidencias,
        CancellationToken ct)
    {
        if (gps is null)
        {
            incidencias.Add(new IncidenciaPendiente(TipoIncidencia.SinGps, null));
            return;
        }

        venta.Latitud = gps.Latitud;
        venta.Longitud = gps.Longitud;
        venta.PrecisionM = gps.PrecisionM;
        var precisionMaxima = await catalogos.ObtenerParametroAsync(ClavesParametro.GpsPrecisionMaxima, 100m, ct);
        var umbral = await catalogos.ObtenerParametroAsync(ClavesParametro.GpsUmbralDistancia, 150m, ct);
        if (gps.PrecisionM is { } precision && precision > precisionMaxima)
        {
            incidencias.Add(new IncidenciaPendiente(TipoIncidencia.GpsImpreciso, new { precisionM = precision, precisionMaxima }));
        }

        if (cliente is { UbicacionAprobada: true, Latitud: { } lat, Longitud: { } lng })
        {
            var distancia = Geo.DistanciaMetros(lat, lng, gps.Latitud, gps.Longitud);
            venta.DistanciaClienteM = distancia;
            if (distancia > umbral)
            {
                venta.RequiereRevision = true;
                incidencias.Add(new IncidenciaPendiente(TipoIncidencia.GpsFueraDeRango, new { distanciaM = distancia, umbralM = umbral }));
            }
        }
        else if (cliente.LatitudPropuesta is null)
        {
            cliente.LatitudPropuesta = gps.Latitud;
            cliente.LongitudPropuesta = gps.Longitud;
            incidencias.Add(new IncidenciaPendiente(TipoIncidencia.UbicacionPorAprobar,
                new { latitud = gps.Latitud, longitud = gps.Longitud, precisionM = gps.PrecisionM, ventaId = venta.Id }));
        }
    }

    /// <summary>Los movimientos salen en el orden de las líneas; una línea puede partirse en varios lotes (FEFO).</summary>
    private static void AsignarLotes(Venta venta, IReadOnlyList<MovimientoRegistrado> movimientos)
    {
        var indice = 0;
        foreach (var partida in venta.Partidas)
        {
            var lotes = new List<string>();
            var acumulado = 0m;
            while (indice < movimientos.Count && acumulado < partida.CantidadBase)
            {
                acumulado += -movimientos[indice].Cantidad;
                if (movimientos[indice].Lote.Length > 0 && !lotes.Contains(movimientos[indice].Lote))
                {
                    lotes.Add(movimientos[indice].Lote);
                }

                indice++;
            }

            partida.Lote = string.Join(",", lotes);
        }
    }

    // ───────────────────────────── Estimado (Producción) ─────────────────────────────

    public async Task<IReadOnlyList<Guid>> ProductosDeCanalAsync(Guid canalId, CancellationToken ct) =>
        await (from l in db.ListasPrecio.AsNoTracking()
               join p in db.ListasPrecioPartidas.AsNoTracking() on l.Id equals p.ListaPrecioId
               where l.CanalId == canalId && l.Activo
               select p.ProductoId).Distinct().ToListAsync(ct);

    public async Task<IReadOnlyList<DemandaPartidaInfo>> DemandaPorFechaEntregaAsync(DateOnly fechaEntrega, Guid? canalId,
        CancellationToken ct) =>
        await (from p in db.Pedidos.AsNoTracking()
               join c in db.Clientes.AsNoTracking() on p.ClienteId equals c.Id
               from x in p.Partidas
               where p.FechaEntrega == fechaEntrega && p.Estado != EstadoPedido.Cancelado && (canalId == null || c.CanalId == canalId)
               orderby p.Folio
               select new DemandaPartidaInfo(p.Id, p.Folio, p.ClienteId, c.CanalId, p.RutaId, p.EsRuta, p.Estado, x.ProductoId,
                   x.UnidadId, x.Cantidad, x.CantidadBase, x.Tipo, x.Notas))
            .ToListAsync(ct);

    public async Task<Result<Guid?>> GuardarPedidoRutaAsync(Guid rutaId, string rutaCodigo, DateOnly fechaEntrega,
        IReadOnlyList<CeldaPedidoRuta> celdas, CancellationToken ct)
    {
        var codigo = $"GEN-{rutaCodigo}";
        var generico = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Codigo == codigo && c.EsGenerico, ct);
        if (generico is null)
        {
            return Error.NoEncontrado(ErroresVentas.ClienteNoEncontrado, $"La ruta {rutaCodigo} no tiene cliente genérico.");
        }

        var pedido = await db.Pedidos.AsNoTracking().Include(p => p.Partidas)
            .FirstOrDefaultAsync(p => p.EsRuta && p.ClienteId == generico.Id && p.FechaEntrega == fechaEntrega
                && p.Estado != EstadoPedido.Cancelado, ct);
        if (pedido is not null && pedido.Estado != EstadoPedido.Capturado)
        {
            return Error.Conflicto(ErroresVentas.EstadoPedido, $"El pedido {pedido.Folio} de la ruta ya está {pedido.Estado}; no se edita.");
        }

        // Las celdas reemplazan la partida de Venta de su producto; lo demás del pedido se conserva.
        var partidas = (pedido?.Partidas ?? [])
            .Where(x => x.Tipo == TipoPartidaPedido.CambioFisico || celdas.All(c => c.ProductoId != x.ProductoId))
            .Select(x => new PartidaPedidoRequest(x.ProductoId, x.UnidadId, x.Cantidad, x.Tipo, x.Notas, x.Id))
            .ToList();
        foreach (var celda in celdas.Where(c => c.Cantidad > 0))
        {
            var actual = pedido?.Partidas.FirstOrDefault(x => x.ProductoId == celda.ProductoId && x.Tipo == TipoPartidaPedido.Venta);
            partidas.Add(new PartidaPedidoRequest(celda.ProductoId, celda.UnidadId, celda.Cantidad, TipoPartidaPedido.Venta,
                actual?.Notas, actual?.Id));
        }

        if (partidas.Count == 0)
        {
            if (pedido is null)
            {
                return (Guid?)null;
            }

            await pedidos.CancelarAsync(pedido.Id, ct);
            return (Guid?)null;
        }

        var solicitud = new GuardarPedidoRequest(generico.Id, fechaEntrega, pedido?.OrdenCompraCliente, pedido?.Notas, partidas);
        var guardado = pedido is null
            ? await pedidos.GuardarInternoAsync(null, solicitud, null, null, ct, rutaId)
            : await pedidos.GuardarInternoAsync(pedido.Id, solicitud, null, null, ct);
        return guardado.EsExito ? guardado.Valor.Id : guardado.Error!;
    }

    public async Task<IReadOnlyList<PedidoParaCargaInfo>> PedidosParaCargaAsync(DateOnly hasta, CancellationToken ct)
    {
        var pedidos = await (from p in db.Pedidos.AsNoTracking().Include(p => p.Partidas)
                             join c in db.Clientes.AsNoTracking() on p.ClienteId equals c.Id
                             where p.EnvioId == null && (p.Estado == EstadoPedido.Confirmado || p.Estado == EstadoPedido.Asignado)
                                 && (p.FechaEntrega ?? p.Fecha) <= hasta
                             orderby p.Folio
                             select new { p, c.Nombre }).ToListAsync(ct);
        return pedidos.Select(x => new PedidoParaCargaInfo(x.p.Id, x.p.Folio, x.p.ClienteId, x.Nombre, x.p.RutaId, x.p.FechaEntrega ?? x.p.Fecha,
                x.p.Partidas.Where(d => d.CantidadBase > d.CantidadEntregadaBase)
                    .Select(d => new PartidaParaCargaInfo(d.ProductoId, d.Tipo, d.CantidadBase - d.CantidadEntregadaBase)).ToList()))
            .Where(p => p.Partidas.Count > 0)
            .ToList();
    }

    public Task<Result<int>> ConfirmarPedidosAsync(IReadOnlyCollection<Guid> pedidoIds, CancellationToken ct) =>
        unitOfWork.EjecutarAsync(async t =>
        {
            var capturados = await db.Pedidos.AsNoTracking().Where(p => pedidoIds.Contains(p.Id) && p.Estado == EstadoPedido.Capturado)
                .Select(p => p.Id).ToListAsync(t);
            foreach (var id in capturados)
            {
                var confirmado = await pedidos.ConfirmarAsync(id, t);
                if (!confirmado.EsExito)
                {
                    return Result<int>.Falla(confirmado.Error!);
                }
            }

            return Result<int>.Exito(capturados.Count);
        }, ct);

    public Task<Result<int>> CopiarPedidosAsync(IReadOnlyCollection<Guid> pedidoIds, DateOnly fechaEntrega, CancellationToken ct) =>
        unitOfWork.EjecutarAsync(async t =>
        {
            var origen = await db.Pedidos.AsNoTracking().Include(p => p.Partidas).Where(p => pedidoIds.Contains(p.Id)).ToListAsync(t);
            foreach (var p in origen)
            {
                var solicitud = new GuardarPedidoRequest(p.ClienteId, fechaEntrega, p.OrdenCompraCliente, p.Notas,
                    p.Partidas.Select(x => new PartidaPedidoRequest(x.ProductoId, x.UnidadId, x.Cantidad, x.Tipo, x.Notas)).ToList());
                var copia = await pedidos.GuardarInternoAsync(null, solicitud, null, null, t, p.RutaId);
                if (!copia.EsExito)
                {
                    return Result<int>.Falla(copia.Error!);
                }
            }

            return Result<int>.Exito(origen.Count);
        }, ct);

    // ───────────────────────────── Pedido desde la app ─────────────────────────────

    public async Task<Result<PedidoCapturado>> CapturarPedidoAsync(CapturarPedidoComando c, CancellationToken ct)
    {
        // RN-SYN-02: el reenvío (mismo op_id o mismo id generado por la app) devuelve el pedido original.
        var previo = await db.Pedidos.AsNoTracking().Where(p => p.OpId == c.OpId || p.Id == c.PedidoId)
            .Select(p => new { p.Id, p.Folio }).FirstOrDefaultAsync(ct);
        if (previo is not null)
        {
            return new PedidoCapturado(previo.Id, previo.Folio, true);
        }

        var solicitud = new GuardarPedidoRequest(c.ClienteId, c.FechaEntrega, c.OrdenCompraCliente, c.Notas,
            c.Partidas.Select(p => new PartidaPedidoRequest(p.ProductoId, p.UnidadId, p.Cantidad, p.Tipo, p.Notas)).ToList());
        if (c.Partidas.Count == 0 || c.Partidas.Any(p => p.Cantidad <= 0))
        {
            return Error.Validacion("VAL-001", "El pedido requiere partidas con cantidad mayor que cero.");
        }

        var captura = new CapturaPedido(OrigenVenta.App, c.OpId, c.CapturadoPor, c.DispositivoId, c.RutaId, c.FechaCaptura);
        var guardado = await pedidos.GuardarInternoAsync(null, solicitud, captura, c.PedidoId, ct);
        return guardado.EsExito ? new PedidoCapturado(guardado.Valor.Id, guardado.Valor.Folio, false) : guardado.Error!;
    }

    // ───────────────────────────── Entrega de pedido ─────────────────────────────

    public async Task<Result<VentaRegistrada>> EntregarPedidoAsync(EntregarPedidoComando c, CancellationToken ct)
    {
        if (c.Partidas.Count == 0 || c.Partidas.Any(p => p.Cantidad <= 0))
        {
            return Error.Validacion("VAL-001", "La entrega requiere partidas con cantidad mayor que cero.");
        }

        var previa = await BuscarPreviaAsync(c.OpId, c.Folio, ct);
        if (previa is not null)
        {
            return previa;
        }

        return await ConReintentoIdempotenteAsync(c.OpId, c.Folio, () => unitOfWork.EjecutarAsync(async t =>
        {
            var pedido = await db.Pedidos.Include(p => p.Partidas).FirstOrDefaultAsync(p => p.Id == c.PedidoId, t);
            if (pedido is null)
            {
                return Error.NoEncontrado(ErroresVentas.PedidoNoEncontrado, "El pedido no existe.");
            }

            if (pedido.Estado is not (EstadoPedido.Confirmado or EstadoPedido.Asignado or EstadoPedido.EntregadoParcial))
            {
                return Error.Conflicto(ErroresVentas.EstadoPedido, $"No se puede entregar un pedido en estado {pedido.Estado}.");
            }

            var cliente = await db.Clientes.FirstAsync(x => x.Id == pedido.ClienteId, t);
            var partidas = new List<PartidaPreparada>();
            foreach (var entrega in c.Partidas)
            {
                var partida = pedido.Partidas.FirstOrDefault(p => p.Id == entrega.PedidoPartidaId);
                if (partida is null)
                {
                    return Error.Validacion(ErroresVentas.PedidoNoEncontrado, "Una partida no pertenece al pedido.");
                }

                // RN-VEN-17: el cambio físico se entrega con un retorno CambioFisico, no con venta.
                if (partida.Tipo == TipoPartidaPedido.CambioFisico)
                {
                    return Error.Validacion("VAL-001", "Las partidas de cambio físico se entregan con un retorno, no con venta.");
                }

                var conversion = await catalogos.ConvertirABaseAsync(partida.ProductoId, partida.UnidadId, entrega.Cantidad, null, t);
                if (!conversion.EsExito)
                {
                    return conversion.Error!;
                }

                if (partida.CantidadEntregadaBase + conversion.Valor.Base > partida.CantidadBase)
                {
                    return Error.Validacion(ErroresVentas.EntregaExcedida, "La entrega excede lo pendiente del pedido.");
                }

                // El pedido conserva su precio (RN-VEN-13); la referencia es el vigente del cliente ese día.
                var referencia = await db.VigenteAsync(cliente, partida.ProductoId, partida.UnidadId, c.FechaOperativa, t);
                partidas.Add(new PartidaPreparada(partida.ProductoId, partida.UnidadId, entrega.Cantidad, partida.FactorABase,
                    conversion.Valor.Base, partida.PrecioUnitario, partida.TasaIva, partida.TasaIeps, partida.Id, referencia));
            }

            var condicion = cliente.CondicionPago;
            var forma = condicion == CondicionPago.Credito ? FormaPago.Credito : c.FormaPago;
            var encabezado = new Encabezado(c.Origen, TipoVenta.EntregaPedido, pedido.Id, c.AlmacenId, c.VendedorId,
                c.FechaOperativa, condicion, forma, c.VentaId, c.OpId, c.Folio, c.RutaId ?? pedido.RutaId, c.DispositivoId,
                c.FechaHoraDispositivo, c.TotalApp, c.Gps);
            var venta = await CrearAsync(encabezado, cliente, partidas, [], t);
            if (!venta.EsExito)
            {
                return venta;
            }

            // RN-VEN-08: acumula lo entregado y actualiza el estado del pedido.
            foreach (var p in partidas)
            {
                pedido.Partidas.Single(x => x.Id == p.PedidoPartidaId).CantidadEntregadaBase += p.Base;
            }

            // RN-VEN-17: el estado se calcula solo con las partidas de venta.
            pedido.Estado = pedido.Partidas.Where(p => p.Tipo == TipoPartidaPedido.Venta).All(p => p.CantidadEntregadaBase >= p.CantidadBase)
                ? EstadoPedido.Entregado
                : EstadoPedido.EntregadoParcial;
            await db.SaveChangesAsync(t);
            return venta;
        }, ct), ct);
    }

    // ───────────────────────────── Idempotencia ─────────────────────────────

    /// <summary>RN-VEN-09 / RN-SYN-02: mismo <c>op_id</c> = devolver la original; folio repetido con otro <c>op_id</c> = VEN-009.</summary>
    private async Task<Result<VentaRegistrada>?> BuscarPreviaAsync(Guid? opId, string? folio, CancellationToken ct)
    {
        if (opId is { } op)
        {
            var existente = await db.Ventas.AsNoTracking().FirstOrDefaultAsync(v => v.OpId == op, ct);
            if (existente is not null)
            {
                return await DuplicadaAsync(existente, ct);
            }
        }

        if (folio is not null && await db.Ventas.AnyAsync(v => v.Folio == folio, ct))
        {
            return Error.Conflicto(ErroresVentas.FolioDuplicado, $"El folio {folio} ya existe con otra operación.");
        }

        return null;
    }

    private async Task<Result<VentaRegistrada>> DuplicadaAsync(Venta venta, CancellationToken ct)
    {
        var tipos = await db.Incidencias.AsNoTracking().Where(i => i.EntidadTipo == "Venta" && i.EntidadId == venta.Id)
            .Select(i => i.Tipo).Distinct().ToListAsync(ct);
        return new VentaRegistrada(venta.Id, venta.Folio, venta.Subtotal, venta.Ieps, venta.Iva, venta.Total,
            venta.RequiereRevision, tipos, true);
    }

    /// <summary>Si dos reenvíos llegan a la vez, el índice único decide; el perdedor devuelve la venta del ganador.</summary>
    private async Task<Result<VentaRegistrada>> ConReintentoIdempotenteAsync(Guid? opId, string? folio,
        Func<Task<Result<VentaRegistrada>>> accion, CancellationToken ct)
    {
        try
        {
            return await accion();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            db.ChangeTracker.Clear();
            if (pg.ConstraintName == "ux_ventas_op_id" && opId is not null)
            {
                var previa = await BuscarPreviaAsync(opId, null, ct);
                if (previa is not null)
                {
                    return previa;
                }
            }

            if (pg.ConstraintName == "ux_ventas_folio")
            {
                return Error.Conflicto(ErroresVentas.FolioDuplicado, $"El folio {folio} ya existe con otra operación.");
            }

            throw;
        }
    }

    // ───────────────────────────── Cobros ─────────────────────────────

    public async Task<Result<CobroRegistrado>> RegistrarCobroAsync(RegistrarCobroComando c, CancellationToken ct)
    {
        if (c.OpId is { } op)
        {
            var previo = await db.Cobros.AsNoTracking().FirstOrDefaultAsync(x => x.OpId == op, ct);
            if (previo is not null)
            {
                return new CobroRegistrado(previo.Id, previo.Folio, previo.Importe, await SaldoClienteAsync(previo.ClienteId, ct), true);
            }
        }

        if (c.Importe <= 0)
        {
            return Error.Campo("VAL-001", "importe", "El importe debe ser mayor que cero.");
        }

        if (c.FormaPago is FormaPago.Credito or FormaPago.Cheque)
        {
            return Error.Campo(ErroresVentas.FormaPagoInvalida, "formaPago", "Un cobro se recibe en efectivo o transferencia.");
        }

        return await unitOfWork.EjecutarAsync(async t =>
        {
            if (!await db.Clientes.AnyAsync(x => x.Id == c.ClienteId, t))
            {
                return Error.NoEncontrado(ErroresVentas.ClienteNoEncontrado, "El cliente no existe.");
            }

            var cobro = new Cobro
            {
                Id = c.CobroId ?? Id.Nuevo(),
                OpId = c.OpId,
                Folio = await folios.SiguienteAsync(VentasDbContext.Esquema, "COB", t),
                ClienteId = c.ClienteId,
                RutaId = c.RutaId,
                DispositivoId = c.DispositivoId,
                FechaOperativa = c.FechaOperativa,
                FormaPago = c.FormaPago,
                Importe = Dinero.Redondear(c.Importe),
            };

            // Se aplica a las ventas a crédito con saldo, de la más antigua a la más reciente.
            var pendientes = await PendientesCreditoAsync(c.ClienteId, t);
            var restante = cobro.Importe;
            foreach (var (ventaId, pendiente) in pendientes.Where(p => c.VentaIds is not { Count: > 0 } || c.VentaIds.Contains(p.VentaId)))
            {
                if (restante <= 0)
                {
                    break;
                }

                var aplicar = Math.Min(restante, pendiente);
                cobro.Aplicaciones.Add(new CobroAplicacion { CobroId = cobro.Id, VentaId = ventaId, Importe = aplicar });
                restante -= aplicar;
            }

            db.Cobros.Add(cobro);
            await db.SaveChangesAsync(t);
            return Result<CobroRegistrado>.Exito(new CobroRegistrado(cobro.Id, cobro.Folio, cobro.Importe,
                await SaldoClienteAsync(c.ClienteId, t), false));
        }, ct);
    }

    internal async Task<decimal> SaldoClienteAsync(Guid clienteId, CancellationToken ct)
    {
        var ventas = await db.Ventas.Where(v => v.ClienteId == clienteId && v.Estado == EstadoVenta.Registrada
            && v.CondicionPago == CondicionPago.Credito).SumAsync(v => v.Total, ct);
        var cobros = await db.Cobros.Where(x => x.ClienteId == clienteId && x.Estado == EstadoCobro.Registrado)
            .SumAsync(x => x.Importe, ct);
        return ventas - cobros;
    }

    private async Task<List<(Guid VentaId, decimal Pendiente)>> PendientesCreditoAsync(Guid clienteId, CancellationToken ct)
    {
        var ventas = await db.Ventas.AsNoTracking()
            .Where(v => v.ClienteId == clienteId && v.Estado == EstadoVenta.Registrada && v.CondicionPago == CondicionPago.Credito)
            .OrderBy(v => v.FechaOperativa).ThenBy(v => v.FechaHoraServidor)
            .Select(v => new { v.Id, v.Total })
            .ToListAsync(ct);
        var ids = ventas.Select(v => v.Id).ToList();
        var aplicado = await db.Cobros.Where(x => x.Estado == EstadoCobro.Registrado)
            .SelectMany(x => x.Aplicaciones).Where(a => ids.Contains(a.VentaId))
            .GroupBy(a => a.VentaId).Select(g => new { g.Key, Total = g.Sum(a => a.Importe) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        return ventas.Select(v => (v.Id, v.Total - aplicado.GetValueOrDefault(v.Id))).Where(v => v.Item2 > 0).ToList();
    }

    // ───────────────────────────── Clientes ─────────────────────────────

    public async Task<Result<IReadOnlyList<TipoIncidencia>>> ProponerUbicacionClienteAsync(ProponerUbicacionComando c,
        CancellationToken ct)
    {
        var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Id == c.ClienteId, ct);
        if (cliente is null)
        {
            return Error.NoEncontrado(ErroresVentas.ClienteNoEncontrado, "El cliente no existe.");
        }

        if (cliente.EsGenerico)
        {
            return Result<IReadOnlyList<TipoIncidencia>>.Exito([]);
        }

        cliente.LatitudPropuesta = c.Gps.Latitud;
        cliente.LongitudPropuesta = c.Gps.Longitud;
        db.Incidencias.Add(NuevaIncidencia(TipoIncidencia.UbicacionPorAprobar, "Cliente", cliente.Id,
            new { latitud = c.Gps.Latitud, longitud = c.Gps.Longitud, precisionM = c.Gps.PrecisionM, dispositivoId = c.DispositivoId }));
        await db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<TipoIncidencia>>.Exito([TipoIncidencia.UbicacionPorAprobar]);
    }

    public async Task<Result<ClienteInfo>> CrearClienteGenericoAsync(string rutaCodigo, Guid canalId, CancellationToken ct)
    {
        var canal = await db.Canales.AsNoTracking().FirstOrDefaultAsync(x => x.Id == canalId, ct);
        var lista = canal is null
            ? null
            : await db.ListasPrecio.AsNoTracking().Where(l => l.Activo)
                .OrderBy(l => l.CanalId == canal.Id ? 0 : 1).ThenBy(l => l.Nombre).FirstOrDefaultAsync(ct);
        if (canal is null || lista is null)
        {
            return Error.Conflicto(ErroresVentas.NoEncontrado, "Configura el canal y al menos una lista de precios antes de crear rutas.");
        }

        var existente = await db.Clientes.FirstOrDefaultAsync(x => x.RutaGenerico == rutaCodigo, ct);
        if (existente is not null)
        {
            // La ruta cambió de canal (o se migró de v0.1): el genérico la sigue.
            if (existente.CanalId != canal.Id)
            {
                var listaActual = await db.ListasPrecio.AsNoTracking().FirstOrDefaultAsync(l => l.Id == existente.ListaPrecioId, ct);
                existente.CanalId = canal.Id;
                if (listaActual?.CanalId != canal.Id)
                {
                    existente.ListaPrecioId = lista.Id;
                }

                await db.SaveChangesAsync(ct);
            }

            return await InfoAsync(existente, ct);
        }

        var cliente = new Cliente
        {
            Codigo = $"GEN-{rutaCodigo}",
            Nombre = $"Público en general {rutaCodigo}",
            CanalId = canal.Id,
            ListaPrecioId = lista.Id,
            CondicionPago = CondicionPago.Contado,
            EsGenerico = true,
            RutaGenerico = rutaCodigo,
            RazonSocial = "PUBLICO EN GENERAL",
            Rfc = "XAXX010101000",
        };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync(ct);
        return await InfoAsync(cliente, ct);
    }

    public async Task<IReadOnlyList<CanalInfo>> ListarCanalesAsync(CancellationToken ct) =>
        await db.Canales.AsNoTracking().OrderBy(c => c.Orden).ThenBy(c => c.Clave)
            .Select(c => new CanalInfo(c.Id, c.Clave, c.Nombre, c.FormatoLiquidacion, c.AplicaProductoACobro, c.PedidoPor, c.Orden, c.Activo))
            .ToListAsync(ct);

    public async Task<ClienteInfo?> ClienteGenericoDeRutaAsync(string rutaCodigo, CancellationToken ct)
    {
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.RutaGenerico == rutaCodigo, ct);
        return cliente is null ? null : await InfoAsync(cliente, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, ClienteInfo>> ObtenerClientesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        var clientes = await db.Clientes.AsNoTracking().Where(x => lista.Contains(x.Id)).ToListAsync(ct);
        var canales = await db.Canales.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Clave, ct);
        return clientes.ToDictionary(x => x.Id, x => AInfo(x, canales));
    }

    public async Task<IReadOnlyList<ClienteInfo>> ListarClientesAsync(bool incluirGenericos, CancellationToken ct)
    {
        var consulta = db.Clientes.AsNoTracking().Where(x => x.Activo);
        if (!incluirGenericos)
        {
            consulta = consulta.Where(x => !x.EsGenerico);
        }

        var clientes = await consulta.OrderBy(x => x.Codigo).ToListAsync(ct);
        var canales = await db.Canales.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Clave, ct);
        return clientes.Select(x => AInfo(x, canales)).ToList();
    }

    private async Task<ClienteInfo> InfoAsync(Cliente cliente, CancellationToken ct)
    {
        var canales = await db.Canales.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Clave, ct);
        return AInfo(cliente, canales);
    }

    internal static ClienteInfo AInfo(Cliente c, IReadOnlyDictionary<Guid, string> canales) => new(
        c.Id, c.Codigo, c.Nombre, c.CanalId, canales.GetValueOrDefault(c.CanalId) ?? string.Empty, c.GrupoClienteId, c.ListaPrecioId,
        c.CondicionPago, c.DiasCredito, c.LimiteCredito, c.RequiereFactura, c.Rfc, c.RazonSocial, c.RegimenFiscal,
        c.CodigoPostalFiscal, c.UsoCfdi, c.EmailFacturacion, c.Telefono, c.Direccion, c.Latitud, c.Longitud,
        c.UbicacionAprobada, c.EsGenerico, c.Activo, c.VersionSync);

    // ───────────────────────────── Consultas de ventas ─────────────────────────────

    public async Task<VentaInfo?> ObtenerVentaAsync(Guid id, CancellationToken ct)
    {
        var venta = await db.Ventas.AsNoTracking().Include(v => v.Partidas).FirstOrDefaultAsync(v => v.Id == id, ct);
        return venta is null ? null : AInfo(venta);
    }

    public async Task<VentaInfo?> ObtenerVentaPorFolioAsync(string folio, CancellationToken ct)
    {
        var normalizado = folio.Trim().ToUpperInvariant();
        var venta = await db.Ventas.AsNoTracking().Include(v => v.Partidas).FirstOrDefaultAsync(v => v.Folio == normalizado, ct);
        return venta is null ? null : AInfo(venta);
    }

    public async Task<IReadOnlyList<VentaInfo>> ObtenerVentasAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return (await db.Ventas.AsNoTracking().Include(v => v.Partidas).Where(v => lista.Contains(v.Id)).ToListAsync(ct))
            .Select(AInfo).ToList();
    }

    public async Task<ResumenRutaDia> ResumenRutaDiaAsync(Guid rutaId, DateOnly fecha, CancellationToken ct)
    {
        var ventas = await db.Ventas.AsNoTracking().Where(v => v.RutaId == rutaId && v.FechaOperativa == fecha).ToListAsync(ct);
        var vendido = new Dictionary<(Guid, string), decimal>();
        foreach (var venta in ventas)
        {
            // Venta (−) y su cancelación (+) quedan en el mismo documento: el neto es lo vendido.
            var movimientos = await inventario.MovimientosDeDocumentoAsync(DocumentoInventario.Venta, venta.Id, ct);
            foreach (var m in movimientos)
            {
                var llave = (m.ProductoId, m.Lote);
                vendido[llave] = vendido.GetValueOrDefault(llave) - m.Cantidad;
            }
        }

        var registradas = ventas.Where(v => v.Estado == EstadoVenta.Registrada).ToList();
        var contadoEfectivo = registradas.Where(v => v.CondicionPago == CondicionPago.Contado && v.FormaPago == FormaPago.Efectivo)
            .Sum(v => v.Total);
        var cobrosDia = await db.Cobros.AsNoTracking()
            .Where(x => x.RutaId == rutaId && x.FechaOperativa == fecha && x.Estado == EstadoCobro.Registrado)
            .Select(x => new { x.FormaPago, x.Importe }).ToListAsync(ct);
        var cobros = cobrosDia.Where(x => x.FormaPago == FormaPago.Efectivo).Sum(x => x.Importe);

        // RN-LOG-23: (precio de referencia del cliente − precio cobrado) × cantidad; positivo = cobró de menos.
        var ids = registradas.Select(v => v.Id).ToList();
        var partidas = await db.VentasPartidas.AsNoTracking().Where(p => ids.Contains(p.VentaId)).ToListAsync(ct);
        var diferenciaPrecios = Dinero.Redondear(partidas.Where(p => p.PrecioReferencia is { } r && r != p.PrecioUnitario)
            .Sum(p => (p.PrecioReferencia!.Value - p.PrecioUnitario) * p.Cantidad));
        var importes = partidas.GroupBy(p => p.ProductoId)
            .Select(g => new ImporteProductoDia(g.Key, g.Sum(p => p.Importe + p.Ieps + p.Iva))).ToList();
        var aCredito = registradas.Where(v => v.CondicionPago == CondicionPago.Credito).ToList();
        var nombres = await db.Clientes.AsNoTracking().Where(c => aCredito.Select(v => v.ClienteId).Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        return new ResumenRutaDia(rutaId, fecha,
            vendido.Where(v => v.Value != 0).Select(v => new VendidoProductoLote(v.Key.Item1, v.Key.Item2, v.Value)).ToList(),
            contadoEfectivo, cobros, contadoEfectivo + cobros, registradas.Count, registradas.Sum(v => v.Total),
            aCredito.Sum(v => v.Total), cobrosDia.Sum(x => x.Importe),
            registradas.Where(v => v.FormaPago == FormaPago.Transferencia).Sum(v => v.Total),
            cobrosDia.Where(x => x.FormaPago == FormaPago.Transferencia).Sum(x => x.Importe),
            registradas.Where(v => v.FormaPago == FormaPago.Cheque).Sum(v => v.Total), diferenciaPrecios, importes,
            aCredito.OrderBy(v => v.Folio).Select(v => new CreditoDelDia(v.Id, v.Folio, v.ClienteId, nombres.GetValueOrDefault(v.ClienteId) ?? "?", v.Total))
                .ToList());
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> PreciosBaseClienteAsync(Guid clienteId, IReadOnlyCollection<Guid> productoIds,
        DateOnly fecha, CancellationToken ct)
    {
        var resultado = new Dictionary<Guid, decimal>();
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId, ct);
        if (cliente is null)
        {
            return resultado;
        }

        var productos = await catalogos.ObtenerProductosAsync(productoIds, ct);
        foreach (var producto in productos.Values)
        {
            if (await db.VigenteAsync(cliente, producto.Id, producto.UnidadBaseId, fecha, ct) is { } precio)
            {
                resultado[producto.Id] = precio;
            }
        }

        return resultado;
    }

    public async Task<IReadOnlyList<VentaVendedorDia>> VentasDeVendedoresAsync(DateOnly desde, DateOnly hasta,
        IReadOnlyCollection<Guid>? vendedorIds, CancellationToken ct)
    {
        var consulta = db.Ventas.AsNoTracking()
            .Where(v => v.Estado == EstadoVenta.Registrada && v.FechaOperativa >= desde && v.FechaOperativa <= hasta);
        if (vendedorIds is not null)
        {
            consulta = consulta.Where(v => vendedorIds.Contains(v.VendedorId));
        }

        var filas = await consulta.SelectMany(v => v.Partidas.Select(p => new
            {
                v.VendedorId, v.FechaOperativa, p.ProductoId, p.CantidadBase, Importe = p.Importe + p.Ieps + p.Iva,
            }))
            .ToListAsync(ct);
        return filas.GroupBy(f => (f.VendedorId, f.FechaOperativa, f.ProductoId))
            .Select(g => new VentaVendedorDia(g.Key.VendedorId, g.Key.FechaOperativa, g.Key.ProductoId, g.Sum(f => f.CantidadBase),
                g.Sum(f => f.Importe)))
            .OrderBy(v => v.Fecha).ToList();
    }

    public async Task<IReadOnlyList<VentaInfo>> VentasParaFacturaGlobalAsync(DateOnly desde, DateOnly hasta, CancellationToken ct) =>
        (await db.Ventas.AsNoTracking().Include(v => v.Partidas)
            .Where(v => v.Estado == EstadoVenta.Registrada && v.CondicionPago == CondicionPago.Contado && v.FacturaId == null
                && v.FechaOperativa >= desde && v.FechaOperativa <= hasta)
            .OrderBy(v => v.FechaOperativa).ThenBy(v => v.Folio)
            .ToListAsync(ct)).Select(AInfo).ToList();

    public async Task<Result> AsignarFacturaAsync(IReadOnlyCollection<Guid> ventaIds, Guid facturaId, CancellationToken ct)
    {
        var ventas = await db.Ventas.Where(v => ventaIds.Contains(v.Id)).ToListAsync(ct);
        if (ventas.Count != ventaIds.Distinct().Count())
        {
            return Error.NoEncontrado(ErroresVentas.VentaNoEncontrada, "Alguna venta no existe.");
        }

        var ocupada = ventas.FirstOrDefault(v => v.Estado != EstadoVenta.Registrada || (v.FacturaId is { } f && f != facturaId));
        if (ocupada is not null)
        {
            return Error.Conflicto(ErroresVentas.FacturaVigente,
                $"La venta {ocupada.Folio} ya está facturada o cancelada.");
        }

        foreach (var venta in ventas)
        {
            venta.FacturaId = facturaId;
        }

        await db.SaveChangesAsync(ct);
        return Result.Exito();
    }

    public async Task LiberarFacturaAsync(Guid facturaId, CancellationToken ct) =>
        await db.Ventas.Where(v => v.FacturaId == facturaId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.FacturaId, (Guid?)null), ct);

    public async Task<Result> VincularPedidosEnvioAsync(IReadOnlyCollection<Guid> pedidoIds, Guid envioId, Guid rutaId, CancellationToken ct)
    {
        var pedidos = await db.Pedidos.Where(p => pedidoIds.Contains(p.Id)).ToListAsync(ct);
        if (pedidos.Count != pedidoIds.Distinct().Count())
        {
            return Error.NoEncontrado(ErroresVentas.PedidoNoEncontrado, "Algún pedido no existe.");
        }

        var invalido = pedidos.FirstOrDefault(p => p.Estado is not (EstadoPedido.Confirmado or EstadoPedido.Asignado));
        if (invalido is not null)
        {
            return Error.Conflicto(ErroresVentas.EstadoPedido, $"El pedido {invalido.Folio} está {invalido.Estado}.");
        }

        var otraRuta = pedidos.FirstOrDefault(p => p.RutaId is { } r && r != rutaId);
        if (otraRuta is not null)
        {
            return Error.Conflicto(ErroresVentas.PedidoOtraCarga, $"El pedido {otraRuta.Folio} está asignado a otra ruta.");
        }

        var otraCarga = pedidos.FirstOrDefault(p => p.EnvioId is { } e && e != envioId);
        if (otraCarga is not null)
        {
            return Error.Conflicto(ErroresVentas.PedidoOtraCarga, $"El pedido {otraCarga.Folio} ya va en otra carga.");
        }

        foreach (var pedido in pedidos)
        {
            pedido.RutaId = rutaId;
            pedido.EnvioId = envioId;
            pedido.Estado = EstadoPedido.Asignado;
        }

        await db.SaveChangesAsync(ct);
        return Result.Exito();
    }

    public async Task DesvincularPedidosEnvioAsync(Guid envioId, CancellationToken ct)
    {
        var pedidos = await db.Pedidos.Where(p => p.EnvioId == envioId && p.Estado == EstadoPedido.Asignado).ToListAsync(ct);
        foreach (var pedido in pedidos)
        {
            pedido.EnvioId = null;
        }

        await db.SaveChangesAsync(ct);
    }

    internal static VentaInfo AInfo(Venta v) => new(
        v.Id, v.Folio, v.Origen, v.Tipo, v.ClienteId, v.RutaId, v.AlmacenId, v.VendedorId, v.FechaOperativa,
        v.FechaHoraServidor, v.CondicionPago, v.FormaPago, v.Subtotal, v.Iva, v.Ieps, v.Total, v.Estado, v.FacturaId,
        v.Partidas.Select(p => new VentaPartidaInfo(p.Id, p.ProductoId, p.UnidadId, p.FactorABase, p.Cantidad, p.CantidadBase,
            p.Lote, p.PrecioUnitario, p.TasaIva, p.TasaIeps, p.Importe, p.Iva, p.Ieps)).ToList());

    internal static Incidencia NuevaIncidencia(TipoIncidencia tipo, string entidadTipo, Guid entidadId, object? detalle) => new()
    {
        Tipo = tipo,
        EntidadTipo = entidadTipo,
        EntidadId = entidadId,
        Detalle = detalle is null ? null : JsonSerializer.Serialize(detalle, JsonSerializerOptions.Web),
    };
}

public sealed class IncidenciasService(VentasDbContext db) : IIncidenciasService
{
    public async Task<Guid> CrearAsync(TipoIncidencia tipo, string entidadTipo, Guid entidadId, object? detalle, CancellationToken ct)
    {
        var incidencia = VentasService.NuevaIncidencia(tipo, entidadTipo, entidadId, detalle);
        db.Incidencias.Add(incidencia);
        await db.SaveChangesAsync(ct);
        return incidencia.Id;
    }
}

public sealed class ConsultasSyncVentas(VentasDbContext db) : IConsultasSyncVentas
{
    public async Task<IReadOnlyList<PrecioClienteInfo>> PreciosClientesAsync(IReadOnlyCollection<Guid> clienteIds, long desde,
        CancellationToken ct) =>
        clienteIds.Count == 0
            ? []
            : await db.PreciosClientes.AsNoTracking().Where(p => clienteIds.Contains(p.ClienteId) && p.VersionSync > desde)
                .OrderBy(p => p.VersionSync)
                .Select(p => new PrecioClienteInfo(p.ClienteId, p.ProductoId, p.UnidadId, p.PrecioUnitario, p.VigenteDesde, p.Activo, p.VersionSync))
                .ToListAsync(ct);

    public async Task<IReadOnlyList<ProductoExclusivoInfo>> ProductosExclusivosAsync(long desde, CancellationToken ct) =>
        await db.ProductosExclusivos.AsNoTracking().Where(p => p.VersionSync > desde).OrderBy(p => p.VersionSync)
            .Select(p => new ProductoExclusivoInfo(p.Id, p.ProductoId, p.ClienteId, p.GrupoClienteId, p.Activo, p.VersionSync))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ClienteInfo>> ClientesAsync(IReadOnlyCollection<Guid> ids, long desde, CancellationToken ct)
    {
        var canales = await db.Canales.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Clave, ct);
        return (await db.Clientes.AsNoTracking().Where(c => ids.Contains(c.Id) && c.VersionSync > desde)
            .OrderBy(c => c.VersionSync).ToListAsync(ct)).Select(c => VentasService.AInfo(c, canales)).ToList();
    }

    public async Task<IReadOnlyList<ListaPrecioInfo>> ListasPrecioAsync(IReadOnlyCollection<Guid> listaIds, long desde, CancellationToken ct) =>
        await db.ListasPrecio.AsNoTracking().Where(l => listaIds.Contains(l.Id) && l.VersionSync > desde)
            .OrderBy(l => l.VersionSync)
            .Select(l => new ListaPrecioInfo(l.Id, l.Nombre, l.CanalId, l.Activo, l.VersionSync))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ListaPrecioPartidaInfo>> PartidasListasAsync(IReadOnlyCollection<Guid> listaIds, long desde,
        CancellationToken ct) =>
        await db.ListasPrecioPartidas.AsNoTracking().Where(p => listaIds.Contains(p.ListaPrecioId) && p.VersionSync > desde)
            .OrderBy(p => p.VersionSync)
            .Select(p => new ListaPrecioPartidaInfo(p.ListaPrecioId, p.ProductoId, p.UnidadId, p.PrecioUnitario, p.VersionSync))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PedidoInfo>> PedidosDeRutaAsync(Guid rutaId, long desde, CancellationToken ct) =>
        (await db.Pedidos.AsNoTracking().Include(p => p.Partidas).Where(p => p.RutaId == rutaId && p.VersionSync > desde)
            .OrderBy(p => p.VersionSync).ToListAsync(ct)).Select(PedidosHandler.AInfo).ToList();

    public async Task<IReadOnlyDictionary<Guid, decimal>> SaldosClientesAsync(IReadOnlyCollection<Guid> clienteIds, CancellationToken ct)
    {
        var ventas = await db.Ventas.AsNoTracking()
            .Where(v => clienteIds.Contains(v.ClienteId) && v.Estado == EstadoVenta.Registrada && v.CondicionPago == CondicionPago.Credito)
            .GroupBy(v => v.ClienteId).Select(g => new { g.Key, Total = g.Sum(v => v.Total) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var cobros = await db.Cobros.AsNoTracking()
            .Where(c => clienteIds.Contains(c.ClienteId) && c.Estado == EstadoCobro.Registrado)
            .GroupBy(c => c.ClienteId).Select(g => new { g.Key, Total = g.Sum(c => c.Importe) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        return clienteIds.Distinct().ToDictionary(id => id, id => ventas.GetValueOrDefault(id) - cobros.GetValueOrDefault(id));
    }
}
