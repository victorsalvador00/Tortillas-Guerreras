using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TG.Inventario.Publico;
using TG.Logistica.Dominio;
using TG.Logistica.Infraestructura;
using TG.Logistica.Publico;
using TG.Seguridad.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Resultados;
using TG.Ventas.Publico;

namespace TG.Logistica.Aplicacion;

// ───────────────────────────── Vehículos ─────────────────────────────

public sealed record VehiculoDto(Guid Id, string Placas, string Descripcion, decimal? CapacidadKg, bool Activo);

public sealed record GuardarVehiculoRequest(string Placas, string Descripcion, decimal? CapacidadKg, bool Activo);

public sealed class GuardarVehiculoValidator : AbstractValidator<GuardarVehiculoRequest>
{
    public GuardarVehiculoValidator()
    {
        RuleFor(x => x.Placas).NotEmpty().MaximumLength(15).WithMessage("Captura las placas.");
        RuleFor(x => x.Descripcion).NotEmpty().WithMessage("Describe el vehículo.");
        RuleFor(x => x.CapacidadKg).GreaterThan(0).When(x => x.CapacidadKg is not null);
    }
}

public sealed class VehiculosHandler(LogisticaDbContext db)
{
    public async Task<IReadOnlyList<VehiculoDto>> ListarAsync(bool? activo, CancellationToken ct)
    {
        var consulta = db.Vehiculos.AsNoTracking();
        if (activo is not null)
        {
            consulta = consulta.Where(v => v.Activo == activo);
        }

        return await consulta.OrderBy(v => v.Placas)
            .Select(v => new VehiculoDto(v.Id, v.Placas, v.Descripcion, v.CapacidadKg, v.Activo)).ToListAsync(ct);
    }

    public async Task<Result<VehiculoDto>> CrearAsync(GuardarVehiculoRequest r, CancellationToken ct)
    {
        var placas = r.Placas.Trim().ToUpperInvariant();
        if (await db.Vehiculos.AnyAsync(v => v.Placas == placas, ct))
        {
            return Error.Campo(ErroresLogistica.CodigoDuplicado, "placas", "Ya existe un vehículo con esas placas.");
        }

        var vehiculo = new Vehiculo { Placas = placas, Descripcion = r.Descripcion.Trim(), CapacidadKg = r.CapacidadKg, Activo = r.Activo };
        db.Vehiculos.Add(vehiculo);
        await db.SaveChangesAsync(ct);
        return ADto(vehiculo);
    }

    public async Task<Result<VehiculoDto>> ActualizarAsync(Guid id, GuardarVehiculoRequest r, CancellationToken ct)
    {
        var vehiculo = await db.Vehiculos.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vehiculo is null)
        {
            return Error.NoEncontrado(ErroresLogistica.VehiculoNoEncontrado, "El vehículo no existe.");
        }

        var placas = r.Placas.Trim().ToUpperInvariant();
        if (placas != vehiculo.Placas && await db.Vehiculos.AnyAsync(v => v.Placas == placas, ct))
        {
            return Error.Campo(ErroresLogistica.CodigoDuplicado, "placas", "Ya existe un vehículo con esas placas.");
        }

        vehiculo.Placas = placas;
        vehiculo.Descripcion = r.Descripcion.Trim();
        vehiculo.CapacidadKg = r.CapacidadKg;
        vehiculo.Activo = r.Activo;
        await db.SaveChangesAsync(ct);
        return ADto(vehiculo);
    }

    private static VehiculoDto ADto(Vehiculo v) => new(v.Id, v.Placas, v.Descripcion, v.CapacidadKg, v.Activo);
}

// ───────────────────────────── Rutas ─────────────────────────────

public sealed record RutaDto(
    Guid Id,
    string Codigo,
    string Nombre,
    Guid AlmacenId,
    string AlmacenCodigo,
    Guid? VendedorId,
    string? VendedorNombre,
    Guid? VehiculoId,
    string? VehiculoPlacas,
    Guid? ClienteGenericoId,
    int Clientes,
    bool Activo);

