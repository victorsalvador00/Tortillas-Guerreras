using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.SharedKernel.Resultados;
using TG.Ventas.Dominio;
using TG.Ventas.Infraestructura;
using TG.Ventas.Publico;

namespace TG.Ventas.Aplicacion;

// ───────────────────────────── Grupos de clientes ─────────────────────────────

public sealed record GrupoClienteDto(Guid Id, string Clave, string Nombre, Guid CanalId, string Canal, Guid? ListaPrecioId,
    string? ListaPrecio, int Clientes, bool Activo);

public sealed record GuardarGrupoClienteRequest(string Clave, string Nombre, Guid CanalId, Guid? ListaPrecioId, bool Activo);

public sealed class GuardarGrupoClienteValidator : AbstractValidator<GuardarGrupoClienteRequest>
{
    public GuardarGrupoClienteValidator()
    {
        RuleFor(x => x.Clave).NotEmpty().MaximumLength(20).WithMessage("Captura la clave del grupo (hasta 20 caracteres).");
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Captura el nombre del grupo.");
        RuleFor(x => x.CanalId).NotEmpty().WithMessage("Elige el canal.");
    }
}

/// <summary>Corporativos (Walmart, Chedraui, SANBORNS…): exclusividades (RN-VEN-14) y lista heredada (RN-VEN-12).</summary>
public sealed class GruposClientesHandler(VentasDbContext db)
{
    public async Task<IReadOnlyList<GrupoClienteDto>> ListarAsync(bool? activo, CancellationToken ct)
    {
        var consulta = db.GruposClientes.AsNoTracking();
        if (activo is not null)
        {
            consulta = consulta.Where(g => g.Activo == activo);
        }

        return await MapearAsync(await consulta.OrderBy(g => g.Nombre).ToListAsync(ct), ct);
    }

    public async Task<Result<GrupoClienteDto>> GuardarAsync(Guid? id, GuardarGrupoClienteRequest r, CancellationToken ct)
    {
        var clave = r.Clave.Trim().ToUpperInvariant();
        if (await db.GruposClientes.AnyAsync(g => g.Clave == clave && g.Id != id, ct))
        {
            return Error.Campo(ErroresVentas.Duplicado, "clave", "Ya existe un grupo con esa clave.");
        }

        if (!await db.Canales.AnyAsync(c => c.Id == r.CanalId, ct))
        {
            return Error.Campo(ErroresVentas.NoEncontrado, "canalId", "El canal no existe.");
        }

        if (r.ListaPrecioId is { } lista && !await db.ListasPrecio.AnyAsync(l => l.Id == lista, ct))
        {
            return Error.Campo(ErroresVentas.NoEncontrado, "listaPrecioId", "La lista de precios no existe.");
        }

        GrupoCliente? grupo;
        if (id is null)
        {
            grupo = new GrupoCliente();
            db.GruposClientes.Add(grupo);
        }
        else
        {
            grupo = await db.GruposClientes.FirstOrDefaultAsync(g => g.Id == id, ct);
            if (grupo is null)
            {
                return Error.NoEncontrado(ErroresVentas.NoEncontrado, "El grupo no existe.");
            }
        }

        grupo.Clave = clave;
        grupo.Nombre = r.Nombre.Trim();
        grupo.CanalId = r.CanalId;
        grupo.ListaPrecioId = r.ListaPrecioId;
        grupo.Activo = r.Activo;
        await db.SaveChangesAsync(ct);
        return (await MapearAsync([grupo], ct))[0];
    }

