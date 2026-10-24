using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TG.Catalogos.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Folios;
using TG.Ventas.Dominio;
using TG.Ventas.Publico;

namespace TG.Ventas.Infraestructura;

/// <summary>Canales (siempre); listas de precio y 15 clientes de ejemplo (demo).</summary>
internal sealed class InicializadorVentas : InicializadorModulo<VentasDbContext>
{
    public override int Orden => 60;

    /// <summary>Canales v0.2 (03-modelo-datos): formato de liquidación, producto a cobro y pedido por.</summary>
    internal static readonly (string Clave, string Nombre, bool Factura, FormatoLiquidacion Formato, bool ACobro, PedidoPor PedidoPor, short Orden)[] Canales =
    [
        (ClavesCanal.Autoservicio, "Autoservicio", true, FormatoLiquidacion.Completo, true, PedidoPor.Cliente, 1),
        (ClavesCanal.Detalle, "Detalle", false, FormatoLiquidacion.Completo, true, PedidoPor.Ruta, 2),
        (ClavesCanal.Institucional, "Institucional", true, FormatoLiquidacion.Simplificado, false, PedidoPor.Cliente, 3),
        (ClavesCanal.Cedis, "CEDIS", true, FormatoLiquidacion.Simplificado, false, PedidoPor.Cliente, 4),
    ];

    /// <summary>Precios de ejemplo sin impuestos [SUP-07]: (INST, AUTO, DET); CEDIS usa los de AUTO.</summary>
    private static readonly Dictionary<string, (decimal Inst, decimal Auto, decimal Det)> Precios = new()
    {
        ["PT-TORT-KG"] = (19m, 20m, 22m),
        ["PT-MASA-KG"] = (12m, 13m, 14m),
        ["PT-TOSTADA"] = (25m, 26m, 28m),
        ["PT-TOTOPO"] = (15m, 16m, 18m),
        ["RV-SALSA"] = (22m, 23m, 25m),
    };

    // (nombre, canal, condición, límite, latitud, longitud)
    private static readonly (string, string, CondicionPago, decimal?, decimal, decimal)[] Clientes =
    [
        ("Abarrotes Lupita", "DET", CondicionPago.Contado, null, 19.180120m, -96.140310m),
        ("Comedor Doña Mary", "INST", CondicionPago.Credito, 15000m, 19.182450m, -96.138900m),
        ("Súper El Ahorro", "AUTO", CondicionPago.Credito, 30000m, 19.178900m, -96.143200m),
        ("Tienda La Esquina", "DET", CondicionPago.Contado, null, 19.181700m, -96.136500m),
        ("Hospital Regional (cocina)", "INST", CondicionPago.Credito, 50000m, 19.176300m, -96.141800m),
        ("Minisúper Las Palmas", "AUTO", CondicionPago.Credito, 20000m, 19.184200m, -96.144100m),
        ("Miscelánea San José", "DET", CondicionPago.Credito, 3000m, 19.179600m, -96.137700m),
        ("Escuela Primaria Benito Juárez", "INST", CondicionPago.Credito, 10000m, 19.185100m, -96.139400m),
        ("Autoservicio Costa Verde", "AUTO", CondicionPago.Credito, 25000m, 19.177500m, -96.135900m),
        ("Abarrotes El Güero", "DET", CondicionPago.Contado, null, 19.183300m, -96.142600m),
        ("Restaurante Mariscos Boca", "INST", CondicionPago.Credito, 12000m, 19.175800m, -96.138200m),
        ("Súper Mercado Arrecife", "AUTO", CondicionPago.Credito, 40000m, 19.186000m, -96.140900m),
        ("Tienda Don Pepe", "DET", CondicionPago.Contado, null, 19.180900m, -96.145000m),
        ("Comedor Industrial Puerto", "INST", CondicionPago.Credito, 35000m, 19.174900m, -96.143700m),
        ("Tiendita La Bendición", "DET", CondicionPago.Contado, null, 19.182800m, -96.134800m),
    ];

