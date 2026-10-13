# Módulo Compras (COM)

Registra lo que entra al negocio desde proveedores: materias primas, empaques y productos de reventa.

## Casos de uso
1. Administrar proveedores.
2. Crear orden de compra (borrador), editarla, autorizarla, cancelarla.
3. Registrar recepción total o parcial contra una orden.
4. Registrar compra directa sin orden `[SUP-04]` (requiere permiso `compras.compra_directa`).
5. Cancelar una recepción (genera contra-movimiento).
6. Consultar historial de precios por proveedor y producto.

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
- **RN-COM-08** Al registrar una recepción se actualiza `proveedores_productos.ultimo_precio`.
- **RN-COM-09** Productos con `maneja_lote` exigen lote (y caducidad si `vida_util_dias` no es nulo
  y el proveedor la informa).

Fuera de alcance por ahora: cuentas por pagar y facturas de proveedor `[SUP-04]`.

## Endpoints (`/api/v1/compras`)
| Método | Ruta | Permiso |
|---|---|---|
| GET/POST/PUT | `/proveedores` | `compras.ver` / `compras.editar` |
| GET/POST | `/ordenes-compra` | `compras.ver` / `compras.editar` |
| GET/PUT | `/ordenes-compra/{id}` | |
| POST | `/ordenes-compra/{id}/autorizar` | `compras.autorizar` |
| POST | `/ordenes-compra/{id}/cancelar`, `/cerrar` | `compras.autorizar` |
| GET/POST | `/recepciones` | `compras.recibir` |
| POST | `/recepciones/{id}/cancelar` | `compras.autorizar` |

## Criterios de aceptación
- Recibir 1 TON de maíz a $6,500/TON genera `EntradaCompra` de +1000 KG con costo 6.5/KG.
- Dos recepciones parciales que suman lo pedido dejan la orden en `Recibida`.
- Una recepción que excede la tolerancia devuelve `COM-004`.
