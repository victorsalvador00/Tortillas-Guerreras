using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TG.Facturacion.Dominio;
using TG.Facturacion.Infraestructura;
using TG.Facturacion.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Resultados;

namespace TG.Facturacion.Aplicacion;

/// <summary>
/// Procesa el outbox de facturación (lo ejecuta Hangfire cada minuto): timbra con el proveedor, guarda XML y PDF y
/// los envía por correo (RN-FAC-09). Reintentos a 1, 5, 15 y 60 minutos, máximo 10. Cada mensaje en su propia
/// transacción y con <c>FOR UPDATE SKIP LOCKED</c> para que dos trabajadores no timbren lo mismo.
/// </summary>
public sealed class ProcesadorOutboxFacturacion(IServiceScopeFactory scopes, TimeProvider reloj, ILogger<ProcesadorOutboxFacturacion> log)
{
    public async Task<int> ProcesarPendientesAsync(CancellationToken ct)
    {
        List<Guid> ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var ahora = reloj.GetUtcNow();
            ids = await db.Outbox.AsNoTracking()
                .Where(o => o.Tipo == TipoOutbox.TimbrarFactura
                    && (o.Estado == EstadoOutbox.Pendiente || o.Estado == EstadoOutbox.Error)
                    && o.SiguienteIntentoEn != null && o.SiguienteIntentoEn <= ahora)
                .OrderBy(o => o.SiguienteIntentoEn).Select(o => o.Id).Take(50).ToListAsync(ct);
        }

        var procesados = 0;
        foreach (var id in ids)
        {
            await using var scope = scopes.CreateAsyncScope();
            if (await ProcesarAsync(scope.ServiceProvider, id, ct))
            {
                procesados++;
            }
        }

