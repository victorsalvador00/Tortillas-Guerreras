using TG.SharedKernel.Datos;
using TG.SharedKernel.Dominio;

namespace TG.Compras.Dominio;

public sealed class Proveedor : EntidadAuditable
{
    public string Codigo { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string? NombreComercial { get; set; }
    public string? Rfc { get; set; }
    public string? Contacto { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public int DiasCredito { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Último precio por proveedor y producto (RN-COM-08).</summary>
public sealed class ProveedorProducto
{
    public Guid ProveedorId { get; set; }
    public Guid ProductoId { get; set; }
    public Guid UnidadId { get; set; }
    public decimal UltimoPrecio { get; set; }
    public DateTimeOffset? UltimaCompraEn { get; set; }
}

public enum EstadoOrdenCompra
{
    Borrador,
    Autorizada,
    RecibidaParcial,
    Recibida,
    Cerrada,
    Cancelada,
}

public sealed class OrdenCompra : EntidadAuditable, IConcurrencia
{
    public string Folio { get; set; } = string.Empty;
    public Guid ProveedorId { get; set; }
    public DateOnly Fecha { get; set; }
    public DateOnly? FechaEntregaEsperada { get; set; }
    public Guid AlmacenDestinoId { get; set; }
    public EstadoOrdenCompra Estado { get; set; } = EstadoOrdenCompra.Borrador;
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    public string? Notas { get; set; }
    public Guid? AutorizadaPor { get; set; }
    public DateTimeOffset? AutorizadaEn { get; set; }
    public uint Version { get; set; }
    public List<OrdenCompraPartida> Partidas { get; set; } = [];

    public void RecalcularTotales()
    {
        Subtotal = Partidas.Sum(p => p.Importe);
        Iva = Partidas.Sum(p => p.Iva);
        Total = Subtotal + Iva;
    }
}

public sealed class OrdenCompraPartida
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid OrdenCompraId { get; set; }
    public Guid ProductoId { get; set; }
    public Guid UnidadId { get; set; }
    public decimal FactorABase { get; set; }
    public decimal Cantidad { get; set; }
    public decimal CantidadBase { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal TasaIva { get; set; }
    public decimal Importe { get; set; }
    public decimal Iva { get; set; }
    public decimal CantidadRecibidaBase { get; set; }
}

public enum EstadoRecepcion
{
    Registrada,
    Cancelada,
}

public sealed class Recepcion : EntidadAuditable
{
    public string Folio { get; set; } = string.Empty;
    public Guid? OrdenCompraId { get; set; }
    public Guid ProveedorId { get; set; }
    public Guid AlmacenId { get; set; }
    public DateOnly FechaOperativa { get; set; }
    public string? FacturaProveedor { get; set; }
    public EstadoRecepcion Estado { get; set; } = EstadoRecepcion.Registrada;
    public string? Notas { get; set; }
    public DateTimeOffset? CanceladaEn { get; set; }
    public Guid? CanceladaPor { get; set; }
    public List<RecepcionPartida> Partidas { get; set; } = [];
}

public sealed class RecepcionPartida
{
    public Guid Id { get; set; } = SharedKernel.Dominio.Id.Nuevo();
    public Guid RecepcionId { get; set; }
    public Guid? OrdenCompraPartidaId { get; set; }
    public Guid ProductoId { get; set; }
    public Guid UnidadId { get; set; }
    public decimal FactorABase { get; set; }
    public decimal Cantidad { get; set; }
    public decimal CantidadBase { get; set; }

    /// <summary>Precio sin IVA en la unidad capturada; base del historial de precios.</summary>
    public decimal PrecioUnitario { get; set; }

    public decimal CostoUnitarioBase { get; set; }
    public string? Lote { get; set; }
    public DateOnly? FechaCaducidad { get; set; }
}

/// <summary>Reglas puras de compras.</summary>
public static class ReglasCompras
{
    /// <summary>Importe e IVA por partida, redondeados a 2 decimales (02-convenciones).</summary>
    public static (decimal Importe, decimal Iva) Importes(decimal cantidad, decimal precio, decimal tasaIva)
    {
        var importe = Dinero.Redondear(cantidad * precio);
        return (importe, Dinero.Redondear(importe * tasaIva));
    }

    /// <summary>RN-COM-05: costo por unidad base sin IVA = precio de la partida / factor.</summary>
    public static decimal CostoUnitarioBase(decimal precio, decimal factor) => Dinero.RedondearCosto(precio / factor);

    /// <summary>RN-COM-04: se puede recibir de más hasta la tolerancia; arriba se rechaza.</summary>
    public static bool ExcedeTolerancia(decimal pedidaBase, decimal recibidaBase, decimal nuevaBase, decimal tolerancia) =>
        recibidaBase + nuevaBase > Dinero.RedondearCantidad(pedidaBase * (1 + tolerancia));

    public static EstadoOrdenCompra EstadoPorRecepciones(IEnumerable<OrdenCompraPartida> partidas)
    {
        var lista = partidas.ToList();
        if (lista.Count > 0 && lista.All(p => p.CantidadRecibidaBase >= p.CantidadBase))
        {
            return EstadoOrdenCompra.Recibida;
        }

        return lista.Any(p => p.CantidadRecibidaBase > 0) ? EstadoOrdenCompra.RecibidaParcial : EstadoOrdenCompra.Autorizada;
    }
}
