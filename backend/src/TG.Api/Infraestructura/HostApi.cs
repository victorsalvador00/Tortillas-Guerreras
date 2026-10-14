using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;
using TG.Catalogos;
using TG.Compras;
using TG.Facturacion;
using TG.Inventario;
using TG.Logistica;
using TG.Produccion;
using TG.Seguridad;
using TG.SharedKernel;
using TG.SharedKernel.Datos;
using TG.Sincronizacion;
using TG.Ventas;

namespace TG.Api.Infraestructura;

public static class HostApi
{
    public const string PoliticaCors = "web";
    public const string LimitePortal = "portal";
    public const string LimiteLogin = "login";

    /// <summary>
    /// <c>true</c> cuando el host lo levanta la herramienta que genera <c>openapi.json</c> al compilar: no hay base de
    /// datos ni se arrancan trabajos en segundo plano.
    /// </summary>
    public static bool EsGeneracionDeDocumento =>
        Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    public static WebApplicationBuilder AgregarServiciosApi(this WebApplicationBuilder builder)
    {
        var servicios = builder.Services;
        var configuracion = builder.Configuration;

        builder.Host.UseSerilog((contexto, log) =>
        {
            log.ReadFrom.Configuration(contexto.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console(new RenderedCompactJsonFormatter());
            var archivo = contexto.Configuration["Logs:Archivo"];
            if (!string.IsNullOrWhiteSpace(archivo))
            {
                log.WriteTo.File(new RenderedCompactJsonFormatter(), archivo, rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14);
            }
        });

        servicios.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            // Números como números (sin "number | string" en OpenAPI ni en el cliente TS).
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });

        servicios.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
        {
            c.ProblemDetails.Extensions.TryAdd("traceId", c.HttpContext.TraceIdentifier);
            if (c.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
            {
                c.ProblemDetails.Title = "Ocurrió un error inesperado. Intenta de nuevo.";
                c.ProblemDetails.Extensions.TryAdd("codigo", "GEN-500");
            }
        });
        servicios.AddExceptionHandler<ManejadorExcepciones>();

        servicios.AddOpenApi("v1", o =>
        {
            o.AddDocumentTransformer<TransformadorDocumentoOpenApi>();
            o.AddSchemaTransformer<TransformadorOpcionalesOpenApi>();
        });

        servicios.AddCors(o => o.AddPolicy(PoliticaCors, p => p
            .WithOrigins(configuracion.GetSection("Cors:Origenes").Get<string[]>() ?? [])
            .AllowAnyHeader()
            .AllowAnyMethod()));

