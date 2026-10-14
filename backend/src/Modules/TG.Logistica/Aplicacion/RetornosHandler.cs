using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TG.Catalogos.Publico;
using TG.Inventario.Publico;
using TG.Logistica.Dominio;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.SharedKernel.Api;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Folios;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Logistica.Aplicacion;

/// <summary>
/// Retornos de ruta compartidos web/app. <c>DevolucionCliente</c>: <c>RetornoCliente</c> (+) en el almacén de la ruta y,
/// si la partida es no vendible, <c>Merma</c> (−) con su motivo (RN-LOG-04). <c>RechazoEntrega</c>: sin movimientos
/// (RN-LOG-05). Idempotente por <c>op_id</c>.
/// </summary>
public sealed class ServicioRetornos(
    LogisticaDbContext db,
    IInventarioService inventario,
    ICatalogosService catalogos,
    IVentasService ventas,
    IIncidenciasService incidencias,
    IGeneradorFolios folios,
    IUnitOfWork unitOfWork,
    TimeProvider reloj)
{
    public const string LoteSinIdentificar = "SIN-LOTE";

    public async Task<Result<RetornoRegistrado>> RegistrarAsync(RegistrarRetornoComando c, CancellationToken ct)
    {
        var previo = await BuscarPrevioAsync(c, ct);
        if (previo is not null)
        {
            return previo;
        }

        if (c.Partidas.Count == 0 || c.Partidas.Any(p => p.Cantidad <= 0))
        {
            return Error.Validacion("VAL-001", "El retorno requiere partidas con cantidad mayor que cero.");
        }

        var ruta = await db.Rutas.AsNoTracking().FirstOrDefaultAsync(r => r.Id == c.RutaId, ct);
        if (ruta is null)
        {
            return Error.NoEncontrado(ErroresLogistica.RutaNoEncontrada, "La ruta no existe.");
        }

        if (c.ClienteId is { } clienteId && !(await ventas.ObtenerClientesAsync([clienteId], ct)).ContainsKey(clienteId))
        {
            return Error.Campo(ErroresLogistica.ClienteInvalido, "clienteId", "El cliente no existe.");
        }

        var venta = c.VentaId is { } ventaId ? await ventas.ObtenerVentaAsync(ventaId, ct) : null;
        var productos = await catalogos.ObtenerProductosAsync(c.Partidas.Select(p => p.ProductoId), ct);
        var motivoDevolucion = await catalogos.ObtenerMotivoPorClaveAsync(ClavesMotivoMerma.DevolucionNoVendible, ct);

        var partidas = new List<RetornoPartida>();
        foreach (var p in c.Partidas)
        {
            if (!productos.TryGetValue(p.ProductoId, out var producto))
            {
                return Error.Campo(ErroresLogistica.ProductoInvalido, "partidas", "Uno de los productos no existe.");
            }

            var cantidad = p.Cantidad;
            if (p.UnidadId is { } unidad)
            {
                var conversion = await catalogos.ConvertirABaseAsync(p.ProductoId, unidad, p.Cantidad, null, ct);
                if (!conversion.EsExito)
                {
                    return conversion.Error!;
                }

                cantidad = conversion.Valor.Base;
            }

            var condicion = c.Tipo == TipoRetorno.RechazoEntrega
                ? CondicionRetorno.Vendible
                : p.Condicion ?? ReglasLogistica.CondicionPredeterminada(producto.Tipo, producto.VidaUtilDias);
            Guid? motivoId = null;
            if (condicion == CondicionRetorno.NoVendible)
            {
                var motivo = p.MotivoMermaId is { } m ? await catalogos.ObtenerMotivoAsync(m, ct) : motivoDevolucion;
                if (motivo is null || !motivo.Activo || !motivo.AplicaRetorno)
                {
                    return Error.Campo(ErroresLogistica.MotivoInvalido, "motivoMermaId", "El motivo no existe o no aplica a retornos.");
                }

                motivoId = motivo.Id;
            }

            var lote = producto.ManejaLote ? await ResolverLoteAsync(p.Lote, p.ProductoId, ruta, venta, ct) : string.Empty;
            partidas.Add(new RetornoPartida
            {
                ProductoId = p.ProductoId, Lote = lote, CantidadBase = cantidad, Condicion = condicion, MotivoMermaId = motivoId,
            });
        }

        try
        {
            return await unitOfWork.EjecutarAsync(async t =>
            {
                var retorno = new Retorno
                {
                    Id = c.RetornoId ?? SharedKernel.Dominio.Id.Nuevo(),
                    Folio = string.IsNullOrWhiteSpace(c.Folio)
                        ? await folios.SiguienteAsync(LogisticaDbContext.Esquema, "RET", t)
                        : c.Folio.Trim().ToUpperInvariant(),
                    OpId = c.OpId,
                    Tipo = c.Tipo,
                    RutaId = c.RutaId,
                    ClienteId = c.ClienteId,
                    VentaId = c.VentaId,
                    FechaOperativa = c.FechaOperativa,
                    FechaHoraDispositivo = c.FechaHoraDispositivo,
                    DispositivoId = c.DispositivoId,
                    Latitud = c.Latitud,
                    Longitud = c.Longitud,
                    PrecisionM = c.PrecisionM,
                    Notas = string.IsNullOrWhiteSpace(c.Notas) ? null : c.Notas.Trim(),
                };
                partidas.ForEach(p => p.RetornoId = retorno.Id);
                retorno.Partidas = partidas;

                if (c.Tipo == TipoRetorno.DevolucionCliente)
                {
                    var lineas = new List<LineaMovimiento>();
                    lineas.AddRange(partidas.Select(p => new LineaMovimiento(TipoMovimiento.RetornoCliente, ruta.AlmacenId, p.ProductoId,
                        p.Lote, p.CantidadBase)));
                    lineas.AddRange(partidas.Where(p => p.Condicion == CondicionRetorno.NoVendible).Select(p => new LineaMovimiento(
                        TipoMovimiento.Merma, ruta.AlmacenId, p.ProductoId, p.Lote, -p.CantidadBase, MotivoMermaId: p.MotivoMermaId,
                        Notas: "Devolución no vendible")));
                    var movimientos = await inventario.RegistrarMovimientosAsync(new RegistrarMovimientosRequest(
                        DocumentoInventario.Retorno, retorno.Id, retorno.Folio, c.FechaOperativa, c.UsuarioId, lineas, c.DispositivoId,
                        retorno.Notas), t);
                    if (!movimientos.EsExito)
                    {
                        return Result<RetornoRegistrado>.Falla(movimientos.Error!);
                    }
                }

                db.Retornos.Add(retorno);
                await db.SaveChangesAsync(t);

                // RN-SYN-06: si la liquidación del día ya se cerró, se aplica igual y se avisa.
                if (await db.Liquidaciones.AnyAsync(l => l.RutaId == c.RutaId && l.FechaOperativa == c.FechaOperativa
                        && l.Estado == EstadoLiquidacion.Cerrada, t))
                {
                    await incidencias.CrearAsync(TipoIncidencia.DiferenciaLiquidacion, "Retorno", retorno.Id,
                        new { motivo = "Retorno registrado después de cerrar la liquidación", retorno.Folio, ruta = ruta.Codigo }, t);
                }

                return Result<RetornoRegistrado>.Exito(new RetornoRegistrado(retorno.Id, retorno.Folio, false));
            }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            // Carrera con un reenvío del mismo op_id: se devuelve el original.
            var otro = await BuscarPrevioAsync(c, ct);
            return otro ?? Error.Conflicto(ErroresLogistica.FolioDuplicado, "El folio del retorno ya existe.");
        }
    }

    private async Task<Result<RetornoRegistrado>?> BuscarPrevioAsync(RegistrarRetornoComando c, CancellationToken ct)
    {
        Retorno? previo = null;
        if (c.OpId is { } opId)
        {
            previo = await db.Retornos.AsNoTracking().FirstOrDefaultAsync(r => r.OpId == opId, ct);
        }

        if (previo is null && c.RetornoId is { } retornoId)
        {
            previo = await db.Retornos.AsNoTracking().FirstOrDefaultAsync(r => r.Id == retornoId, ct);
        }

        if (previo is not null)
        {
            return Result<RetornoRegistrado>.Exito(new RetornoRegistrado(previo.Id, previo.Folio, true));
        }

        if (!string.IsNullOrWhiteSpace(c.Folio))
        {
            var folio = c.Folio.Trim().ToUpperInvariant();
            if (await db.Retornos.AsNoTracking().AnyAsync(r => r.Folio == folio, ct))
            {
                return Error.Conflicto(ErroresLogistica.FolioDuplicado, $"El folio {folio} ya existe con otra operación.");
            }
        }

        return null;
    }

    /// <summary>Lote de la devolución: el indicado, el de la venta original o el más reciente que llevó la camioneta.</summary>
    private async Task<string> ResolverLoteAsync(string? indicado, Guid productoId, Ruta ruta, VentaInfo? venta, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(indicado))
        {
            return indicado.Trim().ToUpperInvariant();
        }

        var deVenta = venta?.Partidas.FirstOrDefault(p => p.ProductoId == productoId && p.Lote.Length > 0)?.Lote;
        if (deVenta is not null)
        {
            return deVenta.Split(',')[0].Trim();
        }

        var existencias = await inventario.ExistenciasDeAlmacenAsync(ruta.AlmacenId, ct);
        var enCamioneta = existencias.Where(e => e.ProductoId == productoId && e.Lote.Length > 0)
            .OrderByDescending(e => e.FechaCaducidad).ThenByDescending(e => e.Lote, StringComparer.Ordinal).FirstOrDefault();
        if (enCamioneta is not null)
        {
            return enCamioneta.Lote;
        }

        var limite = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime).AddDays(-7);
        var cargado = await db.Envios.AsNoTracking().Where(e => e.RutaId == ruta.Id && e.FechaOperativa >= limite)
            .OrderByDescending(e => e.FechaOperativa)
            .SelectMany(e => e.Partidas).Where(p => p.ProductoId == productoId && p.Lote != "")
            .Select(p => p.Lote).FirstOrDefaultAsync(ct);
        return cargado ?? LoteSinIdentificar;
    }
}

