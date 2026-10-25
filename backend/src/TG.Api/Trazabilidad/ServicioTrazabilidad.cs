using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using TG.Catalogos.Publico;
using TG.Compras.Publico;
using TG.Inventario.Publico;
using TG.Logistica.Publico;
using TG.Produccion.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.Ventas.Publico;

namespace TG.Api.Trazabilidad;

/// <summary>Por dónde se entra a la trazabilidad: un lote, una orden de compra o un documento que movió inventario.</summary>
public enum TipoEntradaTraza
{
    Lote,
    OrdenCompra,
    Recepcion,
    OrdenProduccion,
    Envio,
    Venta,
    Retorno,
    Liquidacion,
    Traspaso,
    Ajuste,
    ConteoFisico,
}

public sealed record EntradaTrazaDto(TipoEntradaTraza Tipo, Guid? Id, Guid? ProductoId, string? Lote, string Etiqueta);

/// <param name="Avisos">
/// <c>sin_lotes</c> (la entrada no tiene lotes que seguir), <c>truncado</c> (se alcanzó el tope, RN-INV-17) y
/// <c>componentes_sin_lote</c> (RN-INV-16).
/// </param>
public sealed record TrazaDto(
    EntradaTrazaDto Entrada,
    DireccionTraza Direccion,
    DateTimeOffset Al,
    bool Truncado,
    IReadOnlyList<string> Avisos,
    IReadOnlyList<NodoTrazaDto> Nodos,
    IReadOnlyList<AristaTrazaDto> Aristas,
    IReadOnlyList<EventoTrazaDto> Eventos);

public sealed record OpcionTrazaDto(TipoEntradaTraza Tipo, Guid? Id, Guid? ProductoId, string? Lote, string Etiqueta, string Detalle);

public static class ErroresTrazabilidad
{
    public const string EntradaInvalida = "INV-020";
    public const string NoEncontrada = "INV-021";
}

public static class AvisosTraza
{
    public const string SinLotes = "sin_lotes";
    public const string Truncado = "truncado";
    public const string ComponentesSinLote = "componentes_sin_lote";
}

