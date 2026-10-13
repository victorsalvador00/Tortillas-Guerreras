using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TG.SharedKernel.Datos;

/// <summary>Convenciones de tipos de 02-convenciones: cantidades, importes, precios, costos y tasas.</summary>
public static class ConvencionesModelo
{
    public static PropertyBuilder<decimal> Cantidad(this PropertyBuilder<decimal> p) => p.HasPrecision(18, 4);

    public static PropertyBuilder<decimal?> Cantidad(this PropertyBuilder<decimal?> p) => p.HasPrecision(18, 4);

    public static PropertyBuilder<decimal> Importe(this PropertyBuilder<decimal> p) => p.HasPrecision(18, 2);

    public static PropertyBuilder<decimal?> Importe(this PropertyBuilder<decimal?> p) => p.HasPrecision(18, 2);

    public static PropertyBuilder<decimal> Precio(this PropertyBuilder<decimal> p) => p.HasPrecision(18, 4);

    public static PropertyBuilder<decimal?> Precio(this PropertyBuilder<decimal?> p) => p.HasPrecision(18, 4);

    public static PropertyBuilder<decimal> Costo(this PropertyBuilder<decimal> p) => p.HasPrecision(18, 6);

    public static PropertyBuilder<decimal?> Costo(this PropertyBuilder<decimal?> p) => p.HasPrecision(18, 6);

    public static PropertyBuilder<decimal> Tasa(this PropertyBuilder<decimal> p) => p.HasPrecision(9, 6);

    public static PropertyBuilder<decimal?> Tasa(this PropertyBuilder<decimal?> p) => p.HasPrecision(9, 6);

    public static PropertyBuilder<decimal?> Coordenada(this PropertyBuilder<decimal?> p) => p.HasPrecision(9, 6);

    public static PropertyBuilder<decimal?> Metros(this PropertyBuilder<decimal?> p) => p.HasPrecision(9, 2);

    /// <summary>Enum guardado como <c>text</c> con <c>CHECK</c> de valores válidos (02-convenciones).</summary>
    public static PropertyBuilder<TEnum> ComoTexto<TEnum>(this PropertyBuilder<TEnum> p, string tabla)
        where TEnum : struct, Enum
    {
        p.HasConversion<string>();
        AgregarCheck<TEnum>(p.Metadata, tabla, nullable: false);
        return p;
    }

    public static PropertyBuilder<TEnum?> ComoTexto<TEnum>(this PropertyBuilder<TEnum?> p, string tabla)
        where TEnum : struct, Enum
    {
        p.HasConversion<string>();
        AgregarCheck<TEnum>(p.Metadata, tabla, nullable: true);
        return p;
    }

    private static void AgregarCheck<TEnum>(IMutableProperty propiedad, string tabla, bool nullable) where TEnum : struct, Enum
    {
        var columna = SnakeCase(propiedad.Name);
        var valores = string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"));
        var condicion = nullable ? $"{columna} IS NULL OR {columna} IN ({valores})" : $"{columna} IN ({valores})";
        propiedad.DeclaringType.ContainingEntityType.AddCheckConstraint($"ck_{tabla}_{columna}", condicion);
    }

    public static string SnakeCase(string nombre) =>
        string.Concat(nombre.Select((c, i) => i > 0 && char.IsUpper(c) && !char.IsUpper(nombre[i - 1]) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    /// <summary>Concurrencia optimista con la columna de sistema <c>xmin</c>.</summary>
    public static void ConcurrenciaXmin<T>(this EntityTypeBuilder<T> builder) where T : class, IConcurrencia =>
        builder.Property(e => e.Version).IsRowVersion();
}

public interface IConcurrencia
{
    uint Version { get; set; }
}
