namespace TG.SharedKernel.Dominio;

/// <summary>Identificadores UUID v7 (ordenables por tiempo). Regla 6 de CLAUDE.md.</summary>
public static class Id
{
    public static Guid Nuevo() => Guid.CreateVersion7();
}
