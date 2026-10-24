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
  rechaza en web. **(v0.2)** Antes de la lista se busca un precio especial vigente en `precios_cliente`
  (RN-VEN-12). En la app se usa el precio descargado; si al sincronizar difiere del vigente, se
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

### Reglas nuevas (v0.2)
- **RN-VEN-11** Canales `AUTO`, `DET`, `INST`, `CEDIS` (DET reemplaza a TRAD). La coincidencia del canal del
  cliente con el de sus rutas la valida Logística (RN-LOG-31), que depende de Ventas; Ventas no consulta rutas.
  `CrearClienteGenericoAsync` recibe el canal de la ruta.
- **RN-VEN-12** Precio especial por cliente: si existe `precios_cliente` activo con `vigente_desde ≤ fecha` para
  (cliente, producto, unidad), gana sobre la lista. Al aplicar una venta, cada partida guarda en `precio_referencia`
  el precio vigente del cliente (especial o lista); lo usa la diferencia de precios de la liquidación (RN-LOG-23). Un cliente nuevo de un grupo hereda la `lista_precio_id` del
  grupo si no se indica otra.
- **RN-VEN-13** Cambiar un precio (lista o especial) no afecta pedidos ya `Confirmado` ni ventas registradas; los
  pedidos `Capturado` se recalculan al confirmar.
- **RN-VEN-14** Si un producto tiene registros activos en `productos_exclusivos`, solo puede venderse o pedirse a
  los clientes listados o a los de los grupos listados (`VEN-019`). Sin registros = disponible para todos.
  Una venta de la app con un producto exclusivo ajeno ya se entregó: se aplica con incidencia `ProductoExclusivo`
  [SUP-28].
- **RN-VEN-15** Pedidos con `fecha_entrega` ≥ fecha operativa de captura; se aceptan fechas futuras sin límite
  (entregas de fin de semana, pedidos "para el lunes"); fecha anterior: `VEN-021`. El consolidado, el estimado y las cargas sugeridas usan
  `fecha_entrega`, nunca `fecha`.
- **RN-VEN-16** Canal con `pedido_por = Ruta` (DET): el pedido es **global de la ruta**, al cliente genérico
  `GEN-<ruta>`, con `es_ruta = true`; hay a lo más uno no cancelado por ruta y fecha de entrega (`VEN-020`; si ya
  existe se edita). Canal con `pedido_por = Cliente`: un pedido por cliente y fecha de entrega (se permite más de
  uno; la web avisa si ya existe).
- **RN-VEN-17** Partidas `CambioFisico`: precio 0, no suman a subtotal ni impuestos, no generan venta al entregarse
  (se entregan con un retorno `CambioFisico`, RN-LOG-15); sí suman a la carga (RN-LOG-11/14). **No cuentan** para
  el estado del pedido: `Entregado`/`EntregadoParcial` se calcula solo con las partidas `Venta` (RN-VEN-08).
- **RN-VEN-18** *Validación en oficina*: un pedido de la app entra `Capturado` con `cantidad_solicitada` =
  `cantidad`. Mientras esté `Capturado`, Captura puede corregir cantidades, agregar o quitar partidas; al
  **confirmar** queda fijo (regla actual) y se registra `confirmado_por/en`. Cada corrección queda en la bitácora con
  antes y después (RN-SEG-06). La app no edita pedidos ya enviados (`[SUP-14]` se mantiene).

**Público para otros módulos (v0.2):** `IVentasService` agrega `DemandaPorFechaEntregaAsync(fecha, canalId?)`
(partidas por ruta y producto, con tipo), `GuardarPedidoRutaAsync` (RN-VEN-16, para la cuadrícula del estimado) y
`ConfirmarPedidosAsync(ids)`, que consume Producción (RN-PRO-11/13); `ConsumosPromedioAsync(clienteIds, n)`
(promedio por cliente y producto de las últimas `ruta.consumo_promedio_visitas` ventas/entregas, para la bajada
`consumos_clientes`, RN-APP-09), que consume Sincronización; y `CrearClienteGenericoAsync(ruta, canalId)`.
El historial de un pedido se consulta en `/seguridad/bitacora?documentoId=` (RN-SEG-07).

**Errores nuevos (v0.2):** `VEN-019` producto exclusivo de otro cliente (RN-VEN-14), `VEN-020` pedido de ruta
duplicado (RN-VEN-16), `VEN-021` fecha de entrega anterior a la fecha operativa (RN-VEN-15).

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
| GET | `/pedidos?estado&clienteId&rutaId&desde&hasta&envioId&sinCarga` (filtros de la lista; `envioId`/`sinCarga` para las cargas, RN-LOG-11) | `ventas.pedidos` |
| GET/POST | `/ventas` | `ventas.ver` / `ventas.registrar` |
| POST | `/ventas/{id}/cancelar` | `ventas.cancelar` |
| GET/POST | `/cobros` | `ventas.cobrar` |
| GET | `/incidencias`, `POST /incidencias/{id}/revisar` | `ventas.supervisar` |

**(v0.2)**
| Método | Ruta | Permiso |
|---|---|---|
| GET/POST/PUT | `/grupos-clientes` | `ventas.configurar` (GET: cualquier usuario autenticado, lo usa el formulario de clientes) |
| GET/PUT | `/clientes/{id}/precios` (precios especiales; el PUT reemplaza el conjunto y desactiva los que faltan) | `ventas.configurar` (GET: autenticado) |
| GET/PUT | `/productos/{id}/exclusivos` (el PUT reemplaza el conjunto) | `ventas.configurar` (GET: autenticado) |
| GET | `/pedidos?origen=App&estado=Capturado` (bandeja "Por validar") | `ventas.pedidos` |

**Notas de implementación (T-8.5).** El precio vigente (especial o lista) se resuelve en un solo lugar
(`Precios.VigenteAsync`) y lo usan `RegistrarVenta` (web y app), la entrega de pedidos (referencia) y los pedidos
(captura y confirmación). En la app, la incidencia `PrecioDistinto` compara contra el vigente, no contra la lista.
La entrega de un pedido conserva el precio del pedido y guarda como `precio_referencia` el vigente de ese día.
`GuardarClienteRequest.listaPrecioId` es opcional si se indica `grupoClienteId` con lista.

Las ventas, entregas, retornos y cobros desde la app entran por `sync` (ver
`05-sincronizacion-offline.md`), que invoca los mismos handlers.

## Criterios de aceptación

(v0.2)
- Un cliente de detalle con precio especial de T-800 vende a ese precio en la app y en la web; otro cliente del
  mismo canal sigue con la lista.
- Un producto exclusivo de SANBORNS no aparece en la app para clientes de otros grupos y la API rechaza un pedido con
  `VEN-019`.
- Un pedido de la app con 20 paquetes que Captura corrige a 15 deja `cantidad_solicitada = 20`, `cantidad = 15` y la
  bitácora muestra el cambio con usuario y hora.

(v0.1)
- Una venta de la app con GPS a 400 m del cliente aprobado se guarda con
  `requiere_revision = true` e incidencia `GpsFueraDeRango`.
- Reenviar la misma venta (mismo `op_id`) no crea un segundo registro ni un segundo movimiento.
