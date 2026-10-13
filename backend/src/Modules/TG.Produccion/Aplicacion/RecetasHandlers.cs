using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Produccion.Dominio;
using TG.Produccion.Infraestructura;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Resultados;

namespace TG.Produccion.Aplicacion;

public static class ErroresProduccion
{
    public static Error EstadoInvalido(string mensaje) => Error.Conflicto("PRO-001", mensaje);

    public const string SinExistencia = "PRO-002";

    public static readonly Error RecetaNoEncontrada = Error.NoEncontrado("PRO-003", "La receta no existe.");

    public static readonly Error RecetaNoEditable =
        Error.Conflicto("PRO-004", "Solo se edita una receta en Borrador; crea una nueva versión.");

    public static readonly Error ProductoNoProducible =
        Error.Campo("PRO-005", "productoId", "El producto no existe o no es producible.");

    public static readonly Error OrdenNoEncontrada = Error.NoEncontrado("PRO-006", "La orden de producción no existe.");

    public static readonly Error MotivoNoAplica =
        Error.Campo("PRO-007", "motivoMermaId", "El motivo no existe o no aplica a producción.");

    public static readonly Error SinRecetaVigente =
        Error.Campo("PRO-008", "recetaId", "El producto no tiene una receta Vigente.");

    public static readonly Error CantidadProducidaInvalida =
        Error.Campo("PRO-009", "cantidadProducida", "La cantidad producida es obligatoria y debe ser mayor que cero.");

    public static readonly Error LineaNoEncontrada = Error.NoEncontrado("PRO-010", "La línea no existe.");

    public static readonly Error LineaDuplicada = Error.Campo("PRO-010", "codigo", "Ya existe una línea con ese código.");

    public static Error AlmacenInvalido(string campo) =>
        Error.Campo("PRO-011", campo, "El almacén no existe, está inactivo o es de ruta.");

    public static readonly Error ComponenteAjeno =
        Error.Campo("PRO-012", "productoId", "El producto no es componente ni producto de la orden.");

    public static Error ComponenteInvalido(string mensaje) => Error.Campo("PRO-013", "componentes", mensaje);
}

// ───────────────────────────── Líneas ─────────────────────────────

public sealed record LineaDto(Guid Id, string Codigo, string Nombre, TipoLinea Tipo, bool Activo);

public sealed record GuardarLineaRequest(string Codigo, string Nombre, TipoLinea Tipo, bool Activo);

public sealed class GuardarLineaValidator : AbstractValidator<GuardarLineaRequest>
{
    public GuardarLineaValidator()
    {
        RuleFor(x => x.Codigo).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Nombre).NotEmpty();
    }
}

public sealed class LineasHandler(ProduccionDbContext db)
{
    public async Task<IReadOnlyList<LineaDto>> ListarAsync(bool? activo, CancellationToken ct)
    {
        var consulta = db.Lineas.AsNoTracking();
        if (activo is not null)
        {
            consulta = consulta.Where(l => l.Activo == activo);
        }

        return await consulta.OrderBy(l => l.Codigo).Select(l => new LineaDto(l.Id, l.Codigo, l.Nombre, l.Tipo, l.Activo))
            .ToListAsync(ct);
    }

    public async Task<Result<LineaDto>> CrearAsync(GuardarLineaRequest r, CancellationToken ct)
    {
        var codigo = r.Codigo.Trim().ToUpperInvariant();
        if (await db.Lineas.AnyAsync(l => l.Codigo == codigo, ct))
        {
            return ErroresProduccion.LineaDuplicada;
        }

        var linea = new Linea { Codigo = codigo, Nombre = r.Nombre.Trim(), Tipo = r.Tipo, Activo = r.Activo };
        db.Lineas.Add(linea);
        await db.SaveChangesAsync(ct);
        return new LineaDto(linea.Id, linea.Codigo, linea.Nombre, linea.Tipo, linea.Activo);
    }

