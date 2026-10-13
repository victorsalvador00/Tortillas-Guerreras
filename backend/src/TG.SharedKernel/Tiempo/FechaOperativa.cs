namespace TG.SharedKernel.Tiempo;

/// <summary>
/// Fecha de negocio (<c>fecha_operativa</c>): fecha local en <c>America/Mexico_City</c> (regla 7). Las horas se
/// guardan en UTC; nunca se usa <c>DateTime.Now</c>.
/// </summary>
public interface IFechaOperativa
{
    TimeZoneInfo Zona { get; }

    /// <summary>Fecha operativa del momento actual según el <see cref="TimeProvider"/> inyectado.</summary>
    DateOnly Hoy();

    /// <summary>Fecha operativa de un instante dado (por ejemplo, <c>creadaEn</c> de una operación de la app).</summary>
    DateOnly De(DateTimeOffset instante);

    /// <summary>Inicio del día operativo en UTC.</summary>
    DateTimeOffset InicioUtc(DateOnly fecha);
}

public sealed class FechaOperativa : IFechaOperativa
{
    public const string ZonaPredeterminada = "America/Mexico_City";

    private readonly TimeProvider _reloj;

    public FechaOperativa(TimeProvider reloj, string zonaHoraria = ZonaPredeterminada)
    {
        _reloj = reloj;
        Zona = TimeZoneInfo.FindSystemTimeZoneById(zonaHoraria);
    }

    public TimeZoneInfo Zona { get; }

    public DateOnly Hoy() => De(_reloj.GetUtcNow());

    public DateOnly De(DateTimeOffset instante) => Calcular(instante, Zona);

    public DateTimeOffset InicioUtc(DateOnly fecha)
    {
        var local = fecha.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zona.GetUtcOffset(local)).ToUniversalTime();
    }

    public static DateOnly Calcular(DateTimeOffset instante, TimeZoneInfo zona) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instante, zona).DateTime);
}
