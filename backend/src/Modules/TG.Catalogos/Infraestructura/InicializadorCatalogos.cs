using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TG.Catalogos.Dominio;
using TG.Catalogos.Publico;
using TG.SharedKernel.Datos;

namespace TG.Catalogos.Infraestructura;

/// <summary>
/// Orden 10 (el primero): su migración crea el esquema <c>sync</c> y la secuencia global <c>sync.version_seq</c>.
/// Semilla: unidades, parámetros y motivos (siempre); productos de ejemplo de 03-modelo-datos (demo).
/// </summary>
internal sealed class InicializadorCatalogos : InicializadorModulo<CatalogosDbContext>
{
    public override int Orden => 10;

    private static readonly (string Clave, string Nombre, TipoUnidad Tipo, string Sat, short Decimales)[] Unidades =
    [
        ("KG", "Kilogramo", TipoUnidad.Masa, "KGM", 3),
        ("G", "Gramo", TipoUnidad.Masa, "GRM", 0),
        ("TON", "Tonelada", TipoUnidad.Masa, "TNE", 3),
        ("L", "Litro", TipoUnidad.Volumen, "LTR", 3),
        ("ML", "Mililitro", TipoUnidad.Volumen, "MLT", 0),
        ("PZA", "Pieza", TipoUnidad.Pieza, "H87", 0),
        ("PAQ", "Paquete", TipoUnidad.Pieza, "XPK", 0),
        ("BUL", "Bulto", TipoUnidad.Pieza, "XBG", 0),
        ("CAJ", "Caja", TipoUnidad.Pieza, "XBX", 0),
    ];

    private static readonly (string Clave, string Valor, TipoParametro Tipo, string Descripcion)[] Parametros =
    [
        (ClavesParametro.ZonaHoraria, "America/Mexico_City", TipoParametro.text, "Zona horaria de la fecha operativa"),
        (ClavesParametro.GpsUmbralDistancia, "150", TipoParametro.@int, "Distancia máxima (m) entre la venta y el cliente [SUP-09]"),
        (ClavesParametro.GpsPrecisionMaxima, "100", TipoParametro.@int, "Precisión GPS máxima aceptable (m) [SUP-09]"),
        (ClavesParametro.RutaPermiteNegativo, "true", TipoParametro.@bool, "Vender en ruta aunque la camioneta quede en negativo [SUP-10]"),
        (ClavesParametro.RutaDiasRetencion, "7", TipoParametro.@int, "Días que la app conserva operaciones ya sincronizadas"),
        (ClavesParametro.AutofacturaLimite, "fin_de_mes", TipoParametro.text, "Plazo de autofactura: fin_de_mes o número de días [SUP-11]"),
        (ClavesParametro.FacturaGlobalPeriodicidad, "Mensual", TipoParametro.text, "Periodicidad de la factura global [SUP-11]"),
        (ClavesParametro.ProduccionConsumoSinExistencia, "false", TipoParametro.@bool, "Permitir terminar órdenes sin existencia de componentes [SUP-12]"),
        (ClavesParametro.ProduccionUmbralVariacion, "0.05", TipoParametro.@decimal, "Umbral de variación teórico vs real (0.05 = 5 %)"),
        (ClavesParametro.ComprasTolerancia, "0.05", TipoParametro.@decimal, "Tolerancia para recibir de más (0.05 = 5 %) [SUP-04]"),
        (ClavesParametro.MetodoCosteo, "PromedioPonderado", TipoParametro.text, "Método de costeo [SUP-06]"),
        (ClavesParametro.DiasToleranciaDevolucion, "11", TipoParametro.@int, "Días de antigüedad del lote devuelto sin cargo al chofer [SUP-19]"),
        (ClavesParametro.PrecioProductoACobro, "ListaCanal", TipoParametro.text, "Valuación del producto a cobro: ListaCanal o Costo [SUP-20]"),
        (ClavesParametro.CargoFaltanteProducto, "true", TipoParametro.@bool, "El faltante de producto en la liquidación genera cargo al chofer [SUP-21]"),
        (ClavesParametro.CargoFaltanteEfectivo, "true", TipoParametro.@bool, "El faltante de efectivo en la liquidación genera cargo al chofer [SUP-21]"),
        (ClavesParametro.RequiereRevisionSupervisor, "true", TipoParametro.@bool, "La liquidación pasa por la revisión del supervisor (RN-LOG-20)"),
        (ClavesParametro.DiaInicioSemanaCorte, "1", TipoParametro.@int, "Día de inicio del corte semanal (1 = lunes) [SUP-22]"),
        (ClavesParametro.DiasEstimadoReferencia, "7", TipoParametro.@int, "Sin pedido, el estimado toma el de hace N días [SUP-23]"),
        (ClavesParametro.AlertaCantidadFactor, "3", TipoParametro.@decimal, "La app pide confirmar si la cantidad supera N veces el promedio (RN-APP-09)"),
        (ClavesParametro.AlertaCantidadMaxima, "500", TipoParametro.@decimal, "Tope por partida en unidad de venta que pide confirmación (RN-APP-09)"),
        (ClavesParametro.ConsumoPromedioVisitas, "8", TipoParametro.@int, "Ventas/entregas que promedia el consumo del cliente (RN-APP-09)"),
        (ClavesParametro.ChoferUnaRutaPorDia, "true", TipoParametro.@bool, "Un chofer lleva una sola ruta por día (RN-LOG-13) [SUP-24]"),
    ];

