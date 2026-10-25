using TG.Inventario.Publico;

namespace TG.Api.Trazabilidad;

// Trazabilidad de lotes (T-8.16, RN-INV-14 a RN-INV-17). El grafo se arma sobre el kardex: cada movimiento trae
// producto, lote, almacén y documento origen. Un nodo "lote" es un lote en un almacén; los documentos (recepción,
// orden de producción, carga, venta, retorno, liquidación…) lo unen con los lotes que entran y salen de él.

public enum DireccionTraza
{
    Origen,
    Destino,
    Ambas,
}

public enum TipoNodoTraza
{
    Proveedor,
    Recepcion,
    Lote,
    Produccion,
    Envio,
    Traspaso,
    Venta,
    Cliente,
    Retorno,
    Liquidacion,
    Ajuste,
    Merma,
    Regreso,
}

public enum TipoAristaTraza
{
    Suministro,
    Entrada,
    Consumo,
    Produccion,
    Salida,
    Llegada,
    Venta,
    Entrega,
    Merma,
    Rechazo,
    Regreso,
}

public sealed record DatoTrazaDto(string Etiqueta, string Valor);

/// <summary>Componente consumido por una orden sin control de lote (RN-INV-16): no tiene origen de compra.</summary>
public sealed record ConsumoSinLoteDto(Guid ProductoId, string Sku, string Producto, decimal Cantidad, string Unidad);

/// <param name="Datos">Pares etiqueta/valor para la ficha de detalle (proveedor, OC, ruta, chofer…).</param>
public sealed record NodoTrazaDto(
    string Id,
    TipoNodoTraza Tipo,
    string Titulo,
    string? Subtitulo,
    Guid? ProductoId,
    string? Sku,
    string? Producto,
    string? TipoProducto,
    string? Unidad,
    string? Lote,
    DateOnly? Caducidad,
    string? Almacen,
    string? TipoAlmacen,
    decimal? Entradas,
    decimal? Salidas,
    decimal? Existencia,
    DocumentoInventario? DocumentoTipo,
    Guid? DocumentoId,
    string? Folio,
    DateTimeOffset? Fecha,
    string? Estado,
    IReadOnlyList<DatoTrazaDto> Datos,
    IReadOnlyList<ConsumoSinLoteDto> ConsumoSinLote);

/// <param name="Unidad">Unidad base del producto; nula si la arista junta productos de distinta unidad.</param>
public sealed record AristaTrazaDto(string Desde, string Hacia, TipoAristaTraza Tipo, decimal? Cantidad, string? Unidad);

public sealed record EventoTrazaDto(
    Guid Id,
    DateTimeOffset FechaHora,
    DateOnly FechaOperativa,
    TipoMovimiento Tipo,
    DocumentoInventario DocumentoTipo,
    Guid DocumentoId,
    string Folio,
    string? NodoId,
    string Sku,
    string Producto,
    string Lote,
    decimal Cantidad,
    string Unidad,
    string Almacen);

/// <summary>Opciones del recorrido: profundidad en niveles y tope de nodos (RN-INV-17).</summary>
public sealed record OpcionesTraza(DireccionTraza Direccion, int ProfundidadMaxima = 20, int NodosMaximos = 2000);

/// <summary>Lote en un almacén: la unidad mínima del kardex.</summary>
public readonly record struct LoteAlmacen(Guid ProductoId, string Lote, Guid AlmacenId)
{
    public string Id => $"lot:{ProductoId:N}:{AlmacenId:N}:{Lote}";
}

public readonly record struct DocumentoTraza(DocumentoInventario Tipo, Guid Id)
{
    public string NodoId => $"doc:{Id:N}";
}

/// <summary>Resultado crudo del recorrido, antes de ponerle nombres (proveedor, cliente, producto…).</summary>
public sealed class GrafoTraza
{
    public HashSet<LoteAlmacen> Lotes { get; } = [];

    /// <summary>Documento → folio y primera fecha en que movió inventario.</summary>
    public Dictionary<DocumentoTraza, (string Folio, DateTimeOffset Fecha)> Documentos { get; } = [];

    /// <summary>Arista → (cantidad, producto). La cantidad se asigna, no se acumula: recorrer dos veces da lo mismo.</summary>
    public Dictionary<(string Desde, string Hacia, TipoAristaTraza Tipo), (decimal Cantidad, Guid? ProductoId)> Aristas { get; } = [];

