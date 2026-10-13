using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TG.Catalogos.Dominio;
using TG.Catalogos.Infraestructura;
using TG.SharedKernel.Resultados;

namespace TG.Catalogos.Aplicacion;

public sealed record UnidadDto(Guid Id, string Clave, string Nombre, TipoUnidad Tipo, string ClaveSat, short Decimales, bool Activo);

public sealed record GuardarUnidadRequest(string Clave, string Nombre, TipoUnidad Tipo, string ClaveSat, short Decimales);

public sealed class GuardarUnidadValidator : AbstractValidator<GuardarUnidadRequest>
{
    public GuardarUnidadValidator()
    {
        RuleFor(x => x.Clave).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Nombre).NotEmpty();
        RuleFor(x => x.ClaveSat).NotEmpty().WithMessage("Captura la clave de unidad del SAT.");
        RuleFor(x => x.Decimales).InclusiveBetween((short)0, (short)4);
    }
}

public sealed class UnidadesHandler(CatalogosDbContext db)
{
    public async Task<IReadOnlyList<UnidadDto>> ListarAsync(CancellationToken ct) =>
        await db.Unidades.AsNoTracking().OrderBy(u => u.Clave)
            .Select(u => new UnidadDto(u.Id, u.Clave, u.Nombre, u.Tipo, u.ClaveSat, u.Decimales, u.Activo))
            .ToListAsync(ct);

    public async Task<Result<UnidadDto>> CrearAsync(GuardarUnidadRequest r, CancellationToken ct)
    {
        var clave = r.Clave.Trim().ToUpperInvariant();
        if (await db.Unidades.AnyAsync(u => u.Clave == clave, ct))
        {
            return ErroresCatalogos.ClaveDuplicada();
        }

        var unidad = new UnidadMedida { Clave = clave };
        Aplicar(unidad, r);
        db.Unidades.Add(unidad);
        await db.SaveChangesAsync(ct);
        return ADto(unidad);
    }

    public async Task<Result<UnidadDto>> ActualizarAsync(Guid id, GuardarUnidadRequest r, CancellationToken ct)
    {
        var unidad = await db.Unidades.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unidad is null)
        {
            return ErroresCatalogos.UnidadNoEncontrada;
        }

        var clave = r.Clave.Trim().ToUpperInvariant();
        if (clave != unidad.Clave && await db.Unidades.AnyAsync(u => u.Clave == clave, ct))
        {
            return ErroresCatalogos.ClaveDuplicada();
        }

        unidad.Clave = clave;
        Aplicar(unidad, r);
        await db.SaveChangesAsync(ct);
        return ADto(unidad);
    }

    public async Task<Result<UnidadDto>> CambiarActivoAsync(Guid id, bool activo, CancellationToken ct)
    {
        var unidad = await db.Unidades.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unidad is null)
        {
            return ErroresCatalogos.UnidadNoEncontrada;
        }

        unidad.Activo = activo;
        await db.SaveChangesAsync(ct);
        return ADto(unidad);
    }

    private static void Aplicar(UnidadMedida u, GuardarUnidadRequest r)
    {
        u.Nombre = r.Nombre.Trim();
        u.Tipo = r.Tipo;
        u.ClaveSat = r.ClaveSat.Trim().ToUpperInvariant();
        u.Decimales = r.Decimales;
    }

    private static UnidadDto ADto(UnidadMedida u) => new(u.Id, u.Clave, u.Nombre, u.Tipo, u.ClaveSat, u.Decimales, u.Activo);
}

public sealed record MotivoMermaDto(
    Guid Id,
    string Clave,
    string Nombre,
    bool AplicaProduccion,
    bool AplicaAlmacen,
    bool AplicaRuta,
    bool AplicaRetorno,
    bool Activo);

public sealed record GuardarMotivoMermaRequest(
    string Clave,
    string Nombre,
    bool AplicaProduccion,
    bool AplicaAlmacen,
    bool AplicaRuta,
    bool AplicaRetorno);

public sealed class GuardarMotivoMermaValidator : AbstractValidator<GuardarMotivoMermaRequest>
{
    public GuardarMotivoMermaValidator()
    {
        RuleFor(x => x.Clave).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Nombre).NotEmpty();
        RuleFor(x => x).Must(x => x.AplicaProduccion || x.AplicaAlmacen || x.AplicaRuta || x.AplicaRetorno)
            .WithName("aplicaProduccion").WithMessage("Marca al menos dónde aplica el motivo.");
    }
}

