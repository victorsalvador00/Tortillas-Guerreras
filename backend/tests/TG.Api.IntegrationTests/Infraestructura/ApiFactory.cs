using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace TG.Api.IntegrationTests.Infraestructura;

public sealed class ApiFactory(string cadenaConexion, bool inicializar = false) : WebApplicationFactory<Program>
{
    public const string Contrasena = "Tortillas2026!";

    /// <summary>Reloj controlable; arranca en la hora real (Identity calcula bloqueos con la hora del sistema).</summary>
    public FakeTimeProvider Reloj { get; } = new(DateTimeOffset.UtcNow);

    public Dictionary<string, string?> Ajustes { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", cadenaConexion);
        builder.UseSetting("Jwt:Key", "clave-de-pruebas-de-integracion-con-mas-de-32-bytes");
        builder.UseSetting("Hangfire:Habilitado", "false");
        builder.UseSetting("BaseDatos:MigrarAlIniciar", inicializar ? "true" : "false");
        builder.UseSetting("BaseDatos:Semilla", inicializar ? "true" : "false");
        builder.UseSetting("BaseDatos:DatosDemo", "true");
        builder.UseSetting("LimiteSolicitudes:LoginPorMinuto", "100000");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        foreach (var (clave, valor) in Ajustes)
        {
            builder.UseSetting(clave, valor);
        }

        builder.ConfigureServices(servicios =>
        {
            servicios.RemoveAll<TimeProvider>();
            servicios.AddSingleton<TimeProvider>(Reloj);
        });
    }
}

/// <summary>Una base de datos y un host por clase de pruebas.</summary>
public class ApiFixture : IAsyncLifetime
{
    private readonly Dictionary<string, string> _tokens = new();

    public ApiFactory Factory { get; private set; } = null!;

    public string CadenaConexion { get; private set; } = string.Empty;

    protected virtual void Configurar(ApiFactory factory)
    {
    }

    public async ValueTask InitializeAsync()
    {
        CadenaConexion = await BaseDatosPruebas.CrearBaseAsync();
        Factory = new ApiFactory(CadenaConexion);
        Configurar(Factory);
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public HttpClient ClienteAnonimo() => Factory.CreateClient();

    /// <summary>Cliente autenticado como un usuario semilla (<c>admin</c>, <c>vendedor1</c>, <c>compras</c>…).</summary>
    public async Task<HttpClient> ClienteAsync(string usuario = "admin")
    {
        if (!_tokens.TryGetValue(usuario, out var token))
        {
            using var anonimo = Factory.CreateClient();
            var respuesta = await anonimo.PostAsJsonAsync("/api/v1/seguridad/auth/login",
                new { usuario, contrasena = ApiFactory.Contrasena });
            respuesta.EnsureSuccessStatusCode();
            var sesion = await respuesta.LeerAsync<SesionPrueba>();
            token = sesion.AccessToken;
            _tokens[usuario] = token;
        }

        var cliente = Factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    /// <summary>Ejecuta código con un scope de servicios del host (consultas directas en pruebas).</summary>
    public async Task<T> ConScopeAsync<T>(Func<IServiceProvider, Task<T>> accion)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await accion(scope.ServiceProvider);
    }
}

public sealed record SesionPrueba(string AccessToken, string RefreshToken, PerfilPrueba Usuario);

public sealed record PerfilPrueba(Guid Id, string Usuario, IReadOnlyList<string> Permisos);
