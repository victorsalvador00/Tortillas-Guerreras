using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.Compras.Dominio;
using TG.Compras.Infraestructura;
using TG.SharedKernel.Api;
using TG.SharedKernel.Resultados;

namespace TG.Compras.Aplicacion;

public static class ErroresCompras
{
    public static Error NoComprable(string producto) =>
        Error.Campo("COM-001", "productoId", $"{producto} no es un producto comprable.");

    public static readonly Error NoEditable = Error.Conflicto("COM-002", "La orden de compra solo se edita en Borrador.");

    public static readonly Error SinPartidas = Error.Validacion("COM-003", "La orden de compra necesita al menos una partida.");

    public static Error ExcedeTolerancia(string producto, decimal maximo) =>
        Error.Campo("COM-004", "cantidad", $"La cantidad de {producto} excede lo pedido más la tolerancia (máximo {maximo:0.####} en unidad base).");

    public static readonly Error SinPermisoCompraDirecta =
        Error.Prohibido("COM-005", "No tienes permiso para registrar compras directas sin orden.");

    public static Error TransicionInvalida(string mensaje) => Error.Conflicto("COM-006", mensaje);

    public static readonly Error RecepcionCancelada = Error.Conflicto("COM-007", "La recepción ya está cancelada.");

    public static readonly Error OrdenNoRecibible =
        Error.Conflicto("COM-008", "La orden de compra no está autorizada o ya no admite recepciones.");

    public static readonly Error ProveedorNoEncontrado = Error.NoEncontrado("COM-010", "El proveedor no existe.");

    public static readonly Error ProveedorInactivo = Error.Campo("COM-010", "proveedorId", "El proveedor está inactivo.");

    public static readonly Error OrdenNoEncontrada = Error.NoEncontrado("COM-011", "La orden de compra no existe.");

    public static readonly Error RecepcionNoEncontrada = Error.NoEncontrado("COM-012", "La recepción no existe.");

    public static readonly Error CodigoDuplicado = Error.Campo("COM-013", "codigo", "Ya existe un proveedor con ese código.");

    public static readonly Error PartidaOrdenInvalida =
        Error.Campo("COM-014", "ordenCompraPartidaId", "La partida no pertenece a la orden de compra.");

    public static readonly Error AlmacenInvalido =
        Error.Campo("COM-015", "almacenId", "El almacén no existe, está inactivo o es de ruta.");

    public static readonly Error ProductoNoEncontrado = Error.Campo("COM-016", "productoId", "El producto no existe.");

    public static Error FueraDeCatalogo(string producto, string proveedor) =>
        Error.Campo("COM-017", "productoId", $"{proveedor} no vende {producto}: agrégalo a su catálogo en Proveedores → Productos.");

    public static Error ProductoYaEnCatalogo(string producto) =>
        Error.Campo("COM-018", "productoId", $"{producto} ya está en el catálogo del proveedor.");

    public static readonly Error ProductoNoEnCatalogoProveedor =
        Error.NoEncontrado("COM-019", "El producto no está en el catálogo del proveedor.");

    public static readonly Error ProveedorInactivoCatalogo =
        Error.Conflicto("COM-020", "El proveedor está inactivo: reactívalo para editar sus productos.");
}

public sealed record ProveedorDto(
    Guid Id,
    string Codigo,
    string RazonSocial,
    string? NombreComercial,
    string? Rfc,
    string? Contacto,
    string? Telefono,
    string? Email,
    int DiasCredito,
    bool Activo,
    int Productos = 0);

public sealed record GuardarProveedorRequest(
    string Codigo,
    string RazonSocial,
    string? NombreComercial,
    string? Rfc,
    string? Contacto,
    string? Telefono,
    string? Email,
    int DiasCredito);

public sealed class GuardarProveedorValidator : AbstractValidator<GuardarProveedorRequest>
{
    public GuardarProveedorValidator()
    {
        RuleFor(x => x.Codigo).NotEmpty().MaximumLength(20).WithMessage("Captura el código (máximo 20 caracteres).");
        RuleFor(x => x.RazonSocial).NotEmpty().WithMessage("Captura la razón social.");
        RuleFor(x => x.Rfc).Matches("^[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}$").When(x => !string.IsNullOrWhiteSpace(x.Rfc))
            .WithMessage("El RFC no tiene un formato válido.");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("El correo no es válido.");
        RuleFor(x => x.DiasCredito).GreaterThanOrEqualTo(0);
    }
}

public sealed record PrecioProveedorDto(
    Guid ProductoId,
    string Sku,
    string Producto,
    Guid UnidadId,
    string Unidad,
    decimal UltimoPrecio,
    DateTimeOffset? UltimaCompraEn,
    IReadOnlyList<HistorialPrecioDto> Historial);

public sealed record HistorialPrecioDto(DateOnly Fecha, string FolioRecepcion, string Unidad, decimal Cantidad, decimal PrecioUnitario,
    decimal CostoUnitarioBase);

public sealed class ProveedoresHandler(ComprasDbContext db, ICatalogosService catalogos)
{
    public async Task<Paginado<ProveedorDto>> ListarAsync(string? buscar, bool? activo, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Proveedores.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var patron = $"%{buscar.Trim()}%";
            consulta = consulta.Where(p => EF.Functions.ILike(p.Codigo, patron) || EF.Functions.ILike(p.RazonSocial, patron)
                || (p.NombreComercial != null && EF.Functions.ILike(p.NombreComercial, patron)) || (p.Rfc != null && EF.Functions.ILike(p.Rfc, patron)));
        }

        if (activo is not null)
        {
            consulta = consulta.Where(p => p.Activo == activo);
        }

