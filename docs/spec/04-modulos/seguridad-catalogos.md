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

### Cambios v0.2
- Roles nuevos **Captura** (`ventas.pedidos`, `ventas.ver`, `produccion.ver`, `logistica.ver`) y **Liquidaciones**
  (`logistica.ver`, `logistica.liquidar`, `logistica.cargos_chofer`, `ventas.ver`). Almacén recibe
  `logistica.liquidar_producto`; Supervisor `logistica.revisar_liquidacion`, `logistica.cargos_chofer`,
  `logistica.cambios_cancelar`; Vendedor `ventas.pedidos_app` (capturar pedidos desde la app). Permiso nuevo
  `seguridad.bitacora`.
- **RN-SEG-06** *Bitácora*: toda alta, cambio o baja lógica de las entidades marcadas con `[Bitacora]` guarda una fila
  en `seguridad.bitacora` con usuario, dispositivo (si vino de la app), origen y los campos que cambiaron con su
  valor anterior y nuevo, en la misma transacción. Entidades: pedidos y partidas, envíos y partidas, retornos,
  liquidaciones y partidas, gastos, cambios pendientes, cargos y cortes de chofer, rutas y clientes por ruta,
  clientes, precios (listas y especiales), productos y conversiones, usuarios y dispositivos.
- **RN-SEG-07** La bitácora no se edita ni se borra desde el sistema. Se consulta por documento, por usuario y por
  rango de fechas (`seguridad.bitacora`); cada pantalla de detalle de las entidades anteriores tiene la pestaña
  "Historial" que la filtra por `documento_id`.

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
| GET | **(v0.2)** `/bitacora?tabla&entidadId&documentoId&usuarioId&desde&hasta` | `seguridad.bitacora`; filtrando por `documentoId` basta el permiso de ver de la tabla según el mapa tabla → permiso de Seguridad (`ventas.pedidos` para pedidos, `logistica.ver` para envíos y liquidaciones, etc.) |

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

### Cambios v0.2
- **RN-CAT-07** El catálogo de productos se carga con los **códigos del cliente** como `sku` (`PV01`, `PV62`…). Los
  códigos "no utilizados" del cliente no se cargan; si ya existen, se desactivan (RN-CAT-05).
- **RN-CAT-08** `nombre_corto` es único entre productos activos (si se captura) y tiene máximo 15 caracteres.
- **RN-CAT-09** *Cajas y paquetes*: si el producto tiene unidad `predeterminada_traslado`, toda cantidad de surtido
  (estimado, carga, plan, inventario de camioneta) se muestra además como `N <traslado> + M <venta>`:
  `N = floor(cantidad_venta / paquetes_por_caja)`, `M = resto`, con
  `paquetes_por_caja = factor_traslado / factor_venta` (debe ser entero; si no, se rechaza la conversión con
  `CAT-011`). El helper vive en `@tg/shared` para web y app y en `ICatalogosService.DesglosarCajas` para reportes.
- **RN-CAT-10** `dias_tolerancia_devolucion` es nulo o > 0; solo se captura si `aplica_producto_a_cobro`.

**Errores nuevos (v0.2):** `CAT-011` conversión de traslado que no es múltiplo entero de la de venta, `CAT-012`
nombre corto repetido o mayor de 15 caracteres (RN-CAT-08).

### Endpoints (`/api/v1/catalogos`)
`/unidades`, `/productos` (incluye `conversiones`), `/motivos-merma`, `/parametros`
(GET/POST/PUT; desactivar con `POST /{id}/desactivar`). Lectura para cualquier usuario
autenticado; escritura con `catalogos.editar`.

### Público para otros módulos
`ICatalogosService`: `ObtenerProducto`, `ConvertirABase(productoId, unidadId, cantidad)`,
`ObtenerParametro<T>(clave)`.
