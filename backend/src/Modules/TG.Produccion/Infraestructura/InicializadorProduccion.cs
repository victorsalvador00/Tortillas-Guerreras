using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TG.Catalogos.Publico;
using TG.Produccion.Dominio;
using TG.SharedKernel.Datos;

namespace TG.Produccion.Infraestructura;

/// <summary>Demo: líneas de ejemplo <c>[SUP-05]</c> y las 4 recetas de 03-modelo-datos publicadas (no son las reales).</summary>
internal sealed class InicializadorProduccion : InicializadorModulo<ProduccionDbContext>
{
    public override int Orden => 50;

    private static readonly (string Codigo, string Nombre, TipoLinea Tipo)[] Lineas =
    [
        ("MOL-1", "Molino 1", TipoLinea.Molino),
        ("TOR-1", "Tortilladora 1", TipoLinea.Tortilladora),
        ("TOR-2", "Tortilladora 2", TipoLinea.Tortilladora),
        ("FRE-1", "Freidora 1", TipoLinea.Freidora),
        ("EMP-1", "Empaque 1", TipoLinea.Empaque),
    ];

    // (producto, nombre, rendimiento, merma esperada, componentes (sku, cantidad en unidad base))
    private static readonly (string, string, decimal, decimal, (string, decimal)[])[] Recetas =
    [
        ("SEM-MASA", "Masa de maíz", 160m, 0.02m, [("MP-MAIZ-BCO", 100m), ("MP-CAL", 1m)]),
        ("PT-TORT-KG", "Tortilla de maíz", 88m, 0.03m, [("SEM-MASA", 100m), ("MP-CONSERV", 0.3m), ("EMP-BOLSA-1K", 88m)]),
        ("PT-MASA-KG", "Masa para venta", 100m, 0m, [("SEM-MASA", 100m)]),
        ("PT-TOSTADA", "Tostada", 60m, 0.04m, [("SEM-MASA", 50m), ("MP-ACEITE", 6m)]),
    ];

    public override async Task SembrarAsync(IServiceProvider servicios, bool datosDemo, CancellationToken ct)
    {
        if (!datosDemo)
        {
            return;
        }

        var db = servicios.GetRequiredService<ProduccionDbContext>();
        var reloj = servicios.GetRequiredService<TimeProvider>();
        if (!await db.Lineas.AnyAsync(ct))
        {
            foreach (var (codigo, nombre, tipo) in Lineas)
            {
                db.Lineas.Add(new Linea { Codigo = codigo, Nombre = nombre, Tipo = tipo });
            }
        }

        if (!await db.Recetas.AnyAsync(ct))
        {
            var productos = (await servicios.GetRequiredService<ICatalogosService>().ListarProductosAsync(null, true, ct))
                .ToDictionary(p => p.Sku);
            foreach (var (sku, nombre, rendimiento, merma, componentes) in Recetas)
            {
                if (!productos.TryGetValue(sku, out var producto) || componentes.Any(c => !productos.ContainsKey(c.Item1)))
                {
                    continue;
                }

                var receta = new Receta
                {
                    ProductoId = producto.Id,
                    NumeroVersion = 1,
                    Nombre = $"{nombre} v1",
                    RendimientoCantidad = rendimiento,
                    MermaEsperadaPct = merma,
                    Estado = EstadoReceta.Vigente,
                    VigenteDesde = reloj.GetUtcNow(),
                    Notas = "Receta de ejemplo; no es la receta real del cliente.",
                };
                short orden = 0;
                foreach (var (componenteSku, cantidad) in componentes)
                {
                    var componente = productos[componenteSku];
                    receta.Componentes.Add(new RecetaComponente
                    {
                        RecetaId = receta.Id,
                        ProductoId = componente.Id,
                        UnidadId = componente.UnidadBaseId,
                        FactorABase = 1m,
                        Cantidad = cantidad,
                        CantidadBase = cantidad,
                        Orden = orden++,
                    });
                }

                db.Recetas.Add(receta);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