public sealed class ServicioTrazabilidad(
    IConsultaKardex kardex,
    ICatalogosService catalogos,
    IInventarioService inventario,
    IConsultaCompras compras,
    IConsultaOrdenesProduccion produccion,
    IConsultaEnvios envios,
    IConsultaVentas ventas,
    IUsuarioActual usuario,
    TimeProvider reloj,
    ILogger<ServicioTrazabilidad> log)
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>RN-INV-14: una OC se sigue hacia adelante y una venta hacia atrás; lo demás en ambas direcciones.</summary>
    public static DireccionTraza DireccionPredeterminada(TipoEntradaTraza tipo) => tipo switch
    {
        TipoEntradaTraza.OrdenCompra or TipoEntradaTraza.Recepcion => DireccionTraza.Destino,
        TipoEntradaTraza.Venta => DireccionTraza.Origen,
        _ => DireccionTraza.Ambas,
    };

    public async Task<Result<TrazaDto>> RastrearAsync(TipoEntradaTraza tipo, Guid? id, Guid? productoId, string? lote,
        DireccionTraza? direccion, CancellationToken ct)
    {
        var sentido = direccion ?? DireccionPredeterminada(tipo);
        var opciones = new OpcionesTraza(sentido);
        var constructor = new ConstructorTraza(kardex);
        GrafoTraza grafo;
        string etiqueta;
        switch (tipo)
        {
            case TipoEntradaTraza.Lote:
            {
                if (productoId is null || string.IsNullOrWhiteSpace(lote))
                {
                    return Error.Validacion(ErroresTrazabilidad.EntradaInvalida, "Indica el producto y el lote.");
                }

                var texto = lote.Trim().ToUpperInvariant();
                var producto = await catalogos.ObtenerProductoAsync(productoId.Value, ct);
                if (producto is null)
                {
                    return Error.NoEncontrado(ErroresTrazabilidad.NoEncontrada, "El producto no existe.");
                }

                grafo = await constructor.DesdeLoteAsync(producto.Id, texto, opciones, ct);
                if (grafo.Lotes.Count == 0)
                {
                    return Error.NoEncontrado(ErroresTrazabilidad.NoEncontrada, $"El lote {texto} de {producto.Nombre} no tiene movimientos.");
                }

                etiqueta = $"{texto} · {producto.Nombre}";
                break;
            }

            case TipoEntradaTraza.OrdenCompra:
            {
                var orden = id is { } ocId ? await compras.ObtenerOrdenAsync(ocId, ct) : null;
                if (orden is null)
                {
                    return Error.NoEncontrado(ErroresTrazabilidad.NoEncontrada, "La orden de compra no existe.");
                }

                var recepciones = await compras.RecepcionesDeOrdenAsync(orden.Id, ct);
                grafo = await constructor.DesdeDocumentosAsync(
                    recepciones.Select(r => new DocumentoTraza(DocumentoInventario.Recepcion, r)).ToList(), opciones, ct);
                etiqueta = $"{orden.Folio} · {orden.Proveedor}";
                break;
            }

            default:
            {
                if (id is null)
                {
                    return Error.Validacion(ErroresTrazabilidad.EntradaInvalida, "Indica el documento.");
                }

                var documento = new DocumentoTraza(Enum.Parse<DocumentoInventario>(tipo.ToString()), id.Value);
                grafo = await constructor.DesdeDocumentosAsync([documento], opciones, ct);
                if (!grafo.Documentos.TryGetValue(documento, out var info))
                {
                    return Error.NoEncontrado(ErroresTrazabilidad.NoEncontrada, "El documento no movió inventario.");
                }

                etiqueta = info.Folio;
                break;
            }
        }

        var traza = await DescribirAsync(grafo, new EntradaTrazaDto(tipo, id, productoId, lote?.Trim().ToUpperInvariant(), etiqueta), sentido, ct);
        log.LogInformation("Trazabilidad {Tipo} {Etiqueta} ({Direccion}) por {Usuario}: {Nodos} nodos, truncado {Truncado}",
            tipo, etiqueta, sentido, usuario.Id, traza.Nodos.Count, traza.Truncado);
        return traza;
    }

    public async Task<IReadOnlyList<OpcionTrazaDto>> BuscarAsync(string? texto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length < 2)
        {
            return [];
        }

        const int limite = 10;
        var lotes = await kardex.BuscarLotesAsync(texto, limite, ct);
        var productos = await catalogos.ObtenerProductosAsync(lotes.Select(l => l.ProductoId).Distinct(), ct);
        var documentos = await kardex.BuscarDocumentosAsync(texto, limite * 3, ct);
        var ordenes = await compras.BuscarOrdenesAsync(texto, limite, ct);

        var opciones = new List<OpcionTrazaDto>();
        opciones.AddRange(ordenes.Select(o => new OpcionTrazaDto(TipoEntradaTraza.OrdenCompra, o.Id, null, null, o.Folio,
            $"Orden de compra · {o.Proveedor}")));
        opciones.AddRange(documentos.Select(d => new OpcionTrazaDto(Enum.Parse<TipoEntradaTraza>(d.Tipo.ToString()), d.Id, null, null, d.Folio,
            NombreDocumento(d.Tipo))));
        opciones.AddRange(lotes.Select(l =>
        {
            var p = productos.GetValueOrDefault(l.ProductoId);
            var caducidad = l.FechaCaducidad is { } c ? $" · caduca {c.ToString("dd/MM/yyyy", Mx)}" : string.Empty;
            return new OpcionTrazaDto(TipoEntradaTraza.Lote, null, l.ProductoId, l.Lote, l.Lote, $"Lote · {p?.Sku} {p?.Nombre}{caducidad}");
        }));
        return opciones;
    }

    private static string NombreDocumento(DocumentoInventario tipo) => tipo switch
    {
        DocumentoInventario.Recepcion => "Recepción",
        DocumentoInventario.OrdenProduccion => "Orden de producción",
        DocumentoInventario.Envio => "Carga a ruta",
        DocumentoInventario.Venta => "Venta",
        DocumentoInventario.Retorno => "Retorno",
        DocumentoInventario.Liquidacion => "Liquidación",
        DocumentoInventario.Ajuste => "Ajuste",
        DocumentoInventario.Traspaso => "Traspaso",
        DocumentoInventario.ConteoFisico => "Conteo físico",
        _ => tipo.ToString(),
    };

    private static TipoNodoTraza TipoNodo(DocumentoInventario tipo) => tipo switch
    {
        DocumentoInventario.Recepcion => TipoNodoTraza.Recepcion,
        DocumentoInventario.OrdenProduccion => TipoNodoTraza.Produccion,
        DocumentoInventario.Envio => TipoNodoTraza.Envio,
        DocumentoInventario.Traspaso => TipoNodoTraza.Traspaso,
        DocumentoInventario.Venta => TipoNodoTraza.Venta,
        DocumentoInventario.Retorno => TipoNodoTraza.Retorno,
        DocumentoInventario.Liquidacion => TipoNodoTraza.Liquidacion,
        _ => TipoNodoTraza.Ajuste,
    };

    /// <summary>Pone nombres al grafo crudo con lo que exponen los módulos (solo su <c>Publico/</c>).</summary>
    private async Task<TrazaDto> DescribirAsync(GrafoTraza grafo, EntradaTrazaDto entrada, DireccionTraza direccion, CancellationToken ct)
    {
        var ids = (DocumentoInventario tipo) => grafo.Documentos.Keys.Where(d => d.Tipo == tipo).Select(d => d.Id).ToList();
        var productoIds = grafo.Lotes.Select(l => l.ProductoId)
            .Concat(grafo.ConsumoSinLote.Values.SelectMany(v => v.Select(x => x.ProductoId)))
            .Concat(grafo.Movimientos.Select(m => m.ProductoId))
            .Distinct().ToList();
        var productos = await catalogos.ObtenerProductosAsync(productoIds, ct);
        var almacenes = (await inventario.ListarAlmacenesAsync(ct)).ToDictionary(a => a.Id);
        var recepciones = (await compras.RecepcionesAsync(ids(DocumentoInventario.Recepcion), ct)).ToDictionary(r => r.Id);
        var ordenes = (await produccion.OrdenesAsync(ids(DocumentoInventario.OrdenProduccion), ct)).ToDictionary(o => o.Id);
        var cargas = (await envios.EnviosAsync(ids(DocumentoInventario.Envio), ct)).ToDictionary(e => e.Id);
        var retornos = (await envios.RetornosAsync(ids(DocumentoInventario.Retorno), ct)).ToDictionary(r => r.Id);
        var liquidaciones = (await envios.LiquidacionesAsync(ids(DocumentoInventario.Liquidacion), ct)).ToDictionary(l => l.Id);
        var ventasInfo = (await ventas.VentasAsync(ids(DocumentoInventario.Venta), ct)).ToDictionary(v => v.Id);
        var clientes = (await ventas.ClientesAsync(retornos.Values.Where(r => r.ClienteId != null).Select(r => r.ClienteId!.Value).ToList(), ct))
            .ToDictionary(c => c.Id);

        string Unidad(Guid? productoId) => productoId is { } p && productos.TryGetValue(p, out var info) ? info.UnidadBaseClave : string.Empty;
        string Cantidad(decimal? cantidad, string unidad) => cantidad is { } c ? $"{c.ToString("#,0.####", Mx)} {unidad}".Trim() : "—";
        string Fecha(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", Mx);

        var nodos = new List<NodoTrazaDto>();
        var aristas = grafo.Aristas.Select(a => new AristaTrazaDto(a.Key.Desde, a.Key.Hacia, a.Key.Tipo, a.Value.Cantidad, Unidad(a.Value.ProductoId)))
            .ToList();

        // Lotes en almacén: entradas, salidas y existencia salen del kardex completo del lote en ese almacén.
        foreach (var lote in grafo.Lotes)
        {
            var p = productos.GetValueOrDefault(lote.ProductoId);
            var a = almacenes.GetValueOrDefault(lote.AlmacenId);
            var movimientos = grafo.Movimientos.Where(m => m.ProductoId == lote.ProductoId && m.Lote == lote.Lote && m.AlmacenId == lote.AlmacenId)
                .ToList();
            var caducidad = movimientos.Select(m => m.FechaCaducidad).FirstOrDefault(f => f is not null);
            nodos.Add(Nodo(lote.Id, TipoNodoTraza.Lote, lote.Lote, $"{p?.Sku} · {p?.Nombre}") with
            {
                ProductoId = lote.ProductoId,
                Sku = p?.Sku,
                Producto = p?.Nombre,
                TipoProducto = p?.Tipo,
                Unidad = p?.UnidadBaseClave,
                Lote = lote.Lote,
                Caducidad = caducidad,
                Almacen = a is null ? null : $"{a.Codigo} · {a.Nombre}",
                TipoAlmacen = a?.Tipo.ToString(),
                Entradas = movimientos.Where(m => m.Cantidad > 0).Sum(m => m.Cantidad),
                Salidas = -movimientos.Where(m => m.Cantidad < 0).Sum(m => m.Cantidad),
                Existencia = movimientos.Sum(m => m.Cantidad),
            });
        }

        var avisos = new List<string>();
        foreach (var (documento, (folio, fecha)) in grafo.Documentos)
        {
            if (grafo.Aristas.Keys.All(k => k.Desde != documento.NodoId && k.Hacia != documento.NodoId))
            {
                continue; // Solo se registró como contexto de una merma.
            }

            var datos = new List<DatoTrazaDto>();
            var nodo = Nodo(documento.NodoId, TipoNodo(documento.Tipo), folio, NombreDocumento(documento.Tipo)) with
            {
                DocumentoTipo = documento.Tipo,
                DocumentoId = documento.Id,
                Folio = folio,
                Fecha = fecha,
            };
            switch (documento.Tipo)
            {
                case DocumentoInventario.Recepcion when recepciones.TryGetValue(documento.Id, out var r):
                {
                    var proveedorId = $"prov:{r.ProveedorId:N}";
                    if (nodos.All(n => n.Id != proveedorId))
                    {
                        nodos.Add(Nodo(proveedorId, TipoNodoTraza.Proveedor, r.Proveedor, r.Rfc ?? r.ProveedorCodigo) with
                        {
                            Datos = [new("Código", r.ProveedorCodigo), .. r.Rfc is null ? Array.Empty<DatoTrazaDto>() : [new("RFC", r.Rfc)]],
                        });
                    }

                    aristas.Add(new AristaTrazaDto(proveedorId, documento.NodoId, TipoAristaTraza.Suministro, null, null));
                    nodo = nodo with { Subtitulo = r.OrdenCompraFolio ?? "Compra directa", Estado = r.Estado };
                    datos.Add(new("Proveedor", r.Proveedor));
                    datos.Add(new("Orden de compra", r.OrdenCompraFolio is null ? "Compra directa" : $"{r.OrdenCompraFolio} del {Fecha(r.OrdenCompraFecha!.Value)}"));
                    if (r.FacturaProveedor is not null)
                    {
                        datos.Add(new("Factura", r.FacturaProveedor));
                    }

                    datos.Add(new("Fecha", Fecha(r.FechaOperativa)));
                    break;
                }

                case DocumentoInventario.OrdenProduccion when ordenes.TryGetValue(documento.Id, out var o):
                {
                    var p = productos.GetValueOrDefault(o.ProductoId);
                    nodo = nodo with { Subtitulo = $"{p?.Nombre} · {o.Lote}", Estado = o.Estado, ProductoId = o.ProductoId, Sku = p?.Sku, Producto = p?.Nombre,
                        Unidad = p?.UnidadBaseClave, Lote = o.Lote };
                    datos.Add(new("Producto", $"{p?.Sku} · {p?.Nombre}"));
                    datos.Add(new("Fecha y turno", $"{Fecha(o.FechaOperativa)} · {o.Turno}"));
                    if (o.Receta is not null)
                    {
                        datos.Add(new("Receta", $"{o.Receta} v{o.RecetaVersion}"));
                    }

                    if (o.Linea is not null)
                    {
                        datos.Add(new("Línea", o.Linea));
                    }

                    datos.Add(new("Planeado", Cantidad(o.CantidadPlaneada, p?.UnidadBaseClave ?? string.Empty)));
                    datos.Add(new("Producido", Cantidad(o.CantidadProducida, p?.UnidadBaseClave ?? string.Empty)));
                    break;
                }

                case DocumentoInventario.Envio when cargas.TryGetValue(documento.Id, out var e):
                    nodo = nodo with { Subtitulo = $"{e.RutaCodigo} · {e.RutaNombre}", Estado = e.Estado };
                    datos.Add(new("Ruta", $"{e.RutaCodigo} · {e.RutaNombre}"));
                    datos.Add(new("Tipo", e.Tipo == "Recarga" ? "Recarga" : "Carga inicial"));
                    datos.Add(new("Chofer", e.Chofer ?? "—"));
                    datos.Add(new("Vehículo", e.Vehiculo ?? "—"));
                    datos.Add(new("Fecha", Fecha(e.FechaOperativa)));
                    break;

                case DocumentoInventario.Venta when ventasInfo.TryGetValue(documento.Id, out var v):
                {
                    var clienteId = $"cli:{v.ClienteId:N}";
                    if (nodos.All(n => n.Id != clienteId))
                    {
                        nodos.Add(Nodo(clienteId, TipoNodoTraza.Cliente, v.Cliente, v.ClienteCodigo) with { Datos = [new("Código", v.ClienteCodigo)] });
                    }

                    var vendido = aristas.Where(x => x.Hacia == documento.NodoId).ToList();
                    var unidades = vendido.Select(x => x.Unidad).Distinct().ToList();
                    aristas.Add(new AristaTrazaDto(documento.NodoId, clienteId, TipoAristaTraza.Entrega,
                        vendido.Sum(x => x.Cantidad ?? 0), unidades.Count == 1 ? unidades[0] : null));
                    nodo = nodo with { Subtitulo = v.Cliente, Estado = v.Estado };
                    datos.Add(new("Cliente", $"{v.ClienteCodigo} · {v.Cliente}"));
                    datos.Add(new("Fecha", Fecha(v.FechaOperativa)));
                    break;
                }

                case DocumentoInventario.Retorno when retornos.TryGetValue(documento.Id, out var r):
                {
                    var cliente = r.ClienteId is { } c ? clientes.GetValueOrDefault(c) : null;
                    nodo = nodo with { Subtitulo = cliente?.Nombre ?? $"Retorno · {r.RutaCodigo}" };
                    datos.Add(new("Tipo", r.Tipo));
                    datos.Add(new("Ruta", r.RutaCodigo));
                    if (cliente is not null)
                    {
                        datos.Add(new("Cliente", $"{cliente.Codigo} · {cliente.Nombre}"));
                    }

                    datos.Add(new("Fecha", Fecha(r.FechaOperativa)));
                    break;
                }

                case DocumentoInventario.Liquidacion when liquidaciones.TryGetValue(documento.Id, out var l):
                    nodo = nodo with { Subtitulo = $"Liquidación · {l.RutaCodigo}", Estado = l.Estado };
                    datos.Add(new("Ruta", l.RutaCodigo));
                    datos.Add(new("Chofer", l.Chofer ?? "—"));
                    datos.Add(new("Fecha", Fecha(l.FechaOperativa)));
                    break;
            }

            var sinLote = grafo.ConsumoSinLote.GetValueOrDefault(documento)?.Select(x =>
            {
                var p = productos.GetValueOrDefault(x.ProductoId);
                return new ConsumoSinLoteDto(x.ProductoId, p?.Sku ?? string.Empty, p?.Nombre ?? string.Empty, x.Cantidad, p?.UnidadBaseClave ?? string.Empty);
            }).ToList() ?? [];
            nodos.Add(nodo with { Datos = datos, ConsumoSinLote = sinLote });
        }

        foreach (var (id, (tipo, documento, almacenId)) in grafo.Terminales)
        {
            var folio = grafo.Documentos.GetValueOrDefault(documento).Folio;
            var titulo = tipo == TipoNodoTraza.Merma
                ? "Merma"
                : $"Regresó a {(almacenId is { } a ? almacenes.GetValueOrDefault(a)?.Codigo : null) ?? "almacén"}";
            nodos.Add(Nodo(id, tipo, titulo, folio) with { DocumentoTipo = documento.Tipo, DocumentoId = documento.Id, Folio = folio });
        }

        if (grafo.Lotes.Count == 0)
        {
            avisos.Add(AvisosTraza.SinLotes);
        }

        if (grafo.Truncado)
        {
            avisos.Add(AvisosTraza.Truncado);
        }

        if (grafo.ConsumoSinLote.Count > 0)
        {
            avisos.Add(AvisosTraza.ComponentesSinLote);
        }

        var eventos = grafo.Movimientos.Select(m =>
        {
            var p = productos.GetValueOrDefault(m.ProductoId);
            var nodoLote = m.Lote == string.Empty ? null : new LoteAlmacen(m.ProductoId, m.Lote, m.AlmacenId).Id;
            var documento = new DocumentoTraza(m.DocumentoTipo, m.DocumentoId).NodoId;
            var nodoId = nodos.Any(n => n.Id == documento) ? documento : nodoLote;
            return new EventoTrazaDto(m.Id, m.FechaHora, m.FechaOperativa, m.Tipo, m.DocumentoTipo, m.DocumentoId, m.DocumentoFolio, nodoId,
                p?.Sku ?? string.Empty, p?.Nombre ?? string.Empty, m.Lote, m.Cantidad, p?.UnidadBaseClave ?? string.Empty,
                almacenes.GetValueOrDefault(m.AlmacenId)?.Codigo ?? string.Empty);
        }).ToList();

        return new TrazaDto(entrada, direccion, reloj.GetUtcNow(), grafo.Truncado, avisos, nodos, aristas, eventos);
    }

    private static NodoTrazaDto Nodo(string id, TipoNodoTraza tipo, string titulo, string? subtitulo) =>
        new(id, tipo, titulo, subtitulo, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, [], []);
}

public static class TrazabilidadEndpoints
{
    public static IEndpointRouteBuilder MapTrazabilidadEndpoints(this IEndpointRouteBuilder api)
    {
        var grupo = api.MapGroup("/inventario/trazabilidad").WithTags("Inventario").RequireAuthorization(Permisos.Inventario.Trazabilidad);
        grupo.MapGet("/", async Task<Results<Ok<TrazaDto>, ProblemHttpResult>> (TipoEntradaTraza tipo, Guid? id, Guid? productoId, string? lote,
                    DireccionTraza? direccion, ServicioTrazabilidad servicio, CancellationToken ct) =>
                (await servicio.RastrearAsync(tipo, id, productoId, lote, direccion, ct)).AHttp())
            .WithName("rastrearTrazabilidad");
        grupo.MapGet("/buscar", (string? q, ServicioTrazabilidad servicio, CancellationToken ct) => servicio.BuscarAsync(q, ct))
            .WithName("buscarTrazabilidad");
        return api;
    }
}