        var pagina_ = await consulta.OrderBy(p => p.RazonSocial).PaginarAsync(pagina, tamano, ct);
        var ids = pagina_.Items.Select(p => p.Id).ToList();
        var conteos = await db.ProveedoresProductos.AsNoTracking().Where(pp => ids.Contains(pp.ProveedorId) && pp.Activo)
            .GroupBy(pp => pp.ProveedorId).Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Total, ct);
        return pagina_.Map(p => ADto(p, conteos.GetValueOrDefault(p.Id)));
    }

    public async Task<Result<ProveedorDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Proveedores.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? ErroresCompras.ProveedorNoEncontrado : ADto(p);
    }

    public async Task<Result<ProveedorDto>> CrearAsync(GuardarProveedorRequest r, CancellationToken ct)
    {
        var codigo = r.Codigo.Trim().ToUpperInvariant();
        if (await db.Proveedores.AnyAsync(p => p.Codigo == codigo, ct))
        {
            return ErroresCompras.CodigoDuplicado;
        }

        var proveedor = new Proveedor { Codigo = codigo };
        Aplicar(proveedor, r);
        db.Proveedores.Add(proveedor);
        await db.SaveChangesAsync(ct);
        return ADto(proveedor);
    }

    public async Task<Result<ProveedorDto>> ActualizarAsync(Guid id, GuardarProveedorRequest r, CancellationToken ct)
    {
        var proveedor = await db.Proveedores.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (proveedor is null)
        {
            return ErroresCompras.ProveedorNoEncontrado;
        }

        var codigo = r.Codigo.Trim().ToUpperInvariant();
        if (codigo != proveedor.Codigo && await db.Proveedores.AnyAsync(p => p.Codigo == codigo, ct))
        {
            return ErroresCompras.CodigoDuplicado;
        }

        proveedor.Codigo = codigo;
        Aplicar(proveedor, r);
        await db.SaveChangesAsync(ct);
        return ADto(proveedor);
    }

    public async Task<Result<ProveedorDto>> CambiarActivoAsync(Guid id, bool activo, CancellationToken ct)
    {
        var proveedor = await db.Proveedores.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (proveedor is null)
        {
            return ErroresCompras.ProveedorNoEncontrado;
        }

        proveedor.Activo = activo;
        if (!activo)
        {
            // RN-COM-11: desactivar un proveedor le quita sus preferencias.
            await db.ProveedoresProductos.Where(pp => pp.ProveedorId == id && pp.Preferido)
                .ExecuteUpdateAsync(s => s.SetProperty(pp => pp.Preferido, false), ct);
        }

        await db.SaveChangesAsync(ct);
        return ADto(proveedor);
    }

    /// <summary>Último precio por producto (RN-COM-08) e historial de recepciones del proveedor.</summary>
    public async Task<Result<IReadOnlyList<PrecioProveedorDto>>> PreciosAsync(Guid id, Guid? productoId, CancellationToken ct)
    {
        if (!await db.Proveedores.AnyAsync(p => p.Id == id, ct))
        {
            return ErroresCompras.ProveedorNoEncontrado;
        }

        var ultimos = await db.ProveedoresProductos.AsNoTracking()
            .Where(pp => pp.ProveedorId == id && (productoId == null || pp.ProductoId == productoId)).ToListAsync(ct);
        var historial = await (from r in db.Recepciones.AsNoTracking()
                               from p in r.Partidas
                               where r.ProveedorId == id && r.Estado == EstadoRecepcion.Registrada
                                     && (productoId == null || p.ProductoId == productoId)
                               orderby r.FechaOperativa descending, r.Folio descending
                               select new { r.FechaOperativa, r.Folio, p.ProductoId, p.UnidadId, p.Cantidad, p.PrecioUnitario, p.CostoUnitarioBase })
            .Take(500).ToListAsync(ct);
        var productos = await catalogos.ObtenerProductosAsync(ultimos.Select(u => u.ProductoId), ct);
        var unidades = await catalogos.ObtenerUnidadesAsync(ct);
        return ultimos.Select(u =>
            {
                var producto = productos.GetValueOrDefault(u.ProductoId);
                return new PrecioProveedorDto(u.ProductoId, producto?.Sku ?? "?", producto?.Nombre ?? "?", u.UltimaUnidadId ?? u.UnidadId,
                    unidades.GetValueOrDefault(u.UltimaUnidadId ?? u.UnidadId)?.Clave ?? "?", u.UltimoPrecio, u.UltimaCompraEn,
                    historial.Where(h => h.ProductoId == u.ProductoId).Select(h => new HistorialPrecioDto(h.FechaOperativa, h.Folio,
                        unidades.GetValueOrDefault(h.UnidadId)?.Clave ?? "?", h.Cantidad, h.PrecioUnitario, h.CostoUnitarioBase)).ToList());
            })
            .OrderBy(p => p.Producto).ToList();
    }

    private static void Aplicar(Proveedor p, GuardarProveedorRequest r)
    {
        p.RazonSocial = r.RazonSocial.Trim();
        p.NombreComercial = Limpiar(r.NombreComercial);
        p.Rfc = Limpiar(r.Rfc)?.ToUpperInvariant();
        p.Contacto = Limpiar(r.Contacto);
        p.Telefono = Limpiar(r.Telefono);
        p.Email = Limpiar(r.Email);
        p.DiasCredito = r.DiasCredito;
    }

    private static string? Limpiar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    internal static ProveedorDto ADto(Proveedor p) => ADto(p, 0);

    internal static ProveedorDto ADto(Proveedor p, int productos) =>
        new(p.Id, p.Codigo, p.RazonSocial, p.NombreComercial, p.Rfc, p.Contacto, p.Telefono, p.Email, p.DiasCredito, p.Activo, productos);
}