// ───────────────────────────── Web ─────────────────────────────

public sealed record PartidaRetornoRequest(Guid ProductoId, decimal Cantidad, Guid? UnidadId, string? Lote, CondicionRetorno? Condicion,
    Guid? MotivoMermaId);

public sealed record RegistrarRetornoRequest(
    TipoRetorno Tipo,
    Guid RutaId,
    DateOnly? FechaOperativa,
    Guid? ClienteId,
    Guid? VentaId,
    string? Notas,
    IReadOnlyList<PartidaRetornoRequest> Partidas);

public sealed class RegistrarRetornoValidator : AbstractValidator<RegistrarRetornoRequest>
{
    public RegistrarRetornoValidator()
    {
        RuleFor(x => x.RutaId).NotEmpty().WithMessage("Elige la ruta.");
        RuleFor(x => x.Partidas).NotEmpty().WithMessage("Agrega al menos un producto.");
        RuleForEach(x => x.Partidas).ChildRules(p =>
        {
            p.RuleFor(x => x.ProductoId).NotEmpty();
            p.RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad debe ser mayor que cero.");
        });
    }
}

public sealed record RetornoPartidaDto(Guid Id, Guid ProductoId, string Sku, string Producto, string Unidad, string Lote, decimal CantidadBase,
    CondicionRetorno Condicion, Guid? MotivoMermaId, string? Motivo);

