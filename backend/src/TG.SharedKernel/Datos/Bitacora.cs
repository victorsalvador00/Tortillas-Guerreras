using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Seguridad;

namespace TG.SharedKernel.Datos;

/// <summary>
/// RN-SEG-06: las altas, cambios y bajas de la entidad se guardan en <c>seguridad.bitacora</c>.
/// <see cref="Documento"/> es la propiedad con el id del registro padre (p. ej. <c>PedidoId</c> en una partida);
/// vacío = la propia entidad es el documento. <see cref="Ignorar"/>: propiedades técnicas que cambian solas (último
/// sync, contadores); <see cref="Ocultar"/>: se registra que cambiaron pero no su valor (contraseñas).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BitacoraAttribute : Attribute
{
    public string? Documento { get; init; }
    public string[] Ignorar { get; init; } = [];
    public string[] Ocultar { get; init; } = [];
}

public static class AccionBitacora
{
    public const string Alta = "Alta";
    public const string Cambio = "Cambio";
    public const string Baja = "Baja";
}

public static class OrigenBitacora
{
    public const string Web = "Web";
    public const string App = "App";
    public const string Sistema = "Sistema";
}

/// <summary>Fila de <c>seguridad.bitacora</c> (solo inserciones, RN-SEG-07).</summary>
public sealed class RegistroBitacora
{
    public Guid Id { get; set; } = Dominio.Id.Nuevo();
    public DateTimeOffset FechaHora { get; set; }
    public string Esquema { get; set; } = string.Empty;
    public string Tabla { get; set; } = string.Empty;
    public Guid EntidadId { get; set; }
    public Guid? DocumentoId { get; set; }
    public string Accion { get; set; } = string.Empty;

    /// <summary><c>{ "cantidad": [100, 80] }</c> (antes, después); en <c>Alta</c> solo después y en <c>Baja</c> solo antes.</summary>
    public string Cambios { get; set; } = "{}";
    public Guid? UsuarioId { get; set; }
    public Guid? DispositivoId { get; set; }
    public string Origen { get; set; } = string.Empty;
}

public static class MapeoBitacora
{
    public const string Esquema = "seguridad";
    public const string Tabla = "bitacora";

    /// <summary>
    /// Mapea <c>seguridad.bitacora</c> en el contexto para que el interceptor guarde la bitácora en el mismo
    /// <c>SaveChanges</c> (misma transacción). Solo Seguridad es <paramref name="propietario"/> (crea la tabla).
    /// </summary>
    public static ModelBuilder MapearBitacora(this ModelBuilder builder, bool propietario = false)
    {
        builder.Entity<RegistroBitacora>(e =>
        {
            e.ToTable(Tabla, Esquema, t =>
            {
                if (!propietario)
                {
                    t.ExcludeFromMigrations();
                }
            });
            e.HasKey(b => b.Id);
            e.Property(b => b.Cambios).HasColumnType("jsonb");
            e.HasIndex(b => new { b.Tabla, b.EntidadId, b.FechaHora }).HasDatabaseName("ix_bitacora_tabla_entidad");
            e.HasIndex(b => b.DocumentoId).HasDatabaseName("ix_bitacora_documento");
            e.HasIndex(b => new { b.UsuarioId, b.FechaHora }).HasDatabaseName("ix_bitacora_usuario_fecha");
        });
        return builder;
    }
}

/// <summary>
/// RN-SEG-06: agrega las filas de bitácora de las entidades con <see cref="BitacoraAttribute"/> al mismo
/// <c>SaveChanges</c>; si la transacción se revierte, la bitácora también.
/// </summary>
public sealed class BitacoraInterceptor(IUsuarioActual usuario, TimeProvider reloj) : SaveChangesInterceptor
{
    private static readonly HashSet<string> Ignoradas =
    [
        nameof(IAuditable.CreadoEn), nameof(IAuditable.CreadoPor), nameof(IAuditable.ActualizadoEn),
        nameof(IAuditable.ActualizadoPor), nameof(IVersionSync.VersionSync), "Version",
    ];

