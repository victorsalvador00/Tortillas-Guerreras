using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TG.SharedKernel.Resultados;

namespace TG.SharedKernel.Datos;

/// <summary>
/// Unidad de trabajo por solicitud: una sola <see cref="NpgsqlConnection"/> compartida por todos los
/// <c>DbContext</c> de los módulos y una transacción para las escrituras que cruzan módulos (regla 5).
/// </summary>
public interface IUnitOfWork
{
    DbConnection Conexion { get; }

    DbTransaction? Transaccion { get; }

    /// <summary>
    /// Ejecuta <paramref name="accion"/> en una transacción. Confirma si el resultado es éxito y revierte si es falla
    /// o hay excepción. Si ya hay una transacción abierta, la acción se une a ella (la externa decide).
    /// </summary>
    Task<Result<T>> EjecutarAsync<T>(Func<CancellationToken, Task<Result<T>>> accion, CancellationToken ct,
        IsolationLevel aislamiento = IsolationLevel.ReadCommitted);

    Task<Result> EjecutarAsync(Func<CancellationToken, Task<Result>> accion, CancellationToken ct);

    /// <summary>Hace que el contexto use la transacción en curso (lo llaman los interceptores).</summary>
    void Enlistar(DbContext contexto);
}

public sealed class UnitOfWork : IUnitOfWork, IAsyncDisposable, IDisposable
{
    private readonly NpgsqlConnection _conexion;
    private readonly List<DbContext> _enlistados = [];
    private NpgsqlTransaction? _transaccion;

    public UnitOfWork(NpgsqlDataSource dataSource) => _conexion = dataSource.CreateConnection();

    public DbConnection Conexion => _conexion;

    public DbTransaction? Transaccion => _transaccion;

    public async Task<Result<T>> EjecutarAsync<T>(Func<CancellationToken, Task<Result<T>>> accion, CancellationToken ct,
        IsolationLevel aislamiento = IsolationLevel.ReadCommitted)
    {
        if (_transaccion is not null)
        {
            return await accion(ct);
        }

        if (_conexion.State != ConnectionState.Open)
        {
            await _conexion.OpenAsync(ct);
        }

        _transaccion = await _conexion.BeginTransactionAsync(aislamiento, ct);
        try
        {
            var resultado = await accion(ct);
            if (resultado.EsExito)
            {
                await _transaccion.CommitAsync(ct);
            }
            else
            {
                await _transaccion.RollbackAsync(CancellationToken.None);
                LimpiarSeguimiento();
            }

            return resultado;
        }
        catch
        {
            await RevertirSinFallarAsync();
            LimpiarSeguimiento();
            throw;
        }
        finally
        {
            foreach (var contexto in _enlistados)
            {
                contexto.Database.UseTransaction(null);
            }

            _enlistados.Clear();
            await _transaccion.DisposeAsync();
            _transaccion = null;
        }
    }

    public async Task<Result> EjecutarAsync(Func<CancellationToken, Task<Result>> accion, CancellationToken ct)
    {
        var resultado = await EjecutarAsync<bool>(async c =>
        {
            var r = await accion(c);
            return r.EsExito ? Result<bool>.Exito(true) : Result<bool>.Falla(r.Error!);
        }, ct);
        return resultado.EsExito ? Result.Exito() : Result.Falla(resultado.Error!);
    }

    public void Enlistar(DbContext contexto)
    {
        if (_transaccion is null || _enlistados.Contains(contexto))
        {
            return;
        }

        contexto.Database.UseTransaction(_transaccion);
        _enlistados.Add(contexto);
    }

    /// <summary>Un error de Npgsql puede dejar la transacción ya cerrada: revertir no debe ocultar el error original.</summary>
    private async Task RevertirSinFallarAsync()
    {
        try
        {
            await _transaccion!.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or NpgsqlException)
        {
            // La conexión ya descartó la transacción.
        }
    }

    /// <summary>Tras revertir, lo que quedó en los contextos ya no refleja la base de datos.</summary>
    private void LimpiarSeguimiento()
    {
        foreach (var contexto in _enlistados)
        {
            contexto.ChangeTracker.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaccion is not null)
        {
            await _transaccion.DisposeAsync();
        }

        await _conexion.DisposeAsync();
    }

    public void Dispose()
    {
        _transaccion?.Dispose();
        _conexion.Dispose();
    }
}
