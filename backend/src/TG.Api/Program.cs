using TG.Api.Infraestructura;

var builder = WebApplication.CreateBuilder(args);

builder.AgregarServiciosApi();

var app = builder.Build();

app.UsarPipelineApi();

if (!HostApi.EsGeneracionDeDocumento)
{
    await app.InicializarBaseDatosAsync();

    // Despliegue: solo carga datos base y termina (las migraciones van con los bundles de EF).
    if (app.Configuration.GetValue("BaseDatos:SoloInicializar", false))
    {
        return;
    }
}

await app.RunAsync();

/// <summary>Expuesto para <c>WebApplicationFactory</c> en las pruebas de integración.</summary>
public partial class Program;
