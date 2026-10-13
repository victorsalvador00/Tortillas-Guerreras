using Npgsql;
using Testcontainers.PostgreSql;

namespace TG.Api.IntegrationTests.Infraestructura;

/// <summary>
/// Un solo contenedor PostgreSQL 16 por ejecución. Se migra y siembra (con datos demo) una base plantilla una vez;
/// cada clase de pruebas obtiene su propia base con <c>CREATE DATABASE … TEMPLATE</c> (rápido y aislado).
/// </summary>
public static class BaseDatosPruebas
{
    private const string Plantilla = "tg_plantilla";
    private static readonly SemaphoreSlim Candado = new(1, 1);
    private static PostgreSqlContainer? _contenedor;

    public static async Task<string> CrearBaseAsync()
    {
        await AsegurarPlantillaAsync();
        var nombre = "t_" + Guid.NewGuid().ToString("N")[..16];
        await Candado.WaitAsync();
        try
        {
            await using var conexion = new NpgsqlConnection(CadenaPara("postgres"));
            await conexion.OpenAsync();
            await using var comando = new NpgsqlCommand($"CREATE DATABASE {nombre} TEMPLATE {Plantilla}", conexion);
            await comando.ExecuteNonQueryAsync();
        }
        finally
        {
            Candado.Release();
        }

        return CadenaPara(nombre);
    }

    private static async Task AsegurarPlantillaAsync()
    {
        if (_contenedor is not null)
        {
            return;
        }

        await Candado.WaitAsync();
        try
        {
            if (_contenedor is not null)
            {
                return;
            }

            var contenedor = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("postgres")
                .WithUsername("tg")
                .WithPassword("tg")
                .WithCommand("-c", "fsync=off", "-c", "full_page_writes=off", "-c", "synchronous_commit=off",
                    "-c", "max_connections=300")
                .Build();
            await contenedor.StartAsync();
            _contenedor = contenedor;

            await using (var conexion = new NpgsqlConnection(CadenaPara("postgres")))
            {
                await conexion.OpenAsync();
                await using var comando = new NpgsqlCommand($"CREATE DATABASE {Plantilla}", conexion);
                await comando.ExecuteNonQueryAsync();
            }

            await using (var factory = new ApiFactory(CadenaPara(Plantilla), inicializar: true))
            {
                // Al crear el cliente arranca el host: migra y siembra la plantilla.
                using var _ = factory.CreateClient();
            }

            NpgsqlConnection.ClearAllPools();
        }
        finally
        {
            Candado.Release();
        }
    }

    private static string CadenaPara(string baseDatos) =>
        new NpgsqlConnectionStringBuilder(_contenedor!.GetConnectionString())
        {
            Database = baseDatos,
            Pooling = true,
            MaxPoolSize = 50,
            IncludeErrorDetail = true,
        }.ConnectionString;
}
