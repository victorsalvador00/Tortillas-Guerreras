using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TG.Catalogos.Publico;
using TG.Compras.Dominio;
using TG.SharedKernel.Datos;

namespace TG.Compras.Infraestructura;

/// <summary>Semilla demo: proveedores de ejemplo con sus productos y precio de referencia.</summary>
internal sealed class InicializadorCompras : InicializadorModulo<ComprasDbContext>
{
    public override int Orden => 40;

    // (código, razón social, nombre comercial, RFC, días de crédito, [(sku, unidad de compra, precio)])
    private static readonly (string, string, string, string, int, (string Sku, string Unidad, decimal Precio)[])[] Proveedores =
    [
        ("GRANOS", "Granos del Golfo SA de CV", "Granos del Golfo", "GGO010101AB1", 15,
            [("MP-MAIZ-BCO", "TON", 6500m), ("MP-HARINA", "BUL", 210m), ("MP-SAL", "KG", 9m), ("MP-ACEITE", "CAJ", 420m)]),
        ("QUIMICA", "Química Industrial del Centro SA de CV", "QUICEN", "QIC020202CD2", 30,
            [("MP-CAL", "BUL", 95m), ("MP-CONSERV", "KG", 180m)]),
        ("EMPAQUES", "Empaques y Envases de México SA de CV", "EmpaMex", "EEM030303EF3", 30,
            [("EMP-BOLSA-1K", "CAJ", 350m), ("EMP-PAPEL", "KG", 28m), ("RV-SALSA", "CAJ", 216m)]),
    ];

    public override async Task SembrarAsync(IServiceProvider servicios, bool datosDemo, CancellationToken ct)
    {
        if (!datosDemo)
        {
            return;
        }

        var db = servicios.GetRequiredService<ComprasDbContext>();
        if (await db.Proveedores.AnyAsync(ct))
        {
            return;
        }

        var catalogos = servicios.GetRequiredService<ICatalogosService>();
        var productos = (await catalogos.ListarProductosAsync(null, true, ct)).ToDictionary(p => p.Sku);
        var unidades = (await catalogos.ObtenerUnidadesAsync(ct)).Values.ToDictionary(u => u.Clave);
        foreach (var (codigo, razon, comercial, rfc, credito, articulos) in Proveedores)
        {
            var proveedor = new Proveedor
            {
                Codigo = codigo, RazonSocial = razon, NombreComercial = comercial, Rfc = rfc, DiasCredito = credito,
            };
            db.Proveedores.Add(proveedor);
            foreach (var (sku, unidad, precio) in articulos)
            {
                if (productos.TryGetValue(sku, out var producto) && unidades.TryGetValue(unidad, out var u))
                {
                    db.ProveedoresProductos.Add(new ProveedorProducto
                    {
                        ProveedorId = proveedor.Id, ProductoId = producto.Id, UnidadId = u.Id, UltimoPrecio = precio,
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
