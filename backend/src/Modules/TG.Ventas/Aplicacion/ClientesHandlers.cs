using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Catalogos.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.Ventas.Dominio;
using TG.Ventas.Infraestructura;
using TG.Ventas.Publico;

namespace TG.Ventas.Aplicacion;

// ───────────────────────────── Canales ─────────────────────────────

public sealed record CanalDto(Guid Id, string Clave, string Nombre, bool RequiereFacturaDefault,
    FormatoLiquidacion FormatoLiquidacion, bool AplicaProductoACobro, PedidoPor PedidoPor, short Orden, bool Activo);

public sealed record GuardarCanalRequest(string Clave, string Nombre, bool RequiereFacturaDefault, bool Activo,
    FormatoLiquidacion FormatoLiquidacion = FormatoLiquidacion.Completo, bool AplicaProductoACobro = false,
    PedidoPor PedidoPor = PedidoPor.Cliente, short Orden = 0);

public sealed class GuardarCanalValidator : AbstractValidator<GuardarCanalRequest>
{
    public GuardarCanalValidator()
    {
        RuleFor(x => x.Clave).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Nombre).NotEmpty();
        RuleFor(x => x.FormatoLiquidacion).IsInEnum();
        RuleFor(x => x.PedidoPor).IsInEnum();
        RuleFor(x => x.Orden).GreaterThanOrEqualTo((short)0);
    }
}

public sealed class CanalesHandler(VentasDbContext db)
{
    public async Task<IReadOnlyList<CanalDto>> ListarAsync(CancellationToken ct) =>
        await db.Canales.AsNoTracking().OrderBy(c => c.Orden).ThenBy(c => c.Clave)
            .Select(c => new CanalDto(c.Id, c.Clave, c.Nombre, c.RequiereFacturaDefault, c.FormatoLiquidacion,
                c.AplicaProductoACobro, c.PedidoPor, c.Orden, c.Activo)).ToListAsync(ct);

    public async Task<Result<CanalDto>> GuardarAsync(Guid? id, GuardarCanalRequest r, CancellationToken ct)
    {
        var clave = r.Clave.Trim().ToUpperInvariant();
        if (await db.Canales.AnyAsync(c => c.Clave == clave && c.Id != id, ct))
        {
            return Error.Campo(ErroresVentas.Duplicado, "clave", "Ya existe un canal con esa clave.");
        }

        Canal? canal;
        if (id is null)
        {
            canal = new Canal();
            db.Canales.Add(canal);
        }
        else
        {
            canal = await db.Canales.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (canal is null)
            {
                return Error.NoEncontrado(ErroresVentas.NoEncontrado, "El canal no existe.");
            }
        }

        canal.Clave = clave;
        canal.Nombre = r.Nombre.Trim();
        canal.RequiereFacturaDefault = r.RequiereFacturaDefault;
        canal.FormatoLiquidacion = r.FormatoLiquidacion;
        canal.AplicaProductoACobro = r.AplicaProductoACobro;
        canal.PedidoPor = r.PedidoPor;
        canal.Orden = r.Orden;
        canal.Activo = r.Activo;
        await db.SaveChangesAsync(ct);
        return new CanalDto(canal.Id, canal.Clave, canal.Nombre, canal.RequiereFacturaDefault, canal.FormatoLiquidacion,
            canal.AplicaProductoACobro, canal.PedidoPor, canal.Orden, canal.Activo);
    }
}

// ───────────────────────────── Listas de precio ─────────────────────────────

public sealed record ListaPrecioPartidaDto(Guid ProductoId, string Sku, string Producto, Guid UnidadId, string Unidad,
    decimal PrecioUnitario);

public sealed record ListaPrecioDto(Guid Id, string Nombre, Guid? CanalId, DateOnly VigenteDesde, bool Activo, int TotalPartidas,
    IReadOnlyList<ListaPrecioPartidaDto> Partidas);

public sealed record GuardarListaPrecioRequest(string Nombre, Guid? CanalId, DateOnly VigenteDesde, bool Activo);

public sealed record PartidaListaRequest(Guid ProductoId, Guid UnidadId, decimal PrecioUnitario);

public sealed record PartidasListaRequest(IReadOnlyList<PartidaListaRequest> Partidas);

public sealed class GuardarListaPrecioValidator : AbstractValidator<GuardarListaPrecioRequest>
{
    public GuardarListaPrecioValidator() => RuleFor(x => x.Nombre).NotEmpty();
}

