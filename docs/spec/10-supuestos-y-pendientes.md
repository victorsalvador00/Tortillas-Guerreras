# 10 — Supuestos y pendientes

## Cómo complementar el spec cuando llegue la información del cliente

El cliente enviará: el recorrido completo de su operación, los datos clave por proceso y ejemplos de
sus Excel. Procedimiento (también disponible como comando `/actualizar-modelo`):

1. Guardar los archivos del cliente en `docs/cliente/` (sin datos personales reales si es posible).
2. Por cada Excel, hacer un mapeo **columna del cliente → tabla.columna** en
   `docs/cliente/mapeo.md`, marcando: existe igual, existe con otro nombre, falta, sobra.
3. Por cada supuesto de la tabla de abajo, decidir: **Confirmado**, **Cambiado** (anotar el nuevo
   valor) o **Sigue abierto**.
4. Actualizar `03-modelo-datos.md` y el módulo afectado en `04-modulos/`; agregar reglas nuevas
   con el siguiente número `RN-…` libre.
5. Agregar las tareas nuevas o cambiadas a `09-plan-implementacion.md` (si la tarea original ya
   se implementó, crear `T-x.y-b` con la migración y el ajuste).
6. Generar migraciones y actualizar datos semilla. Pruebas de las reglas cambiadas.

## Registro de supuestos

| ID | Supuesto (valor por defecto) | Impacta | Confirma | Estado |
|---|---|---|---|---|
| SUP-01 | Roles: Administrador, Direccion, Compras, Produccion, Almacen, Logistica, Vendedor, Facturacion, Supervisor | Seguridad | Cliente | Abierto |
| SUP-02 | Producto terminado maneja lote; tortilla vida útil 2 días, masa 1 día | Catálogos, Inventario | Cliente | Abierto |
| SUP-03 | Claves SAT de productos y tasas: tortilla/masa IVA 0 %, totopos IVA 16 % + IEPS 8 % | Catálogos, Facturación | Contador del cliente | Abierto |
| SUP-04 | Se permiten compras directas sin OC con permiso; tolerancia de recepción 5 %; sin cuentas por pagar | Compras | Cliente | Abierto |
| SUP-05 | Producción por turnos (Matutino, Vespertino, Nocturno) y líneas (molino, tortilladora) | Producción | Cliente | Abierto |
| SUP-06 | Costeo promedio ponderado global por producto, sin costos indirectos | Inventario, Producción | Cliente / contador | Abierto |
| SUP-07 | Precios de lista sin impuestos, una lista por canal | Ventas | Cliente | Abierto |
| SUP-08 | Hay ventas a crédito con límite y cobranza en ruta | Ventas, App | Cliente | Abierto |
| SUP-09 | Umbral GPS 150 m; precisión máxima aceptable 100 m | Ventas | Cliente | Abierto |
| SUP-10 | Se permite vender en ruta aunque la camioneta quede en negativo (con incidencia) | Inventario, App | Cliente | Abierto |
| SUP-11 | Autofactura hasta fin del mes de la venta; factura global mensual | Facturación | Contador | Abierto |
| SUP-12 | No se consume sin existencia en producción; multinivel por inventario (masa → tortilla) | Producción | Cliente | Abierto |
| SUP-13 | Tortilla devuelta = no vendible → merma; reproceso se registra como merma con motivo | Logística | Cliente | Abierto |
| SUP-14 | La app no edita ni cancela ventas; la cancelación se hace en web el mismo día | Ventas, Sync | Cliente | Abierto |
| SUP-15 | Cancelación de facturas desde el sistema; portal con límite por IP y captcha simple | Facturación | Cliente | Abierto |
| SUP-16 | Teléfonos Android | App | Cliente | Abierto |
| SUP-17 | Impresión de tickets en ruta opcional (ESC/POS Bluetooth); por defecto se comparte por WhatsApp | App | Cliente | Abierto |
| SUP-18 | Dominio propio del cliente con subdominios `app`, `api`, `factura` | Infra | Cliente | Abierto |

## Decisiones pendientes

| ID | Tema | Bloquea | Falta |
|---|---|---|---|
| D-06 | Integración de facturación: agente local con SDK de CONTPAQi (preferida) o API de facturación en la nube | T-6.4 | Producto y versión exactos (AdminPAQ o CONTPAQi Comercial) y si timbra CFDI 4.0; equipo Windows encendido 24/7; facturas por mes y PAC actual |

## Preguntas abiertas para el cliente
- ¿Cuántas rutas, vendedores, dispositivos y clientes por ruta?
- ¿Cuántos almacenes físicos (materia prima, producto terminado, centros de distribución)?
- ¿Se vende masa además de tortilla? ¿Qué otros productos producen y cuáles revenden?
- ¿Cómo manejan hoy las devoluciones de las tiendas (cambio, crédito, merma)?
- ¿Quién cierra la ruta al final del día y cómo entregan el efectivo?
- ¿Usan pedidos de clientes institucionales/autoservicio con orden de compra del cliente?
