using System.Diagnostics.CodeAnalysis;

namespace TG.SharedKernel.Resultados;

/// <summary>Resultado de una operación sin valor de retorno.</summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool EsExito => Error is null;

    public static Result Exito() => new(null);

    public static Result Falla(Error error) => new(error);

    public static Result<T> Exito<T>(T valor) => Result<T>.Exito(valor);

    public static implicit operator Result(Error error) => Falla(error);
}

/// <summary>Resultado de una operación con valor de retorno.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _valor;

    private Result(T? valor, Error? error) : base(error) => _valor = valor;

    public T Valor => EsExito ? _valor! : throw new InvalidOperationException($"El resultado es una falla: {Error!.Codigo}");

    public static Result<T> Exito(T valor) => new(valor, null);

    public static new Result<T> Falla(Error error) => new(default, error);

    public static implicit operator Result<T>(T valor) => Exito(valor);

    public static implicit operator Result<T>(Error error) => Falla(error);

    public Result<TOut> Map<TOut>(Func<T, TOut> map) => EsExito ? Result<TOut>.Exito(map(Valor)) : Result<TOut>.Falla(Error!);
}
