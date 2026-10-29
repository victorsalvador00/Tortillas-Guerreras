using System.Data;
using Dapper;
using TG.Catalogos.Publico;
using TG.Produccion.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Seguridad;
using TG.SharedKernel.Tiempo;

namespace TG.Api.Infraestructura;

// RN-WEB-01 (07-web): lo que espera al usuario, para los globos del menú. Conteos de solo lectura entre esquemas con
// Dapper, en el host, como el tablero; el plan sugerido se cuenta con la misma consulta de su pantalla.

/// <summary>
/// Cuántos documentos esperan al usuario, contados como la lista que abre el menú. Cada conteo llega nulo sin su permiso.
/// </summary>
/// <param name="OrdenesCompraPorAutorizar">OC en <c>Borrador</c> (compras.autorizar).</param>
/// <param name="OrdenesCompraPorRecibir">OC <c>Autorizada</c> o <c>RecibidaParcial</c> (compras.recibir).</param>
/// <param name="PlanPorProgramar">Productos del plan sugerido de mañana con faltante por producir (produccion.planear).</param>
/// <param name="OrdenesPorLiberar">Órdenes de producción <c>Planeada</c> (produccion.planear).</param>
/// <param name="OrdenesPorProducir">Órdenes de producción <c>Liberada</c> (produccion.operar).</param>
/// <param name="OrdenesEnProceso">Órdenes de producción <c>EnProceso</c> (produccion.cerrar).</param>
/// <param name="LiquidacionesPorConfirmar">Liquidaciones <c>Abierta</c>: falta confirmar el producto (logistica.liquidar_producto).</param>
/// <param name="LiquidacionesPorRevisar">
/// Liquidaciones <c>ProductoConfirmado</c> cuando se requiere revisión del supervisor (logistica.revisar_liquidacion).
/// </param>
/// <param name="LiquidacionesPorCerrar">
/// Liquidaciones <c>Revisada</c>, o <c>ProductoConfirmado</c> si no se requiere revisión (logistica.liquidar, RN-LOG-20).
/// </param>
/// <param name="PedidosPorValidar">Pedidos de la app en <c>Capturado</c>, la bandeja "Por validar" (ventas.pedidos, RN-VEN-18).</param>
/// <param name="IncidenciasAbiertas">Incidencias <c>Abierta</c> (ventas.supervisar).</param>
/// <param name="ClientesPorAprobar">Clientes con ubicación propuesta por aprobar (ventas.clientes).</param>
/// <param name="SolicitudesAutofactura">Solicitudes de autofactura <c>Recibida</c> o con <c>Error</c> (facturacion.emitir).</param>
public sealed record PendientesDto(
    int? OrdenesCompraPorAutorizar,
    int? OrdenesCompraPorRecibir,
    int? PlanPorProgramar,
    int? OrdenesPorLiberar,
    int? OrdenesPorProducir,
    int? OrdenesEnProceso,
    int? LiquidacionesPorConfirmar,
    int? LiquidacionesPorRevisar,
    int? LiquidacionesPorCerrar,
    int? PedidosPorValidar,
    int? IncidenciasAbiertas,
    int? ClientesPorAprobar,
    int? SolicitudesAutofactura);

internal sealed class ConsultasPendientes(
    IUnitOfWork unitOfWork,
    IUsuarioActual usuario,
    IPendientesProduccion produccion,
    ICatalogosService catalogos,
    IFechaOperativa fechaOperativa)
{
    private IDbConnection Conexion => unitOfWork.Conexion;

    public async Task<PendientesDto> ObtenerAsync(CancellationToken ct)
    {
        var requiereRevision = await catalogos.ObtenerParametroAsync(ClavesParametro.RequiereRevisionSupervisor, true, ct);
        return new PendientesDto(
            await Contar(Permisos.Compras.Autorizar, "select count(*) from compras.ordenes_compra where estado = 'Borrador'", ct),
            await Contar(Permisos.Compras.Recibir,
                "select count(*) from compras.ordenes_compra where estado in ('Autorizada', 'RecibidaParcial')", ct),
            usuario.TienePermiso(Permisos.Produccion.Planear)
                ? await produccion.PlanPorProgramarAsync(fechaOperativa.Hoy().AddDays(1), ct)
                : null,
            await Contar(Permisos.Produccion.Planear, "select count(*) from produccion.ordenes_produccion where estado = 'Planeada'", ct),
            await Contar(Permisos.Produccion.Operar, "select count(*) from produccion.ordenes_produccion where estado = 'Liberada'", ct),
            await Contar(Permisos.Produccion.Cerrar, "select count(*) from produccion.ordenes_produccion where estado = 'EnProceso'", ct),
            await Contar(Permisos.Logistica.LiquidarProducto, "select count(*) from logistica.liquidaciones where estado = 'Abierta'", ct),
            requiereRevision
                ? await Contar(Permisos.Logistica.RevisarLiquidacion,
                    "select count(*) from logistica.liquidaciones where estado = 'ProductoConfirmado'", ct)
                : usuario.TienePermiso(Permisos.Logistica.RevisarLiquidacion) ? 0 : null,
            await Contar(Permisos.Logistica.Liquidar, requiereRevision
                ? "select count(*) from logistica.liquidaciones where estado = 'Revisada'"
                : "select count(*) from logistica.liquidaciones where estado in ('ProductoConfirmado', 'Revisada')", ct),
            await Contar(Permisos.Ventas.Pedidos, "select count(*) from ventas.pedidos where origen = 'App' and estado = 'Capturado'", ct),
            await Contar(Permisos.Ventas.Supervisar, "select count(*) from ventas.incidencias where estado = 'Abierta'", ct),
            await Contar(Permisos.Ventas.Clientes, "select count(*) from ventas.clientes where latitud_propuesta is not null", ct),
            await Contar(Permisos.Facturacion.Emitir,
                "select count(*) from facturacion.solicitudes_autofactura where estado in ('Recibida', 'Error')", ct));
    }

    private async Task<int?> Contar(string permiso, string sql, CancellationToken ct) =>
        usuario.TienePermiso(permiso)
            ? await Conexion.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct))
            : null;
}

public static class PendientesEndpoints
{
    public static IEndpointRouteBuilder MapPendientesEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/pendientes", (ConsultasPendientes consultas, CancellationToken ct) => consultas.ObtenerAsync(ct))
            .RequireAuthorization().WithTags("Tablero").WithName("obtenerPendientes");
        return api;
    }
}
