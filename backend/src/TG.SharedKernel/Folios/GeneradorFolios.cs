using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using TG.SharedKernel.Datos;

namespace TG.SharedKernel.Folios;

/// <summary>
/// Folios legibles <c>&lt;PREFIJO&gt;-000001</c> tomados de una secuencia de PostgreSQL por prefijo
/// (<c>&lt;esquema&gt;.folio_&lt;prefijo&gt;_seq</c>). Las secuencias se crean en la migración de cada módulo.
/// </summary>
public interface IGeneradorFolios
{
    Task<string> SiguienteAsync(string esquema, string prefijo, CancellationToken ct);
}

public sealed partial class GeneradorFolios(IUnitOfWork unitOfWork) : IGeneradorFolios
{
    public async Task<string> SiguienteAsync(string esquema, string prefijo, CancellationToken ct)
    {
        var secuencia = NombreSecuencia(esquema, prefijo);
        var conexion = unitOfWork.Conexion;
        var abrir = conexion.State != ConnectionState.Open;
        if (abrir)
        {
            await conexion.OpenAsync(ct);
        }

        try
        {
            await using var comando = conexion.CreateCommand();
            comando.Transaction = unitOfWork.Transaccion;
            comando.CommandText = $"SELECT nextval('{secuencia}')";
            var valor = Convert.ToInt64(await comando.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            return Formatear(prefijo, valor);
        }
        finally
        {
            if (abrir)
            {
                await conexion.CloseAsync();
            }
        }
    }

    public static string Formatear(string prefijo, long consecutivo) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefijo}-{consecutivo:D6}");

    /// <summary>Folio de la app: <c>&lt;ruta&gt;-&lt;dispositivo&gt;-&lt;consecutivo 6 dígitos&gt;</c> (RN-VEN-09).</summary>
    public static string FormatearApp(string ruta, string dispositivo, long consecutivo) =>
        string.Create(CultureInfo.InvariantCulture, $"{ruta}-{dispositivo}-{consecutivo:D6}");

    public static bool EsFolioApp(string folio) => FolioAppRegex().IsMatch(folio);

    public static string NombreSecuencia(string esquema, string prefijo)
    {
        if (!IdentificadorRegex().IsMatch(esquema) || !IdentificadorRegex().IsMatch(prefijo))
        {
            throw new ArgumentException("Esquema o prefijo de folio inválido.");
        }

        return $"{esquema}.folio_{prefijo.ToLowerInvariant()}_seq";
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex IdentificadorRegex();

    [GeneratedRegex(@"^[A-Z0-9]+-D\d{2,}-\d{6}$")]
    private static partial Regex FolioAppRegex();
}