    public async Task<Result<LineaDto>> ActualizarAsync(Guid id, GuardarLineaRequest r, CancellationToken ct)
    {
        var linea = await db.Lineas.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (linea is null)
        {
            return ErroresProduccion.LineaNoEncontrada;
        }

        var codigo = r.Codigo.Trim().ToUpperInvariant();
        if (codigo != linea.Codigo && await db.Lineas.AnyAsync(l => l.Codigo == codigo, ct))
        {
            return ErroresProduccion.LineaDuplicada;
        }

        linea.Codigo = codigo;
        linea.Nombre = r.Nombre.Trim();
        linea.Tipo = r.Tipo;
        linea.Activo = r.Activo;
        await db.SaveChangesAsync(ct);
        return new LineaDto(linea.Id, linea.Codigo, linea.Nombre, linea.Tipo, linea.Activo);
    }
}

// ───────────────────────────── Recetas ─────────────────────────────

public sealed record ComponenteDto(Guid Id, Guid ProductoId, string Sku, string Producto, Guid UnidadId, string Unidad,
    decimal FactorABase, decimal Cantidad, decimal CantidadBase, string UnidadBase, short Orden);

public sealed record RecetaDto(Guid Id, Guid ProductoId, string Sku, string Producto, string UnidadBase, int Version,
    string Nombre, decimal RendimientoCantidad, decimal MermaEsperadaPct, EstadoReceta Estado, DateTimeOffset? VigenteDesde,
    string? Notas, DateTimeOffset CreadoEn, IReadOnlyList<ComponenteDto> Componentes);

public sealed record ComponenteRequest(Guid ProductoId, Guid? UnidadId, decimal Cantidad);

public sealed record GuardarRecetaRequest(Guid ProductoId, string Nombre, decimal RendimientoCantidad,
    decimal MermaEsperadaPct, string? Notas, IReadOnlyList<ComponenteRequest> Componentes);

public sealed class GuardarRecetaValidator : AbstractValidator<GuardarRecetaRequest>
{
    public GuardarRecetaValidator()
    {
        RuleFor(x => x.ProductoId).NotEmpty().WithMessage("Elige el producto que se obtiene.");
        RuleFor(x => x.Nombre).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RendimientoCantidad).GreaterThan(0).WithMessage("El rendimiento debe ser mayor que cero.");
        RuleFor(x => x.MermaEsperadaPct).InclusiveBetween(0m, 1m).WithMessage("La merma esperada va de 0 a 1 (0.03 = 3 %).");
        RuleFor(x => x.Componentes).NotEmpty().WithMessage("Agrega al menos un componente.");
        RuleForEach(x => x.Componentes).ChildRules(c =>
        {
            c.RuleFor(x => x.ProductoId).NotEmpty();
            c.RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad debe ser mayor que cero.");
        });
        RuleFor(x => x.Componentes)
            .Must(c => c is null || c.Select(x => x.ProductoId).Distinct().Count() == c.Count)
            .WithMessage("Hay componentes repetidos.");
    }
}

/// <summary>Recetas versionadas: una sola Vigente por producto (RN-PRO-01).</summary>
public sealed class RecetasHandler(ProduccionDbContext db, ICatalogosService catalogos, IUnitOfWork unitOfWork, TimeProvider reloj)
{
    public async Task<IReadOnlyList<RecetaDto>> ListarAsync(Guid? productoId, EstadoReceta? estado, CancellationToken ct)
    {
        var consulta = db.Recetas.AsNoTracking().Include(r => r.Componentes).AsQueryable();
        if (productoId is not null)
        {
            consulta = consulta.Where(r => r.ProductoId == productoId);
        }

        if (estado is not null)
        {
            consulta = consulta.Where(r => r.Estado == estado);
        }

        var recetas = await consulta.OrderBy(r => r.Nombre).ThenByDescending(r => r.NumeroVersion).ToListAsync(ct);
        return await MapearAsync(recetas, ct);
    }