public sealed class PartidasListaValidator : AbstractValidator<PartidasListaRequest>
{
    public PartidasListaValidator()
    {
        RuleForEach(x => x.Partidas).ChildRules(p =>
            p.RuleFor(x => x.PrecioUnitario).GreaterThanOrEqualTo(0).WithMessage("El precio no puede ser negativo."));
        RuleFor(x => x.Partidas).Must(p => p.Select(x => (x.ProductoId, x.UnidadId)).Distinct().Count() == p.Count)
            .WithMessage("Hay productos con la misma unidad repetidos.");
    }
}

public sealed class ListasPrecioHandler(VentasDbContext db, ICatalogosService catalogos)
{
    public async Task<IReadOnlyList<ListaPrecioDto>> ListarAsync(CancellationToken ct)
    {
        var listas = await db.ListasPrecio.AsNoTracking().Include(l => l.Partidas).OrderBy(l => l.Nombre).ToListAsync(ct);
        return listas.Select(l => new ListaPrecioDto(l.Id, l.Nombre, l.CanalId, l.VigenteDesde, l.Activo, l.Partidas.Count, [])).ToList();
    }

    public async Task<Result<ListaPrecioDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var lista = await db.ListasPrecio.AsNoTracking().Include(l => l.Partidas).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lista is null)
        {
            return Error.NoEncontrado(ErroresVentas.NoEncontrado, "La lista de precios no existe.");
        }

        var productos = await catalogos.ObtenerProductosAsync(lista.Partidas.Select(p => p.ProductoId), ct);
        var unidades = await catalogos.ObtenerUnidadesAsync(ct);
        return new ListaPrecioDto(lista.Id, lista.Nombre, lista.CanalId, lista.VigenteDesde, lista.Activo, lista.Partidas.Count,
            lista.Partidas.Select(p => new ListaPrecioPartidaDto(p.ProductoId, productos.GetValueOrDefault(p.ProductoId)?.Sku ?? "?",
                    productos.GetValueOrDefault(p.ProductoId)?.Nombre ?? "?", p.UnidadId,
                    unidades.GetValueOrDefault(p.UnidadId)?.Clave ?? "?", p.PrecioUnitario))
                .OrderBy(p => p.Sku).ToList());
    }

    public async Task<Result<ListaPrecioDto>> GuardarAsync(Guid? id, GuardarListaPrecioRequest r, CancellationToken ct)
    {
        ListaPrecio? lista;
        if (id is null)
        {
            lista = new ListaPrecio();
            db.ListasPrecio.Add(lista);
        }
        else
        {
            lista = await db.ListasPrecio.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (lista is null)
            {
                return Error.NoEncontrado(ErroresVentas.NoEncontrado, "La lista de precios no existe.");
            }
        }

        lista.Nombre = r.Nombre.Trim();
        lista.CanalId = r.CanalId;
        lista.VigenteDesde = r.VigenteDesde;
        lista.Activo = r.Activo;
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(lista.Id, ct);
    }

    /// <summary>Reemplaza las partidas; cada cambio de precio sube su <c>version_sync</c> para la app.</summary>
    public async Task<Result<ListaPrecioDto>> GuardarPartidasAsync(Guid id, PartidasListaRequest r, CancellationToken ct)
    {
        var lista = await db.ListasPrecio.Include(l => l.Partidas).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lista is null)
        {
            return Error.NoEncontrado(ErroresVentas.NoEncontrado, "La lista de precios no existe.");
        }

        foreach (var p in r.Partidas)
        {
            // RN-CAT-03: la unidad debe tener conversión de venta.
            var conversion = await catalogos.ConvertirABaseAsync(p.ProductoId, p.UnidadId, 1, UsoUnidad.Venta, ct);
            if (!conversion.EsExito)
            {
                return conversion.Error!;
            }
        }

        var borrar = lista.Partidas.Where(x => !r.Partidas.Any(p => p.ProductoId == x.ProductoId && p.UnidadId == x.UnidadId)).ToList();
        db.ListasPrecioPartidas.RemoveRange(borrar);
        foreach (var p in r.Partidas)
        {
            var existente = lista.Partidas.FirstOrDefault(x => x.ProductoId == p.ProductoId && x.UnidadId == p.UnidadId);
            if (existente is null)
            {
                db.ListasPrecioPartidas.Add(new ListaPrecioPartida
                {
                    ListaPrecioId = id, ProductoId = p.ProductoId, UnidadId = p.UnidadId, PrecioUnitario = p.PrecioUnitario,
                });
            }
            else if (existente.PrecioUnitario != p.PrecioUnitario)
            {
                existente.PrecioUnitario = p.PrecioUnitario;
            }
        }

        db.Entry(lista).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }
}

