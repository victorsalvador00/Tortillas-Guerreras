using System.Reflection;
using NetArchTest.Rules;

namespace TG.ArchitectureTests;

/// <summary>
/// Regla 4 de CLAUDE.md: un módulo no referencia el <c>DbContext</c> ni los tipos internos de otro; solo su carpeta
/// <c>Publico/</c>.
/// </summary>
public sealed class FronterasModulosTests
{
    public static readonly string[] Modulos =
        ["Seguridad", "Catalogos", "Compras", "Produccion", "Inventario", "Logistica", "Ventas", "Facturacion", "Sincronizacion"];

    private static readonly string[] CarpetasInternas = ["Dominio", "Aplicacion", "Infraestructura", "Endpoints"];

    public static TheoryData<string> DatosModulos()
    {
        var datos = new TheoryData<string>();
        foreach (var modulo in Modulos)
        {
            datos.Add(modulo);
        }

        return datos;
    }

    private static Assembly Ensamblado(string modulo) => Assembly.Load($"TG.{modulo}");

    [Theory]
    [MemberData(nameof(DatosModulos))]
    public void Modulo_NoUsaInternosDeOtrosModulos(string modulo)
    {
        var prohibidos = Modulos.Where(m => m != modulo)
            .SelectMany(m => CarpetasInternas.Select(c => $"TG.{m}.{c}"))
            .ToArray();

        var resultado = Types.InAssembly(Ensamblado(modulo))
            .ShouldNot()
            .HaveDependencyOnAny(prohibidos)
            .GetResult();

        resultado.IsSuccessful.ShouldBeTrue(
            $"TG.{modulo} usa internos de otro módulo: {string.Join(", ", resultado.FailingTypeNames ?? [])}");
    }

    [Theory]
    [MemberData(nameof(DatosModulos))]
    public void Publico_NoExponeTiposInternosDelPropioModulo(string modulo)
    {
        var resultado = Types.InAssembly(Ensamblado(modulo))
            .That().ResideInNamespace($"TG.{modulo}.Publico")
            .ShouldNot()
            .HaveDependencyOnAny($"TG.{modulo}.Infraestructura", $"TG.{modulo}.Endpoints")
            .GetResult();

        resultado.IsSuccessful.ShouldBeTrue(
            $"TG.{modulo}.Publico expone infraestructura: {string.Join(", ", resultado.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Inventario_YCatalogos_NoDependenDeOtrosModulos()
    {
        var resultado = Types.InAssembly(Ensamblado("Catalogos"))
            .ShouldNot()
            .HaveDependencyOnAny(Modulos.Where(m => m != "Catalogos").Select(m => $"TG.{m}").ToArray())
            .GetResult();
        resultado.IsSuccessful.ShouldBeTrue(string.Join(", ", resultado.FailingTypeNames ?? []));

        var inventario = Types.InAssembly(Ensamblado("Inventario"))
            .ShouldNot()
            .HaveDependencyOnAny(Modulos.Where(m => m is not "Inventario" and not "Catalogos").Select(m => $"TG.{m}").ToArray())
            .GetResult();
        inventario.IsSuccessful.ShouldBeTrue(string.Join(", ", inventario.FailingTypeNames ?? []));
    }

    [Fact]
    public void SoloInventario_DeclaraEntidadesDeInventario()
    {
        // Regla 3: nadie más escribe en inventario.* — ningún otro DbContext mapea el esquema "inventario".
        foreach (var modulo in Modulos.Where(m => m != "Inventario"))
        {
            var contextos = Ensamblado(modulo).GetTypes()
                .Where(t => typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(t));
            foreach (var contexto in contextos)
            {
                var esquema = contexto.GetField("Esquema", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                esquema.ShouldNotBe("inventario", $"{contexto.FullName} no debe usar el esquema de inventario.");
            }
        }
    }
}
