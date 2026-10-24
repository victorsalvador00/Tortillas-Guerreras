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
| SUP-01 | Roles: Administrador, Direccion, Compras, Produccion, Almacen, Logistica, Vendedor, Facturacion, Supervisor. **v0.2:** + Captura y Liquidaciones; Vendedor = chofer/operador | Seguridad | Cliente | Cambiado (v0.2) |
| SUP-02 | Producto terminado maneja lote; tortilla vida útil 2 días, masa 1 día | Catálogos, Inventario | Cliente | Abierto |
| SUP-03 | Claves SAT de productos y tasas: tortilla/masa IVA 0 %, totopos IVA 16 % + IEPS 8 % | Catálogos, Facturación | Contador del cliente | Abierto |
| SUP-04 | Se permiten compras directas sin OC con permiso; tolerancia de recepción 5 %; sin cuentas por pagar | Compras | Cliente | Abierto |
| SUP-05 | Producción por turnos (Matutino, Vespertino, Nocturno) y líneas (molino, tortilladora) | Producción | Cliente | Abierto |
| SUP-06 | Costeo promedio ponderado global por producto, sin costos indirectos | Inventario, Producción | Cliente / contador | Abierto |
| SUP-07 | Precios de lista sin impuestos. **v0.2:** lista por canal o por corporativo + precio especial por cliente (RN-VEN-12) | Ventas | Cliente | Cambiado (v0.2) |
| SUP-08 | Hay ventas a crédito con límite y cobranza en ruta | Ventas, App | Cliente | Confirmado que existen (hojas de liquidación). La cobranza de clientes queda **fuera del spec v0.2**; lo implementado no cambia |
| SUP-09 | Umbral GPS 150 m; precisión máxima aceptable 100 m | Ventas | Cliente | Abierto |
| SUP-10 | Se permite vender en ruta aunque la camioneta quede en negativo (con incidencia) | Inventario, App | Cliente | Abierto |
| SUP-11 | Autofactura hasta fin del mes de la venta; factura global mensual | Facturación | Contador | Abierto |
| SUP-12 | No se consume sin existencia en producción; multinivel por inventario (masa → tortilla) | Producción | Cliente | Abierto |
| SUP-13 | Tortilla devuelta = no vendible → merma; reproceso se registra como merma con motivo | Logística | Cliente | Confirmado: devolución = merma; sobrante = reutilizable si la caducidad lo permite |
| SUP-14 | La app no edita ni cancela ventas; la cancelación se hace en web el mismo día | Ventas, Sync | Cliente | Abierto |
| SUP-15 | Cancelación de facturas desde el sistema; portal con límite por IP y captcha simple | Facturación | Cliente | Abierto |
| SUP-16 | Teléfonos Android | App | Cliente | Confirmado: Android, APK instalado directo (RN-APP-10) |
| SUP-17 | Impresión de tickets en ruta opcional (ESC/POS Bluetooth); por defecto se comparte por WhatsApp | App | Cliente | Abierto |
| SUP-18 | Dominio propio del cliente con subdominios `app`, `api`, `factura` | Infra | Cliente | Abierto |
| SUP-19 | Tolerancia de devolución antes de "producto a cobro": 11 días medidos desde la fecha del lote, configurable por producto. **Contradice SUP-02** (tortilla con 2 días de vida útil): confirmar vida útil real del producto empacado y desde cuándo se cuentan los días | Logística, Catálogos | Almacén | Abierto (v0.2) |
| SUP-20 | El producto a cobro y los faltantes de producto se valúan a precio de lista del canal de la ruta | Logística | Cliente / crédito y cobranza | Abierto (v0.2) |
| SUP-21 | Faltante de producto y de efectivo generan cargo automático al chofer; diferencia de precio solo si el supervisor lo decide | Logística | Cliente | Abierto (v0.2) |
| SUP-22 | El corte semanal va de lunes a domingo | Logística | Crédito y cobranza | Abierto (v0.2) |
| SUP-23 | Sin pedido, el estimado propone lo del mismo día de la semana anterior (7 días) | Producción | Cliente | Abierto (v0.2) |
| SUP-24 | Un chofer lleva una sola ruta por día (`logistica.chofer_una_ruta_por_dia`, por defecto `true`) | Logística, Sync | Cliente | Abierto (v0.2) |
| SUP-25 | Lo retirado en un cambio físico es `NoVendible` por defecto | Logística, App | Almacén | Abierto (v0.2) |
| SUP-26 | Paquetes por caja propios de cada producto (deducidos de los formatos: 24 T-800 y HNA, 18 sopes, 15 tostadas, 30 mini, 36 flautas) | Catálogos | Cliente | Abierto (v0.2) |
| SUP-27 | Los gastos de ruta se capturan en la liquidación como texto libre, sin catálogo de conceptos | Logística | Cliente | Abierto (v0.2) |
| SUP-30 | Las columnas del estimado son los productos con demanda en la fecha (o en la referencia) más los que tienen precio en una lista activa del canal ("marcados para el canal") | Producción | Cliente | Abierto (v0.2) |
| SUP-29 | Un pedido a un cliente de un canal que pide por ruta (DET) que no es el genérico se acepta como pedido por cliente (no se redirige al pedido global de la ruta) | Ventas | Cliente | Abierto (v0.2) |
| SUP-28 | Una venta de la app con un producto exclusivo de otro cliente (catálogo desactualizado) se acepta con incidencia `ProductoExclusivo`, porque el producto ya se entregó | Ventas, Sync | Cliente | Abierto (v0.2) |