// ───────────────────────────── Clientes ─────────────────────────────

public sealed record ClienteDto(
    Guid Id,
    string Codigo,
    string Nombre,
    Guid CanalId,
    string Canal,
    Guid ListaPrecioId,
    string ListaPrecio,
    CondicionPago CondicionPago,
    int DiasCredito,
    decimal? LimiteCredito,
    bool RequiereFactura,
    string? Rfc,
    string? RazonSocial,
    string? RegimenFiscal,
    string? CodigoPostalFiscal,
    string? UsoCfdi,
    string? EmailFacturacion,
    string? Telefono,
    string? Calle,
    string? Colonia,
    string? Municipio,
    string? Estado,
    string? CodigoPostal,
    decimal? Latitud,
    decimal? Longitud,
    bool UbicacionAprobada,
    decimal? LatitudPropuesta,
    decimal? LongitudPropuesta,
    bool EsGenerico,
    bool Activo,
    Guid? GrupoClienteId,
    string? Grupo);

/// <param name="ListaPrecioId">Opcional si el cliente es de un grupo con lista: la hereda (RN-VEN-12).</param>
public sealed record GuardarClienteRequest(
    string? Codigo,
    string Nombre,
    Guid CanalId,
    Guid? ListaPrecioId,
    CondicionPago CondicionPago,
    int DiasCredito,
    decimal? LimiteCredito,
    bool RequiereFactura,
    string? Rfc,
    string? RazonSocial,
    string? RegimenFiscal,
    string? CodigoPostalFiscal,
    string? UsoCfdi,
    string? EmailFacturacion,
    string? Telefono,
    string? Calle,
    string? Colonia,
    string? Municipio,
    string? Estado,
    string? CodigoPostal,
    decimal? Latitud,
    decimal? Longitud,
    bool Activo,
    Guid? GrupoClienteId = null);

public sealed partial class GuardarClienteValidator : AbstractValidator<GuardarClienteRequest>
{
    public GuardarClienteValidator()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Captura el nombre del cliente.");
        RuleFor(x => x.CanalId).NotEmpty().WithMessage("Elige el canal.");
        RuleFor(x => x.ListaPrecioId).NotEmpty().When(x => x.GrupoClienteId is null).WithMessage("Elige la lista de precios.");
        RuleFor(x => x.DiasCredito).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LimiteCredito).GreaterThanOrEqualTo(0).When(x => x.LimiteCredito is not null);
        RuleFor(x => x.Rfc).Must(r => RfcRegex().IsMatch(r!.Trim().ToUpperInvariant()))
            .When(x => !string.IsNullOrWhiteSpace(x.Rfc)).WithMessage("El RFC no tiene un formato válido.");
        RuleFor(x => x.Rfc).NotEmpty().When(x => x.RequiereFactura).WithMessage("Captura el RFC para facturar.");
        RuleFor(x => x.RazonSocial).NotEmpty().When(x => x.RequiereFactura).WithMessage("Captura la razón social para facturar.");
        RuleFor(x => x.CodigoPostalFiscal).Matches("^[0-9]{5}$").When(x => !string.IsNullOrWhiteSpace(x.CodigoPostalFiscal))
            .WithMessage("El código postal fiscal debe tener 5 dígitos.");
        RuleFor(x => x.EmailFacturacion).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.EmailFacturacion));
        RuleFor(x => x.Latitud).InclusiveBetween(-90, 90).When(x => x.Latitud is not null);
        RuleFor(x => x.Longitud).InclusiveBetween(-180, 180).When(x => x.Longitud is not null);
    }

    [GeneratedRegex("^[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}$")]
    private static partial Regex RfcRegex();
}

public sealed record SaldoClienteDto(Guid ClienteId, decimal Saldo, decimal? LimiteCredito, decimal? Disponible);

public sealed record AprobarUbicacionRequest(decimal? Latitud, decimal? Longitud);