    // (clave, nombre, producción, almacén, ruta, retorno)
    private static readonly (string, string, bool, bool, bool, bool)[] Motivos =
    [
        ("MASA_AGRIA", "Masa pasada/agria", true, true, false, false),
        ("TORT_ROTA", "Tortilla rota o deforme", true, true, false, false),
        ("FALLA_MAQ", "Falla de máquina", true, false, false, false),
        ("CADUCADO", "Producto caducado", false, true, true, true),
        ("CONTAMINACION", "Contaminación", true, true, false, false),
        (ClavesMotivoMerma.DevolucionNoVendible, "Devolución no vendible", false, false, true, true),
        ("DANO_TRANSPORTE", "Daño en transporte", false, true, true, true),
        (ClavesMotivoMerma.FaltanteRuta, "Faltante de ruta", false, false, true, false),
        // RN-LOG-07: el reproceso se registra como merma con este motivo hasta definir su flujo [SUP-13].
        (ClavesMotivoMerma.Reproceso, "Reproceso", false, true, true, false),
        (ClavesMotivoMerma.Otro, "Otro", true, true, true, true),
    ];

    public override async Task SembrarAsync(IServiceProvider servicios, bool datosDemo, CancellationToken ct)
    {
        var db = servicios.GetRequiredService<CatalogosDbContext>();

        var unidades = await db.Unidades.ToDictionaryAsync(u => u.Clave, ct);
        foreach (var (clave, nombre, tipo, sat, decimales) in Unidades.Where(u => !unidades.ContainsKey(u.Clave)))
        {
            var unidad = new UnidadMedida { Clave = clave, Nombre = nombre, Tipo = tipo, ClaveSat = sat, Decimales = decimales };
            db.Unidades.Add(unidad);
            unidades[clave] = unidad;
        }

        var parametros = await db.Parametros.Select(p => p.Clave).ToListAsync(ct);
        foreach (var (clave, valor, tipo, descripcion) in Parametros.Where(p => !parametros.Contains(p.Clave)))
        {
            db.Parametros.Add(new Parametro { Clave = clave, Valor = valor, Tipo = tipo, Descripcion = descripcion });
        }

        var motivos = await db.MotivosMerma.Select(m => m.Clave).ToListAsync(ct);
        foreach (var (clave, nombre, produccion, almacen, ruta, retorno) in Motivos.Where(m => !motivos.Contains(m.Item1)))
        {
            db.MotivosMerma.Add(new MotivoMerma
            {
                Clave = clave, Nombre = nombre, AplicaProduccion = produccion, AplicaAlmacen = almacen,
                AplicaRuta = ruta, AplicaRetorno = retorno,
            });
        }

        await db.SaveChangesAsync(ct);

        if (datosDemo && !await db.Productos.AnyAsync(ct))
        {
            SembrarProductos(db, unidades);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Productos de ejemplo de 03-modelo-datos (no son los del cliente).</summary>
    private static void SembrarProductos(CatalogosDbContext db, Dictionary<string, UnidadMedida> u)
    {
        // [SUP-03] Clave SAT genérica "01010101" hasta que el contador confirme las reales.
        const string satGenerica = "01010101";

        Producto Nuevo(string sku, string nombre, TipoProducto tipo, string unidadBase, decimal iva, decimal? ieps = null,
            int? vidaUtil = null, string? claveSat = null, decimal? minimo = null)
        {
            var banderas = Producto.BanderasPorTipo(tipo);
            var p = new Producto
            {
                Sku = sku, Nombre = nombre, Tipo = tipo, UnidadBaseId = u[unidadBase].Id, EsComprable = banderas.Comprable,
                EsProducible = banderas.Producible, EsVendible = banderas.Vendible, ManejaLote = banderas.ManejaLote,
                VidaUtilDias = vidaUtil, TasaIva = iva, TasaIeps = ieps, ClaveProdServSat = claveSat, StockMinimo = minimo,
            };
            db.Productos.Add(p);
            return p;
        }

        void Conversion(Producto p, string unidad, decimal factor, bool compra = false, bool venta = false,
            bool produccion = false, bool predeterminada = false, bool traslado = false) =>
            p.Conversiones.Add(new ProductoUnidad
            {
                ProductoId = p.Id, UnidadId = u[unidad].Id, FactorABase = factor, UsoCompra = compra, UsoVenta = venta,
                UsoProduccion = produccion, PredeterminadaVenta = predeterminada, UsoTraslado = traslado,
                PredeterminadaTraslado = traslado,
            });

        Conversion(Nuevo("MP-MAIZ-BCO", "Maíz blanco", TipoProducto.MateriaPrima, "KG", 0m, minimo: 2000), "TON", 1000m, compra: true);
        Conversion(Nuevo("MP-CAL", "Cal (hidróxido de calcio)", TipoProducto.MateriaPrima, "KG", 0.16m, minimo: 50), "BUL", 25m, compra: true);
        Conversion(Nuevo("MP-HARINA", "Harina de maíz nixtamalizada", TipoProducto.MateriaPrima, "KG", 0m), "BUL", 20m, compra: true);
        Nuevo("MP-CONSERV", "Conservador", TipoProducto.MateriaPrima, "KG", 0.16m, minimo: 5);
        Conversion(Nuevo("MP-ACEITE", "Aceite vegetal", TipoProducto.MateriaPrima, "L", 0m), "CAJ", 12m, compra: true);
        Nuevo("MP-SAL", "Sal", TipoProducto.MateriaPrima, "KG", 0m);
        Conversion(Nuevo("EMP-BOLSA-1K", "Bolsa para 1 kg", TipoProducto.Empaque, "PZA", 0.16m, minimo: 2000), "CAJ", 1000m, compra: true);
        Nuevo("EMP-PAPEL", "Papel estraza", TipoProducto.Empaque, "KG", 0.16m);
        Nuevo("SEM-MASA", "Masa de maíz", TipoProducto.Semiterminado, "KG", 0m, vidaUtil: 1);
        // [SUP-02] Tortilla: vida útil 2 días; masa: 1 día.
        // [SUP-26] Paquetes por caja propios de cada producto (24 de tortilla, 15 de tostada).
        var tortilla = Nuevo("PT-TORT-KG", "Tortilla de maíz", TipoProducto.Terminado, "KG", 0m, vidaUtil: 2, claveSat: satGenerica, minimo: 100);
        tortilla.NombreCorto = "TORT";
        tortilla.AplicaProductoACobro = true;
        Conversion(tortilla, "PAQ", 1m, venta: true, predeterminada: true);
        Conversion(tortilla, "CAJ", 24m, traslado: true);
        Nuevo("PT-MASA-KG", "Masa para venta", TipoProducto.Terminado, "KG", 0m, vidaUtil: 1, claveSat: satGenerica);
        var tostada = Nuevo("PT-TOSTADA", "Tostada (paquete 30 pz)", TipoProducto.Terminado, "PAQ", 0m, vidaUtil: 30, claveSat: satGenerica);
        tostada.NombreCorto = "TOST";
        tostada.AplicaProductoACobro = true;
        Conversion(tostada, "CAJ", 15m, traslado: true);
        Nuevo("PT-TOTOPO", "Totopo (bolsa 500 g)", TipoProducto.Terminado, "PAQ", 0.16m, ieps: 0.08m, vidaUtil: 60, claveSat: satGenerica);
        Conversion(Nuevo("RV-SALSA", "Salsa embotellada", TipoProducto.Reventa, "PZA", 0.16m, claveSat: satGenerica), "CAJ", 12m, compra: true);
    }
}
