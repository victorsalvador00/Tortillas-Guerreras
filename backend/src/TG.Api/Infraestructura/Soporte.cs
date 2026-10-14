using System.Data;
using System.Text;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using TG.SharedKernel.Datos;

namespace TG.Api.Infraestructura;

/// <summary>Datos generales del documento OpenAPI y esquema de seguridad Bearer (JWT).</summary>
internal sealed class TransformadorDocumentoOpenApi : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "TortillasGuerreras API",
            Version = "v1",
            Description = "Compras, recetas, producción, inventario, logística, ventas en ruta y facturación.",
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        };
        document.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] },
        ];
        return Task.CompletedTask;
    }
}

/// <summary>
/// Las propiedades que aceptan <c>null</c> no son obligatorias en el documento: el cliente TS generado puede omitirlas
/// en lugar de enviar <c>null</c> explícito.
/// </summary>
internal sealed class TransformadorOpcionalesOpenApi : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Properties is null || schema.Required is null || schema.Required.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var (nombre, propiedad) in schema.Properties)
        {
            if (AceptaNulo(propiedad))
            {
                schema.Required.Remove(nombre);
            }
        }

        return Task.CompletedTask;
    }

    private static bool AceptaNulo(IOpenApiSchema propiedad) =>
        (propiedad.Type is { } tipo && tipo.HasFlag(JsonSchemaType.Null))
        || (propiedad.OneOf?.Any(AceptaNulo) ?? false)
        || (propiedad.AnyOf?.Any(AceptaNulo) ?? false);
}

/// <summary><c>/salud</c>: la API responde y PostgreSQL acepta consultas.</summary>
internal sealed class SaludBaseDatos(IUnitOfWork unitOfWork) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var conexion = unitOfWork.Conexion;
            if (conexion.State != ConnectionState.Open)
            {
                await conexion.OpenAsync(cancellationToken);
            }

            await using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT 1";
            await comando.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL no responde.", ex);
        }
    }
}

/// <summary>Protege el tablero de Hangfire con usuario y contraseña (Basic) de la configuración.</summary>
internal sealed class FiltroDashboardHangfire(IConfiguration configuracion) : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var usuario = configuracion["Hangfire:DashboardUser"];
        var contrasena = configuracion["Hangfire:DashboardPassword"];
        if (string.IsNullOrEmpty(usuario) || string.IsNullOrEmpty(contrasena))
        {
            return false;
        }

        var encabezado = http.Request.Headers.Authorization.ToString();
        if (encabezado.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var credenciales = Encoding.UTF8.GetString(Convert.FromBase64String(encabezado[6..])).Split(':', 2);
                if (credenciales.Length == 2 && credenciales[0] == usuario && credenciales[1] == contrasena)
                {
                    return true;
                }
            }
            catch (FormatException)
            {
                // Encabezado mal formado: se vuelve a pedir credenciales.
            }
        }

        http.Response.Headers.WWWAuthenticate = "Basic realm=\"Hangfire\"";
        return false;
    }
}