    private const string ValorOculto = "***";

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Registrar(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Registrar(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Registrar(DbContext? contexto)
    {
        if (contexto is null)
        {
            return;
        }

        contexto.ChangeTracker.DetectChanges();
        var entradas = contexto.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => (Entrada: e, Atributo: e.Metadata.ClrType.GetCustomAttribute<BitacoraAttribute>()))
            .Where(x => x.Atributo is not null)
            .ToList();
        if (entradas.Count == 0)
        {
            return;
        }

        if (contexto.Model.FindEntityType(typeof(RegistroBitacora)) is null)
        {
            throw new InvalidOperationException(
                $"{contexto.GetType().Name} tiene entidades con [Bitacora] pero no llama a MapearBitacora().");
        }

        var ahora = reloj.GetUtcNow();
        var dispositivo = usuario.DispositivoId;
        var origen = dispositivo is not null ? OrigenBitacora.App : usuario.Id is null ? OrigenBitacora.Sistema : OrigenBitacora.Web;
        foreach (var (entrada, atributo) in entradas)
        {
            var (accion, cambios) = Cambios(entrada, atributo!);
            if (cambios.Count == 0)
            {
                continue;
            }

            var entidadId = EntidadId(entrada);
            var documento = atributo!.Documento is { } propiedad ? entrada.Property(propiedad).CurrentValue as Guid? : entidadId;
            contexto.Add(new RegistroBitacora
            {
                FechaHora = ahora,
                Esquema = entrada.Metadata.GetSchema() ?? contexto.Model.GetDefaultSchema() ?? string.Empty,
                Tabla = entrada.Metadata.GetTableName() ?? entrada.Metadata.ClrType.Name,
                EntidadId = entidadId,
                DocumentoId = documento,
                Accion = accion,
                Cambios = JsonSerializer.Serialize(cambios, Json),
                UsuarioId = usuario.Id,
                DispositivoId = dispositivo,
                Origen = origen,
            });
        }
    }

    private static (string Accion, Dictionary<string, object?> Cambios) Cambios(EntityEntry entrada, BitacoraAttribute atributo)
    {
        var cambios = new Dictionary<string, object?>();
        var propiedades = entrada.Properties.Where(p => !Ignoradas.Contains(p.Metadata.Name)
            && !atributo.Ignorar.Contains(p.Metadata.Name) && !p.Metadata.IsShadowProperty());
        object? Valor(PropertyEntry p, object? v) => v is not null && atributo.Ocultar.Contains(p.Metadata.Name) ? ValorOculto : v;
        switch (entrada.State)
        {
            case EntityState.Added:
                foreach (var p in propiedades.Where(p => !p.Metadata.IsPrimaryKey() && p.CurrentValue is not null))
                {
                    cambios[Columna(p)] = Valor(p, p.CurrentValue);
                }

                return (AccionBitacora.Alta, cambios);
            case EntityState.Deleted:
                foreach (var p in propiedades.Where(p => !p.Metadata.IsPrimaryKey() && p.OriginalValue is not null))
                {
                    cambios[Columna(p)] = Valor(p, p.OriginalValue);
                }

                return (AccionBitacora.Baja, cambios);
            default:
                foreach (var p in propiedades.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)))
                {
                    cambios[Columna(p)] = new[] { Valor(p, p.OriginalValue), Valor(p, p.CurrentValue) };
                }

                // Baja lógica: el registro se desactiva o se cancela.
                var baja = entrada.Properties.Any(p => p.Metadata.Name == "Activo" && p.IsModified
                    && Equals(p.OriginalValue, true) && Equals(p.CurrentValue, false));
                return (baja ? AccionBitacora.Baja : AccionBitacora.Cambio, cambios);
        }
    }

    /// <summary>Llave <c>Guid</c> simple; en llaves compuestas (conversiones, clientes de ruta) la última parte.</summary>
    private static Guid EntidadId(EntityEntry entrada)
    {
        var llave = entrada.Metadata.FindPrimaryKey()!.Properties;
        return llave.Select(p => entrada.Property(p.Name).CurrentValue).OfType<Guid>().LastOrDefault();
    }

    private static string Columna(PropertyEntry p) => p.Metadata.GetColumnName();
}
