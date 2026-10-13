# Módulo Ventas (VEN)

Clientes, precios, pedidos y ventas (en ruta desde la app o desde la web).

## Casos de uso
1. Administrar canales, listas de precio y clientes (incluye datos fiscales CFDI 4.0).
2. Aprobar ubicación GPS capturada en ruta para un cliente nuevo o movido.
3. Capturar pedidos institucionales/autoservicio en la web; confirmarlos; asignarlos a ruta.
4. Registrar venta en ruta (app, offline) o entrega de pedido (app).
5. Registrar venta en web (mostrador o captura posterior) `[SUP-14]`.
6. Cancelar venta (permiso, mismo día operativo).
7. Registrar cobros de ventas a crédito `[SUP-08]`.
8. Revisar incidencias (GPS, existencia negativa, precio distinto).

## Reglas
- **RN-VEN-01** Precio = lista del cliente para el producto y unidad; si no existe, la venta se
  rechaza en web. En la app se usa el precio descargado; si al sincronizar difiere del vigente, se
  respeta el de la app y se crea incidencia `PrecioDistinto`.
- **RN-VEN-02** El servidor recalcula importes: `importe = round(cantidad × precio, 2)`,
  `ieps = round(importe × tasa_ieps, 2)`, `iva = round((importe + ieps) × tasa_iva, 2)`.
  Si difiere del total de la app en más de $0.05 se guarda el del servidor y se crea incidencia.
- **RN-VEN-03** Cada partida genera un movimiento `Venta` (−) en el almacén de la ruta
  (o el almacén indicado en web).
- **RN-VEN-04** Venta a crédito: solo clientes con `condicion_pago = Credito` y saldo + venta ≤
  `limite_credito` (en app se valida con el saldo descargado; el servidor acepta y crea incidencia
  si se excede) `[SUP-08]`.
- **RN-VEN-05** GPS: si `precision_m > gps.precision_maxima_m` → incidencia `GpsImpreciso`; si el
  cliente tiene ubicación aprobada y la distancia > `gps.umbral_distancia_m` → `GpsFueraDeRango` y
  `requiere_revision = true`. Sin GPS → `SinGps`. Nunca se rechaza la venta por GPS.
- **RN-VEN-06** Si el cliente no tiene ubicación aprobada, la primera venta con GPS propone su
  ubicación (`UbicacionPorAprobar`).
- **RN-VEN-07** Cancelar una venta genera el movimiento contrario (+) y solo se permite si no está
  facturada.
- **RN-VEN-08** Entrega de pedido: crea venta `EntregaPedido` ligada al pedido y actualiza
  `cantidad_entregada_base`; el pedido pasa a `Entregado` o `EntregadoParcial`.
- **RN-VEN-09** El folio de ventas de la app es `<ruta>-<dispositivo>-<consecutivo 6 dígitos>` y es
  único global; un duplicado con distinto `op_id` se rechaza (`VEN-009`) y se reporta.
- **RN-VEN-10** Cada ruta tiene un cliente genérico "Público en general" para ventas sin cliente
  registrado.

### Aclaraciones de implementación
- RN-VEN-02: la incidencia por diferencia de total es `TotalDistinto`. RN-VEN-04 en app: `CreditoExcedido`.
- RN-VEN-05/06 aplican solo a ventas de la app y no a clientes genéricos. La ubicación propuesta se guarda en
  `clientes.latitud_propuesta/longitud_propuesta` hasta aprobarse.
- RN-VEN-07: el contra-movimiento es `RetornoCliente` (+) con el mismo costo, documento `Venta` de la venta original.
- Cobros [SUP-08]: saldo = ventas a crédito registradas − cobros registrados; el cobro se aplica FIFO a las ventas
  a crédito con saldo (o a las indicadas). Folios `VTA-`, `PED-`, `COB-` y clientes `CLI-`.

## Endpoints (`/api/v1/ventas`)
| Método | Ruta | Permiso |
|---|---|---|
| GET/POST/PUT | `/canales`, `/listas-precio`, `/listas-precio/{id}/partidas` | `ventas.configurar` |
| GET/POST/PUT | `/clientes` | `ventas.clientes` |
| POST | `/clientes/{id}/aprobar-ubicacion` | `ventas.supervisar` |
| GET/POST/PUT | `/pedidos`, `POST /pedidos/{id}/confirmar`, `/asignar`, `/cancelar` | `ventas.pedidos` |
| GET/POST | `/ventas` | `ventas.ver` / `ventas.registrar` |
| POST | `/ventas/{id}/cancelar` | `ventas.cancelar` |
| GET/POST | `/cobros` | `ventas.cobrar` |
| GET | `/incidencias`, `POST /incidencias/{id}/revisar` | `ventas.supervisar` |

Las ventas, entregas, retornos y cobros desde la app entran por `sync` (ver
`05-sincronizacion-offline.md`), que invoca los mismos handlers.

## Criterios de aceptación
- Una venta de la app con GPS a 400 m del cliente aprobado se guarda con
  `requiere_revision = true` e incidencia `GpsFueraDeRango`.
- Reenviar la misma venta (mismo `op_id`) no crea un segundo registro ni un segundo movimiento.
