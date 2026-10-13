using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TG.Seguridad.Dominio;
using TG.SharedKernel.Datos;
using P = TG.SharedKernel.Seguridad.Permisos;

namespace TG.Seguridad.Infraestructura;

/// <summary>Permisos, roles por defecto <c>[SUP-01]</c>, administrador y un usuario por rol en demo.</summary>
internal sealed class InicializadorSeguridad : InicializadorModulo<SeguridadDbContext>
{
    public const string ContrasenaDesarrollo = "Tortillas2026!";

    public override int Orden => 20;

    /// <summary>Permisos por rol <c>[SUP-01]</c>. Administrador recibe todos.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> RolesPredeterminados = new Dictionary<string, string[]>
    {
        ["Administrador"] = P.Todos.Select(p => p.Clave).ToArray(),
        ["Direccion"] =
        [
            P.Tablero.Ver, P.Compras.Ver, P.Compras.Autorizar, P.Produccion.Ver, P.Inventario.Ver, P.Inventario.Costos,
            P.Logistica.Ver, P.Ventas.Ver, P.Facturacion.Ver,
        ],
        ["Compras"] =
        [
            P.Tablero.Ver, P.Compras.Ver, P.Compras.Editar, P.Compras.Autorizar, P.Compras.Recibir, P.Compras.CompraDirecta,
            P.Inventario.Ver,
        ],
        ["Produccion"] =
        [
            P.Tablero.Ver, P.Produccion.Ver, P.Produccion.Recetas, P.Produccion.Planear, P.Produccion.Operar,
            P.Produccion.Cerrar, P.Inventario.Ver,
        ],
        ["Almacen"] =
        [
            P.Tablero.Ver, P.Inventario.Ver, P.Inventario.Ajustar, P.Inventario.Traspasar, P.Inventario.Contar,
            P.Compras.Ver, P.Compras.Recibir, P.Logistica.Ver, P.Logistica.Cargar, P.Logistica.Retornos,
        ],
        ["Logistica"] =
        [
            P.Tablero.Ver, P.Logistica.Configurar, P.Logistica.Ver, P.Logistica.Cargar, P.Logistica.Retornos,
            P.Logistica.Liquidar, P.Ventas.Ver, P.Ventas.Pedidos, P.Ventas.Clientes, P.Inventario.Ver,
        ],
        ["Vendedor"] = [P.Sincronizacion.Operar, P.Ventas.Registrar, P.Ventas.Cobrar],
        ["Facturacion"] =
        [
            P.Tablero.Ver, P.Facturacion.Ver, P.Facturacion.Emitir, P.Facturacion.Cancelar, P.Facturacion.Configurar,
            P.Ventas.Ver, P.Ventas.Clientes,
        ],
        ["Supervisor"] =
        [
            P.Tablero.Ver, P.Ventas.Ver, P.Ventas.Supervisar, P.Ventas.Cancelar, P.Logistica.Ver, P.Logistica.Liquidar,
            P.Logistica.ForzarCierre, P.Seguridad.Dispositivos, P.Inventario.Ver,
        ],
    };

    public static readonly (string Usuario, string Nombre, string Rol)[] UsuariosDemo =
    [
        ("direccion", "Dirección General", "Direccion"),
        ("compras", "Responsable de Compras", "Compras"),
        ("produccion", "Jefe de Producción", "Produccion"),
        ("almacen", "Encargado de Almacén", "Almacen"),
        ("logistica", "Coordinador de Logística", "Logistica"),
        ("vendedor1", "Vendedor Ruta 1", "Vendedor"),
        ("vendedor2", "Vendedor Ruta 2", "Vendedor"),
        ("vendedor3", "Vendedor Ruta 3", "Vendedor"),
        ("facturacion", "Auxiliar de Facturación", "Facturacion"),
        ("supervisor", "Supervisor de Rutas", "Supervisor"),
    ];

    public override async Task SembrarAsync(IServiceProvider servicios, bool datosDemo, CancellationToken ct)
    {
        var db = servicios.GetRequiredService<SeguridadDbContext>();
        var configuracion = servicios.GetRequiredService<IConfiguration>();

        var existentes = await db.Permisos.ToDictionaryAsync(p => p.Clave, ct);
        foreach (var (clave, descripcion) in P.Todos)
        {
            if (existentes.TryGetValue(clave, out var permiso))
            {
                permiso.Descripcion = descripcion;
            }
            else
            {
                db.Permisos.Add(new Permiso { Clave = clave, Descripcion = descripcion });
            }
        }

        await db.SaveChangesAsync(ct);
        var permisos = await db.Permisos.ToDictionaryAsync(p => p.Clave, ct);

        foreach (var (nombre, claves) in RolesPredeterminados)
        {
            var normalizado = nombre.ToUpperInvariant();
            var rol = await db.Roles.Include(r => r.Permisos).FirstOrDefaultAsync(r => r.NormalizedName == normalizado, ct);
            if (rol is null)
            {
                rol = new Rol(nombre);
                db.Roles.Add(rol);
                foreach (var clave in claves)
                {
                    rol.Permisos.Add(new RolPermiso { RolId = rol.Id, PermisoId = permisos[clave].Id });
                }
            }
            else if (nombre == "Administrador")
            {
                // El administrador siempre tiene todos los permisos, incluidos los nuevos.
                foreach (var clave in claves.Where(c => rol.Permisos.All(rp => rp.PermisoId != permisos[c].Id)))
                {
                    rol.Permisos.Add(new RolPermiso { RolId = rol.Id, PermisoId = permisos[clave].Id });
                }
            }
        }

        await db.SaveChangesAsync(ct);

        var usuarios = servicios.GetRequiredService<UserManager<Usuario>>();
        var contrasenaAdmin = configuracion["Semilla:ContrasenaAdmin"] ?? ContrasenaDesarrollo;
        await CrearUsuarioAsync(usuarios, "admin", "Administrador del sistema", "Administrador", contrasenaAdmin);
        if (datosDemo)
        {
            foreach (var (usuario, nombre, rol) in UsuariosDemo)
            {
                await CrearUsuarioAsync(usuarios, usuario, nombre, rol, ContrasenaDesarrollo);
            }
        }
    }

    private static async Task CrearUsuarioAsync(UserManager<Usuario> usuarios, string nombreUsuario, string nombre,
        string rol, string contrasena)
    {
        if (await usuarios.FindByNameAsync(nombreUsuario) is not null)
        {
            return;
        }

        var usuario = new Usuario { UserName = nombreUsuario, NombreCompleto = nombre, Activo = true, LockoutEnabled = true };
        var creado = await usuarios.CreateAsync(usuario, contrasena);
        if (!creado.Succeeded)
        {
            throw new InvalidOperationException(
                $"No se pudo crear el usuario semilla {nombreUsuario}: {string.Join(", ", creado.Errors.Select(e => e.Description))}");
        }

        await usuarios.AddToRoleAsync(usuario, rol);
    }
}
