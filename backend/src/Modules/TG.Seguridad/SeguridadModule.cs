using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TG.Seguridad.Aplicacion;
using TG.Seguridad.Dominio;
using TG.Seguridad.Endpoints;
using TG.Seguridad.Infraestructura;
using TG.Seguridad.Publico;
using TG.SharedKernel.Datos;
using TG.SharedKernel.Seguridad;

namespace TG.Seguridad;

public static class SeguridadModule
{
    public static IServiceCollection AddSeguridadModule(this IServiceCollection services, IConfiguration configuracion)
    {
        services.AgregarDbContextModulo<SeguridadDbContext>(SeguridadDbContext.Esquema);
        services.AgregarHandlers(typeof(SeguridadModule).Assembly);
        services.AddOptions<OpcionesJwt>()
            .Bind(configuracion.GetSection("Jwt"))
            // La herramienta que genera openapi.json al compilar levanta el host sin configuración: no se valida ahí.
            .Validate(o => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider"
                    || System.Text.Encoding.UTF8.GetByteCount(o.Key) >= 32,
                "Falta Jwt:Key (mínimo 32 bytes). En local arranca con ASPNETCORE_ENVIRONMENT=Development; en servidor defínela en .env.")
            .ValidateOnStart();
        services.AddScoped<ServicioTokens>();
        services.AddScoped<ISeguridadService, SeguridadService>();
        services.AddScoped<IInicializadorModulo, InicializadorSeguridad>();

        services.AddIdentityCore<Usuario>(o =>
            {
                // RN-SEG-05: bloqueo tras 5 intentos fallidos por 15 minutos.
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireDigit = true;
                o.User.RequireUniqueEmail = false;
            })
            .AddRoles<Rol>()
            .AddEntityFrameworkStores<SeguridadDbContext>()
            .AddErrorDescriber<DescriptorErroresIdentity>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<OpcionesJwt>, TimeProvider>((o, jwt, reloj) =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = ServicioTokens.ClaveFirma(jwt.Value),
                    NameClaimType = "usuario",
                    RoleClaimType = "rol",
                    ClockSkew = TimeSpan.FromSeconds(30),
                    // Vigencia con el reloj inyectado (regla 7), no con DateTime.UtcNow.
                    LifetimeValidator = (inicio, fin, _, _) =>
                    {
                        var ahora = reloj.GetUtcNow().UtcDateTime;
                        return (inicio is null || inicio <= ahora.AddSeconds(30)) && (fin is null || fin > ahora.AddSeconds(-30));
                    },
                };
            });

        // RN-SEG-04: cada permiso es una política; los roles solo agrupan permisos.
        services.AddSingleton<IAuthorizationPolicyProvider, ProveedorPoliticasPermiso>();
        services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    public static IEndpointRouteBuilder MapSeguridadEndpoints(this IEndpointRouteBuilder api) =>
        SeguridadEndpoints.Mapear(api);
}

/// <summary>Política por nombre de permiso: <c>RequireAuthorization("inventario.ajustar")</c>.</summary>
internal sealed class ProveedorPoliticasPermiso(IOptions<AuthorizationOptions> opciones)
    : DefaultAuthorizationPolicyProvider(opciones)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        await base.GetPolicyAsync(policyName)
        ?? new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(Permisos.Claim, policyName)
            .Build();
}

/// <summary>Mensajes de Identity en español de México.</summary>
internal sealed class DescriptorErroresIdentity : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"La contraseña debe tener al menos {length} caracteres." };

    public override IdentityError PasswordRequiresDigit() =>
        new() { Code = nameof(PasswordRequiresDigit), Description = "La contraseña debe incluir al menos un número." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = $"El usuario '{userName}' ya existe." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = "El nombre de usuario no es válido." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "La contraseña no es correcta." };
}
