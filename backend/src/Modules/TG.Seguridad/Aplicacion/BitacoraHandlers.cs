using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TG.Seguridad.Infraestructura;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;

namespace TG.Seguridad.Aplicacion;

public sealed record RegistroBitacoraDto(
    Guid Id,
    DateTimeOffset FechaHora,
    string Esquema,
    string Tabla,
    Guid EntidadId,
    Guid? DocumentoId,
    string Accion,
    JsonElement Cambios,
    Guid? UsuarioId,
    string? Usuario,
    Guid? DispositivoId,
    string Origen);

/// <summary>RN-SEG-07: la bitácora solo se consulta (no hay alta, edición ni borrado desde el sistema).</summary>
public sealed class ConsultaBitacoraHandler(SeguridadDbContext db, IUsuarioActual usuario, IFechaOperativa fechas)
{
    /// <summary>
    /// Permiso para ver el historial de un documento sin <c>seguridad.bitacora</c>. Cadena vacía = cualquier usuario
    /// autenticado (los catálogos de productos son de lectura libre).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> PermisoPorTabla = new Dictionary<string, string>
    {
        ["productos"] = string.Empty,
        ["productos_unidades"] = string.Empty,
        ["clientes"] = Permisos.Ventas.Ver,
        ["listas_precio"] = Permisos.Ventas.Ver,
        ["listas_precio_partidas"] = Permisos.Ventas.Ver,
        ["pedidos"] = Permisos.Ventas.Pedidos,
        ["pedidos_partidas"] = Permisos.Ventas.Pedidos,
        ["rutas"] = Permisos.Logistica.Ver,
        ["rutas_clientes"] = Permisos.Logistica.Ver,
        ["envios"] = Permisos.Logistica.Ver,
        ["envios_partidas"] = Permisos.Logistica.Ver,
        ["retornos"] = Permisos.Logistica.Ver,
        ["retornos_partidas"] = Permisos.Logistica.Ver,
        ["liquidaciones"] = Permisos.Logistica.Ver,
        ["liquidaciones_partidas"] = Permisos.Logistica.Ver,
        ["usuarios"] = Permisos.Seguridad.Usuarios,
        ["dispositivos"] = Permisos.Seguridad.Dispositivos,
    };

    public async Task<Result<Paginado<RegistroBitacoraDto>>> ListarAsync(string? tabla, Guid? entidadId, Guid? documentoId,
        Guid? usuarioId, DateOnly? desde, DateOnly? hasta, int? pagina, int? tamano, CancellationToken ct)
    {
        if (!usuario.TienePermiso(Permisos.Seguridad.Bitacora))
        {
            // Pestaña "Historial": un documento de una tabla cuyo permiso de ver tiene el usuario.
            var permitido = documentoId is not null && tabla is not null
                && PermisoPorTabla.TryGetValue(tabla, out var permiso)
                && (permiso.Length == 0 || usuario.TienePermiso(permiso));
            if (!permitido)
            {
                return ErroresSeguridad.BitacoraSinPermiso;
            }
        }

        var consulta = db.Bitacora.AsNoTracking();
        if (documentoId is not null)
        {
            // El historial de un documento incluye sus partidas (otras tablas con el mismo documento_id).
            consulta = consulta.Where(b => b.DocumentoId == documentoId);
        }
        else if (!string.IsNullOrWhiteSpace(tabla))
        {
            consulta = consulta.Where(b => b.Tabla == tabla);
        }

        if (entidadId is not null)
        {
            consulta = consulta.Where(b => b.EntidadId == entidadId);
        }

        if (usuarioId is not null)
        {
            consulta = consulta.Where(b => b.UsuarioId == usuarioId);
        }

        if (desde is not null)
        {
            var inicio = fechas.InicioUtc(desde.Value);
            consulta = consulta.Where(b => b.FechaHora >= inicio);
        }

        if (hasta is not null)
        {
            var fin = fechas.InicioUtc(hasta.Value.AddDays(1));
            consulta = consulta.Where(b => b.FechaHora < fin);
        }

        var paginado = await consulta.OrderByDescending(b => b.FechaHora).ThenByDescending(b => b.Id).PaginarAsync(pagina, tamano, ct);
        var ids = paginado.Items.Where(b => b.UsuarioId is not null).Select(b => b.UsuarioId!.Value).Distinct().ToList();
        var nombres = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.NombreCompleto, ct);
        return paginado.Map(b => new RegistroBitacoraDto(b.Id, b.FechaHora, b.Esquema, b.Tabla, b.EntidadId, b.DocumentoId,
            b.Accion, JsonDocument.Parse(b.Cambios).RootElement.Clone(), b.UsuarioId,
            b.UsuarioId is { } u ? nombres.GetValueOrDefault(u) : null, b.DispositivoId, b.Origen));
    }
}