    /// <summary>Nodos de merma por documento y de regreso por documento y almacén.</summary>
    public Dictionary<string, (TipoNodoTraza Tipo, DocumentoTraza Documento, Guid? AlmacenId)> Terminales { get; } = [];

    public Dictionary<DocumentoTraza, List<(Guid ProductoId, decimal Cantidad)>> ConsumoSinLote { get; } = [];

    public List<MovimientoKardex> Movimientos { get; } = [];

    public bool Truncado { get; set; }

    public int Nodos => Lotes.Count + Documentos.Count + Terminales.Count;
}

/// <summary>
/// Recorre el kardex por niveles (búsqueda en anchura) hacia el origen y hacia el destino. Cada nivel se lee en una
/// sola consulta por tipo (lotes o documentos), nunca una por nodo.
/// </summary>
public sealed class ConstructorTraza(IConsultaKardex kardex)
{
    private readonly Dictionary<(Guid ProductoId, string Lote), List<MovimientoKardex>> _porLote = [];
    private readonly Dictionary<Guid, List<MovimientoKardex>> _porDocumento = [];
    private readonly GrafoTraza _grafo = new();

    /// <summary>Documentos cuyos movimientos de entrada a otro almacén son retornos o sobrantes, no origen.</summary>
    private static bool EsRegreso(DocumentoInventario tipo) => tipo is DocumentoInventario.Retorno or DocumentoInventario.Liquidacion;

    /// <summary>Documentos que trasladan el mismo lote de un almacén a otro.</summary>
    private static bool EsTraslado(DocumentoInventario tipo) => tipo is DocumentoInventario.Envio or DocumentoInventario.Traspaso;

    public async Task<GrafoTraza> DesdeLoteAsync(Guid productoId, string lote, OpcionesTraza opciones, CancellationToken ct)
    {
        await CargarLotesAsync([(productoId, lote)], ct);
        var lotes = Movimientos(productoId, lote).Select(m => new LoteAlmacen(productoId, lote, m.AlmacenId)).Distinct().ToList();
        await RecorrerAsync(lotes, lotes, opciones, ct);
        return _grafo;
    }

    public async Task<GrafoTraza> DesdeDocumentosAsync(IReadOnlyCollection<DocumentoTraza> documentos, OpcionesTraza opciones, CancellationToken ct)
    {
        await CargarDocumentosAsync(documentos, ct);
        var origen = new HashSet<LoteAlmacen>();
        var destino = new HashSet<LoteAlmacen>();
        foreach (var documento in documentos)
        {
            var movimientos = _porDocumento.GetValueOrDefault(documento.Id) ?? [];
            foreach (var m in movimientos.Where(m => m.Tipo == TipoMovimiento.Merma && m.Lote != string.Empty))
            {
                Merma(Lote(m), documento, m);
            }

            foreach (var (lote, neto) in Netos(movimientos))
            {
                Documento(documento, movimientos);
                if (neto < 0)
                {
                    Arista(lote.Id, documento.NodoId, TipoSalida(documento.Tipo), -neto, lote.ProductoId);
                    origen.Add(lote);
                }
                else if (documento.Tipo == DocumentoInventario.Retorno)
                {
                    Arista(lote.Id, documento.NodoId, TipoAristaTraza.Rechazo, neto, lote.ProductoId);
                    origen.Add(lote);
                }
                else if (documento.Tipo == DocumentoInventario.Liquidacion)
                {
                    Regreso(documento, lote, neto);
                }
                else
                {
                    Arista(documento.NodoId, lote.Id, TipoEntrada(documento.Tipo), neto, lote.ProductoId);
                    destino.Add(lote);
                }
            }

            if (movimientos.Count > 0)
            {
                Documento(documento, movimientos);
            }
        }

        await RecorrerAsync(origen, destino, opciones, ct);
        return _grafo;
    }

    private async Task RecorrerAsync(IReadOnlyCollection<LoteAlmacen> origen, IReadOnlyCollection<LoteAlmacen> destino, OpcionesTraza opciones,
        CancellationToken ct)
    {
        foreach (var lote in origen.Concat(destino))
        {
            _grafo.Lotes.Add(lote);
        }

        if (opciones.Direccion is DireccionTraza.Origen or DireccionTraza.Ambas)
        {
            await PasadaAsync(origen, haciaOrigen: true, opciones, ct);
        }

        if (opciones.Direccion is DireccionTraza.Destino or DireccionTraza.Ambas)
        {
            await PasadaAsync(destino, haciaOrigen: false, opciones, ct);
        }

        await CerrarAsync(ct);
    }

