using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TG.SharedKernel.Datos;

/// <summary>
/// Migra el esquema de un módulo y carga sus datos semilla. El host los ejecuta en orden (<see cref="Orden"/>)
/// para respetar dependencias: Catalogos crea el esquema <c>sync</c> y la secuencia global antes que los demás.
/// </summary>
public interface IInicializadorModulo
{
    int Orden { get; }

    string Nombre { get; }

    Task MigrarAsync(IServiceProvider servicios, CancellationToken ct);

    /// <summary>Datos base (siempre) y, si <paramref name="datosDemo"/>, los datos de ejemplo de 03-modelo-datos.</summary>
    Task SembrarAsync(IServiceProvider servicios, bool datosDemo, CancellationToken ct);
}

/// <summary>Inicializador estándar: aplica las migraciones del <typeparamref name="TContexto"/> del módulo.</summary>
public abstract class InicializadorModulo<TContexto> : IInicializadorModulo where TContexto : DbContext
{
    public abstract int Orden { get; }

    public virtual string Nombre => typeof(TContexto).Name.Replace("DbContext", string.Empty, StringComparison.Ordinal);

    public virtual Task MigrarAsync(IServiceProvider servicios, CancellationToken ct) =>
        servicios.GetRequiredService<TContexto>().Database.MigrateAsync(ct);

    public virtual Task SembrarAsync(IServiceProvider servicios, bool datosDemo, CancellationToken ct) => Task.CompletedTask;
}
