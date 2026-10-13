using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using TG.Seguridad.Aplicacion;
using TG.SharedKernel.Api;
using TG.SharedKernel.Resultados;
using TG.SharedKernel.Seguridad;

namespace TG.Seguridad.Endpoints;

public static class SeguridadEndpoints
{
    internal static IEndpointRouteBuilder Mapear(IEndpointRouteBuilder api)
    {
        var grupo = api.MapGroup("/seguridad").WithTags("Seguridad");

        var auth = grupo.MapGroup("/auth");
        auth.MapPost("/login", (LoginRequest r, LoginHandler h, CancellationToken ct) => Ejecutar(h.EjecutarAsync(r, ct)))
            .AllowAnonymous().RequireRateLimiting("login").Validar<LoginRequest>().WithName("login");
        auth.MapPost("/refresh", (RefreshRequest r, RefreshHandler h, CancellationToken ct) => Ejecutar(h.EjecutarAsync(r, ct)))
            .AllowAnonymous().WithName("refrescarSesion");
        auth.MapPost("/logout", async (RefreshRequest r, LogoutHandler h, CancellationToken ct) => (await h.EjecutarAsync(r, ct)).AHttp())
            .RequireAuthorization().WithName("cerrarSesion");
        auth.MapGet("/yo", async (PerfilHandler h, CancellationToken ct) => (await h.EjecutarAsync(ct)).AHttp())
            .RequireAuthorization().WithName("obtenerPerfil");
        auth.MapPost("/cambiar-contrasena", async (CambiarContrasenaRequest r, CambiarContrasenaHandler h, CancellationToken ct) =>
                (await h.EjecutarAsync(r, ct)).AHttp())
            .RequireAuthorization().Validar<CambiarContrasenaRequest>().WithName("cambiarContrasena");

        var usuarios = grupo.MapGroup("/usuarios").RequireAuthorization(Permisos.Seguridad.Usuarios);
        usuarios.MapGet("/", (string? buscar, int? pagina, int? tamano, ConsultasUsuariosHandler h, CancellationToken ct) =>
            h.ListarAsync(buscar, pagina, tamano, ct)).WithName("listarUsuarios");
        usuarios.MapGet("/{id:guid}", async (Guid id, ConsultasUsuariosHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).WithName("obtenerUsuario");
        usuarios.MapPost("/", async (GuardarUsuarioRequest r, CrearUsuarioHandler h, CancellationToken ct) =>
            (await h.EjecutarAsync(r, ct)).AHttp()).Validar<GuardarUsuarioRequest>().WithName("crearUsuario");
        usuarios.MapPut("/{id:guid}", async (Guid id, GuardarUsuarioRequest r, ActualizarUsuarioHandler h, CancellationToken ct) =>
            (await h.EjecutarAsync(id, r, ct)).AHttp()).Validar<GuardarUsuarioRequest>().WithName("actualizarUsuario");
        usuarios.MapPost("/{id:guid}/desbloquear", async (Guid id, DesbloquearUsuarioHandler h) =>
            (await h.EjecutarAsync(id)).AHttp()).WithName("desbloquearUsuario");

        var roles = grupo.MapGroup("/roles").RequireAuthorization(Permisos.Seguridad.Roles);
        roles.MapGet("/", (RolesHandler h, CancellationToken ct) => h.ListarAsync(ct)).WithName("listarRoles");
        roles.MapPost("/", async (CrearRolRequest r, RolesHandler h, CancellationToken ct) =>
            (await h.CrearAsync(r, ct)).AHttp()).Validar<CrearRolRequest>().WithName("crearRol");
        roles.MapPut("/{id:guid}/permisos", async (Guid id, PermisosRolRequest r, RolesHandler h, CancellationToken ct) =>
            (await h.ActualizarPermisosAsync(id, r, ct)).AHttp()).WithName("actualizarPermisosRol");
        grupo.MapGet("/permisos", (RolesHandler h, CancellationToken ct) => h.PermisosAsync(ct))
            .RequireAuthorization(Permisos.Seguridad.Roles).WithName("listarPermisos");

        var dispositivos = grupo.MapGroup("/dispositivos");
        dispositivos.MapGet("/", (DispositivosHandler h, CancellationToken ct) => h.ListarAsync(ct))
            .RequireAuthorization(Permisos.Seguridad.Dispositivos).WithName("listarDispositivos");
        dispositivos.MapGet("/{id:guid}", async (Guid id, DispositivosHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).RequireAuthorization().WithName("obtenerDispositivo");
        dispositivos.MapPost("/solicitar", async (SolicitarDispositivoRequest r, DispositivosHandler h, CancellationToken ct) =>
                (await h.SolicitarAsync(r, ct)).AHttp())
            .RequireAuthorization().Validar<SolicitarDispositivoRequest>().WithName("solicitarDispositivo");
        dispositivos.MapPost("/{id:guid}/aprobar", async (Guid id, AprobarDispositivoRequest r, DispositivosHandler h, CancellationToken ct) =>
            (await h.AprobarAsync(id, r, ct)).AHttp()).RequireAuthorization(Permisos.Seguridad.Dispositivos).WithName("aprobarDispositivo");
        dispositivos.MapPost("/{id:guid}/desactivar", async (Guid id, DispositivosHandler h, CancellationToken ct) =>
            (await h.DesactivarAsync(id, ct)).AHttp()).RequireAuthorization(Permisos.Seguridad.Dispositivos).WithName("desactivarDispositivo");

        return api;
    }

    private static async Task<Results<Ok<SesionDto>, ProblemHttpResult>> Ejecutar(Task<Result<SesionDto>> tarea) =>
        (await tarea).AHttp();
}