public sealed record GuardarRutaRequest(string Codigo, string Nombre, Guid? VendedorId, Guid? VehiculoId, bool Activo);

public sealed partial class GuardarRutaValidator : AbstractValidator<GuardarRutaRequest>
{
    public GuardarRutaValidator()
    {
        // El código va en folios de la app (<ruta>-<dispositivo>-<consecutivo>): solo letras y números.
        RuleFor(x => x.Codigo).NotEmpty().MaximumLength(8).Must(c => CodigoRegex().IsMatch(c?.Trim() ?? string.Empty))
            .WithMessage("El código lleva de 1 a 8 letras o números, sin espacios (R01).");
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("Captura el nombre de la ruta.");
    }

    [GeneratedRegex("^[A-Za-z0-9]{1,8}$")]
    private static partial Regex CodigoRegex();
}

public sealed record ClienteRutaDto(Guid ClienteId, string Codigo, string Nombre, short OrdenVisita, IReadOnlyList<short> DiasVisita,
    bool EsGenerico, decimal? Latitud, decimal? Longitud);

public sealed record ClienteRutaRequest(Guid ClienteId, short OrdenVisita, IReadOnlyList<short>? DiasVisita);

public sealed record ClientesRutaRequest(IReadOnlyList<ClienteRutaRequest> Clientes);

public sealed class ClientesRutaValidator : AbstractValidator<ClientesRutaRequest>
{
    public ClientesRutaValidator()
    {
        RuleFor(x => x.Clientes).NotNull();
        RuleFor(x => x.Clientes).Must(c => c is null || c.Select(x => x.ClienteId).Distinct().Count() == c.Count)
            .WithMessage("Hay clientes repetidos.");
        RuleForEach(x => x.Clientes).ChildRules(c =>
        {
            c.RuleFor(x => x.ClienteId).NotEmpty();
            c.RuleFor(x => x.OrdenVisita).GreaterThanOrEqualTo((short)0);
            c.RuleFor(x => x.DiasVisita).Must(d => d is null || ReglasLogistica.DiasValidos(d))
                .WithMessage("Los días de visita van de 1 (lunes) a 7 (domingo).");
        });
    }
}

