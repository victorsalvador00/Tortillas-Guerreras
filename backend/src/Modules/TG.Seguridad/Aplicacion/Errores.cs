using TG.SharedKernel.Resultados;

namespace TG.Seguridad.Aplicacion;

public static class ErroresSeguridad
{
    public static readonly Error CredencialesInvalidas =
        new("SEG-001", "Usuario o contraseña incorrectos.", TipoError.NoAutorizado);

    public static Error Bloqueado(DateTimeOffset hasta) =>
        new("SEG-002", "La cuenta está bloqueada por intentos fallidos. Intenta más tarde.", TipoError.NoAutorizado,
            Detalle: new { bloqueadoHasta = hasta });

    public static readonly Error RefreshInvalido =
        new("SEG-003", "La sesión expiró. Inicia sesión de nuevo.", TipoError.NoAutorizado);

    public static readonly Error RefreshReutilizado =
        new("SEG-004", "La sesión se cerró por seguridad. Inicia sesión de nuevo.", TipoError.NoAutorizado);

    public static readonly Error DispositivoInactivo =
        new("SEG-005", "El dispositivo no está aprobado o está desactivado.", TipoError.Prohibido);

    public static readonly Error UsuarioInactivo =
        new("SEG-006", "El usuario está desactivado.", TipoError.NoAutorizado);

    public static readonly Error UsuarioDuplicado =
        Error.Campo("SEG-007", "usuario", "Ya existe un usuario con ese nombre.");

    public static readonly Error UsuarioNoEncontrado = Error.NoEncontrado("SEG-008", "El usuario no existe.");

    public static readonly Error RolNoEncontrado = Error.NoEncontrado("SEG-009", "El rol no existe.");

    public static readonly Error DispositivoNoEncontrado = Error.NoEncontrado("SEG-010", "El dispositivo no existe.");

    public static Error PermisoDesconocido(string clave) =>
        Error.Campo("SEG-011", "permisos", $"El permiso '{clave}' no existe.");

    public static Error Identity(IEnumerable<string> mensajes) =>
        Error.Validacion("SEG-012", "No se pudo guardar el usuario.",
            new Dictionary<string, string[]> { ["contrasena"] = mensajes.ToArray() });

    public static readonly Error RolDuplicado = Error.Campo("SEG-013", "nombre", "Ya existe un rol con ese nombre.");

    public static readonly Error ContrasenaActualIncorrecta =
        Error.Campo("SEG-014", "contrasenaActual", "La contraseña actual no es correcta.");
}
