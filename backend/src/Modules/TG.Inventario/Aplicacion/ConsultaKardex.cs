using Microsoft.EntityFrameworkCore;
using TG.Inventario.Dominio;
using TG.Inventario.Infraestructura;
using TG.Inventario.Publico;

namespace TG.Inventario.Aplicacion;

/// <summary>Lecturas del kardex para la trazabilidad de lotes; nunca escribe (regla 3).</summary>
internal sealed class ConsultaKardex(InventarioDbContext db) : IConsultaKardex
{
    public async Task<IReadOnlyList<MovimientoKardex>> MovimientosDeLotesAsync(IReadOnlyCollection<(Guid ProductoId, string Lote)> lotes,
        CancellationToken ct)
    {
        var pares = lotes.Where(l => l.Lote != string.Empty).ToHashSet();
        if (pares.Count == 0)
        {
            return [];
        }

        var textos = pares.Select(p => p.Lote).Distinct().ToList();
        var productos = pares.Select(p => p.ProductoId).Distinct().ToList();
        var filas = await Proyectar(db.Movimientos.Where(m => textos.Contains(m.Lote) && productos.Contains(m.ProductoId)))
            .ToListAsync(ct);
        return filas.Where(m => pares.Contains((m.ProductoId, m.Lote))).ToList();
    }

    public async Task<IReadOnlyList<MovimientoKardex>> MovimientosDeDocumentosAsync(
        IReadOnlyCollection<(DocumentoInventario Tipo, Guid Id)> documentos, CancellationToken ct)
    {
        if (documentos.Count == 0)
        {
            return [];
        }

        var tipos = documentos.Select(d => d.Tipo).Distinct().ToList();
        var ids = documentos.Select(d => d.Id).Distinct().ToList();
        return await Proyectar(db.Movimientos.Where(m => tipos.Contains(m.DocumentoTipo) && ids.Contains(m.DocumentoId))).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LoteKardex>> BuscarLotesAsync(string texto, int limite, CancellationToken ct)
    {
        var patron = $"%{texto.Trim()}%";
        var lotes = await db.Movimientos
            .Where(m => m.Lote != string.Empty && EF.Functions.ILike(m.Lote, patron))
            .GroupBy(m => new { m.ProductoId, m.Lote })
            .Select(g => new { g.Key.ProductoId, g.Key.Lote, Caducidad = g.Max(m => m.FechaCaducidad) })
            .OrderByDescending(l => l.Lote)
            .Take(limite)
            .ToListAsync(ct);
        return lotes.Select(l => new LoteKardex(l.ProductoId, l.Lote, l.Caducidad)).ToList();
    }

    public async Task<IReadOnlyList<DocumentoKardex>> BuscarDocumentosAsync(string texto, int limite, CancellationToken ct)
    {
        var patron = $"%{texto.Trim()}%";
        var documentos = await db.Movimientos
            .Where(m => EF.Functions.ILike(m.DocumentoFolio, patron))
            .GroupBy(m => new { m.DocumentoTipo, m.DocumentoId, m.DocumentoFolio })
            .Select(g => g.Key)
            .OrderByDescending(d => d.DocumentoFolio)
            .Take(limite)
            .ToListAsync(ct);
        return documentos.Select(d => new DocumentoKardex(d.DocumentoTipo, d.DocumentoId, d.DocumentoFolio)).ToList();
    }

    private static IQueryable<MovimientoKardex> Proyectar(IQueryable<Movimiento> consulta) =>
        consulta.Select(m => new MovimientoKardex(m.Id, m.FechaHora, m.FechaOperativa, m.Tipo, m.AlmacenId, m.ProductoId, m.Lote,
            m.Cantidad, m.DocumentoTipo, m.DocumentoId, m.DocumentoFolio, m.FechaCaducidad));
}
