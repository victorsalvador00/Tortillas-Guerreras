# Módulo Logística (LOG)

Mueve el producto del almacén a las rutas y cierra cada día de ruta con una liquidación que cuadra
producto y efectivo.

## Casos de uso
1. Administrar vehículos y rutas (con su almacén tipo `Ruta`, vendedor y clientes en orden de visita).
2. Preparar la carga inicial del día y recargas (envíos), despacharlas.
3. Asignar pedidos institucionales/autoservicio a una ruta o envío.
4. Registrar retornos de clientes (desde la app o la web).
5. Liquidar la ruta al final del día: contar sobrante, recibir efectivo, cerrar.

## Envíos (cargas)

| Estado | Pasa a | Efecto |
|---|---|---|
| Preparado | EnRuta, Cancelado | editable |
| EnRuta | Liquidado | al despachar: `TraspasoSalida` de PT y `TraspasoEntrada` al almacén de la ruta; baja a la app |
| Liquidado | — | se asocia a la liquidación del día |
| Cancelado | — | solo desde `Preparado` |

## Reglas
- **RN-LOG-01** Una ruta tiene exactamente un almacén tipo `Ruta`; se crea junto con la ruta.
- **RN-LOG-02** Despachar un envío exige existencia suficiente en el almacén origen (RN-INV-02) y
  respeta FEFO para lotes.
- **RN-LOG-03** Solo puede haber una liquidación `Abierta` por ruta y fecha operativa; se crea al
  despachar la primera carga del día.
- **RN-LOG-04** Retorno `DevolucionCliente`: genera `RetornoCliente` (+) en el almacén de la ruta.
  Partidas `NoVendible` generan además `Merma` (−) en ese almacén con el motivo indicado.
  `[SUP-13]` por defecto tortilla devuelta = `NoVendible`.
- **RN-LOG-05** Retorno `RechazoEntrega` (el cliente no recibe un pedido): no hay venta; el
  producto sigue en el almacén de la ruta.
- **RN-LOG-06** Liquidación, por producto y lote:
  `sobrante_esperado = cargado − vendido + devuelto_clientes_vendible` (lo no vendible ya salió
  como merma). `diferencia = sobrante_contado − sobrante_esperado`.
- **RN-LOG-07** Al cerrar la liquidación:
  sobrante `Reintegrar` → traspaso del almacén de ruta a `PT`;
  sobrante `Merma`/`Reproceso` → `Merma` con motivo (reproceso como merma con motivo "Reproceso"
  hasta definir flujo `[SUP-13]`);
  diferencia ≠ 0 → `AjusteSalida`/`AjusteEntrada` con motivo "Faltante de ruta" e incidencia
  `DiferenciaLiquidacion`.
  Al terminar, el almacén de la ruta debe quedar en cero.
- **RN-LOG-08** `efectivo_esperado` = ventas de contado en efectivo + cobros en efectivo del día.
  `diferencia_efectivo` = entregado − esperado; ≠ 0 genera incidencia.
- **RN-LOG-09** No se puede cerrar la liquidación si el dispositivo de la ruta tiene operaciones
  pendientes de sincronizar reportadas en su último contacto (`sync` lo informa); se puede forzar
  con permiso `logistica.forzar_cierre`.
- **RN-LOG-10** Una liquidación cerrada no se reabre; correcciones posteriores son ajustes.

## Endpoints (`/api/v1/logistica`)
| Método | Ruta | Permiso |
|---|---|---|
| GET/POST/PUT | `/vehiculos`, `/rutas`, `/rutas/{id}/clientes` | `logistica.configurar` |
| GET/POST/PUT | `/envios` | `logistica.ver` / `logistica.cargar` |
| POST | `/envios/{id}/despachar`, `/cancelar` | `logistica.cargar` |
| GET/POST | `/retornos` | `logistica.retornos` |
| GET | `/liquidaciones?rutaId&fecha` | `logistica.ver` |
| PUT | `/liquidaciones/{id}/conteo` | `logistica.liquidar` |
| POST | `/liquidaciones/{id}/cerrar` | `logistica.liquidar` |

## Criterios de aceptación
- Ruta R01 carga 300 KG, vende 260 KG, recibe devolución no vendible de 5 KG, cuenta sobrante de
  44 KG → esperado 40 KG, diferencia +4 KG genera `AjusteEntrada` e incidencia; el almacén R01
  queda en 0 al cerrar.

## Aclaraciones de implementación
- Una ruta crea al darse de alta su almacén tipo `Ruta` (mismo código) y su cliente genérico `GEN-<ruta>`, que siempre
  queda en `rutas_clientes`. El código de la ruta no se cambia (va en folios). El vendedor debe tener el rol Vendedor.
- `PUT /rutas/{id}/clientes` reemplaza la lista; los que salen quedan `activo = false` para que la app los borre.
- Un envío en `Preparado` puede llevar partidas sin lote (FEFO al despachar); al despachar las partidas se reemplazan
  por los lotes reales. No se despacha una carga para un día cuya liquidación ya está cerrada (`LOG-007`).
- Retornos: si no se indica lote se usa el de la venta original, el más reciente en la camioneta o el último cargado;
  sin condición, el producto terminado con vida útil ≤ 2 días es `NoVendible` `[SUP-13]` con motivo
  `DEV_NO_VENDIBLE`. Un retorno con fecha de una liquidación ya cerrada se aplica y genera incidencia (RN-SYN-06).
- Liquidación: el detalle abierto se calcula al momento (envíos del día, kardex de ventas, retornos y lo que haya en la
  camioneta). Al cerrar, el ajuste por diferencia se calcula contra la existencia real del lote para garantizar que el
  almacén quede en cero; en el caso normal coincide con `sobrante_contado − sobrante_esperado`. Sobrante a merma usa el
  motivo indicado (o `OTRO`); a reproceso, `REPROCESO`. Se exige efectivo entregado y conteo de toda partida con
  esperado ≠ 0. Una incidencia `DiferenciaLiquidacion` por diferencias de producto y otra por diferencia de efectivo.
- RN-LOG-09: `IEstadoSincronizacionRuta` (por defecto, suma de `operaciones_pendientes` de los dispositivos activos del
  vendedor). Forzar requiere `logistica.forzar_cierre` y `{ "forzar": true }`; queda `cerrada_forzada = true`.