        servicios.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (contexto, ct) =>
            {
                contexto.HttpContext.Response.ContentType = "application/problem+json";
                await contexto.HttpContext.Response.WriteAsJsonAsync(new
                {
                    title = "Demasiadas solicitudes. Intenta más tarde.",
                    status = 429,
                    codigo = "GEN-429",
                }, ct);
            };
            // RN-FAC-08: el portal permite 10 solicitudes por IP por hora [SUP-15].
            o.AddPolicy(LimitePortal, contexto => RateLimitPartition.GetFixedWindowLimiter(
                IpCliente(contexto),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuracion.GetValue("LimiteSolicitudes:PortalPorHora", 10),
                    Window = TimeSpan.FromHours(1),
                }));
            o.AddPolicy(LimiteLogin, contexto => RateLimitPartition.GetFixedWindowLimiter(
                IpCliente(contexto),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuracion.GetValue("LimiteSolicitudes:LoginPorMinuto", 30),
                    Window = TimeSpan.FromMinutes(1),
                }));
        });

        servicios.AddHealthChecks().AddCheck<SaludBaseDatos>("postgresql");

        servicios.AddSharedKernel(configuracion);
        servicios.AddSeguridadModule(configuracion);
        servicios.AddCatalogosModule(configuracion);
        servicios.AddInventarioModule(configuracion);
        servicios.AddComprasModule(configuracion);
        servicios.AddProduccionModule(configuracion);
        servicios.AddVentasModule(configuracion);
        servicios.AddLogisticaModule(configuracion);
        servicios.AddFacturacionModule(configuracion);
        servicios.AddSincronizacionModule(configuracion);
        servicios.AddScoped<ConsultasTablero>();
        servicios.AddScoped<ImportadorCsv>();

        if (HangfireHabilitado(configuracion))
        {
            servicios.AddHangfire(h => h
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(
                    o => o.UseNpgsqlConnection(configuracion.GetConnectionString("Default")),
                    new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
            servicios.AddHangfireServer(o => o.WorkerCount = configuracion.GetValue("Hangfire:Trabajadores", 2));
        }

        return builder;
    }

    public static WebApplication UsarPipelineApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSerilogRequestLogging();
        app.UseCors(PoliticaCors);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapOpenApi("/openapi/{documentName}.json");
        app.MapScalarApiReference("/docs", o => o.WithTitle("TortillasGuerreras API"));
        app.MapHealthChecks("/salud").AllowAnonymous();

        if (HangfireHabilitado(app.Configuration) && !EsGeneracionDeDocumento)
        {
            app.UseHangfireDashboard("/hangfire", new DashboardOptions
            {
                Authorization = [new FiltroDashboardHangfire(app.Configuration)],
                DashboardTitle = "TortillasGuerreras · Trabajos",
            });
            app.ProgramarTrabajosRecurrentes();
        }

        var api = app.MapGroup("/api/v1");
        api.MapSeguridadEndpoints();
        api.MapCatalogosEndpoints();
        api.MapInventarioEndpoints();
        api.MapComprasEndpoints();
        api.MapProduccionEndpoints();
        api.MapVentasEndpoints();
        api.MapLogisticaEndpoints();
        api.MapFacturacionEndpoints();
        api.MapSincronizacionEndpoints();
        api.MapTableroEndpoints();
        api.MapImportacionEndpoints();
        return app;
    }

    public static async Task InicializarBaseDatosAsync(this WebApplication app)
    {
        var configuracion = app.Configuration;
        var migrar = configuracion.GetValue("BaseDatos:MigrarAlIniciar", false);
        var sembrar = configuracion.GetValue("BaseDatos:Semilla", false);
        if (!migrar && !sembrar)
        {
            return;
        }

        var datosDemo = configuracion.GetValue("BaseDatos:DatosDemo", false);
        await InicializadorBaseDatos.EjecutarAsync(app.Services, migrar, sembrar, datosDemo, CancellationToken.None);
    }

    private static bool HangfireHabilitado(IConfiguration configuracion) =>
        configuracion.GetValue("Hangfire:Habilitado", true) && !EsGeneracionDeDocumento;

    private static string IpCliente(HttpContext contexto) =>
        contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
}

/// <summary>Excepciones no controladas → 500 con <c>ProblemDetails</c>; conflictos de concurrencia → 409.</summary>
internal sealed class ManejadorExcepciones(IProblemDetailsService problemDetails, ILogger<ManejadorExcepciones> log)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (estado, codigo, titulo) = exception switch
        {
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict, "GEN-409", "Otro usuario modificó el registro. Recarga e intenta de nuevo."),
            BadHttpRequestException =>
                (StatusCodes.Status400BadRequest, "GEN-400", "La solicitud no es válida."),
            _ => (StatusCodes.Status500InternalServerError, "GEN-500", "Ocurrió un error inesperado. Intenta de nuevo."),
        };
        if (estado == StatusCodes.Status500InternalServerError)
        {
            log.LogError(exception, "Error no controlado en {Ruta}", httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = estado;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = estado, Title = titulo, Extensions = { ["codigo"] = codigo } },
        });
    }
}

/// <summary>Ejecuta, en orden, la migración y la semilla de cada módulo.</summary>
public static class InicializadorBaseDatos
{
    public static async Task EjecutarAsync(IServiceProvider raiz, bool migrar, bool sembrar, bool datosDemo,
        CancellationToken ct)
    {
        await using var scope = raiz.CreateAsyncScope();
        var servicios = scope.ServiceProvider;
        var log = servicios.GetRequiredService<ILoggerFactory>().CreateLogger("InicializadorBaseDatos");
        var modulos = servicios.GetServices<IInicializadorModulo>().OrderBy(m => m.Orden).ToList();
        if (migrar)
        {
            foreach (var modulo in modulos)
            {
                log.LogInformation("Migrando módulo {Modulo}", modulo.Nombre);
                await modulo.MigrarAsync(servicios, ct);
            }
        }

        if (sembrar)
        {
            foreach (var modulo in modulos)
            {
                log.LogInformation("Datos semilla del módulo {Modulo}", modulo.Nombre);
                await using var scopeModulo = raiz.CreateAsyncScope();
                await modulo.SembrarAsync(scopeModulo.ServiceProvider, datosDemo, ct);
            }
        }
    }
}