public sealed record RetornoDto(
    Guid Id,
    string Folio,
    TipoRetorno Tipo,
    Guid RutaId,
    string RutaCodigo,
    Guid? ClienteId,
    string? Cliente,
    Guid? VentaId,
    DateOnly FechaOperativa,
    DateTimeOffset? FechaHoraDispositivo,
    Guid? DispositivoId,
    decimal? Latitud,
    decimal? Longitud,
    EstadoRetorno Estado,
    string? Notas,
    DateTimeOffset CreadoEn,
    IReadOnlyList<RetornoPartidaDto> Partidas);

public sealed class RetornosHandler(
    LogisticaDbContext db,
    ServicioRetornos servicio,
    ICatalogosService catalogos,
    IVentasService ventas,
    IFechaOperativa fechaOperativa,
    IUsuarioActual usuario)
{
    public async Task<Result<RetornoDto>> RegistrarAsync(RegistrarRetornoRequest r, CancellationToken ct)
    {
        var resultado = await servicio.RegistrarAsync(new RegistrarRetornoComando(r.Tipo, r.RutaId, r.FechaOperativa ?? fechaOperativa.Hoy(),
            usuario.Id ?? Guid.Empty,
            r.Partidas.Select(p => new PartidaRetornoComando(p.ProductoId, p.Cantidad, p.UnidadId, p.Lote, p.Condicion, p.MotivoMermaId)).ToList(),
            ClienteId: r.ClienteId, VentaId: r.VentaId, Notas: r.Notas), ct);
        return resultado.EsExito ? await ObtenerAsync(resultado.Valor.RetornoId, ct) : resultado.Error!;
    }

    public async Task<Paginado<RetornoDto>> ListarAsync(DateOnly? desde, DateOnly? hasta, Guid? rutaId, TipoRetorno? tipo, int? pagina,
        int? tamano, CancellationToken ct)
    {
        var consulta = db.Retornos.AsNoTracking().Include(r => r.Partidas).AsQueryable();
        if (desde is not null)
        {
            consulta = consulta.Where(r => r.FechaOperativa >= desde);
        }

        if (hasta is not null)
        {
            consulta = consulta.Where(r => r.FechaOperativa <= hasta);
        }

        if (rutaId is not null)
        {
            consulta = consulta.Where(r => r.RutaId == rutaId);
        }

        if (tipo is not null)
        {
            consulta = consulta.Where(r => r.Tipo == tipo);
        }

        var paginado = await consulta.OrderByDescending(r => r.CreadoEn).PaginarAsync(pagina, tamano, ct);
        var mapeados = await MapearAsync(paginado.Items.ToList(), ct);
        return new Paginado<RetornoDto>(mapeados, paginado.Total, paginado.Pagina, paginado.Tamano);
    }

    public async Task<Result<RetornoDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var retorno = await db.Retornos.AsNoTracking().Include(r => r.Partidas).FirstOrDefaultAsync(r => r.Id == id, ct);
        return retorno is null
            ? Error.NoEncontrado(ErroresLogistica.RetornoNoEncontrado, "El retorno no existe.")
            : (await MapearAsync([retorno], ct))[0];
    }

    private async Task<IReadOnlyList<RetornoDto>> MapearAsync(List<Retorno> retornos, CancellationToken ct)
    {
        var productos = await catalogos.ObtenerProductosAsync(retornos.SelectMany(r => r.Partidas).Select(p => p.ProductoId), ct);
        var clientes = await ventas.ObtenerClientesAsync(retornos.Where(r => r.ClienteId != null).Select(r => r.ClienteId!.Value), ct);
        var motivos = (await catalogos.ListarMotivosAsync(ct)).ToDictionary(m => m.Id, m => m.Nombre);
        var rutas = await db.Rutas.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Codigo, ct);
        return retornos.Select(r => new RetornoDto(r.Id, r.Folio, r.Tipo, r.RutaId, rutas.GetValueOrDefault(r.RutaId) ?? "?", r.ClienteId,
            r.ClienteId is { } c ? clientes.GetValueOrDefault(c)?.Nombre : null, r.VentaId, r.FechaOperativa, r.FechaHoraDispositivo,
            r.DispositivoId, r.Latitud, r.Longitud, r.Estado, r.Notas, r.CreadoEn,
            r.Partidas.Select(p =>
            {
                var producto = productos.GetValueOrDefault(p.ProductoId);
                return new RetornoPartidaDto(p.Id, p.ProductoId, producto?.Sku ?? "?", producto?.Nombre ?? "?",
                    producto?.UnidadBaseClave ?? string.Empty, p.Lote, p.CantidadBase, p.Condicion, p.MotivoMermaId,
                    p.MotivoMermaId is { } m ? motivos.GetValueOrDefault(m) : null);
            }).ToList())).ToList();
    }
}
