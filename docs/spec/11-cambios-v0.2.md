# 11 — Cambios de la versión 0.2 (reunión del 24-oct-2026)

**Estado:** propuesta para revisión. Parte del código en `main` al 24-oct-2026 (`75db3cc`): fases 0 a 7
terminadas salvo T-6.4. Este archivo resume **qué cambia y por qué**; el detalle normativo quedó en cada documento
del spec (`03`, `04-modulos/*`, `05`, `06`, `07`, `09`, `10`), marcado con **(v0.2)**.

Fuentes: reunión con almacén y autoservicio del 24-oct (transcripción en el proyecto de Claude, no en el repo),
formatos en papel del cliente (estimados de detalle y autoservicio, producción-almacén, pedido de autoservicio por
tienda, liquidaciones de institucional/CEDIS y de detalle) y `Códigos de Venta depurados.xlsx`. El mapeo está en
`docs/cliente/mapeo.md`.

## Alcance

| Incluido | Fuera de este spec |
|---|---|
| 4 canales, rutas por canal, chofer y vehículo por carga | **Facturación**: integración CONTPAQi (D-06), código de facturación por producto, cambios al portal |
| Pedidos capturados en la app, validación en oficina | **Cobranza de clientes**: cartera, estados de cuenta, límites de crédito, gestión de saldos. Ventas a crédito y cobros (`ventas.cobros`) **se quedan como están hoy**; la liquidación solo los muestra |
| Estimado de producción por ruta, plan sugerido y cargas sugeridas | Cálculo de comisiones (lo hace crédito y cobranza con el corte semanal) |
| Cambios físicos y cambios pendientes por cliente | Plazas foráneas (Puebla, Mérida, `AUT-FORA`): pendiente de definir (P-07 en `10`) |
| Liquidación v2: columnas del formato, formato por canal, etapas con responsable, gastos, cheques | Marca y logotipos (P-08 en `10`) |
| **Cobranza a choferes**: producto a cobro, faltantes de producto y efectivo, diferencias de precio, cuenta del chofer y corte semanal | |
| Precios por cliente, grupos de clientes (corporativos), productos exclusivos | |
| Producto: nombre corto, unidad de traslado (cajas + paquetes), bandera de producto a cobro | |
| Bitácora de cambios consultable | |
| App: build APK y política de liberación | |

## Decisiones de diseño

| # | Decisión | Por qué |
|---|---|---|
| V2-01 | El **pedido** sigue siendo la unidad de demanda. Detalle captura un pedido global por ruta al cliente genérico `GEN-<ruta>` | Evita una entidad "estimado" paralela; RN-LOG-11 ya sabe meter pedidos en una carga |
| V2-02 | El "estimado de producción" es una **vista** (rutas × productos) sobre los pedidos de una fecha de entrega, editable para pedidos capturados en oficina | Reproduce el formato en papel sin duplicar datos |
| V2-03 | Sin pedido todavía, el estimado propone el **mismo día de la semana anterior** | Es como trabajan hoy ("los lunes autoservicio lleva ~70 cajas"); no requiere tabla de estimados base |
| V2-04 | El borrador del pedido en la app vive **solo en el teléfono**; "Enviar" sube la operación `CapturarPedido` | Mantiene el principio de sync de solo agregar operaciones (05) |
| V2-05 | El chofer y el vehículo se eligen **en cada carga**; `rutas.vendedor_id` pasa a ser el chofer predeterminado | No hay relación fija chofer–camión–ruta |
| V2-06 | El alcance de la app es la **ruta de la carga asignada al chofer** en el día; si no tiene carga, la ruta donde es chofer predeterminado | Hoy el alcance sale solo de `rutas.vendedor_id` (`RutaDeVendedorAsync`) |
| V2-07 | Un cambio físico es un **retorno** de tipo `CambioFisico` con partidas retiradas y entregadas. Lo no entregado queda como cambio pendiente del cliente en Logística | Logística ya depende de Ventas; los pendientes no pueden vivir en Ventas sin crear un ciclo |
| V2-08 | Los movimientos de inventario de la liquidación se generan al **confirmar el producto** (etapa de almacén), no al cierre de dinero | El sobrante reutilizable debe estar en `PT` para la carga del día siguiente aunque el dinero se cierre después |
| V2-09 | La cobranza a choferes vive en **Logística** (`cargos_chofer`, `cortes_chofer`) | Nace de la liquidación; no es cobranza de clientes |
| V2-10 | La bitácora la escribe un interceptor de EF del SharedKernel en `seguridad.bitacora`, en la misma transacción | Cubre web y app sin tocar cada handler |
| V2-11 | Los códigos de ruta se quedan sin guion (`R1`…`R14`, `R51`…`R61`, `R101`…`R103`, `R151`…) | El código va dentro del folio `<ruta>-<dispositivo>-<consecutivo>` y la validación actual es `^[A-Za-z0-9]{1,8}$` |

