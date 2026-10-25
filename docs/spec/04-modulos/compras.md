# Módulo Compras (COM)

Registra lo que entra al negocio desde proveedores: materias primas, empaques y productos de reventa.

## Casos de uso
1. Administrar proveedores.
2. Crear orden de compra (borrador), editarla, autorizarla, cancelarla.
3. Registrar recepción total o parcial contra una orden.
4. Registrar compra directa sin orden `[SUP-04]` (requiere permiso `compras.compra_directa`).
5. Cancelar una recepción (genera contra-movimiento).
6. Consultar historial de precios por proveedor y producto.
7. Administrar el catálogo de productos de cada proveedor (qué se le compra, unidad, clave, precio pactado, días de
   entrega y proveedor preferido) y consultar qué proveedores venden un producto.

## Estados de la orden de compra

| Estado | Pasa a | Disparador |
|---|---|---|
| Borrador | Autorizada, Cancelada | autorizar / cancelar |
| Autorizada | RecibidaParcial, Recibida, Cancelada | recepción / cancelar (sin recepciones) |
| RecibidaParcial | Recibida, Cerrada | recepción que completa / cerrar con faltante |
| Recibida | — | |
| Cerrada | — | cierre manual con faltante |
| Cancelada | — | |

## Reglas
- **RN-COM-01** Solo productos con `es_comprable = true`.
- **RN-COM-02** Una orden solo se edita en `Borrador`.
- **RN-COM-03** Autorizar requiere permiso `compras.autorizar` y al menos una partida.
- **RN-COM-04** Se puede recibir más de lo pedido hasta una tolerancia de `compras.tolerancia_pct`
  (por defecto 5 %) `[SUP-04]`; arriba de eso se rechaza.
- **RN-COM-05** Cada partida recibida genera un movimiento `EntradaCompra` en el almacén destino
  con `costo_unitario_base = precio de la partida / factor_a_base` (sin IVA).
- **RN-COM-06** La entrada recalcula el costo promedio ponderado del producto (ver Inventario).
- **RN-COM-07** Cancelar una recepción genera `AjusteSalida` por las mismas cantidades; se rechaza
  si la existencia actual del lote no alcanza.
- **RN-COM-08** Al registrar una recepción se actualizan `proveedores_productos.ultimo_precio`,
  `ultima_unidad_id` y `ultima_compra_en`; si el producto no estaba en el catálogo del proveedor (compra directa)
  se agrega activo con la unidad y el precio recibidos como pactado.
- **RN-COM-09** El lote de cada partida recibida de un producto con `maneja_lote` lo **genera el sistema** al
  registrar la recepción; no se captura (guía: RN-117 de panissimo). Formato `{SKU sin separadores, en mayúsculas}-{fecha
  operativa DDMMAA}-{NN}`, donde `NN` es el consecutivo de ese producto en ese día con al menos 2 dígitos (`01`, `02`…;
  pasa a 3 después de `99`). Ej.: `MP-MAIZ-BCO` recibido el 25/10/2026 → `MPMAIZBCO-251026-01`. Cada partida (también
  dos del mismo producto en una recepción, o dos recepciones simultáneas) toma su propio consecutivo; cuentan los lotes
  de recepciones anteriores aunque estén canceladas. La caducidad se sigue capturando si `vida_util_dias` no es nulo y
  el proveedor la informa. El lote del proveedor no se registra. La pantalla muestra una **vista previa** de los lotes
  (`GET /recepciones/lotes-propuestos`); no reserva el consecutivo, así que el lote final puede cambiar si otra
  recepción del mismo producto se registra antes.
- **RN-COM-10** El catálogo de un proveedor solo admite productos comprables y activos, una vez cada uno; la unidad
  es la base o una con conversión de compra. El precio pactado es **opcional** (sin IVA, ≥ 0, por esa unidad); los
  días de entrega van de 0 a 365. Los productos de un proveedor inactivo no se editan hasta reactivarlo (`COM-020`).
- **RN-COM-11** Un solo proveedor preferido por producto: marcar uno desmarca el anterior. Una relación inactiva nunca
  es preferida y desactivar un proveedor le quita sus preferencias.
- **RN-COM-12** `[SUP-32]` Con `compras.solo_catalogo_proveedor = true` (por omisión) una orden de compra solo acepta
  productos activos del catálogo del proveedor (`COM-017`). El precio se sugiere del pactado; si no hay, del último
  pagado; siempre es editable. Las compras directas no se bloquean (alimentan el catálogo, RN-COM-08).

Fuera de alcance por ahora: cuentas por pagar y facturas de proveedor `[SUP-04]`.

## Endpoints (`/api/v1/compras`)
| Método | Ruta | Permiso |
|---|---|---|
| GET/POST/PUT | `/proveedores` | `compras.ver` / `compras.editar` |
| GET | `/proveedores/{id}/productos?incluirInactivos` | `compras.ver` |
| POST / PUT | `/proveedores/{id}/productos`, `/proveedores/{id}/productos/{productoId}` | `compras.editar` |
| GET | `/productos/{productoId}/proveedores` (preferido primero, luego más barato por unidad base) | `compras.ver` |
| GET/POST | `/ordenes-compra` | `compras.ver` / `compras.editar` |
| GET/PUT | `/ordenes-compra/{id}` | |
| POST | `/ordenes-compra/{id}/autorizar` | `compras.autorizar` |
| POST | `/ordenes-compra/{id}/cancelar`, `/cerrar` | `compras.autorizar` |
| GET/POST | `/recepciones` | `compras.recibir` |
| GET | `/recepciones/lotes-propuestos?productoIds` | `compras.recibir` |
| POST | `/recepciones/{id}/cancelar` | `compras.autorizar` |

## Criterios de aceptación
- Recibir 1 TON de maíz a $6,500/TON genera `EntradaCompra` de +1000 KG con costo 6.5/KG.
- Dos recepciones parciales que suman lo pedido dejan la orden en `Recibida`.
- Una recepción que excede la tolerancia devuelve `COM-004`.