    public override async Task SembrarAsync(IServiceProvider servicios, bool datosDemo, CancellationToken ct)
    {
        var db = servicios.GetRequiredService<VentasDbContext>();
        var canales = await db.Canales.ToDictionaryAsync(c => c.Clave, ct);
        foreach (var (clave, nombre, factura, formato, aCobro, pedidoPor, orden) in Canales.Where(c => !canales.ContainsKey(c.Clave)))
        {
            var canal = new Canal
            {
                Clave = clave, Nombre = nombre, RequiereFacturaDefault = factura, FormatoLiquidacion = formato,
                AplicaProductoACobro = aCobro, PedidoPor = pedidoPor, Orden = orden,
            };
            db.Canales.Add(canal);
            canales[clave] = canal;
        }

        await db.SaveChangesAsync(ct);
        if (!datosDemo || await db.ListasPrecio.AnyAsync(ct))
        {
            return;
        }

        var catalogos = servicios.GetRequiredService<ICatalogosService>();
        var vendibles = await catalogos.ListarProductosAsync(vendibles: true, soloActivos: true, ct);
        var listas = new Dictionary<string, ListaPrecio>();
        foreach (var (clave, canal) in canales)
        {
            var lista = new ListaPrecio { Nombre = $"Lista {canal.Nombre}", CanalId = canal.Id, VigenteDesde = new DateOnly(2026, 1, 1) };
            foreach (var producto in vendibles)
            {
                var (inst, auto, det) = Precios.GetValueOrDefault(producto.Sku, (10m, 10m, 10m));
                var precio = clave switch
                {
                    ClavesCanal.Institucional => inst,
                    ClavesCanal.Autoservicio or ClavesCanal.Cedis => auto,
                    _ => det,
                };
                lista.Partidas.Add(new ListaPrecioPartida
                {
                    ListaPrecioId = lista.Id, ProductoId = producto.Id, UnidadId = producto.UnidadBaseId, PrecioUnitario = precio,
                });
                foreach (var conversion in producto.Conversiones.Where(c => c.UsoVenta))
                {
                    lista.Partidas.Add(new ListaPrecioPartida
                    {
                        ListaPrecioId = lista.Id, ProductoId = producto.Id, UnidadId = conversion.UnidadId,
                        PrecioUnitario = precio * conversion.FactorABase,
                    });
                }
            }

            db.ListasPrecio.Add(lista);
            listas[clave] = lista;
        }

        await db.SaveChangesAsync(ct);

        var folios = servicios.GetRequiredService<IGeneradorFolios>();
        var numero = 0;
        foreach (var (nombre, canal, condicion, limite, lat, lng) in Clientes)
        {
            numero++;
            var factura = canal != ClavesCanal.Detalle;
            db.Clientes.Add(new Cliente
            {
                Codigo = await folios.SiguienteAsync(VentasDbContext.Esquema, "CLI", ct),
                Nombre = nombre,
                CanalId = canales[canal].Id,
                ListaPrecioId = listas[canal].Id,
                CondicionPago = condicion,
                DiasCredito = condicion == CondicionPago.Credito ? 15 : 0,
                LimiteCredito = limite,
                RequiereFactura = factura,
                Rfc = factura ? $"EJE{numero:D2}0101AB{numero % 10}" : null,
                RazonSocial = factura ? nombre.ToUpperInvariant() + " SA DE CV" : null,
                RegimenFiscal = factura ? "601" : null,
                CodigoPostalFiscal = factura ? "94290" : null,
                UsoCfdi = factura ? "G01" : null,
                Telefono = $"229{numero:D7}",
                Colonia = "Centro",
                Municipio = "Boca del Río",
                Estado = "Veracruz",
                CodigoPostal = "94290",
                Latitud = lat,
                Longitud = lng,
                UbicacionAprobada = true,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