        return procesados;
    }

    private async Task<bool> ProcesarAsync(IServiceProvider sp, Guid mensajeId, CancellationToken ct)
    {
        var db = sp.GetRequiredService<FacturacionDbContext>();
        var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
        var proveedor = sp.GetRequiredService<IProveedorFacturacion>();
        var archivos = sp.GetRequiredService<IAlmacenArchivos>();
        (string Correo, Factura Factura, byte[] Xml, byte[] Pdf)? envio = null;

        var resultado = await unitOfWork.EjecutarAsync(async c =>
        {
            var mensaje = await db.Outbox
                .FromSql($"SELECT * FROM facturacion.outbox WHERE id = {mensajeId} FOR UPDATE SKIP LOCKED")
                .FirstOrDefaultAsync(c);
            if (mensaje is null || mensaje.Estado is EstadoOutbox.Procesado || mensaje.SiguienteIntentoEn is null)
            {
                return Result<bool>.Exito(false);
            }

            var factura = await db.Facturas.Include(f => f.Conceptos).FirstOrDefaultAsync(f => f.Id == mensaje.FacturaId, c);
            var emisor = await db.Emisores.AsNoTracking().FirstOrDefaultAsync(c);
            var ahora = reloj.GetUtcNow();
            if (factura is null || factura.Estado == EstadoFactura.Cancelada)
            {
                mensaje.Estado = EstadoOutbox.Procesado;
                mensaje.ProcesadoEn = ahora;
                mensaje.SiguienteIntentoEn = null;
                await db.SaveChangesAsync(c);
                return Result<bool>.Exito(false);
            }

            ResultadoTimbrado timbrado;
            try
            {
                timbrado = emisor is null
                    ? new ResultadoTimbrado(false, null, null, null, null, "No hay emisor configurado.")
                    : await proveedor.TimbrarAsync(ParaTimbrar(factura, emisor), c);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Error al timbrar la factura {FacturaId}", factura.Id);
                timbrado = new ResultadoTimbrado(false, null, null, null, null, ex.Message);
            }

            mensaje.Intentos++;
            factura.Intentos++;
            var solicitudes = await db.Solicitudes.Where(s => s.FacturaId == factura.Id).ToListAsync(c);
            if (timbrado is { Exito: true, UuidFiscal: { } uuid, Xml: { } xml, Pdf: { } pdf })
            {
                var baseLlave = $"facturas/{ahora:yyyy}/{ahora:MM}/{factura.Serie}{factura.Folio}-{uuid}";
                factura.XmlUrl = await archivos.GuardarAsync($"{baseLlave}.xml", xml, "application/xml", c);
                factura.PdfUrl = await archivos.GuardarAsync($"{baseLlave}.pdf", pdf, "application/pdf", c);
                factura.UuidFiscal = uuid;
                factura.Estado = EstadoFactura.Timbrada;
                factura.TimbradaEn = timbrado.FechaTimbrado ?? ahora;
                factura.ErrorDetalle = null;
                mensaje.Estado = EstadoOutbox.Procesado;
                mensaje.ProcesadoEn = ahora;
                mensaje.SiguienteIntentoEn = null;
                mensaje.Error = null;
                solicitudes.ForEach(s => s.Estado = EstadoSolicitud.Timbrada);
                if (!string.IsNullOrWhiteSpace(factura.EmailReceptor))
                {
                    envio = (factura.EmailReceptor, factura, xml, pdf);
                }
            }
            else
            {
                var siguiente = ReglasFacturacion.SiguienteIntento(mensaje.Intentos, ahora);
                factura.Estado = EstadoFactura.Error;
                factura.ErrorDetalle = timbrado.Error;
                mensaje.Estado = EstadoOutbox.Error;
                mensaje.Error = timbrado.Error;
                mensaje.SiguienteIntentoEn = siguiente;
                if (siguiente is null)
                {
                    solicitudes.ForEach(s =>
                    {
                        s.Estado = EstadoSolicitud.Error;
                        s.MotivoRechazo = timbrado.Error;
                    });
                }
            }

            await db.SaveChangesAsync(c);
            return Result<bool>.Exito(timbrado.Exito);
        }, ct);

        if (envio is { } e)
        {
            try
            {
                var correo = sp.GetRequiredService<IEnviadorCorreo>();
                var nombre = $"{e.Factura.Serie}{e.Factura.Folio}_{e.Factura.ReceptorRfc}";
                await correo.EnviarAsync(e.Correo, $"Factura {e.Factura.Serie}-{e.Factura.Folio}",
                    $"Adjuntamos la factura {e.Factura.Serie}-{e.Factura.Folio} por {e.Factura.Total:C2} (UUID {e.Factura.UuidFiscal}).",
                    [new AdjuntoCorreo($"{nombre}.xml", e.Xml, "application/xml"), new AdjuntoCorreo($"{nombre}.pdf", e.Pdf, "application/pdf")], ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // El envío por correo no es crítico: la factura ya está timbrada y se descarga en el sistema o el portal.
                log.LogWarning(ex, "No se pudo enviar la factura {FacturaId} por correo", e.Factura.Id);
            }
        }

        return resultado.EsExito && resultado.Valor;
    }

    internal static FacturaParaTimbrar ParaTimbrar(Factura f, Emisor e) => new(
        f.Id, f.Tipo.ToString(), f.Serie, f.Folio, f.CreadoEn,
        new EmisorParaTimbrar(e.Rfc, e.RazonSocial, e.RegimenFiscal, e.CodigoPostalExpedicion),
        new ReceptorParaTimbrar(f.ReceptorRfc, f.ReceptorNombre, f.ReceptorRegimen, f.ReceptorCp, f.UsoCfdi),
        f.MetodoPagoSat, f.FormaPagoSat, f.Periodicidad, f.Meses, f.Anio, f.Subtotal, f.Iva, f.Ieps, f.Total,
        f.Conceptos.OrderBy(c => c.Orden).Select(c => new ConceptoParaTimbrar(c.ClaveProdServ, c.NoIdentificacion, c.Cantidad,
            c.ClaveUnidad, c.Descripcion, c.ValorUnitario, c.Importe, c.ObjetoImp, c.TasaIva, c.Iva, c.TasaIeps, c.Ieps)).ToList());
}