    private async Task<IReadOnlyList<GrupoClienteDto>> MapearAsync(List<GrupoCliente> grupos, CancellationToken ct)
    {
        var canales = await db.Canales.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        var listas = await db.ListasPrecio.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Nombre, ct);
        var ids = grupos.Select(g => g.Id).ToList();
        var conteo = await db.Clientes.AsNoTracking().Where(c => c.GrupoClienteId != null && ids.Contains(c.GrupoClienteId.Value) && c.Activo)
            .GroupBy(c => c.GrupoClienteId!.Value).Select(g => new { g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        return grupos.Select(g => new GrupoClienteDto(g.Id, g.Clave, g.Nombre, g.CanalId, canales.GetValueOrDefault(g.CanalId) ?? "?",
            g.ListaPrecioId, g.ListaPrecioId is { } l ? listas.GetValueOrDefault(l) : null, conteo.GetValueOrDefault(g.Id), g.Activo)).ToList();
    }
}

// ───────────────────────────── Precios especiales ─────────────────────────────

/// <param name="PrecioLista">Precio de la lista del cliente para comparar (nulo si la lista no lo tiene).</param>
public sealed record PrecioClienteDto(Guid ProductoId, string Sku, string Producto, Guid UnidadId, string Unidad, decimal PrecioUnitario,
    DateOnly VigenteDesde, decimal? PrecioLista);

public sealed record PrecioClienteRequest(Guid ProductoId, Guid UnidadId, decimal PrecioUnitario, DateOnly VigenteDesde);

public sealed record PreciosClienteRequest(IReadOnlyList<PrecioClienteRequest> Precios);

public sealed class PreciosClienteValidator : AbstractValidator<PreciosClienteRequest>
{
    public PreciosClienteValidator()
    {
        RuleFor(x => x.Precios).NotNull();
        RuleForEach(x => x.Precios).ChildRules(p =>
        {
            p.RuleFor(x => x.ProductoId).NotEmpty();
            p.RuleFor(x => x.UnidadId).NotEmpty();
            p.RuleFor(x => x.PrecioUnitario).GreaterThanOrEqualTo(0).WithMessage("El precio no puede ser negativo.");
        });
        RuleFor(x => x.Precios).Must(p => p is null || p.Select(x => (x.ProductoId, x.UnidadId)).Distinct().Count() == p.Count)
            .WithMessage("Hay productos con la misma unidad repetidos.");
    }
}

/// <summary>RN-VEN-12: precios especiales del cliente. Reemplazar desactiva los que se quitan (la app los borra).</summary>
public sealed class PreciosClienteHandler(VentasDbContext db, ICatalogosService catalogos)
{
    public async Task<Result<IReadOnlyList<PrecioClienteDto>>> ListarAsync(Guid clienteId, CancellationToken ct)
    {
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId, ct);
        if (cliente is null)
        {
            return Error.NoEncontrado(ErroresVentas.ClienteNoEncontrado, "El cliente no existe.");
        }

        var precios = await db.PreciosClientes.AsNoTracking().Where(p => p.ClienteId == clienteId && p.Activo).ToListAsync(ct);
        var productos = await catalogos.ObtenerProductosAsync(precios.Select(p => p.ProductoId), ct);
        var unidades = await catalogos.ObtenerUnidadesAsync(ct);
        var lista = await db.ListasPrecioPartidas.AsNoTracking().Where(x => x.ListaPrecioId == cliente.ListaPrecioId)
            .ToDictionaryAsync(x => (x.ProductoId, x.UnidadId), x => x.PrecioUnitario, ct);
        return precios.Select(p => new PrecioClienteDto(p.ProductoId, productos.GetValueOrDefault(p.ProductoId)?.Sku ?? "?",
                productos.GetValueOrDefault(p.ProductoId)?.Nombre ?? "?", p.UnidadId, unidades.GetValueOrDefault(p.UnidadId)?.Clave ?? "?",
                p.PrecioUnitario, p.VigenteDesde, lista.TryGetValue((p.ProductoId, p.UnidadId), out var l) ? l : null))
            .OrderBy(p => p.Sku).ToList();
    }

    public async Task<Result<IReadOnlyList<PrecioClienteDto>>> ReemplazarAsync(Guid clienteId, PreciosClienteRequest r, CancellationToken ct)
    {
        if (!await db.Clientes.AnyAsync(c => c.Id == clienteId, ct))
        {
            return Error.NoEncontrado(ErroresVentas.ClienteNoEncontrado, "El cliente no existe.");
        }

        var productos = await catalogos.ObtenerProductosAsync(r.Precios.Select(p => p.ProductoId), ct);
        foreach (var p in r.Precios)
        {
            if (!productos.TryGetValue(p.ProductoId, out var producto) || !producto.EsVendible)
            {
                return Error.Validacion(ErroresVentas.ProductoNoVendible, "Uno de los productos no existe o no es vendible.");
            }

            // RN-CAT-03: la unidad debe tener conversión de venta.
            var conversion = await catalogos.ConvertirABaseAsync(p.ProductoId, p.UnidadId, 1, UsoUnidad.Venta, ct);
            if (!conversion.EsExito)
            {
                return conversion.Error!;
            }
        }

        var actuales = await db.PreciosClientes.Where(p => p.ClienteId == clienteId).ToListAsync(ct);
        foreach (var actual in actuales.Where(a => a.Activo && !r.Precios.Any(p => p.ProductoId == a.ProductoId && p.UnidadId == a.UnidadId)))
        {
            actual.Activo = false;
        }

        foreach (var p in r.Precios)
        {
            var actual = actuales.FirstOrDefault(a => a.ProductoId == p.ProductoId && a.UnidadId == p.UnidadId);
            if (actual is null)
            {
                db.PreciosClientes.Add(new PrecioCliente
                {
                    ClienteId = clienteId, ProductoId = p.ProductoId, UnidadId = p.UnidadId, PrecioUnitario = p.PrecioUnitario,
                    VigenteDesde = p.VigenteDesde,
                });
                continue;
            }

            actual.PrecioUnitario = p.PrecioUnitario;
            actual.VigenteDesde = p.VigenteDesde;
            actual.Activo = true;
        }

        await db.SaveChangesAsync(ct);
        return await ListarAsync(clienteId, ct);
    }
}