## Decisiones pendientes

| ID | Tema | Bloquea | Falta |
|---|---|---|---|
| D-06 | Integración de facturación: agente local con SDK de CONTPAQi (preferida) o API de facturación en la nube | T-6.4 | Producto y versión exactos (AdminPAQ o CONTPAQi Comercial) y si timbra CFDI 4.0; equipo Windows encendido 24/7; facturas por mes y PAC actual |

## Preguntas abiertas (v0.2)

| ID | Pregunta | Bloquea |
|---|---|---|
| P-01 | ¿Cómo consumen CONTPAQi (servidor propio o servicio contratado)? Especificaciones del asesor | D-06 (fuera de v0.2) |
| P-02 | ¿Entra la gestión de cartera de clientes? (Gerardo) | Fuera de v0.2 |
| P-03 | Listas de precios por canal, corporativo y cliente | T-8.14 |
| P-04 | ¿Qué son los "descuentos" del pedido de autoservicio por tienda? | Puede cambiar RN-VEN-17 |
| P-05 | Catálogo de rutas real (2 de CEDIS: ¿R151 y R152?) con chofer y vehículo predeterminados | T-8.14 |
| P-06 | Inconsistencias del catálogo (`docs/cliente/mapeo.md`) | T-8.14 |
| P-07 | ¿Qué son PUEBLA, MÉRIDA y `AUT-FORA`? (¿rutas, almacenes o sucursales?) | Fuera de v0.2 |
| P-08 | Marca: "Guerreros del Maíz" vs logotipo "Ochoa" en los formatos y productos "OCHOA"/"O SABOR" | Logotipos, ticket, hoja de liquidación |

## Preguntas abiertas para el cliente (v0.1)
- ¿Cuántas rutas, vendedores, dispositivos y clientes por ruta?
- ¿Cuántos almacenes físicos (materia prima, producto terminado, centros de distribución)?
- ¿Se vende masa además de tortilla? ¿Qué otros productos producen y cuáles revenden?
- ¿Cómo manejan hoy las devoluciones de las tiendas (cambio, crédito, merma)?
- ¿Quién cierra la ruta al final del día y cómo entregan el efectivo?
- ¿Usan pedidos de clientes institucionales/autoservicio con orden de compra del cliente?