    private async Task PasadaAsync(IReadOnlyCollection<LoteAlmacen> inicio, bool haciaOrigen, OpcionesTraza opciones, CancellationToken ct)
    {
        var visitados = new HashSet<LoteAlmacen>();
        var frontera = inicio.ToHashSet();
        for (var nivel = 0; frontera.Count > 0; nivel++)
        {
            if (nivel >= opciones.ProfundidadMaxima || _grafo.Nodos >= opciones.NodosMaximos)
            {
                _grafo.Truncado = true;
                return;
            }

            await CargarLotesAsync(frontera.Select(l => (l.ProductoId, l.Lote)), ct);
            var siguiente = new HashSet<LoteAlmacen>();
            var ordenes = new HashSet<DocumentoTraza>();
            foreach (var lote in frontera.Where(visitados.Add))
            {
                _grafo.Lotes.Add(lote);
                if (haciaOrigen)
                {
                    HaciaOrigen(lote, siguiente, ordenes);
                }
                else
                {
                    HaciaDestino(lote, siguiente, ordenes);
                }
            }

            // Una orden de producción transforma: lo que consumió (hacia el origen) o lo que produjo (hacia el destino)
            // son otros lotes, que se leen por documento.
            await CargarDocumentosAsync(ordenes, ct);
            foreach (var orden in ordenes)
            {
                var movimientos = _porDocumento.GetValueOrDefault(orden.Id) ?? [];
                foreach (var (otro, neto) in Netos(movimientos))
                {
                    if (haciaOrigen && neto < 0)
                    {
                        Arista(otro.Id, orden.NodoId, TipoAristaTraza.Consumo, -neto, otro.ProductoId);
                        siguiente.Add(otro);
                    }
                    else if (!haciaOrigen && neto > 0)
                    {
                        Arista(orden.NodoId, otro.Id, TipoAristaTraza.Produccion, neto, otro.ProductoId);
                        siguiente.Add(otro);
                    }
                }
            }

            siguiente.ExceptWith(visitados);
            frontera = siguiente;
        }
    }

    private void HaciaOrigen(LoteAlmacen lote, HashSet<LoteAlmacen> siguiente, HashSet<DocumentoTraza> ordenes)
    {
        var movimientos = Movimientos(lote.ProductoId, lote.Lote);
        foreach (var grupo in movimientos.Where(m => m.AlmacenId == lote.AlmacenId && m.Tipo != TipoMovimiento.Merma)
                     .GroupBy(m => new DocumentoTraza(m.DocumentoTipo, m.DocumentoId)))
        {
            var documento = grupo.Key;
            var neto = grupo.Sum(m => m.Cantidad);
            if (neto <= 0 || EsRegreso(documento.Tipo) || documento.Tipo == DocumentoInventario.Venta)
            {
                continue;
            }

            Documento(documento, grupo);
            Arista(documento.NodoId, lote.Id, TipoEntrada(documento.Tipo), neto, lote.ProductoId);
            if (documento.Tipo == DocumentoInventario.OrdenProduccion)
            {
                ordenes.Add(documento);
            }
            else if (EsTraslado(documento.Tipo))
            {
                foreach (var (origen, cantidad) in MismoLoteEnDocumento(lote, documento, movimientos).Where(x => x.Neto < 0))
                {
                    Arista(origen.Id, documento.NodoId, TipoAristaTraza.Salida, -cantidad, lote.ProductoId);
                    siguiente.Add(origen);
                }
            }
        }
    }