## Trazabilidad (requerimiento → spec → tarea)

| Requerimiento (reunión) | Reglas | Tarea |
|---|---|---|
| RQ-15/16 canales y rutas por canal | RN-VEN-11, RN-LOG-31 | T-8.1, T-8.4, T-8.14 |
| RQ-17 precios por corporativo y por cliente | RN-VEN-01 (mod.), RN-VEN-12, RN-VEN-13 | T-8.5 |
| RQ-36 productos exclusivos | RN-VEN-14 | T-8.5 |
| RQ-18/34 cajas y paquetes; RQ-35 nombre corto | RN-CAT-08, RN-CAT-09 | T-8.1 |
| RQ-08 chofer y vehículo por carga | RN-LOG-12, RN-LOG-13, RN-SYN-09 | T-8.4 |
| RQ-01/02/03/05 pedidos desde la app y validación | RN-VEN-15 a 18, RN-SYN-10 | T-8.6, T-8.12 |
| RQ-04/39 entregas a futuro | RN-VEN-15 | T-8.6 |
| RQ-07/37/38 estimado por ruta, real y recargas | RN-PRO-11, RN-PRO-12 | T-8.7 |
| RQ-06 plan sugerido y cargas sugeridas | RN-PRO-13, RN-LOG-14 | T-8.7, T-8.8 |
| RQ-19 misma receta, distinta presentación | RN-PRO-14 (copiar receta) | T-8.7 |
| RQ-11/12 cambios físicos y pendientes | RN-LOG-15 a 18, RN-INV-11, RN-SYN-11 | T-8.9, T-8.12 |
| RQ-09/10/32 liquidación v2 | RN-LOG-06 (mod.), RN-LOG-07 (mod.), RN-LOG-19 a 22 | T-8.10 |
| RQ-31 dinero de la liquidación (sin cobranza de clientes) | RN-LOG-08 (mod.), RN-LOG-23 | T-8.10 |
| RQ-13/14/33 cobranza a choferes | RN-LOG-24 a 30, RN-CAT-10 | T-8.11 |
| RQ-24 bitácora | RN-SEG-06, RN-SEG-07 | T-8.2, T-8.3 |
| RQ-28 cantidades confiables | RN-APP-09 | T-8.12 |
| RQ-26/27 APK y liberaciones | RN-APP-10, RN-APP-11 | T-8.13 |
| RQ-40 catálogo depurado | RN-CAT-07 | T-8.14 |

Los RQ-20, 22 y 23 (facturación) no tienen tarea en esta versión.

## Qué no cambia

- Kardex, costo promedio, FEFO, compras, recetas y órdenes de producción (salvo la acción "copiar receta").
- Ventas en ruta, entregas de pedido, retornos `DevolucionCliente`/`RechazoEntrega`, GPS e incidencias.
- Cobros y ventas a crédito (se muestran en la liquidación, sin reglas nuevas).
- Facturación, portal y factura global.
