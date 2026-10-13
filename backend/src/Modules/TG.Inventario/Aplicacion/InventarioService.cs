using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Inventario.Dominio;
using TG.Inventario.Infraestructura;
using TG.Inventario.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Resultados;

namespace TG.Inventario.Aplicacion;

/// <summary>
/// Kardex: registra movimientos inmutables y actualiza existencias en la misma transacción con bloqueo de fila
/// (RN-INV-01 a 07). Orden de bloqueo fijo para evitar interbloqueos: primero <c>costos_producto</c> y luego
/// <c>existencias</c>, ambos ordenados por llave.
/// </summary>
internal sealed class InventarioService(
    InventarioDbContext db,
    IUnitOfWork unitOfWork,
    ICatalogosService catalogos,
    TimeProvider reloj) : IInventarioService
{
    private sealed record Llave(Guid AlmacenId, Guid ProductoId, string Lote);

    public Task<Result<IReadOnlyList<MovimientoRegistrado>>> RegistrarMovimientosAsync(RegistrarMovimientosRequest request,
        CancellationToken ct) =>
        unitOfWork.EjecutarAsync(c => RegistrarEnTransaccionAsync(request, c), ct);

    private async Task<Result<IReadOnlyList<MovimientoRegistrado>>> RegistrarEnTransaccionAsync(
        RegistrarMovimientosRequest request, CancellationToken ct)
    {
        if (request.Lineas.Count == 0)
        {
            return Result<IReadOnlyList<MovimientoRegistrado>>.Exito([]);
        }

        var almacenIds = request.Lineas.Select(l => l.AlmacenId).Distinct().ToList();
        var almacenes = await db.Almacenes.AsNoTracking().Where(a => almacenIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var productos = await catalogos.ObtenerProductosAsync(request.Lineas.Select(l => l.ProductoId), ct);

        var lineas = new List<LineaMovimiento>(request.Lineas.Count);
        foreach (var linea in request.Lineas)
        {
            var validada = Validar(linea, almacenes, productos);
            if (!validada.EsExito)
            {
                return validada.Error!;
            }

            lineas.Add(validada.Valor);
        }

        // 1. Costos: bloqueo de los productos cuyo promedio se recalcula.
        var recalculan = lineas.Where(l => ReglasInventario.RecalculaCosto(l.Tipo, l.CostoUnitario)).Select(l => l.ProductoId)
            .Distinct().Order().ToArray();
        var todosProductos = lineas.Select(l => l.ProductoId).Distinct().Order().ToArray();
        await AsegurarCostosAsync(todosProductos, ct);
        var costos = await db.CostosProducto
            .FromSql($"SELECT * FROM inventario.costos_producto WHERE producto_id = ANY({recalculan}) ORDER BY producto_id FOR UPDATE")
            .ToDictionaryAsync(c => c.ProductoId, ct);
        foreach (var faltante in todosProductos.Where(p => !costos.ContainsKey(p)))
        {
            costos[faltante] = await db.CostosProducto.AsNoTracking().FirstAsync(c => c.ProductoId == faltante, ct);
        }

        var existenciaTotal = new Dictionary<Guid, decimal>();
        foreach (var productoId in recalculan)
        {
            existenciaTotal[productoId] = await db.Existencias.Where(e => e.ProductoId == productoId).SumAsync(e => e.Cantidad, ct);
        }

        // 2. Existencias: se crean (en cero) las filas con lote explícito y se bloquean todas las del par almacén/producto.
        var ahora = reloj.GetUtcNow();
        var explicitas = lineas.Where(l => l.Lote is not null)
            .Select(l => new Llave(l.AlmacenId, l.ProductoId, l.Lote!)).Distinct()
            .OrderBy(k => k.AlmacenId).ThenBy(k => k.ProductoId).ThenBy(k => k.Lote, StringComparer.Ordinal).ToList();
        foreach (var llave in explicitas)
        {
            var caducidad = lineas.FirstOrDefault(l => l.AlmacenId == llave.AlmacenId && l.ProductoId == llave.ProductoId && l.Lote == llave.Lote)?.FechaCaducidad
                ?? await CaducidadDelLoteAsync(llave.ProductoId, llave.Lote, ct);
            await CrearFilaAsync(llave, caducidad, ahora, ct);
        }

        var pares = lineas.Select(l => (l.AlmacenId, l.ProductoId)).Distinct().ToList();
        var almacenesPar = pares.Select(p => p.AlmacenId).ToArray();
        var productosPar = pares.Select(p => p.ProductoId).ToArray();
        var filas = await db.Existencias
            .FromSql($"""
                SELECT *, xmin FROM inventario.existencias
                WHERE (almacen_id, producto_id) IN (SELECT * FROM unnest({almacenesPar}, {productosPar}))
                ORDER BY almacen_id, producto_id, lote
                FOR UPDATE
                """)
            .ToListAsync(ct);
        var existencias = filas.ToDictionary(e => new Llave(e.AlmacenId, e.ProductoId, e.Lote));

        // 3. Aplicar línea por línea.
        var registrados = new List<(Movimiento Movimiento, bool Negativo)>();
        foreach (var linea in lineas)
        {
            var almacen = almacenes[linea.AlmacenId];
            var producto = productos[linea.ProductoId];
            var costo = costos[linea.ProductoId];

            if (ReglasInventario.EsEntrada(linea.Tipo))
            {
                var fila = existencias[new Llave(linea.AlmacenId, linea.ProductoId, linea.Lote!)];
                decimal costoUnitario;
                if (ReglasInventario.RecalculaCosto(linea.Tipo, linea.CostoUnitario))
                {
                    costoUnitario = Dinero.RedondearCosto(linea.CostoUnitario ?? costo.CostoPromedio);
                    var total = existenciaTotal[linea.ProductoId];
                    costo.CostoPromedio = ReglasInventario.CostoPromedioPonderado(total, costo.CostoPromedio, linea.Cantidad, costoUnitario);
                    costo.ActualizadoEn = ahora;
                    existenciaTotal[linea.ProductoId] = total + linea.Cantidad;
                }
                else
                {
                    costoUnitario = Dinero.RedondearCosto(linea.CostoUnitario ?? costo.CostoPromedio);
                }

                registrados.Add(Aplicar(fila, linea, linea.Cantidad, costoUnitario, request, ahora, almacen));
                continue;
            }

            // Salidas: lote explícito o sin lote (productos sin lote usan "").
            var costoSalida = Dinero.RedondearCosto(linea.CostoUnitario ?? costo.CostoPromedio);
            if (linea.Lote is not null)
            {
                var fila = existencias[new Llave(linea.AlmacenId, linea.ProductoId, linea.Lote)];
                if (fila.Cantidad + linea.Cantidad < 0 && !almacen.PermiteNegativo && !request.PermitirNegativo)
                {
                    return Insuficiente(producto, almacen, fila.Cantidad, -linea.Cantidad);
                }

                registrados.Add(Aplicar(fila, linea, linea.Cantidad, costoSalida, request, ahora, almacen));
                AjustarTotal(existenciaTotal, linea.ProductoId, linea.Cantidad);
                continue;
            }

            // RN-INV-07: FEFO sobre los lotes con existencia.
            var lotes = ReglasInventario.OrdenFefo(existencias.Values
                .Where(e => e.AlmacenId == linea.AlmacenId && e.ProductoId == linea.ProductoId && e.Cantidad > 0)).ToList();
            var pendiente = -linea.Cantidad;
            var disponible = lotes.Sum(l => l.Cantidad);
            if (disponible < pendiente && !almacen.PermiteNegativo && !request.PermitirNegativo)
            {
                return Insuficiente(producto, almacen, disponible, pendiente);
            }

            foreach (var lote in lotes)
            {
                if (pendiente <= 0)
                {
                    break;
                }

                var tomar = Math.Min(lote.Cantidad, pendiente);
                registrados.Add(Aplicar(lote, linea, -tomar, costoSalida, request, ahora, almacen));
                pendiente -= tomar;
            }

            if (pendiente > 0)
            {
                // Solo si se permite negativo (ruta, RN-INV-03, o PermitirNegativo): el faltante queda en el lote más reciente.
                var destino = ReglasInventario.OrdenFefo(existencias.Values
                    .Where(e => e.AlmacenId == linea.AlmacenId && e.ProductoId == linea.ProductoId)).LastOrDefault();
                if (destino is null)
                {
                    var llave = new Llave(linea.AlmacenId, linea.ProductoId, string.Empty);
                    await CrearFilaAsync(llave, null, ahora, ct);
                    destino = await db.Existencias
                        .FromSql($"SELECT *, xmin FROM inventario.existencias WHERE almacen_id = {llave.AlmacenId} AND producto_id = {llave.ProductoId} AND lote = '' FOR UPDATE")
                        .SingleAsync(ct);
                    existencias[llave] = destino;
                }

                registrados.Add(Aplicar(destino, linea, -pendiente, costoSalida, request, ahora, almacen));
            }

            AjustarTotal(existenciaTotal, linea.ProductoId, linea.Cantidad);
        }

        foreach (var (movimiento, _) in registrados)
        {
            db.Movimientos.Add(movimiento);
        }

        await db.SaveChangesAsync(ct);

        // Las filas bloqueadas no se quedan en el contexto: la siguiente operación las lee frescas.
        foreach (var entrada in db.ChangeTracker.Entries().Where(e => e.Entity is Existencia or CostoProducto or Movimiento).ToList())
        {
            entrada.State = EntityState.Detached;
        }

        return registrados.Select(r => AResultado(r.Movimiento, r.Negativo)).ToList();
    }

    private static Result<LineaMovimiento> Validar(LineaMovimiento linea, Dictionary<Guid, Almacen> almacenes,
        IReadOnlyDictionary<Guid, ProductoInfo> productos)
    {
        if (!almacenes.TryGetValue(linea.AlmacenId, out var almacen))
        {
            return Error.NoEncontrado(ErroresInventario.AlmacenNoEncontrado, "El almacén no existe.");
        }

        if (!almacen.Activo)
        {
            return Error.Conflicto(ErroresInventario.AlmacenInactivo, $"El almacén {almacen.Codigo} está inactivo.");
        }

        if (!productos.TryGetValue(linea.ProductoId, out var producto))
        {
            return Error.NoEncontrado(ErroresInventario.ProductoNoEncontrado, "El producto no existe.");
        }

        var entrada = ReglasInventario.EsEntrada(linea.Tipo);
        if (linea.Cantidad == 0 || (entrada && linea.Cantidad < 0) || (!entrada && linea.Cantidad > 0))
        {
            return Error.Validacion(ErroresInventario.SignoInvalido,
                $"La cantidad de un movimiento {linea.Tipo} debe ser {(entrada ? "positiva" : "negativa")} y distinta de cero.");
        }

        string? lote;
        if (!producto.ManejaLote)
        {
            lote = string.Empty;
        }
        else
        {
            lote = string.IsNullOrWhiteSpace(linea.Lote) ? null : linea.Lote.Trim().ToUpperInvariant();
            if (entrada && lote is null)
            {
                return Error.Campo(ErroresInventario.LoteRequerido, "lote", $"{producto.Nombre} maneja lote: indica el lote de la entrada.");
            }
        }

        return linea with { Lote = lote, Cantidad = Dinero.RedondearCantidad(linea.Cantidad) };
    }

    private (Movimiento, bool) Aplicar(Existencia fila, LineaMovimiento linea, decimal cantidad, decimal costoUnitario,
        RegistrarMovimientosRequest request, DateTimeOffset ahora, Almacen almacen)
    {
        fila.Cantidad += cantidad;
        fila.ActualizadoEn = ahora;
        if (linea.FechaCaducidad is not null && fila.FechaCaducidad is null)
        {
            fila.FechaCaducidad = linea.FechaCaducidad;
        }

        var movimiento = new Movimiento
        {
            FechaHora = ahora,
            FechaOperativa = request.FechaOperativa,
            Tipo = linea.Tipo,
            AlmacenId = linea.AlmacenId,
            ProductoId = linea.ProductoId,
            Lote = fila.Lote,
            Cantidad = cantidad,
            CostoUnitario = costoUnitario,
            CostoTotal = Dinero.Redondear(cantidad * costoUnitario),
            ExistenciaResultante = fila.Cantidad,
            DocumentoTipo = request.DocumentoTipo,
            DocumentoId = request.DocumentoId,
            DocumentoFolio = request.DocumentoFolio,
            MotivoMermaId = linea.MotivoMermaId,
            UsuarioId = request.UsuarioId,
            DispositivoId = request.DispositivoId,
            Notas = linea.Notas ?? request.Notas,
            FechaCaducidad = fila.FechaCaducidad,
        };
        return (movimiento, fila.Cantidad < 0 && (almacen.PermiteNegativo || request.PermitirNegativo));
    }

    private static void AjustarTotal(Dictionary<Guid, decimal> totales, Guid productoId, decimal cantidad)
    {
        if (totales.TryGetValue(productoId, out var total))
        {
            totales[productoId] = total + cantidad;
        }
    }

    private static Error Insuficiente(ProductoInfo producto, Almacen almacen, decimal disponible, decimal requerido)
    {
        var d = disponible.ToString("0.####", CultureInfo.InvariantCulture);
        var r = requerido.ToString("0.####", CultureInfo.InvariantCulture);
        return new Error(ErroresInventario.ExistenciaInsuficiente,
            $"Existencia insuficiente de {producto.Nombre} en {almacen.Codigo}: disponible {d} {producto.UnidadBaseClave}, requerido {r}.",
            TipoError.Conflicto,
            Detalle: new
            {
                productoId = producto.Id,
                producto = producto.Nombre,
                almacenId = almacen.Id,
                almacen = almacen.Codigo,
                disponible,
                requerido,
            });
    }

    private async Task AsegurarCostosAsync(Guid[] productos, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO inventario.costos_producto (producto_id, costo_promedio, actualizado_en)
            SELECT p, 0, {ahora} FROM unnest({productos}) AS p
            ON CONFLICT DO NOTHING
            """, ct);
    }

    private Task CrearFilaAsync(Llave llave, DateOnly? caducidad, DateTimeOffset ahora, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO inventario.existencias (almacen_id, producto_id, lote, cantidad, fecha_caducidad, fecha_entrada, actualizado_en)
            VALUES ({llave.AlmacenId}, {llave.ProductoId}, {llave.Lote}, 0, {caducidad}, {ahora}, {ahora})
            ON CONFLICT DO NOTHING
            """, ct);

    private Task<DateOnly?> CaducidadDelLoteAsync(Guid productoId, string lote, CancellationToken ct) =>
        lote.Length == 0
            ? Task.FromResult<DateOnly?>(null)
            : db.Existencias.AsNoTracking()
                .Where(e => e.ProductoId == productoId && e.Lote == lote && e.FechaCaducidad != null)
                .Select(e => e.FechaCaducidad)
                .FirstOrDefaultAsync(ct);

    public async Task<decimal> ObtenerExistenciaAsync(Guid almacenId, Guid productoId, string? lote, CancellationToken ct)
    {
        var consulta = db.Existencias.AsNoTracking().Where(e => e.AlmacenId == almacenId && e.ProductoId == productoId);
        if (lote is not null)
        {
            consulta = consulta.Where(e => e.Lote == lote);
        }

        return await consulta.SumAsync(e => e.Cantidad, ct);
    }

    public async Task<decimal> ObtenerCostoPromedioAsync(Guid productoId, CancellationToken ct) =>
        await db.CostosProducto.AsNoTracking().Where(c => c.ProductoId == productoId).Select(c => c.CostoPromedio)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, decimal>> ObtenerCostosPromedioAsync(IEnumerable<Guid> productoIds, CancellationToken ct)
    {
        var ids = productoIds.Distinct().ToList();
        var costos = await db.CostosProducto.AsNoTracking().Where(c => ids.Contains(c.ProductoId))
            .ToDictionaryAsync(c => c.ProductoId, c => c.CostoPromedio, ct);
        return ids.ToDictionary(id => id, id => costos.GetValueOrDefault(id));
    }

    public async Task<IReadOnlyList<ExistenciaInfo>> ExistenciasDeAlmacenAsync(Guid almacenId, CancellationToken ct) =>
        await db.Existencias.AsNoTracking().Where(e => e.AlmacenId == almacenId && e.Cantidad != 0)
            .OrderBy(e => e.ProductoId).ThenBy(e => e.Lote)
            .Select(e => new ExistenciaInfo(e.AlmacenId, e.ProductoId, e.Lote, e.Cantidad, e.FechaCaducidad))
            .ToListAsync(ct);

    public async Task<AlmacenInfo?> ObtenerAlmacenAsync(Guid id, CancellationToken ct) =>
        AInfo(await db.Almacenes.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct));

    public async Task<AlmacenInfo?> ObtenerAlmacenPorCodigoAsync(string codigo, CancellationToken ct) =>
        AInfo(await db.Almacenes.AsNoTracking().FirstOrDefaultAsync(a => a.Codigo == codigo, ct));

    public async Task<AlmacenInfo?> AlmacenDeRutaAsync(Guid rutaId, CancellationToken ct) =>
        AInfo(await db.Almacenes.AsNoTracking().FirstOrDefaultAsync(a => a.RutaId == rutaId, ct));

    public async Task<IReadOnlyList<AlmacenInfo>> ListarAlmacenesAsync(CancellationToken ct) =>
        (await db.Almacenes.AsNoTracking().OrderBy(a => a.Codigo).ToListAsync(ct)).Select(a => AInfo(a)!).ToList();

    public async Task<Result<AlmacenInfo>> CrearAlmacenRutaAsync(Guid rutaId, string codigo, string nombre, CancellationToken ct)
    {
        var existente = await db.Almacenes.FirstOrDefaultAsync(a => a.RutaId == rutaId, ct);
        if (existente is not null)
        {
            return AInfo(existente)!;
        }

        if (await db.Almacenes.AnyAsync(a => a.Codigo == codigo, ct))
        {
            return Error.Campo("INV-007", "codigo", $"Ya existe un almacén con el código {codigo}.");
        }

        var almacen = new Almacen
        {
            Codigo = codigo,
            Nombre = nombre,
            Tipo = TipoAlmacen.Ruta,
            RutaId = rutaId,
            PermiteNegativo = true,
            Activo = true,
        };
        db.Almacenes.Add(almacen);
        await db.SaveChangesAsync(ct);
        return AInfo(almacen)!;
    }

    public async Task<IReadOnlyList<MovimientoRegistrado>> MovimientosDeDocumentoAsync(DocumentoInventario tipo, Guid documentoId,
        CancellationToken ct) =>
        (await db.Movimientos.AsNoTracking().Where(m => m.DocumentoTipo == tipo && m.DocumentoId == documentoId)
            .OrderBy(m => m.FechaHora).ToListAsync(ct))
        .Select(m => AResultado(m, m.ExistenciaResultante < 0)).ToList();

    internal static AlmacenInfo? AInfo(Almacen? a) =>
        a is null ? null : new AlmacenInfo(a.Id, a.Codigo, a.Nombre, a.Tipo, a.RutaId, a.PermiteNegativo, a.Activo);

    private static MovimientoRegistrado AResultado(Movimiento m, bool negativo) =>
        new(m.Id, m.Tipo, m.AlmacenId, m.ProductoId, m.Lote, m.Cantidad, m.CostoUnitario, m.CostoTotal,
            m.ExistenciaResultante, negativo, m.FechaCaducidad);
}

/// <summary>Implementa para Catálogos la consulta de movimientos y existencia (RN-CAT-01, 02 y 05).</summary>
internal sealed class ConsultaInventarioProducto(InventarioDbContext db) : IConsultaInventarioProducto
{
    public Task<bool> TieneMovimientosAsync(Guid productoId, CancellationToken ct) =>
        db.Movimientos.AnyAsync(m => m.ProductoId == productoId, ct);

    public Task<decimal> ExistenciaTotalAsync(Guid productoId, CancellationToken ct) =>
        db.Existencias.Where(e => e.ProductoId == productoId).SumAsync(e => e.Cantidad, ct);
}
