using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TG.Facturacion.Publico;
using TG.SharedKernel.Dominio;

namespace TG.Facturacion.Infraestructura;

/// <summary>
/// Proveedor de timbrado simulado (D-06): genera un UUID, el XML con la estructura del CFDI 4.0 y su PDF. No envía
/// nada al SAT.
/// </summary>
public sealed class ProveedorFacturacionSimulado(TimeProvider reloj) : IProveedorFacturacion
{
    public const string Nombre = "Simulado";

    public Task<ResultadoTimbrado> TimbrarAsync(FacturaParaTimbrar factura, CancellationToken ct)
    {
        if (factura.Conceptos.Count == 0)
        {
            return Task.FromResult(new ResultadoTimbrado(false, null, null, null, null, "El comprobante no tiene conceptos."));
        }

        var uuid = Guid.NewGuid();
        var fecha = reloj.GetUtcNow();
        var xml = XmlCfdi.Generar(factura, uuid, fecha);
        var pdf = PdfCfdi.Generar(factura, uuid, fecha);
        return Task.FromResult(new ResultadoTimbrado(true, uuid, xml, pdf, fecha, null));
    }

    public Task<ResultadoCancelacion> CancelarAsync(Guid uuidFiscal, string motivo, Guid? sustituye, CancellationToken ct) =>
        Task.FromResult(motivo == "01" && sustituye is null
            ? new ResultadoCancelacion(false, "El motivo 01 requiere el UUID de la factura que sustituye.")
            : new ResultadoCancelacion(true, null));
}

/// <summary>Archivos de facturación (XML y PDF). Local en desarrollo; Spaces (S3) en producción.</summary>
public interface IAlmacenArchivos
{
    /// <summary>Guarda y devuelve la llave con la que se vuelve a leer.</summary>
    Task<string> GuardarAsync(string llave, byte[] contenido, string tipoContenido, CancellationToken ct);

    Task<byte[]?> LeerAsync(string llave, CancellationToken ct);
}

public sealed class AlmacenArchivosLocal : IAlmacenArchivos
{
    private readonly string _raiz;

    public AlmacenArchivosLocal(IConfiguration configuracion, IHostEnvironment entorno)
    {
        _raiz = configuracion["Archivos:Ruta"] is { Length: > 0 } ruta
            ? ruta
            : Path.Combine(entorno.ContentRootPath, "App_Data", "archivos");
    }

    public async Task<string> GuardarAsync(string llave, byte[] contenido, string tipoContenido, CancellationToken ct)
    {
        var ruta = Ruta(llave);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        await File.WriteAllBytesAsync(ruta, contenido, ct);
        return llave;
    }

    public async Task<byte[]?> LeerAsync(string llave, CancellationToken ct)
    {
        var ruta = Ruta(llave);
        return File.Exists(ruta) ? await File.ReadAllBytesAsync(ruta, ct) : null;
    }

    private string Ruta(string llave)
    {
        if (llave.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Llave de archivo inválida.");
        }

        return Path.Combine(_raiz, llave.Replace('/', Path.DirectorySeparatorChar));
    }
}

/// <summary>DigitalOcean Spaces (API S3). Variables <c>Spaces__Key</c>, <c>Secret</c>, <c>Bucket</c>, <c>Endpoint</c>.</summary>
public sealed class AlmacenArchivosSpaces : IAlmacenArchivos, IDisposable
{
    private readonly AmazonS3Client _cliente;
    private readonly string _bucket;

    public AlmacenArchivosSpaces(IConfiguration configuracion)
    {
        _bucket = configuracion["Spaces:Bucket"] ?? throw new InvalidOperationException("Falta Spaces:Bucket.");
        _cliente = new AmazonS3Client(configuracion["Spaces:Key"], configuracion["Spaces:Secret"], new AmazonS3Config
        {
            ServiceURL = configuracion["Spaces:Endpoint"] ?? throw new InvalidOperationException("Falta Spaces:Endpoint."),
            ForcePathStyle = false,
        });
    }

