using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TG.Logistica.Aplicacion;
using TG.Logistica.Dominio;
using TG.Seguridad.Publico;
using TG.SharedKernel.Datos;
using TG.Ventas.Publico;

namespace TG.Logistica.Infraestructura;

/// <summary>Semilla demo: 3 vehículos y las rutas R01–R03 con su vendedor, almacén, genérico y 5 clientes cada una.</summary>
internal sealed class InicializadorLogistica : InicializadorModulo<LogisticaDbContext>
{
    public override int Orden => 70;

    public override async Task SembrarAsync(IServiceProvider servicios, bool datosDemo, CancellationToken ct)
    {
        if (!datosDemo)
        {
            return;
        }

        var db = servicios.GetRequiredService<LogisticaDbContext>();
        if (await db.Rutas.AnyAsync(ct))
        {
            return;
        }

        var vehiculos = new[]
        {
            new Vehiculo { Placas = "XA-1234-A", Descripcion = "Nissan NP300 caja seca", CapacidadKg = 1200 },
            new Vehiculo { Placas = "XB-5678-B", Descripcion = "Chevrolet Tornado con termo", CapacidadKg = 700 },
            new Vehiculo { Placas = "XC-9012-C", Descripcion = "Isuzu ELF 400", CapacidadKg = 2500 },
        };
        db.Vehiculos.AddRange(vehiculos);
        await db.SaveChangesAsync(ct);

        var seguridad = servicios.GetRequiredService<ISeguridadService>();
        var ventas = servicios.GetRequiredService<IVentasService>();
        var rutasHandler = servicios.GetRequiredService<RutasHandler>();
        var clientes = await ventas.ListarClientesAsync(incluirGenericos: false, ct);

        var definiciones = new[]
        {
            ("R01", "Ruta Centro", "vendedor1"),
            ("R02", "Ruta Norte", "vendedor2"),
            ("R03", "Ruta Sur", "vendedor3"),
        };
        for (var i = 0; i < definiciones.Length; i++)
        {
            var (codigo, nombre, usuario) = definiciones[i];
            var vendedor = await seguridad.UsuarioPorNombreAsync(usuario, ct);
            var ruta = await rutasHandler.CrearAsync(new GuardarRutaRequest(codigo, nombre, vendedor, vehiculos[i].Id, true), ct);
            if (!ruta.EsExito)
            {
                throw new InvalidOperationException($"No se pudo crear la ruta semilla {codigo}: {ruta.Error!.Mensaje}");
            }

            var asignados = clientes.Skip(i * 5).Take(5).Select((c, j) => new ClienteRutaRequest(c.Id, (short)(j + 1),
                j % 2 == 0 ? [1, 3, 5] : [2, 4, 6])).ToList();
            if (ruta.Valor.ClienteGenericoId is { } generico)
            {
                asignados.Insert(0, new ClienteRutaRequest(generico, 0, [1, 2, 3, 4, 5, 6, 7]));
            }

            await rutasHandler.ReemplazarClientesAsync(ruta.Valor.Id, new ClientesRutaRequest(asignados), ct);
        }
    }
}
