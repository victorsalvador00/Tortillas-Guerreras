using System.Net;
using System.Net.Http.Headers;
using TG.Api.IntegrationTests.Infraestructura;

namespace TG.Api.IntegrationTests.Seguridad;

public sealed class SeguridadTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record DispositivoRespuesta(Guid Id, string? Codigo, bool Activo, string Estado);

    [Fact]
    public async Task Login_CredencialesValidas_DevuelveTokensYPermisos()
    {
        var sesion = await api.ClienteAnonimo().PostOkAsync<SesionPrueba>("/api/v1/seguridad/auth/login",
            new { usuario = "admin", contrasena = ApiFactory.Contrasena });

        sesion.AccessToken.ShouldNotBeNullOrEmpty();
        sesion.RefreshToken.ShouldNotBeNullOrEmpty();
        sesion.Usuario.Permisos.ShouldContain("seguridad.usuarios");
    }

    [Fact]
    [Trait("Regla", "RN-SEG-01")]
    public async Task Refresh_ReusarTokenRotado_Devuelve401YRevocaLaCadena()
    {
        var anonimo = api.ClienteAnonimo();
        var sesion = await anonimo.PostOkAsync<SesionPrueba>("/api/v1/seguridad/auth/login",
            new { usuario = "almacen", contrasena = ApiFactory.Contrasena });

        var rotada = await anonimo.PostOkAsync<SesionPrueba>("/api/v1/seguridad/auth/refresh",
            new { refreshToken = sesion.RefreshToken });
        rotada.RefreshToken.ShouldNotBe(sesion.RefreshToken);

        // Dentro del periodo de gracia (otra pestaña refrescando al mismo tiempo) el reuso se acepta.
        var carrera = await anonimo.PostJsonAsync("/api/v1/seguridad/auth/refresh", new { refreshToken = sesion.RefreshToken });
        carrera.StatusCode.ShouldBe(HttpStatusCode.OK);

        api.Factory.Reloj.Advance(TimeSpan.FromSeconds(31));
        var reuso = await anonimo.PostJsonAsync("/api/v1/seguridad/auth/refresh", new { refreshToken = sesion.RefreshToken });
        await reuso.DebeFallarAsync(HttpStatusCode.Unauthorized, "SEG-004");

        // El token emitido después del rotado también quedó revocado.
        var cadena = await anonimo.PostJsonAsync("/api/v1/seguridad/auth/refresh", new { refreshToken = rotada.RefreshToken });
        await cadena.DebeFallarAsync(HttpStatusCode.Unauthorized, "SEG-004");
    }

    [Fact]
    [Trait("Regla", "RN-SEG-02")]
    [Trait("Regla", "RN-SEG-03")]
    public async Task Dispositivo_AprobarAsignaCodigo_YDesactivadoNoPuedeRefrescar()
    {
        var vendedor = await api.ClienteAsync("vendedor2");
        var solicitado = await vendedor.PostOkAsync<DispositivoRespuesta>("/api/v1/seguridad/dispositivos/solicitar",
            new { nombre = "Moto G ruta 2", plataforma = "android", versionApp = "1.0.0" });
        solicitado.Estado.ShouldBe("PorAprobar");
        solicitado.Codigo.ShouldBeNull();

        var anonimo = api.ClienteAnonimo();
        var sinAprobar = await anonimo.PostJsonAsync("/api/v1/seguridad/auth/login",
            new { usuario = "vendedor2", contrasena = ApiFactory.Contrasena, dispositivoId = solicitado.Id });
        await sinAprobar.DebeFallarAsync(HttpStatusCode.Forbidden, "SEG-005");

        var supervisor = await api.ClienteAsync("supervisor");
        var aprobado = await supervisor.PostOkAsync<DispositivoRespuesta>(
            $"/api/v1/seguridad/dispositivos/{solicitado.Id}/aprobar", new { });
        aprobado.Codigo.ShouldNotBeNull();
        aprobado.Codigo.ShouldMatch("^D\\d{2}$");
        aprobado.Activo.ShouldBeTrue();

        var sesion = await anonimo.PostOkAsync<SesionPrueba>("/api/v1/seguridad/auth/login",
            new { usuario = "vendedor2", contrasena = ApiFactory.Contrasena, dispositivoId = solicitado.Id });

        await supervisor.PostOkAsync<DispositivoRespuesta>($"/api/v1/seguridad/dispositivos/{solicitado.Id}/desactivar");
        var refresh = await anonimo.PostJsonAsync("/api/v1/seguridad/auth/refresh", new { refreshToken = sesion.RefreshToken });
        ((int)refresh.StatusCode).ShouldBeOneOf(401, 403);
    }

    [Fact]
    [Trait("Regla", "RN-SEG-04")]
    public async Task Vendedor_SinPermiso_Recibe403()
    {
        var vendedor = await api.ClienteAsync("vendedor1");
        var respuesta = await vendedor.GetAsync("/api/v1/seguridad/usuarios");
        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var anonimo = await api.ClienteAnonimo().GetAsync("/api/v1/seguridad/usuarios");
        anonimo.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Regla", "RN-SEG-05")]
    public async Task Login_CincoIntentosFallidos_BloqueaQuinceMinutos()
    {
        var anonimo = api.ClienteAnonimo();
        for (var i = 0; i < 4; i++)
        {
            var fallo = await anonimo.PostJsonAsync("/api/v1/seguridad/auth/login", new { usuario = "facturacion", contrasena = "incorrecta1" });
            await fallo.DebeFallarAsync(HttpStatusCode.Unauthorized, "SEG-001");
        }

        var quinto = await anonimo.PostJsonAsync("/api/v1/seguridad/auth/login", new { usuario = "facturacion", contrasena = "incorrecta1" });
        await quinto.DebeFallarAsync(HttpStatusCode.Unauthorized, "SEG-002");

        var correcta = await anonimo.PostJsonAsync("/api/v1/seguridad/auth/login", new { usuario = "facturacion", contrasena = ApiFactory.Contrasena });
        await correcta.DebeFallarAsync(HttpStatusCode.Unauthorized, "SEG-002");

        var admin = await api.ClienteAsync();
        var usuarios = await admin.GetOkAsync<PaginaUsuarios>("/api/v1/seguridad/usuarios?buscar=facturacion");
        var bloqueado = usuarios.Items.Single(u => u.Usuario == "facturacion");
        bloqueado.BloqueadoHasta.ShouldNotBeNull();
        (bloqueado.BloqueadoHasta.Value - DateTimeOffset.UtcNow).TotalMinutes.ShouldBeInRange(13, 16);
    }

    private sealed record UsuarioRespuesta(string Usuario, DateTimeOffset? BloqueadoHasta);

    private sealed record PaginaUsuarios(IReadOnlyList<UsuarioRespuesta> Items);

    [Fact]
    public async Task Usuarios_AdminCreaUsuarioConRol_PuedeIniciarSesion()
    {
        var admin = await api.ClienteAsync();
        await admin.PostOkAsync<object>("/api/v1/seguridad/usuarios", new
        {
            usuario = "nuevo.almacen",
            nombreCompleto = "Nuevo Almacenista",
            email = "nuevo@tortillas.local",
            activo = true,
            roles = new[] { "Almacen" },
            contrasena = "Temporal123",
        });

        var sesion = await api.ClienteAnonimo().PostOkAsync<SesionPrueba>("/api/v1/seguridad/auth/login",
            new { usuario = "nuevo.almacen", contrasena = "Temporal123" });
        sesion.Usuario.Permisos.ShouldContain("inventario.ajustar");
        sesion.Usuario.Permisos.ShouldNotContain("seguridad.usuarios");

        using var cliente = api.ClienteAnonimo();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion.AccessToken);
        (await cliente.GetAsync("/api/v1/seguridad/auth/yo")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
