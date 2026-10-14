# Módulos Seguridad (SEG) y Catálogos (CAT)

## Seguridad

### Casos de uso
- Iniciar sesión (web y app), refrescar token, cerrar sesión, cambiar contraseña.
- Administrar usuarios, roles y permisos.
- Registrar dispositivo de ruta y asignarlo a un usuario.

### Reglas
- **RN-SEG-01** Access token de 15 min; refresh token de 30 días, rotativo (cada uso emite uno
  nuevo y revoca el anterior). Reusar un refresh revocado revoca toda la cadena, salvo dentro de una gracia de
  30 s tras su rotación (`Jwt:SegundosGraciaRefresh`): dos pestañas o una respuesta perdida no cierran la sesión.
- **RN-SEG-02** En la app, el refresh token está ligado al `dispositivo_id`. Un dispositivo
  inactivo no puede refrescar ni sincronizar.
- **RN-SEG-03** El registro de dispositivo lo aprueba un Administrador o Supervisor; al aprobarse
  se asigna `codigo` (`D01`…) que se usa como prefijo de folios.
- **RN-SEG-04** Los permisos se validan por política (`[Authorize(Policy = "inventario.ajustar")]`);
  los roles son solo agrupaciones de permisos.
- **RN-SEG-05** Bloqueo tras 5 intentos fallidos por 15 minutos.

### Endpoints (`/api/v1/seguridad`)
| Método | Ruta | Permiso |
|---|---|---|
| POST | `/auth/login` | anónimo |
| POST | `/auth/refresh` | anónimo (refresh válido) |
| POST | `/auth/logout` | autenticado |
| GET/POST/PUT | `/usuarios`, `/usuarios/{id}` | `seguridad.usuarios` |
| GET/PUT | `/roles`, `/roles/{id}/permisos` | `seguridad.roles` |
| POST | `/dispositivos/solicitar` | autenticado (app) |
| POST | `/dispositivos/{id}/aprobar`, `/desactivar` | `seguridad.dispositivos` |

### Criterios de aceptación
- Un usuario Vendedor no puede acceder a endpoints de compras ni producción (403).
- Un refresh token reutilizado tras rotación devuelve 401 y revoca la cadena.

## Catálogos

### Casos de uso
- CRUD de unidades, productos (con conversiones), motivos de merma y parámetros.

### Reglas
- **RN-CAT-01** El SKU es único y no se puede cambiar si el producto tiene movimientos.
- **RN-CAT-02** La unidad base de un producto no se puede cambiar si tiene movimientos.
- **RN-CAT-03** Toda unidad usada en compra, venta o receta debe tener conversión a la unidad base
  en `productos_unidades` (la unidad base tiene factor 1 implícito).
- **RN-CAT-04** `es_comprable`, `es_producible`, `es_vendible` se proponen según `tipo`, pero son
  editables (ej. masa semiterminada que también se vende).
- **RN-CAT-05** Un producto con existencia distinta de cero no se puede desactivar.
- **RN-CAT-06** Las cantidades capturadas respetan `unidades_medida.decimales`.

### Endpoints (`/api/v1/catalogos`)
`/unidades`, `/productos` (incluye `conversiones`), `/motivos-merma`, `/parametros`
(GET/POST/PUT; desactivar con `POST /{id}/desactivar`). Lectura para cualquier usuario
autenticado; escritura con `catalogos.editar`.

### Público para otros módulos
`ICatalogosService`: `ObtenerProducto`, `ConvertirABase(productoId, unidadId, cantidad)`,
`ObtenerParametro<T>(clave)`.
