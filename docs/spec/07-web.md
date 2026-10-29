# 07 — Web administrativa y portal

## Panel administrativo (`apps/web`)

Menú por módulo; cada opción visible solo con su permiso.

| Sección | Pantallas |
|---|---|
| Tablero | Producción del día vs plan, merma % del día y del mes, ventas por ruta y canal, existencias bajo mínimo, incidencias abiertas, liquidaciones pendientes |
| Catálogos | Productos (con conversiones), unidades, motivos de merma, parámetros |
| Compras | Proveedores, órdenes de compra (lista, detalle, autorizar), recepciones |
| Producción | Recetas (versiones, publicar), líneas, plan del día (tablero por turno/línea), orden (liberar, capturar mermas y consumos, terminar), reporte de variación |
| Inventario | Existencias, kardex, almacenes, ajustes, traspasos, conteos físicos, valuación |
| Logística | Rutas y clientes por ruta (orden de visita, días), vehículos, cargas del día, retornos, liquidaciones |
| Ventas | Clientes (datos fiscales, ubicación en mapa, aprobar ubicación), listas de precio, pedidos, ventas, cobros, incidencias |
| Mapa | Entregas del día por ruta (Leaflet), punto registrado del cliente vs punto de entrega, continuidad de visitas |
| Facturación | Facturas, solicitudes de autofactura, factura global, emisor |
| Seguridad | Usuarios, roles y permisos, dispositivos |

### Cambios v0.2

| Sección | Cambio |
|---|---|
| Producción | **Estimado de producción** (pestañas por canal; cuadrícula rutas × productos con `nombre_corto`, observaciones, TOTAL, CAJAS/PAQUETES y REAL; celdas editables en DET (pedido de ruta); en AUTO, INST y CEDIS la celda abre los pedidos de los clientes de la ruta; "Usar referencia"; "Confirmar estimado"; imprimir igual al formato en papel). **Plan sugerido** (demanda vs existencia vs órdenes; crear órdenes). **Plan del día** vuelve al menú. Recetas: botón "Copiar a otro producto" |
| Ventas | Pedidos: filtro **Por validar** (origen App, `Capturado`), columnas "Solicitado" vs "Validado", pestaña Historial; el detalle lista sus **Entregas** (folio, hora, distancia del GPS al cliente) con enlace a la venta. Clientes: grupo, **precios especiales**. **Grupos de clientes** (nuevo). Productos exclusivos en la ficha del producto (pestaña Ventas) |
| Logística | Rutas: canal, chofer y vehículo predeterminados. Cargas del día: **Generar cargas sugeridas**; nueva carga con chofer y vehículo. **Cambios pendientes** (nuevo). Liquidaciones: rediseño por etapas (ver abajo). **Cuenta de choferes** y **Corte semanal** (nuevos) |
| Catálogos | Productos: nombre corto, producto a cobro, unidad de traslado con paquetes por caja |
| Seguridad | **Bitácora** (nuevo) y pestaña Historial en los detalles |

**Liquidación (v0.2).** Encabezado: ruta, canal, chofer, fecha, etapa y responsables. Pestaña **Producto**
(almacén): tabla con las columnas del formato del canal (RN-LOG-19), captura de sobrante, destino y producto a cobro
(con la sugerencia por antigüedad), botón "Confirmar producto". Pestaña **Dinero** (liquidaciones): venta total,
créditos del día por cliente y folio (solo lectura), cobros y pagos del día (solo lectura), transferencias, cheques,
gastos (captura), diferencia de precios, efectivo esperado, efectivo entregado, diferencia. Pestaña **Cargos**:
cargos generados al chofer y alta de `DiferenciaPrecio`/`Otro`. Botones por etapa: Confirmar producto · Revisar ·
Regresar · Cerrar. **Imprimir hoja** con las 4 firmas (RN-LOG-22).

**Corte semanal.** Filtro por semana y chofer; tabla por chofer con ventas, cargos por tipo y total a descontar;
detalle por día y producto; "Cerrar corte" y "Exportar CSV".

**Pendientes en el menú (RN-WEB-01).** Cada opción del menú muestra un globo rojo con cuántos documentos esperan al
usuario; `GET /pendientes` devuelve los conteos y cada uno llega nulo sin su permiso:

| Opción | Cuenta | Permiso |
|---|---|---|
| Órdenes de compra | por autorizar (`Borrador`) + por recibir (`Autorizada`, `RecibidaParcial`) | `compras.autorizar` / `compras.recibir` |
| Plan sugerido | productos con faltante por producir para mañana, con referencia (como abre la pantalla) | `produccion.planear` |
| Órdenes de producción | por liberar (`Planeada`) + por producir (`Liberada`) + en proceso (`EnProceso`) | `produccion.planear` / `produccion.operar` / `produccion.cerrar` |
| Liquidaciones | por confirmar producto (`Abierta`) + por revisar (`ProductoConfirmado` con revisión) + por cerrar (`Revisada`, o `ProductoConfirmado` sin revisión; RN-LOG-20) | `logistica.liquidar_producto` / `logistica.revisar_liquidacion` / `logistica.liquidar` |
| Pedidos | por validar (origen App, `Capturado`; RN-VEN-18) | `ventas.pedidos` |
| Incidencias | `Abierta` | `ventas.supervisar` |
| Clientes | con ubicación propuesta por aprobar | `ventas.clientes` |
| Autofactura | solicitudes `Recibida` o `Error` | `facturacion.emitir` |

Los pendientes son de un permiso, no de una persona: el globo se apaga para todos cuando alguien atiende el documento;
no hay "leído" ni historial. Se refresca cada minuto, al navegar, al volver a la pestaña y después de guardar un cambio.
Una sección plegada suma los globos de sus opciones; con el menú contraído el globo va sobre el ícono, y en el celular
el botón del menú muestra el total con el menú cerrado. Arriba de 99 se muestra "99+". Tomado de RN-118 de Panissimo
(decidido con el usuario el 2026-10-29).

Pautas:
- Captura rápida en tablets de planta: formularios de una columna, teclado numérico, botones
  grandes en Producción.
- Listados con filtros por fecha operativa, estado y ruta; exportar a CSV.
- Toda acción de cambio de estado pide confirmación y muestra el resultado (movimientos generados).

## Carga inicial por CSV (T-7.3, T-8.14)

Pantalla Catálogos → Carga inicial. Cada plantilla se descarga con encabezados y una fila de ejemplo
(`GET /importacion/{entidad}/plantilla`), admite vista previa (`validarSolo=true`) y se importa en una sola
transacción: con un error en cualquier fila no se guarda nada y se reportan fila y columna. Los importadores reusan
los handlers de cada módulo (mismas validaciones que la captura en pantalla). Orden sugerido, porque cada una usa
claves de las anteriores:

| Plantilla | Llave | Columnas (`*` obligatoria) | Permiso |
|---|---|---|---|
| `productos` | sku | sku\*, nombre\*, tipo\*, unidad_base\*, `nombre_corto`, `producto_a_cobro`, conversiones (`UNIDAD=factor:usos`, incluye uso `traslado`)… | `catalogos.editar` |
| `proveedores` | codigo | … | `compras.editar` |
| `grupos_clientes` | clave | clave\*, nombre\*, canal\*, lista_precio, activo | `ventas.configurar` |
| `clientes` | codigo | …, canal\* (`AUTO`, `DET`, `INST`, `CEDIS`; `TRAD` = `DET`), `grupo` (sin `lista_precio` hereda la del grupo, RN-VEN-12) | `ventas.clientes` |
| `rutas` | codigo | codigo\*, nombre\*, canal\*, chofer (usuario Vendedor), vehiculo (placas), activo; una ruta nueva se crea con su almacén y cliente genérico | `logistica.configurar` |
| `rutas_clientes` | ruta + cliente | ruta\*, cliente\*, orden\*, dias (`L,M,X,J,V,S,D`, nombres o `1`–`7`; vacío = lunes a sábado) | `logistica.configurar` |
| `precios` | lista o cliente + sku + unidad | lista \| cliente (exactamente uno), sku\*, unidad (vacío = base), precio\* (sin impuestos), vigente_desde (solo cliente; vacío = hoy) | `ventas.configurar` |
| `recetas`, `existencias` | — | sin cambios (existencias entran por kardex como ajuste "Carga inicial") | |

Decisiones:
- `rutas_clientes` **reemplaza por ruta**: cada ruta que aparece en el archivo queda solo con sus clientes (los que no
  vienen se quitan; el genérico se queda). Las rutas que no aparecen no se tocan. Valida canal (RN-LOG-31).
- `precios` **combina**: agrega o actualiza los renglones del archivo y deja los demás precios de la lista o del
  cliente como estaban.
- Reimportar la misma plantilla actualiza por la llave, no duplica.
- El catálogo depurado del cliente se cargará con estas plantillas cuando se resuelva P-06 (`docs/cliente/mapeo.md`).

## Portal de autofactura (`apps/portal`)

Pantallas públicas, sin login:
1. Captura de folio y total del ticket (o escaneo del QR, que trae el folio).
2. Datos fiscales: RFC, razón social, régimen, código postal, uso CFDI, correo.
3. Confirmación y estado ("generando", "lista para descargar", "rechazada: motivo").
4. Descarga de XML y PDF.

El QR del ticket apunta a `https://<dominio-portal>/f/<folio>`.