    public async Task<Result<RecetaDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var receta = await db.Recetas.AsNoTracking().Include(r => r.Componentes).FirstOrDefaultAsync(r => r.Id == id, ct);
        return receta is null ? ErroresProduccion.RecetaNoEncontrada : (await MapearAsync([receta], ct))[0];
    }

    public async Task<Result<RecetaDto>> CrearAsync(GuardarRecetaRequest r, CancellationToken ct)
    {
        var receta = new Receta();
        var aplicado = await AplicarAsync(receta, r, ct);
        if (!aplicado.EsExito)
        {
            return aplicado.Error!;
        }

        receta.NumeroVersion = (await db.Recetas.Where(x => x.ProductoId == r.ProductoId).MaxAsync(x => (int?)x.NumeroVersion, ct) ?? 0) + 1;
        db.Recetas.Add(receta);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(receta.Id, ct);
    }

    public async Task<Result<RecetaDto>> ActualizarAsync(Guid id, GuardarRecetaRequest r, CancellationToken ct)
    {
        var receta = await db.Recetas.Include(x => x.Componentes).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (receta is null)
        {
            return ErroresProduccion.RecetaNoEncontrada;
        }

        if (!receta.Editable)
        {
            return ErroresProduccion.RecetaNoEditable;
        }

        if (r.ProductoId != receta.ProductoId)
        {
            return Error.Campo("PRO-013", "productoId", "El producto de una receta no se cambia; crea otra receta.");
        }

        var aplicado = await AplicarAsync(receta, r, ct);
        if (!aplicado.EsExito)
        {
            return aplicado.Error!;
        }

        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    /// <summary>Copia la receta en un Borrador con la siguiente versión.</summary>
    public async Task<Result<RecetaDto>> NuevaVersionAsync(Guid id, CancellationToken ct)
    {
        var origen = await db.Recetas.AsNoTracking().Include(x => x.Componentes).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (origen is null)
        {
            return ErroresProduccion.RecetaNoEncontrada;
        }

        var siguiente = await db.Recetas.Where(x => x.ProductoId == origen.ProductoId).MaxAsync(x => x.NumeroVersion, ct) + 1;
        var copia = new Receta
        {
            ProductoId = origen.ProductoId,
            NumeroVersion = siguiente,
            Nombre = origen.Nombre,
            RendimientoCantidad = origen.RendimientoCantidad,
            MermaEsperadaPct = origen.MermaEsperadaPct,
            Notas = origen.Notas,
        };
        copia.Componentes = origen.Componentes.Select(c => new RecetaComponente
        {
            RecetaId = copia.Id,
            ProductoId = c.ProductoId,
            UnidadId = c.UnidadId,
            FactorABase = c.FactorABase,
            Cantidad = c.Cantidad,
            CantidadBase = c.CantidadBase,
            Orden = c.Orden,
        }).ToList();
        db.Recetas.Add(copia);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(copia.Id, ct);
    }

    /// <summary>RN-PRO-01: publicar deja la receta Vigente y pasa la Vigente anterior a Obsoleta.</summary>
    public async Task<Result<RecetaDto>> PublicarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var receta = await db.Recetas.Include(x => x.Componentes).FirstOrDefaultAsync(x => x.Id == id, c);
            if (receta is null)
            {
                return Result.Falla(ErroresProduccion.RecetaNoEncontrada);
            }

            if (receta.Estado != EstadoReceta.Borrador)
            {
                return Result.Falla(ErroresProduccion.EstadoInvalido("Solo se publica una receta en Borrador."));
            }

            if (receta.Componentes.Count == 0)
            {
                return Result.Falla(ErroresProduccion.ComponenteInvalido("La receta no tiene componentes."));
            }

            var vigentes = await db.Recetas.Where(x => x.ProductoId == receta.ProductoId && x.Estado == EstadoReceta.Vigente).ToListAsync(c);
            foreach (var vigente in vigentes)
            {
                vigente.Estado = EstadoReceta.Obsoleta;
            }

            // Dos pasos: el índice único de Vigente se valida fila por fila.
            await db.SaveChangesAsync(c);
            receta.Estado = EstadoReceta.Vigente;
            receta.VigenteDesde = reloj.GetUtcNow();
            await db.SaveChangesAsync(c);
            return Result.Exito();
        }, ct);

        return resultado.EsExito ? await ObtenerAsync(id, ct) : resultado.Error!;
    }

    private async Task<Result> AplicarAsync(Receta receta, GuardarRecetaRequest r, CancellationToken ct)
    {
        var producto = await catalogos.ObtenerProductoAsync(r.ProductoId, ct);
        if (producto is null || !producto.EsProducible)
        {
            return ErroresProduccion.ProductoNoProducible;
        }

        var componentes = new List<RecetaComponente>();
        short orden = 0;
        foreach (var c in r.Componentes)
        {
            if (c.ProductoId == r.ProductoId)
            {
                return ErroresProduccion.ComponenteInvalido("Un producto no puede ser componente de su propia receta.");
            }

            var componente = await catalogos.ObtenerProductoAsync(c.ProductoId, ct);
            if (componente is null || !componente.Activo)
            {
                return ErroresProduccion.ComponenteInvalido("Un componente no existe o está inactivo.");
            }

            var unidadId = c.UnidadId ?? componente.UnidadBaseId;
            var conversion = await catalogos.ConvertirABaseAsync(c.ProductoId, unidadId, c.Cantidad, UsoUnidad.Produccion, ct);
            if (!conversion.EsExito)
            {
                return conversion.Error!;
            }

            componentes.Add(new RecetaComponente
            {
                RecetaId = receta.Id,
                ProductoId = c.ProductoId,
                UnidadId = unidadId,
                FactorABase = conversion.Valor.FactorABase,
                Cantidad = c.Cantidad,
                CantidadBase = conversion.Valor.Base,
                Orden = orden++,
            });
        }

        receta.ProductoId = r.ProductoId;
        receta.Nombre = r.Nombre.Trim();
        receta.RendimientoCantidad = r.RendimientoCantidad;
        receta.MermaEsperadaPct = r.MermaEsperadaPct;
        receta.Notas = string.IsNullOrWhiteSpace(r.Notas) ? null : r.Notas.Trim();
        receta.Componentes.Clear();
        receta.Componentes.AddRange(componentes);
        return Result.Exito();
    }

    private async Task<IReadOnlyList<RecetaDto>> MapearAsync(List<Receta> recetas, CancellationToken ct)
    {
        var ids = recetas.Select(r => r.ProductoId).Concat(recetas.SelectMany(r => r.Componentes).Select(c => c.ProductoId));
        var productos = await catalogos.ObtenerProductosAsync(ids, ct);
        var unidades = await catalogos.ObtenerUnidadesAsync(ct);
        return recetas.Select(r =>
        {
            var p = productos.GetValueOrDefault(r.ProductoId);
            return new RecetaDto(r.Id, r.ProductoId, p?.Sku ?? "?", p?.Nombre ?? "?", p?.UnidadBaseClave ?? string.Empty,
                r.NumeroVersion, r.Nombre, r.RendimientoCantidad, r.MermaEsperadaPct, r.Estado, r.VigenteDesde, r.Notas,
                r.CreadoEn,
                r.Componentes.OrderBy(c => c.Orden).Select(c =>
                {
                    var cp = productos.GetValueOrDefault(c.ProductoId);
                    return new ComponenteDto(c.Id, c.ProductoId, cp?.Sku ?? "?", cp?.Nombre ?? "?", c.UnidadId,
                        unidades.GetValueOrDefault(c.UnidadId)?.Clave ?? "?", c.FactorABase, c.Cantidad, c.CantidadBase,
                        cp?.UnidadBaseClave ?? string.Empty, c.Orden);
                }).ToList());
        }).ToList();
    }
}