// ───────────────────────────── Productos exclusivos ─────────────────────────────

public sealed record ProductoExclusivoDto(Guid Id, Guid? ClienteId, string? Cliente, Guid? GrupoClienteId, string? Grupo);

public sealed record ProductoExclusivoRequest(Guid? ClienteId, Guid? GrupoClienteId);

public sealed record ProductosExclusivosRequest(IReadOnlyList<ProductoExclusivoRequest> Exclusivos);

public sealed class ProductosExclusivosValidator : AbstractValidator<ProductosExclusivosRequest>
{
    public ProductosExclusivosValidator()
    {
        RuleFor(x => x.Exclusivos).NotNull();
        RuleForEach(x => x.Exclusivos).Must(e => (e.ClienteId is null) != (e.GrupoClienteId is null))
            .WithMessage("Cada registro lleva un cliente o un grupo, no ambos.");
        RuleFor(x => x.Exclusivos).Must(e => e is null || e.Distinct().Count() == e.Count).WithMessage("Hay registros repetidos.");
    }
}

/// <summary>RN-VEN-14: a quién se puede vender un producto. Sin registros activos = disponible para todos.</summary>
public sealed class ProductosExclusivosHandler(VentasDbContext db, ICatalogosService catalogos)
{
    public async Task<Result<IReadOnlyList<ProductoExclusivoDto>>> ListarAsync(Guid productoId, CancellationToken ct)
    {
        var exclusivos = await db.ProductosExclusivos.AsNoTracking().Where(x => x.ProductoId == productoId && x.Activo).ToListAsync(ct);
        var clienteIds = exclusivos.Where(x => x.ClienteId != null).Select(x => x.ClienteId!.Value).ToList();
        var clientes = await db.Clientes.AsNoTracking().Where(c => clienteIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        var grupos = await db.GruposClientes.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Nombre, ct);
        return exclusivos.Select(x => new ProductoExclusivoDto(x.Id, x.ClienteId, x.ClienteId is { } c ? clientes.GetValueOrDefault(c) : null,
                x.GrupoClienteId, x.GrupoClienteId is { } g ? grupos.GetValueOrDefault(g) : null))
            .OrderBy(x => x.Grupo ?? x.Cliente).ToList();
    }

    public async Task<Result<IReadOnlyList<ProductoExclusivoDto>>> ReemplazarAsync(Guid productoId, ProductosExclusivosRequest r,
        CancellationToken ct)
    {
        if (await catalogos.ObtenerProductoAsync(productoId, ct) is not { EsVendible: true })
        {
            return Error.NoEncontrado(ErroresVentas.ProductoNoVendible, "El producto no existe o no es vendible.");
        }

        var clienteIds = r.Exclusivos.Where(x => x.ClienteId != null).Select(x => x.ClienteId!.Value).ToList();
        var grupoIds = r.Exclusivos.Where(x => x.GrupoClienteId != null).Select(x => x.GrupoClienteId!.Value).ToList();
        if (await db.Clientes.CountAsync(c => clienteIds.Contains(c.Id), ct) != clienteIds.Distinct().Count()
            || await db.GruposClientes.CountAsync(g => grupoIds.Contains(g.Id), ct) != grupoIds.Distinct().Count())
        {
            return Error.Campo(ErroresVentas.NoEncontrado, "exclusivos", "Uno de los clientes o grupos no existe.");
        }

        var actuales = await db.ProductosExclusivos.Where(x => x.ProductoId == productoId).ToListAsync(ct);
        foreach (var actual in actuales.Where(a => a.Activo && !r.Exclusivos.Any(e => e.ClienteId == a.ClienteId && e.GrupoClienteId == a.GrupoClienteId)))
        {
            actual.Activo = false;
        }

        foreach (var e in r.Exclusivos)
        {
            var actual = actuales.FirstOrDefault(a => a.ClienteId == e.ClienteId && a.GrupoClienteId == e.GrupoClienteId);
            if (actual is null)
            {
                db.ProductosExclusivos.Add(new ProductoExclusivo { ProductoId = productoId, ClienteId = e.ClienteId, GrupoClienteId = e.GrupoClienteId });
            }
            else
            {
                actual.Activo = true;
            }
        }

        await db.SaveChangesAsync(ct);
        return await ListarAsync(productoId, ct);
    }
}