    private void HaciaDestino(LoteAlmacen lote, HashSet<LoteAlmacen> siguiente, HashSet<DocumentoTraza> ordenes)
    {
        var movimientos = Movimientos(lote.ProductoId, lote.Lote);
        var propios = movimientos.Where(m => m.AlmacenId == lote.AlmacenId).ToList();
        foreach (var m in propios.Where(m => m.Tipo == TipoMovimiento.Merma))
        {
            Merma(lote, new DocumentoTraza(m.DocumentoTipo, m.DocumentoId), m);
        }

        foreach (var grupo in propios.Where(m => m.Tipo != TipoMovimiento.Merma).GroupBy(m => new DocumentoTraza(m.DocumentoTipo, m.DocumentoId)))
        {
            var documento = grupo.Key;
            var neto = grupo.Sum(m => m.Cantidad);
            if (neto > 0 && documento.Tipo == DocumentoInventario.Retorno)
            {
                // RN-INV-15: lo que el cliente regresa se dibuja como rechazo, después del lote.
                Documento(documento, grupo);
                Arista(lote.Id, documento.NodoId, TipoAristaTraza.Rechazo, neto, lote.ProductoId);
                continue;
            }

            if (neto >= 0)
            {
                continue;
            }

            Documento(documento, grupo);
            Arista(lote.Id, documento.NodoId, TipoSalida(documento.Tipo), -neto, lote.ProductoId);
            if (documento.Tipo == DocumentoInventario.OrdenProduccion)
            {
                ordenes.Add(documento);
            }
            else if (EsTraslado(documento.Tipo))
            {
                foreach (var (destino, cantidad) in MismoLoteEnDocumento(lote, documento, movimientos).Where(x => x.Neto > 0))
                {
                    Arista(documento.NodoId, destino.Id, TipoAristaTraza.Llegada, cantidad, lote.ProductoId);
                    siguiente.Add(destino);
                }
            }
            else if (documento.Tipo == DocumentoInventario.Liquidacion)
            {
                foreach (var (destino, cantidad) in MismoLoteEnDocumento(lote, documento, movimientos).Where(x => x.Neto > 0))
                {
                    Regreso(documento, destino, cantidad);
                }
            }
        }
    }

    /// <summary>Lotes, órdenes sin control de lote y bitácora de movimientos de todo lo que quedó en el grafo.</summary>
    private async Task CerrarAsync(CancellationToken ct)
    {
        await CargarLotesAsync(_grafo.Lotes.Select(l => (l.ProductoId, l.Lote)), ct);
        var ordenes = _grafo.Documentos.Keys.Where(d => d.Tipo == DocumentoInventario.OrdenProduccion).ToList();
        await CargarDocumentosAsync(ordenes, ct);
        foreach (var orden in ordenes)
        {
            var sinLote = (_porDocumento.GetValueOrDefault(orden.Id) ?? [])
                .Where(m => m.Lote == string.Empty && m.Tipo != TipoMovimiento.Merma)
                .GroupBy(m => m.ProductoId)
                .Select(g => (ProductoId: g.Key, Cantidad: -g.Sum(m => m.Cantidad)))
                .Where(x => x.Cantidad > 0)
                .ToList();
            if (sinLote.Count > 0)
            {
                _grafo.ConsumoSinLote[orden] = sinLote;
            }
        }

        var vistos = new HashSet<Guid>();
        foreach (var lote in _grafo.Lotes)
        {
            foreach (var m in Movimientos(lote.ProductoId, lote.Lote).Where(m => m.AlmacenId == lote.AlmacenId && vistos.Add(m.Id)))
            {
                _grafo.Movimientos.Add(m);
            }
        }

        foreach (var orden in ordenes)
        {
            foreach (var m in (_porDocumento.GetValueOrDefault(orden.Id) ?? []).Where(m => m.Lote == string.Empty && vistos.Add(m.Id)))
            {
                _grafo.Movimientos.Add(m);
            }
        }

        _grafo.Movimientos.Sort((a, b) => a.FechaHora != b.FechaHora
            ? a.FechaHora.CompareTo(b.FechaHora)
            : string.CompareOrdinal(a.DocumentoFolio, b.DocumentoFolio));
    }

    /// <summary>Neto por lote y almacén dentro de un documento, sin mermas ni productos sin lote (RN-INV-16).</summary>
    private static IEnumerable<(LoteAlmacen Lote, decimal Neto)> Netos(IEnumerable<MovimientoKardex> movimientos) =>
        movimientos.Where(m => m.Lote != string.Empty && m.Tipo != TipoMovimiento.Merma)
            .GroupBy(Lote)
            .Select(g => (g.Key, g.Sum(m => m.Cantidad)))
            .Where(x => x.Item2 != 0);

    private static IEnumerable<(LoteAlmacen Lote, decimal Neto)> MismoLoteEnDocumento(LoteAlmacen lote, DocumentoTraza documento,
        IEnumerable<MovimientoKardex> movimientos) =>
        Netos(movimientos.Where(m => m.DocumentoId == documento.Id && m.AlmacenId != lote.AlmacenId));