/// <summary>Rutas con su almacén (RN-LOG-01), cliente genérico (RN-VEN-10) y clientes en orden de visita.</summary>
public sealed class RutasHandler(
    LogisticaDbContext db,
    IInventarioService inventario,
    IVentasService ventas,
    ISeguridadService seguridad,
    IUnitOfWork unitOfWork)
{
    public const string RolVendedor = "Vendedor";

    public async Task<IReadOnlyList<RutaDto>> ListarAsync(bool? activo, CancellationToken ct)
    {
        var consulta = db.Rutas.AsNoTracking();
        if (activo is not null)
        {
            consulta = consulta.Where(r => r.Activo == activo);
        }

        return await MapearAsync(await consulta.OrderBy(r => r.Codigo).ToListAsync(ct), ct);
    }

    public async Task<Result<RutaDto>> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var ruta = await db.Rutas.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return ruta is null ? Error.NoEncontrado(ErroresLogistica.RutaNoEncontrada, "La ruta no existe.") : (await MapearAsync([ruta], ct))[0];
    }

    public async Task<Result<RutaDto>> CrearAsync(GuardarRutaRequest r, CancellationToken ct)
    {
        var codigo = r.Codigo.Trim().ToUpperInvariant();
        if (await db.Rutas.AnyAsync(x => x.Codigo == codigo, ct))
        {
            return Error.Campo(ErroresLogistica.CodigoDuplicado, "codigo", "Ya existe una ruta con ese código.");
        }

        var validacion = await ValidarAsignacionAsync(r, ct);
        if (!validacion.EsExito)
        {
            return validacion.Error!;
        }

        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var ruta = new Ruta
            {
                Codigo = codigo,
                Nombre = r.Nombre.Trim(),
                VendedorId = r.VendedorId,
                VehiculoId = r.VehiculoId,
                Activo = r.Activo,
            };
            var almacen = await inventario.CrearAlmacenRutaAsync(ruta.Id, codigo, $"Ruta {codigo} · {ruta.Nombre}", c);
            if (!almacen.EsExito)
            {
                return Result<Guid>.Falla(almacen.Error!);
            }

            ruta.AlmacenId = almacen.Valor.Id;
            var generico = await ventas.CrearClienteGenericoAsync(codigo, c);
            if (!generico.EsExito)
            {
                return Result<Guid>.Falla(generico.Error!);
            }

            db.Rutas.Add(ruta);
            db.RutasClientes.Add(new RutaCliente
            {
                RutaId = ruta.Id, ClienteId = generico.Valor.Id, OrdenVisita = 0, DiasVisita = [1, 2, 3, 4, 5, 6, 7],
            });
            await db.SaveChangesAsync(c);
            return Result<Guid>.Exito(ruta.Id);
        }, ct);

        return resultado.EsExito ? await ObtenerAsync(resultado.Valor, ct) : resultado.Error!;
    }

    public async Task<Result<RutaDto>> ActualizarAsync(Guid id, GuardarRutaRequest r, CancellationToken ct)
    {
        var ruta = await db.Rutas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ruta is null)
        {
            return Error.NoEncontrado(ErroresLogistica.RutaNoEncontrada, "La ruta no existe.");
        }

        if (!string.Equals(r.Codigo.Trim(), ruta.Codigo, StringComparison.OrdinalIgnoreCase))
        {
            // El código está en folios, en el almacén y en el cliente genérico: no se cambia.
            return Error.Campo(ErroresLogistica.CodigoDuplicado, "codigo", "El código de una ruta no se puede cambiar.");
        }

        var validacion = await ValidarAsignacionAsync(r, ct);
        if (!validacion.EsExito)
        {
            return validacion.Error!;
        }

        ruta.Nombre = r.Nombre.Trim();
        ruta.VendedorId = r.VendedorId;
        ruta.VehiculoId = r.VehiculoId;
        ruta.Activo = r.Activo;
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    public async Task<Result<IReadOnlyList<ClienteRutaDto>>> ClientesAsync(Guid rutaId, CancellationToken ct)
    {
        if (!await db.Rutas.AnyAsync(r => r.Id == rutaId, ct))
        {
            return Error.NoEncontrado(ErroresLogistica.RutaNoEncontrada, "La ruta no existe.");
        }

        var asignados = await db.RutasClientes.AsNoTracking().Where(rc => rc.RutaId == rutaId && rc.Activo)
            .OrderBy(rc => rc.OrdenVisita).ToListAsync(ct);
        var clientes = await ventas.ObtenerClientesAsync(asignados.Select(a => a.ClienteId), ct);
        return asignados.Where(a => clientes.ContainsKey(a.ClienteId)).Select(a =>
        {
            var c = clientes[a.ClienteId];
            return new ClienteRutaDto(a.ClienteId, c.Codigo, c.Nombre, a.OrdenVisita, a.DiasVisita, c.EsGenerico, c.Latitud, c.Longitud);
        }).ToList();
    }

    /// <summary>Reemplaza la lista de clientes de la ruta; los que salen quedan inactivos (la app los borra).</summary>
    public async Task<Result<IReadOnlyList<ClienteRutaDto>>> ReemplazarClientesAsync(Guid rutaId, ClientesRutaRequest r, CancellationToken ct)
    {
        var ruta = await db.Rutas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == rutaId, ct);
        if (ruta is null)
        {
            return Error.NoEncontrado(ErroresLogistica.RutaNoEncontrada, "La ruta no existe.");
        }

        var clientes = await ventas.ObtenerClientesAsync(r.Clientes.Select(c => c.ClienteId), ct);
        var faltante = r.Clientes.FirstOrDefault(c => !clientes.TryGetValue(c.ClienteId, out var info) || !info.Activo);
        if (faltante is not null)
        {
            return Error.Campo(ErroresLogistica.ClienteInvalido, "clientes", "Uno de los clientes no existe o está inactivo.");
        }

        var genericoAjeno = r.Clientes.Select(c => clientes[c.ClienteId])
            .FirstOrDefault(c => c.EsGenerico && c.Codigo != $"GEN-{ruta.Codigo}");
        if (genericoAjeno is not null)
        {
            return Error.Campo(ErroresLogistica.ClienteInvalido, "clientes", $"{genericoAjeno.Nombre} es el genérico de otra ruta.");
        }

        var actuales = await db.RutasClientes.Where(rc => rc.RutaId == rutaId).ToListAsync(ct);
        var generico = await ventas.ClienteGenericoDeRutaAsync(ruta.Codigo, ct);
        foreach (var actual in actuales)
        {
            var nuevo = r.Clientes.FirstOrDefault(c => c.ClienteId == actual.ClienteId);
            if (nuevo is null)
            {
                // El genérico siempre queda en la ruta (RN-VEN-10).
                if (actual.ClienteId != generico?.Id && actual.Activo)
                {
                    actual.Activo = false;
                }

                continue;
            }

            actual.Activo = true;
            actual.OrdenVisita = nuevo.OrdenVisita;
            actual.DiasVisita = (nuevo.DiasVisita ?? [1, 2, 3, 4, 5, 6]).Distinct().Order().ToArray();
            db.Entry(actual).State = EntityState.Modified;
        }

        foreach (var nuevo in r.Clientes.Where(c => actuales.All(a => a.ClienteId != c.ClienteId)))
        {
            db.RutasClientes.Add(new RutaCliente
            {
                RutaId = rutaId,
                ClienteId = nuevo.ClienteId,
                OrdenVisita = nuevo.OrdenVisita,
                DiasVisita = (nuevo.DiasVisita ?? [1, 2, 3, 4, 5, 6]).Distinct().Order().ToArray(),
            });
        }

        await db.SaveChangesAsync(ct);
        return await ClientesAsync(rutaId, ct);
    }

    private async Task<Result> ValidarAsignacionAsync(GuardarRutaRequest r, CancellationToken ct)
    {
        if (r.VendedorId is { } vendedor && !await seguridad.UsuarioTieneRolAsync(vendedor, RolVendedor, ct))
        {
            return Error.Campo(ErroresLogistica.VendedorInvalido, "vendedorId", "El usuario no existe o no tiene el rol Vendedor.");
        }

        if (r.VehiculoId is { } vehiculo && !await db.Vehiculos.AnyAsync(v => v.Id == vehiculo, ct))
        {
            return Error.Campo(ErroresLogistica.VehiculoNoEncontrado, "vehiculoId", "El vehículo no existe.");
        }

        return Result.Exito();
    }

    private async Task<IReadOnlyList<RutaDto>> MapearAsync(List<Ruta> rutas, CancellationToken ct)
    {
        var vendedores = await seguridad.NombresUsuariosAsync(rutas.Where(r => r.VendedorId != null).Select(r => r.VendedorId!.Value), ct);
        var almacenes = (await inventario.ListarAlmacenesAsync(ct)).ToDictionary(a => a.Id, a => a.Codigo);
        var vehiculos = await db.Vehiculos.AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.Placas, ct);
        var ids = rutas.Select(r => r.Id).ToList();
        var conteo = await db.RutasClientes.AsNoTracking().Where(rc => ids.Contains(rc.RutaId) && rc.Activo)
            .GroupBy(rc => rc.RutaId).Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var resultado = new List<RutaDto>(rutas.Count);
        foreach (var r in rutas)
        {
            var generico = await ventas.ClienteGenericoDeRutaAsync(r.Codigo, ct);
            resultado.Add(new RutaDto(r.Id, r.Codigo, r.Nombre, r.AlmacenId, almacenes.GetValueOrDefault(r.AlmacenId) ?? "?",
                r.VendedorId, r.VendedorId is { } v ? vendedores.GetValueOrDefault(v) : null, r.VehiculoId,
                r.VehiculoId is { } h ? vehiculos.GetValueOrDefault(h) : null, generico?.Id, conteo.GetValueOrDefault(r.Id), r.Activo));
        }

        return resultado;
    }
}
