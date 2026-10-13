using TG.Inventario.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;

namespace TG.Inventario.Dominio;

public sealed class Almacen : EntidadAuditable
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public TipoAlmacen Tipo { get; set; }
    public Guid? RutaId { get; set; }
    public bool PermiteNegativo { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Saldo por almacén, producto y lote. Solo lo modifica el servicio de kardex.</summary>
public sealed class Existencia : IConcurrencia
{
    public Guid AlmacenId { get; set; }
    public Guid ProductoId { get; set; }
    public string Lote { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }

    /// <summary>Para FEFO (RN-INV-07); se copia en traspasos.</summary>
    public DateOnly? FechaCaducidad { get; set; }

    /// <summary>Fecha de la primera entrada del lote al almacén: FIFO cuando no hay caducidad.</summary>
    public DateTimeOffset FechaEntrada { get; set; }

    public DateTimeOffset ActualizadoEn { get; set; }
    public uint Version { get; set; }
}

public sealed class CostoProducto
{
    public Guid ProductoId { get; set; }
    public decimal CostoPromedio { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }
}

/// <summary>Kardex inmutable (RN-INV-01): una corrección es un contra-movimiento.</summary>
public sealed class Movimiento
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public DateTimeOffset FechaHora { get; set; }
    public DateOnly FechaOperativa { get; set; }
    public TipoMovimiento Tipo { get; set; }
    public Guid AlmacenId { get; set; }
    public Guid ProductoId { get; set; }
    public string Lote { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal CostoTotal { get; set; }
    public decimal ExistenciaResultante { get; set; }
    public DocumentoInventario DocumentoTipo { get; set; }
    public Guid DocumentoId { get; set; }
    public string DocumentoFolio { get; set; } = string.Empty;
    public Guid? MotivoMermaId { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid? DispositivoId { get; set; }
    public string? Notas { get; set; }
    public DateOnly? FechaCaducidad { get; set; }
}

public enum EstadoTraspaso
{
    Registrado,
    Cancelado,
}

public sealed class Traspaso : EntidadAuditable
{
    public string Folio { get; set; } = string.Empty;
    public Guid AlmacenOrigenId { get; set; }
    public Guid AlmacenDestinoId { get; set; }
    public DateOnly FechaOperativa { get; set; }
    public EstadoTraspaso Estado { get; set; } = EstadoTraspaso.Registrado;
    public string? Notas { get; set; }
    public List<TraspasoPartida> Partidas { get; set; } = [];
}

public sealed class TraspasoPartida
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid TraspasoId { get; set; }
    public Guid ProductoId { get; set; }
    public string Lote { get; set; } = string.Empty;
    public decimal CantidadBase { get; set; }
}

public enum EstadoConteo
{
    Abierto,
    Cerrado,
    Cancelado,
}

public sealed class ConteoFisico : EntidadAuditable, IConcurrencia
{
    public string Folio { get; set; } = string.Empty;
    public Guid AlmacenId { get; set; }
    public DateOnly FechaOperativa { get; set; }
    public EstadoConteo Estado { get; set; } = EstadoConteo.Abierto;
    public string? Notas { get; set; }
    public DateTimeOffset? CerradoEn { get; set; }
    public Guid? CerradoPor { get; set; }
    public uint Version { get; set; }
    public List<ConteoPartida> Partidas { get; set; } = [];
}

public sealed class ConteoPartida
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid ConteoId { get; set; }
    public Guid ProductoId { get; set; }
    public string Lote { get; set; } = string.Empty;
    public decimal CantidadSistema { get; set; }
    public decimal CantidadContada { get; set; }
    public decimal Diferencia { get; set; }
    public DateTimeOffset CapturadoEn { get; set; }
}

public static class ReglasInventario
{
    public static bool EsEntrada(TipoMovimiento tipo) => tipo is TipoMovimiento.EntradaCompra or TipoMovimiento.EntradaProduccion
        or TipoMovimiento.TraspasoEntrada or TipoMovimiento.RetornoCliente or TipoMovimiento.AjusteEntrada;

    /// <summary>Entradas cuyo costo recalcula el promedio ponderado (RN-INV-05).</summary>
    public static bool RecalculaCosto(TipoMovimiento tipo, decimal? costo) =>
        tipo is TipoMovimiento.EntradaCompra or TipoMovimiento.EntradaProduccion
        || (tipo == TipoMovimiento.AjusteEntrada && costo is not null);

    /// <summary>
    /// RN-INV-05: <c>nuevo = (exist_total × costo_actual + cant × costo_entrada) / (exist_total + cant)</c>
    /// [SUP-06]. Si la existencia previa es cero o negativa (posible en rutas), el costo de la entrada manda.
    /// </summary>
    public static decimal CostoPromedioPonderado(decimal existenciaTotal, decimal costoActual, decimal cantidad, decimal costoEntrada)
    {
        if (existenciaTotal <= 0 || existenciaTotal + cantidad <= 0)
        {
            return Dinero.RedondearCosto(costoEntrada);
        }

        return Dinero.RedondearCosto(((existenciaTotal * costoActual) + (cantidad * costoEntrada)) / (existenciaTotal + cantidad));
    }

    /// <summary>RN-INV-07: primero el que caduca antes; sin caducidad, FIFO por fecha de entrada del lote.</summary>
    public static IEnumerable<Existencia> OrdenFefo(IEnumerable<Existencia> lotes) =>
        lotes.OrderBy(e => e.FechaCaducidad is null ? 1 : 0)
            .ThenBy(e => e.FechaCaducidad)
            .ThenBy(e => e.FechaEntrada)
            .ThenBy(e => e.Lote, StringComparer.Ordinal);
}