public sealed class ClientesHandler(VentasDbContext db, IGeneradorFolios folios, IUsuarioActual usuario, TimeProvider reloj,
    VentasService ventas)
{
    public async Task<Paginado<ClienteDto>> ListarAsync(string? buscar, Guid? canalId, bool? activo, bool? ubicacionPorAprobar,
        bool? esGenerico, int? pagina, int? tamano, CancellationToken ct)
    {
        var consulta = db.Clientes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var patron = $"%{buscar.Trim()}%";
            consulta = consulta.Where(c => EF.Functions.ILike(c.Nombre, patron) || EF.Functions.ILike(c.Codigo, patron)
                || (c.Rfc != null && EF.Functions.ILike(c.Rfc, patron)));
        }

        if (canalId is not null)
        {
            consulta = consulta.Where(c => c.CanalId == canalId);
        }

        if (activo is not null)
        {
            consulta = consulta.Where(c => c.Activo == activo);
        }

        if (ubicacionPorAprobar == true)
        {
            consulta = consulta.Where(c => c.LatitudPropuesta != null);
        }

        if (esGenerico is not null)
        {
            consulta = consulta.Where(c => c.EsGenerico == esGenerico);
        }

        var paginado = await consulta.OrderBy(c => c.Nombre).PaginarAsync(pagina, tamano, ct);
        var (canales, listas, grupos) = await NombresAsync(ct);
        return paginado.Map(c => ADto(c, canales, listas, grupos));
    }

    public async Task<Result<ClienteDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cliente is null)
        {
            return Error.NoEncontrado(ErroresVentas.ClienteNoEncontrado, "El cliente no existe.");
        }

        var (canales, listas, grupos) = await NombresAsync(ct);
        return ADto(cliente, canales, listas, grupos);
    }

    public async Task<Result<ClienteDto>> GuardarAsync(Guid? id, GuardarClienteRequest r, CancellationToken ct)
    {
        GrupoCliente? grupo = null;
        if (r.GrupoClienteId is { } grupoId)
        {
            grupo = await db.GruposClientes.AsNoTracking().FirstOrDefaultAsync(g => g.Id == grupoId, ct);
            if (grupo is null)
            {
                return Error.Campo(ErroresVentas.NoEncontrado, "grupoClienteId", "El grupo no existe.");
            }
        }

        // RN-VEN-12: sin lista indicada, la del grupo.
        var listaId = r.ListaPrecioId is { } l && l != Guid.Empty ? l : grupo?.ListaPrecioId;
        if (listaId is null)
        {
            return Error.Campo(ErroresVentas.NoEncontrado, "listaPrecioId", "Elige la lista de precios; el grupo no tiene una.");
        }

        if (!await db.Canales.AnyAsync(c => c.Id == r.CanalId, ct) || !await db.ListasPrecio.AnyAsync(x => x.Id == listaId, ct))
        {
            return Error.Validacion(ErroresVentas.NoEncontrado, "El canal o la lista de precios no existen.");
        }

        Cliente? cliente;
        if (id is null)
        {
            var codigo = string.IsNullOrWhiteSpace(r.Codigo)
                ? await folios.SiguienteAsync(VentasDbContext.Esquema, "CLI", ct)
                : r.Codigo.Trim().ToUpperInvariant();
            if (await db.Clientes.AnyAsync(c => c.Codigo == codigo, ct))
            {
                return Error.Campo(ErroresVentas.Duplicado, "codigo", "Ya existe un cliente con ese código.");
            }

            cliente = new Cliente { Codigo = codigo };
            db.Clientes.Add(cliente);
        }
        else
        {
            cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (cliente is null)
            {
                return Error.NoEncontrado(ErroresVentas.ClienteNoEncontrado, "El cliente no existe.");
            }

            if (!string.IsNullOrWhiteSpace(r.Codigo))
            {
                var codigo = r.Codigo.Trim().ToUpperInvariant();
                if (codigo != cliente.Codigo && await db.Clientes.AnyAsync(c => c.Codigo == codigo, ct))
                {
                    return Error.Campo(ErroresVentas.Duplicado, "codigo", "Ya existe un cliente con ese código.");
                }

                cliente.Codigo = codigo;
            }
        }

        static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        cliente.Nombre = r.Nombre.Trim();
        cliente.CanalId = r.CanalId;
        cliente.GrupoClienteId = r.GrupoClienteId;
        cliente.ListaPrecioId = listaId.Value;
        cliente.CondicionPago = r.CondicionPago;
        cliente.DiasCredito = r.CondicionPago == CondicionPago.Credito ? r.DiasCredito : 0;
        cliente.LimiteCredito = r.CondicionPago == CondicionPago.Credito ? r.LimiteCredito : null;
        cliente.RequiereFactura = r.RequiereFactura;
        cliente.Rfc = Limpio(r.Rfc)?.ToUpperInvariant();
        cliente.RazonSocial = Limpio(r.RazonSocial)?.ToUpperInvariant();
        cliente.RegimenFiscal = Limpio(r.RegimenFiscal);
        cliente.CodigoPostalFiscal = Limpio(r.CodigoPostalFiscal);
        cliente.UsoCfdi = Limpio(r.UsoCfdi);
        cliente.EmailFacturacion = Limpio(r.EmailFacturacion);
        cliente.Telefono = Limpio(r.Telefono);
        cliente.Calle = Limpio(r.Calle);
        cliente.Colonia = Limpio(r.Colonia);
        cliente.Municipio = Limpio(r.Municipio);
        cliente.Estado = Limpio(r.Estado);
        cliente.CodigoPostal = Limpio(r.CodigoPostal);
        if (r.Latitud != cliente.Latitud || r.Longitud != cliente.Longitud)
        {
            // Capturada en oficina: queda aprobada.
            cliente.Latitud = r.Latitud;
            cliente.Longitud = r.Longitud;
            cliente.UbicacionAprobada = r.Latitud is not null && r.Longitud is not null;
        }

        cliente.Activo = r.Activo;
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(cliente.Id, ct);
    }

    /// <summary>RN-VEN-06: aprueba la ubicación propuesta (o la indicada) y cierra sus incidencias.</summary>
    public async Task<Result<ClienteDto>> AprobarUbicacionAsync(Guid id, AprobarUbicacionRequest r, CancellationToken ct)
    {
        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cliente is null)
        {
            return Error.NoEncontrado(ErroresVentas.ClienteNoEncontrado, "El cliente no existe.");
        }

        var lat = r.Latitud ?? cliente.LatitudPropuesta;
        var lng = r.Longitud ?? cliente.LongitudPropuesta;
        if (lat is null || lng is null)
        {
            return Error.Validacion(ErroresVentas.NoEncontrado, "El cliente no tiene una ubicación por aprobar.");
        }

        cliente.Latitud = lat;
        cliente.Longitud = lng;
        cliente.UbicacionAprobada = true;
        cliente.LatitudPropuesta = null;
        cliente.LongitudPropuesta = null;
        var ahora = reloj.GetUtcNow();
        var abiertas = await db.Incidencias.Where(i => i.EntidadTipo == "Cliente" && i.EntidadId == id
            && i.Tipo == TipoIncidencia.UbicacionPorAprobar && i.Estado == EstadoIncidencia.Abierta).ToListAsync(ct);
        foreach (var incidencia in abiertas)
        {
            incidencia.Estado = EstadoIncidencia.Revisada;
            incidencia.RevisadaEn = ahora;
            incidencia.RevisadaPor = usuario.Id;
            incidencia.NotasRevision = "Ubicación aprobada";
        }

        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    public async Task<Result<SaldoClienteDto>> SaldoAsync(Guid id, CancellationToken ct)
    {
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cliente is null)
        {
            return Error.NoEncontrado(ErroresVentas.ClienteNoEncontrado, "El cliente no existe.");
        }

        var saldo = await ventas.SaldoClienteAsync(id, ct);
        return new SaldoClienteDto(id, saldo, cliente.LimiteCredito, cliente.LimiteCredito is { } l ? l - saldo : null);
    }

    private async Task<(Dictionary<Guid, string> Canales, Dictionary<Guid, string> Listas, Dictionary<Guid, string> Grupos)> NombresAsync(
        CancellationToken ct) =>
        (await db.Canales.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre, ct),
            await db.ListasPrecio.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Nombre, ct),
            await db.GruposClientes.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Nombre, ct));

    private static ClienteDto ADto(Cliente c, Dictionary<Guid, string> canales, Dictionary<Guid, string> listas,
        Dictionary<Guid, string> grupos) => new(
        c.Id, c.Codigo, c.Nombre, c.CanalId, canales.GetValueOrDefault(c.CanalId) ?? "?", c.ListaPrecioId,
        listas.GetValueOrDefault(c.ListaPrecioId) ?? "?", c.CondicionPago, c.DiasCredito, c.LimiteCredito, c.RequiereFactura,
        c.Rfc, c.RazonSocial, c.RegimenFiscal, c.CodigoPostalFiscal, c.UsoCfdi, c.EmailFacturacion, c.Telefono, c.Calle,
        c.Colonia, c.Municipio, c.Estado, c.CodigoPostal, c.Latitud, c.Longitud, c.UbicacionAprobada, c.LatitudPropuesta,
        c.LongitudPropuesta, c.EsGenerico, c.Activo, c.GrupoClienteId,
        c.GrupoClienteId is { } g ? grupos.GetValueOrDefault(g) : null);
}
