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

Pautas:
- Captura rápida en tablets de planta: formularios de una columna, teclado numérico, botones
  grandes en Producción.
- Listados con filtros por fecha operativa, estado y ruta; exportar a CSV.
- Toda acción de cambio de estado pide confirmación y muestra el resultado (movimientos generados).

## Portal de autofactura (`apps/portal`)

Pantallas públicas, sin login:
1. Captura de folio y total del ticket (o escaneo del QR, que trae el folio).
2. Datos fiscales: RFC, razón social, régimen, código postal, uso CFDI, correo.
3. Confirmación y estado ("generando", "lista para descargar", "rechazada: motivo").
4. Descarga de XML y PDF.

El QR del ticket apunta a `https://<dominio-portal>/f/<folio>`.
