using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TG.SharedKernel.Dominio;
using TG.SharedKernel.Seguridad;

namespace TG.SharedKernel.Datos;

/// <summary>Enlista el contexto en la transacción de la unidad de trabajo antes de guardar.</summary>
public sealed class UnitOfWorkInterceptor(IUnitOfWork unitOfWork) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            unitOfWork.Enlistar(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            unitOfWork.Enlistar(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }
}

/// <summary>Las consultas (incluidas las de bloqueo <c>FOR UPDATE</c>) corren dentro de la transacción en curso.</summary>
public sealed class TransaccionComandoInterceptor(IUnitOfWork unitOfWork) : DbCommandInterceptor
{
    public override DbCommand CommandInitialized(CommandEndEventData eventData, DbCommand result)
    {
        if (unitOfWork.Transaccion is not null && result.Transaction is null)
        {
            if (eventData.Context is not null)
            {
                unitOfWork.Enlistar(eventData.Context);
            }

            result.Transaction = unitOfWork.Transaccion;
        }

        return result;
    }
}

/// <summary>Llena <c>creado_en/por</c> y <c>actualizado_en/por</c> (auditoría de 01-arquitectura).</summary>
public sealed class AuditoriaInterceptor(IUsuarioActual usuario, TimeProvider reloj) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Aplicar(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Aplicar(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Aplicar(DbContext? contexto)
    {
        if (contexto is null)
        {
            return;
        }

        var ahora = reloj.GetUtcNow();
        var usuarioId = usuario.Id ?? Guid.Empty;
        foreach (var entrada in contexto.ChangeTracker.Entries<IAuditable>())
        {
            if (entrada.State == EntityState.Added)
            {
                entrada.Entity.CreadoEn = ahora;
                entrada.Entity.CreadoPor = usuarioId;
            }
            else if (entrada.State == EntityState.Modified)
            {
                entrada.Entity.ActualizadoEn = ahora;
                entrada.Entity.ActualizadoPor = usuarioId;
            }
        }
    }
}

/// <summary>Asigna <c>version_sync</c> desde la secuencia global <c>sync.version_seq</c> a lo que baja a la app.</summary>
public sealed class VersionSyncInterceptor(IUnitOfWork unitOfWork) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var contexto = eventData.Context;
        if (contexto is not null)
        {
            var entradas = contexto.ChangeTracker.Entries<IVersionSync>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified)
                .ToList();
            if (entradas.Count > 0)
            {
                unitOfWork.Enlistar(contexto);
                var versiones = await contexto.Database
                    .SqlQueryRaw<long>("SELECT nextval('sync.version_seq') AS \"Value\" FROM generate_series(1, {0})", entradas.Count)
                    .ToListAsync(cancellationToken);
                for (var i = 0; i < entradas.Count; i++)
                {
                    entradas[i].Entity.VersionSync = versiones[i];
                }
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