    private static LoteAlmacen Lote(MovimientoKardex m) => new(m.ProductoId, m.Lote, m.AlmacenId);

    private static TipoAristaTraza TipoEntrada(DocumentoInventario tipo) => tipo switch
    {
        DocumentoInventario.OrdenProduccion => TipoAristaTraza.Produccion,
        DocumentoInventario.Envio or DocumentoInventario.Traspaso => TipoAristaTraza.Llegada,
        _ => TipoAristaTraza.Entrada,
    };

    private static TipoAristaTraza TipoSalida(DocumentoInventario tipo) => tipo switch
    {
        DocumentoInventario.OrdenProduccion => TipoAristaTraza.Consumo,
        DocumentoInventario.Venta => TipoAristaTraza.Venta,
        _ => TipoAristaTraza.Salida,
    };

    private IReadOnlyList<MovimientoKardex> Movimientos(Guid productoId, string lote) =>
        _porLote.GetValueOrDefault((productoId, lote)) ?? [];

    private void Documento(DocumentoTraza documento, IEnumerable<MovimientoKardex> movimientos)
    {
        var primero = movimientos.MinBy(m => m.FechaHora);
        if (primero is null)
        {
            return;
        }

        if (!_grafo.Documentos.TryGetValue(documento, out var actual) || primero.FechaHora < actual.Fecha)
        {
            _grafo.Documentos[documento] = (primero.DocumentoFolio, primero.FechaHora);
        }
    }

    private void Arista(string desde, string hacia, TipoAristaTraza tipo, decimal cantidad, Guid? productoId) =>
        _grafo.Aristas[(desde, hacia, tipo)] = (cantidad, productoId);

    private void Merma(LoteAlmacen lote, DocumentoTraza documento, MovimientoKardex movimiento)
    {
        var id = $"mer:{documento.Id:N}";
        _grafo.Lotes.Add(lote);
        _grafo.Terminales[id] = (TipoNodoTraza.Merma, documento, null);
        _grafo.Documentos.TryAdd(documento, (movimiento.DocumentoFolio, movimiento.FechaHora));
        var cantidad = Movimientos(lote.ProductoId, lote.Lote).Concat(_porDocumento.GetValueOrDefault(documento.Id) ?? [])
            .Where(m => m.AlmacenId == lote.AlmacenId && m.Lote == lote.Lote && m.ProductoId == lote.ProductoId && m.DocumentoId == documento.Id
                        && m.Tipo == TipoMovimiento.Merma)
            .DistinctBy(m => m.Id)
            .Sum(m => -m.Cantidad);
        Arista(lote.Id, id, TipoAristaTraza.Merma, cantidad, lote.ProductoId);
    }

    private void Regreso(DocumentoTraza documento, LoteAlmacen destino, decimal cantidad)
    {
        var id = $"reg:{documento.Id:N}:{destino.AlmacenId:N}";
        _grafo.Terminales[id] = (TipoNodoTraza.Regreso, documento, destino.AlmacenId);
        Arista(documento.NodoId, id, TipoAristaTraza.Regreso, cantidad, destino.ProductoId);
    }

    private async Task CargarLotesAsync(IEnumerable<(Guid ProductoId, string Lote)> lotes, CancellationToken ct)
    {
        var faltan = lotes.Where(l => l.Lote != string.Empty && !_porLote.ContainsKey(l)).Distinct().ToList();
        if (faltan.Count == 0)
        {
            return;
        }

        var movimientos = await kardex.MovimientosDeLotesAsync(faltan, ct);
        foreach (var clave in faltan)
        {
            _porLote[clave] = [];
        }

        foreach (var m in movimientos)
        {
            _porLote[(m.ProductoId, m.Lote)].Add(m);
        }
    }

    private async Task CargarDocumentosAsync(IEnumerable<DocumentoTraza> documentos, CancellationToken ct)
    {
        var faltan = documentos.Where(d => !_porDocumento.ContainsKey(d.Id)).Distinct().ToList();
        if (faltan.Count == 0)
        {
            return;
        }

        var movimientos = await kardex.MovimientosDeDocumentosAsync(faltan.Select(d => (d.Tipo, d.Id)).ToList(), ct);
        foreach (var documento in faltan)
        {
            _porDocumento[documento.Id] = [];
        }

        foreach (var m in movimientos.Where(m => _porDocumento.ContainsKey(m.DocumentoId)))
        {
            _porDocumento[m.DocumentoId].Add(m);
        }
    }
}