public sealed class MotivosMermaHandler(CatalogosDbContext db)
{
    public async Task<IReadOnlyList<MotivoMermaDto>> ListarAsync(string? aplica, bool? activo, CancellationToken ct)
    {
        var consulta = db.MotivosMerma.AsNoTracking();
        consulta = aplica?.ToLowerInvariant() switch
        {
            "produccion" => consulta.Where(m => m.AplicaProduccion),
            "almacen" => consulta.Where(m => m.AplicaAlmacen),
            "ruta" => consulta.Where(m => m.AplicaRuta),
            "retorno" => consulta.Where(m => m.AplicaRetorno),
            _ => consulta,
        };
        if (activo is not null)
        {
            consulta = consulta.Where(m => m.Activo == activo);
        }

        return await consulta.OrderBy(m => m.Nombre)
            .Select(m => new MotivoMermaDto(m.Id, m.Clave, m.Nombre, m.AplicaProduccion, m.AplicaAlmacen, m.AplicaRuta,
                m.AplicaRetorno, m.Activo))
            .ToListAsync(ct);
    }

    public async Task<Result<MotivoMermaDto>> CrearAsync(GuardarMotivoMermaRequest r, CancellationToken ct)
    {
        var clave = r.Clave.Trim().ToUpperInvariant();
        if (await db.MotivosMerma.AnyAsync(m => m.Clave == clave, ct))
        {
            return ErroresCatalogos.ClaveDuplicada();
        }

        var motivo = new MotivoMerma { Clave = clave };
        Aplicar(motivo, r);
        db.MotivosMerma.Add(motivo);
        await db.SaveChangesAsync(ct);
        return ADto(motivo);
    }

    public async Task<Result<MotivoMermaDto>> ActualizarAsync(Guid id, GuardarMotivoMermaRequest r, CancellationToken ct)
    {
        var motivo = await db.MotivosMerma.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (motivo is null)
        {
            return ErroresCatalogos.MotivoNoEncontrado;
        }

        Aplicar(motivo, r);
        await db.SaveChangesAsync(ct);
        return ADto(motivo);
    }

    public async Task<Result<MotivoMermaDto>> CambiarActivoAsync(Guid id, bool activo, CancellationToken ct)
    {
        var motivo = await db.MotivosMerma.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (motivo is null)
        {
            return ErroresCatalogos.MotivoNoEncontrado;
        }

        motivo.Activo = activo;
        await db.SaveChangesAsync(ct);
        return ADto(motivo);
    }

    private static void Aplicar(MotivoMerma m, GuardarMotivoMermaRequest r)
    {
        m.Nombre = r.Nombre.Trim();
        m.AplicaProduccion = r.AplicaProduccion;
        m.AplicaAlmacen = r.AplicaAlmacen;
        m.AplicaRuta = r.AplicaRuta;
        m.AplicaRetorno = r.AplicaRetorno;
    }

    private static MotivoMermaDto ADto(MotivoMerma m) =>
        new(m.Id, m.Clave, m.Nombre, m.AplicaProduccion, m.AplicaAlmacen, m.AplicaRuta, m.AplicaRetorno, m.Activo);
}

public sealed record ParametroDto(string Clave, string Valor, TipoParametro Tipo, string Descripcion);

public sealed record ActualizarParametroRequest(string Valor);

public sealed class ParametrosHandler(CatalogosDbContext db, IMemoryCache cache)
{
    public async Task<IReadOnlyList<ParametroDto>> ListarAsync(CancellationToken ct) =>
        await db.Parametros.AsNoTracking().OrderBy(p => p.Clave)
            .Select(p => new ParametroDto(p.Clave, p.Valor, p.Tipo, p.Descripcion))
            .ToListAsync(ct);

    public async Task<Result<ParametroDto>> ActualizarAsync(string clave, ActualizarParametroRequest r, CancellationToken ct)
    {
        var parametro = await db.Parametros.FirstOrDefaultAsync(p => p.Clave == clave, ct);
        if (parametro is null)
        {
            return ErroresCatalogos.ParametroNoEncontrado;
        }

        var valor = (r.Valor ?? string.Empty).Trim();
        var valido = parametro.Tipo switch
        {
            TipoParametro.@int => int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            TipoParametro.@decimal => decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            TipoParametro.@bool => bool.TryParse(valor, out _),
            _ => valor.Length > 0,
        };
        if (!valido)
        {
            return ErroresCatalogos.ParametroInvalido(parametro.Tipo.ToString());
        }

        parametro.Valor = parametro.Tipo == TipoParametro.@bool ? valor.ToLowerInvariant() : valor;
        await db.SaveChangesAsync(ct);
        cache.Remove(CatalogosService.ClaveCache(clave));
        return new ParametroDto(parametro.Clave, parametro.Valor, parametro.Tipo, parametro.Descripcion);
    }
}
