using Hangfire;
using TG.Facturacion.Aplicacion;

namespace TG.Api.Infraestructura;

public static class TableroEndpoints
{
    public static IEndpointRouteBuilder MapTableroEndpoints(this IEndpointRouteBuilder api) => api;

    public static WebApplication ProgramarTrabajosRecurrentes(this WebApplication app)
    {
        // Outbox de facturación: timbrado con reintentos (04-modulos/facturacion.md).
        RecurringJob.AddOrUpdate<ProcesadorOutboxFacturacion>(
            "facturacion-outbox", p => p.ProcesarPendientesAsync(CancellationToken.None), Cron.Minutely());
        return app;
    }
}
