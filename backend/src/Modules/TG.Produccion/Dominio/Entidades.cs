using System.Globalization;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;

namespace TG.Produccion.Dominio;

public enum TipoLinea
{
    Molino,
    Tortilladora,
    Freidora,
    Empaque,
    Otro,
}

public enum EstadoReceta
{
    Borrador,
    Vigente,
    Obsoleta,
}

/// <summary>Turnos de producción <c>[SUP-05]</c>.</summary>
public enum Turno
{
    Matutino,
    Vespertino,
    Nocturno,
}

public enum EstadoOrden
{
    Planeada,
    Liberada,
    EnProceso,
    Terminada,
    Cancelada,
}

/// <summary>Línea de producción (molino, tortilladora…) <c>[SUP-05]</c>.</summary>
public sealed class Linea : EntidadAuditable
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public TipoLinea Tipo { get; set; }
    public bool Activo { get; set; } = true;
}

public sealed class Receta : EntidadAuditable, IConcurrencia
{
    public Guid ProductoId { get; set; }

    /// <summary>Número de versión de la receta (columna <c>version</c>); no confundir con <see cref="Version"/> (xmin).</summary>
    public int NumeroVersion { get; set; } = 1;

    public string Nombre { get; set; } = string.Empty;
    public decimal RendimientoCantidad { get; set; }
    public decimal MermaEsperadaPct { get; set; }
    public EstadoReceta Estado { get; set; } = EstadoReceta.Borrador;
    public DateTimeOffset? VigenteDesde { get; set; }
    public string? Notas { get; set; }
    public uint Version { get; set; }
    public List<RecetaComponente> Componentes { get; set; } = [];

    /// <summary>RN-PRO-01: una receta Vigente u Obsoleta no se edita; se crea nueva versión.</summary>
    public bool Editable => Estado == EstadoReceta.Borrador;
}

public sealed class RecetaComponente
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid RecetaId { get; set; }
    public Guid ProductoId { get; set; }
    public Guid UnidadId { get; set; }
    public decimal FactorABase { get; set; }
    public decimal Cantidad { get; set; }
    public decimal CantidadBase { get; set; }
    public short Orden { get; set; }

    /// <summary>RN-PRO-11: almacén del que sale por omisión; nulo = el almacén de consumo de la orden.</summary>
    public Guid? AlmacenId { get; set; }
}

public sealed class OrdenProduccion : EntidadAuditable, IConcurrencia
{
    public string Folio { get; set; } = string.Empty;
    public DateOnly FechaOperativa { get; set; }
    public Turno Turno { get; set; }
    public Guid RecetaId { get; set; }
    public Guid ProductoId { get; set; }
    public Guid? LineaId { get; set; }
    public decimal CantidadPlaneada { get; set; }
    public decimal? CantidadProducida { get; set; }
    public Guid AlmacenConsumoId { get; set; }
    public Guid AlmacenDestinoId { get; set; }

    /// <summary>Se genera al liberar (RN-PRO-10); vacío mientras está Planeada.</summary>
    public string Lote { get; set; } = string.Empty;

    public EstadoOrden Estado { get; set; } = EstadoOrden.Planeada;
    public DateTimeOffset? LiberadaEn { get; set; }
    public DateTimeOffset? IniciadaEn { get; set; }
    public DateTimeOffset? TerminadaEn { get; set; }
    public Guid? ResponsableId { get; set; }
    public decimal? CostoTotal { get; set; }
    public decimal? CostoUnitario { get; set; }
    public string? Notas { get; set; }
    public uint Version { get; set; }
    public List<OrdenConsumo> Consumos { get; set; } = [];
    public List<OrdenMerma> Mermas { get; set; } = [];
}

public sealed class OrdenConsumo
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid OrdenProduccionId { get; set; }
    public Guid ProductoId { get; set; }

    /// <summary>RN-PRO-11: de dónde sale el componente; se toma de la receta al liberar y se puede cambiar.</summary>
    public Guid AlmacenId { get; set; }

    /// <summary>Teórico calculado al liberar con la cantidad planeada (RN-PRO-02).</summary>
    public decimal CantidadTeoricaBase { get; set; }

    /// <summary>Capturado por el operador; nulo = no se capturó (RN-PRO-03).</summary>
    public decimal? CantidadRealBase { get; set; }

    public string? LoteConsumido { get; set; }
    public decimal CostoUnitario { get; set; }
}

public sealed class OrdenMerma
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid OrdenProduccionId { get; set; }
    public Guid ProductoId { get; set; }
    public decimal CantidadBase { get; set; }
    public Guid MotivoMermaId { get; set; }
    public string? Notas { get; set; }
    public DateTimeOffset RegistradaEn { get; set; }
}

/// <summary>Fórmulas puras de producción.</summary>
public static class ReglasProduccion
{
    /// <summary>RN-PRO-02: <c>componente.cantidad_base × cantidad / receta.rendimiento_cantidad</c>.</summary>
    public static decimal ConsumoTeorico(decimal componenteBase, decimal cantidad, decimal rendimiento) =>
        rendimiento <= 0 ? 0 : Dinero.RedondearCantidad(componenteBase * cantidad / rendimiento);

    /// <summary>RN-PRO-03: sin consumo real se usa el teórico recalculado con la cantidad producida.</summary>
    public static decimal ConsumoReal(decimal? capturado, decimal teoricoPlaneado, decimal planeada, decimal producida) =>
        capturado ?? (planeada <= 0 ? 0 : Dinero.RedondearCantidad(teoricoPlaneado * producida / planeada));

    /// <summary>RN-PRO-05: costo de consumos entre la cantidad producida (sin indirectos, [SUP-06]).</summary>
    public static decimal CostoUnitario(decimal costoConsumos, decimal producida) =>
        producida <= 0 ? 0 : Dinero.RedondearCosto(costoConsumos / producida);

    /// <summary>RN-PRO-08: <c>(real − teórico) / teórico</c>; nulo si el teórico es cero.</summary>
    public static decimal? Variacion(decimal teorico, decimal real) =>
        teorico == 0 ? null : Math.Round((real - teorico) / teorico, 6, MidpointRounding.AwayFromZero);

    public static bool FueraDeUmbral(decimal? variacion, decimal umbral) => variacion is { } v && Math.Abs(v) > umbral;

    /// <summary>RN-PRO-10: <c>AAMMDD-&lt;folio sin guion&gt;</c>.</summary>
    public static string Lote(DateOnly fecha, string folio) =>
        fecha.ToString("yyMMdd", CultureInfo.InvariantCulture) + "-" + folio.Replace("-", string.Empty, StringComparison.Ordinal);

    /// <summary>RN-PRO-10: caducidad = fecha operativa + vida útil.</summary>
    public static DateOnly? Caducidad(DateOnly fecha, int? vidaUtilDias) => vidaUtilDias is { } d ? fecha.AddDays(d) : null;
}