    public async Task<string> GuardarAsync(string llave, byte[] contenido, string tipoContenido, CancellationToken ct)
    {
        using var flujo = new MemoryStream(contenido);
        await _cliente.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = llave,
            InputStream = flujo,
            ContentType = tipoContenido,
            CannedACL = S3CannedACL.Private,
        }, ct);
        return llave;
    }

    public async Task<byte[]?> LeerAsync(string llave, CancellationToken ct)
    {
        try
        {
            using var respuesta = await _cliente.GetObjectAsync(_bucket, llave, ct);
            using var memoria = new MemoryStream();
            await respuesta.ResponseStream.CopyToAsync(memoria, ct);
            return memoria.ToArray();
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public void Dispose() => _cliente.Dispose();
}

public sealed record AdjuntoCorreo(string Nombre, byte[] Contenido, string TipoContenido);

public sealed record CorreoEnviado(string Destinatario, string Asunto, string Cuerpo, IReadOnlyList<string> Adjuntos, DateTimeOffset EnviadoEn);

/// <summary>RN-FAC-09: envío del XML y PDF al correo del receptor.</summary>
public interface IEnviadorCorreo
{
    Task EnviarAsync(string destinatario, string asunto, string cuerpo, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken ct);
}

/// <summary>Bandeja en memoria con los últimos correos (diagnóstico y pruebas cuando no hay SMTP).</summary>
public sealed class BandejaCorreos
{
    private readonly ConcurrentQueue<CorreoEnviado> _correos = new();

    public IReadOnlyList<CorreoEnviado> Enviados => [.. _correos];

    public void Agregar(CorreoEnviado correo)
    {
        _correos.Enqueue(correo);
        while (_correos.Count > 200 && _correos.TryDequeue(out _))
        {
        }
    }
}

/// <summary>Sin <c>Smtp:Host</c> configurado: el correo solo se registra en el log y en la bandeja en memoria.</summary>
public sealed class EnviadorCorreoRegistro(BandejaCorreos bandeja, TimeProvider reloj, ILogger<EnviadorCorreoRegistro> log) : IEnviadorCorreo
{
    public Task EnviarAsync(string destinatario, string asunto, string cuerpo, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken ct)
    {
        log.LogInformation("Correo (sin SMTP) a {Destinatario}: {Asunto} con {Adjuntos} adjuntos", destinatario, asunto, adjuntos.Count);
        bandeja.Agregar(new CorreoEnviado(destinatario, asunto, cuerpo, adjuntos.Select(a => a.Nombre).ToList(), reloj.GetUtcNow()));
        return Task.CompletedTask;
    }
}

/// <summary>SMTP con <c>Smtp:Host</c>, <c>Port</c>, <c>Usuario</c>, <c>Contrasena</c>, <c>Remitente</c>, <c>Ssl</c>.</summary>
public sealed class EnviadorCorreoSmtp(IConfiguration configuracion, BandejaCorreos bandeja, TimeProvider reloj) : IEnviadorCorreo
{
    public async Task EnviarAsync(string destinatario, string asunto, string cuerpo, IReadOnlyList<AdjuntoCorreo> adjuntos, CancellationToken ct)
    {
        using var mensaje = new MailMessage(configuracion["Smtp:Remitente"] ?? "facturacion@localhost", destinatario, asunto, cuerpo);
        var flujos = new List<MemoryStream>();
        try
        {
            foreach (var a in adjuntos)
            {
                var flujo = new MemoryStream(a.Contenido);
                flujos.Add(flujo);
                mensaje.Attachments.Add(new Attachment(flujo, a.Nombre, a.TipoContenido));
            }

            using var cliente = new SmtpClient(configuracion["Smtp:Host"], configuracion.GetValue("Smtp:Port", 587))
            {
                EnableSsl = configuracion.GetValue("Smtp:Ssl", true),
            };
            if (!string.IsNullOrEmpty(configuracion["Smtp:Usuario"]))
            {
                cliente.Credentials = new NetworkCredential(configuracion["Smtp:Usuario"], configuracion["Smtp:Contrasena"]);
            }

            await cliente.SendMailAsync(mensaje, ct);
            bandeja.Agregar(new CorreoEnviado(destinatario, asunto, cuerpo, adjuntos.Select(a => a.Nombre).ToList(), reloj.GetUtcNow()));
        }
        finally
        {
            foreach (var f in flujos)
            {
                await f.DisposeAsync();
            }
        }
    }
}

/// <summary>Captcha aritmético simple del portal [SUP-15]; cada respuesta se usa una sola vez.</summary>
public sealed class CaptchaPortal(TimeProvider reloj)
{
    private readonly ConcurrentDictionary<Guid, (int Respuesta, DateTimeOffset Expira)> _pendientes = new();

    public (Guid Id, string Pregunta) Nuevo()
    {
        var a = Random.Shared.Next(2, 10);
        var b = Random.Shared.Next(2, 10);
        var id = Id.Nuevo();
        _pendientes[id] = (a + b, reloj.GetUtcNow().AddMinutes(10));
        foreach (var vencido in _pendientes.Where(p => p.Value.Expira < reloj.GetUtcNow()).Select(p => p.Key).ToList())
        {
            _pendientes.TryRemove(vencido, out _);
        }

        return (id, $"¿Cuánto es {a} + {b}?");
    }

    public bool Validar(Guid id, int respuesta) =>
        _pendientes.TryRemove(id, out var esperado) && esperado.Expira >= reloj.GetUtcNow() && esperado.Respuesta == respuesta;
}
