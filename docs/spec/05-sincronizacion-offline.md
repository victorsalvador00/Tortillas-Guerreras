# 05 — Sincronización offline (SYN)

La app trabaja siempre contra su base local. La sincronización tiene dos direcciones:
**bajada** (pull) de catálogos y cargas, y **subida** (push) de operaciones.

## Principios
1. Lo que sube son **operaciones que solo se agregan** (ventas, entregas, retornos, cobros,
   ubicaciones). No hay ediciones de registros desde la app; una corrección es otra operación
   (ej. `CancelarVenta` si se permite) `[SUP-14]`.
2. Cada operación tiene `opId` (UUID v7 generado en el teléfono) y la API es idempotente.
3. El servidor es la fuente de verdad para catálogos (gana el servidor).
4. Una operación nunca se pierde: si se rechaza, queda visible en la app con el motivo y en el
   panel de incidencias.

## Bajada: `GET /api/v1/sync/pull?desde=<cursor>&limite=500`

Devuelve cambios con `version_sync > desde` de las colecciones del alcance del dispositivo
(su ruta asignada):

> **(v0.2) Alcance (RN-SYN-09):** la ruta del dispositivo es la de la carga `Preparado`/`EnRuta` cuyo `chofer_id` es
> el usuario, para la fecha operativa de hoy (o la de ayer si sigue `EnRuta`); si no tiene carga, la ruta donde es
> chofer predeterminado (`rutas.vendedor_id`, comportamiento actual de `RutaDeVendedorAsync`). Si la ruta del alcance
> cambia, el servidor responde `rutaCambiada = true` y la app hace una bajada completa (`desde = 0`) de las
> colecciones por ruta, conservando su cola de operaciones y su consecutivo.

| Colección | Filtro |
|---|---|
| productos (+ conversiones, unidades) | `es_vendible` y activos |
| clientes | clientes de la ruta + genérico |
| rutas_clientes | de la ruta |
| listas_precio_partidas | listas usadas por los clientes de la ruta |
| envios + envios_partidas | de la ruta, estado `EnRuta`, últimos 2 días |
| pedidos + partidas | asignados a la ruta, no entregados; con `envioId`/`envioFolio` de la carga en la que van (RN-LOG-11) |
| saldos_clientes | saldo de crédito por cliente `[SUP-08]` |
| motivos_merma | `aplica_ruta` o `aplica_retorno` |
| parametros | claves `gps.*`, `ruta.*` |
| **(v0.2)** preciosCliente | precios especiales de los clientes de la ruta; los desactivados llegan con `activo = false` (la app los borra); un cliente recién asignado baja completo |
| **(v0.2)** productosExclusivos | **todos** los registros activos, también de clientes ajenos a la ruta (sin ellos la app no puede ocultar el producto a los demás); los desactivados van en `eliminados.productosExclusivos` |
| **(v0.2)** cambios_pendientes | `Pendiente` de clientes de la ruta |
| **(v0.2)** consumos_clientes | `IConsultasSyncVentas.ConsumosPromedioAsync` con las últimas `ruta.consumo_promedio_visitas` (8) ventas/entregas por cliente y producto (RN-APP-09); completo, no incremental. Forma: `consumosClientes: [{ clienteId, productoId, promedioBase }]` (unidad base) |
| **(v0.2)** canales | `formato_liquidacion`, `pedido_por` del canal de la ruta; completo. Forma: `canales: [{ id, clave, nombre, formatoLiquidacion, pedidoPor }]` |
| **(v0.2)** productos | además `nombreCorto` y, en `conversiones`, la unidad de traslado (`usoTraslado`, `predeterminadaTraslado`) aunque no sea de venta (RN-CAT-09) |

```json
{
  "cursor": 18452,
  "hayMas": false,
  "cambios": {
    "productos": [{ "id": "…", "sku": "PT-TORT-KG", "nombre": "Tortilla de maíz", "activo": true, "versionSync": 18001 }],
    "clientes": [],
    "eliminados": { "rutasClientes": ["…"] }
  }
}
```

La app aplica los cambios en una transacción local y guarda `cursor`. Con `hayMas = true` repite.
Borrados se informan en `eliminados` (registros con `activo = false` o fuera del alcance).

## Subida: `POST /api/v1/sync/push`

```json
{
  "dispositivoId": "…",
  "loteId": "…",
  "versionApp": "1.4.0",
  "operaciones": [
    {
      "opId": "0192…",
      "secuencia": 341,
      "tipo": "RegistrarVenta",
      "creadaEn": "2026-11-03T15:42:10-06:00",
      "datos": {
        "ventaId": "0192…", "folio": "R01-D03-000123", "clienteId": "…",
        "condicionPago": "Contado", "formaPago": "Efectivo",
        "partidas": [{ "productoId": "…", "unidadId": "…", "cantidad": 5, "precioUnitario": 22.0 }],
        "totalApp": 110.00,
        "gps": { "lat": 18.4489, "lng": -96.3561, "precisionM": 12, "tomadoEn": "2026-11-03T15:42:08-06:00" }
      }
    }
  ]
}
```

Respuesta:

```json
{
  "resultados": [
    { "opId": "0192…", "estado": "Aplicada", "idsServidor": { "ventaId": "0192…" }, "incidencias": ["GpsFueraDeRango"] }
  ],
  "pendientesServidor": 0
}
```

Estados: `Aplicada`, `Duplicada` (ya existía; se devuelve el resultado original), `Rechazada`
(con `codigo` y `mensaje`).

## Tipos de operación

| Tipo | Handler | Módulo |
|---|---|---|
| `RegistrarVenta` | `RegistrarVentaHandler` | Ventas |
| `EntregarPedido` | `EntregarPedidoHandler` | Ventas |
| `RegistrarRetorno` | `RegistrarRetornoHandler` | Logistica. **(v0.2)** acepta `tipo = CambioFisico` |
| `RegistrarCobro` | `RegistrarCobroHandler` | Ventas `[SUP-08]` |
| **(v0.2)** `CapturarPedido` | `CapturarPedidoHandler` | Ventas |
| `ProponerUbicacionCliente` | `ProponerUbicacionHandler` | Ventas |
| `ReportarEstado` | guarda pendientes, batería, versión | Sincronizacion |

## Reglas
- **RN-SYN-01** Las operaciones de un lote se procesan en orden de `secuencia`, cada una en su
  propia transacción. El fallo de una no detiene las demás.
- **RN-SYN-02** Antes de procesar, se busca `op_id` en `sync.operaciones`; si existe se responde
  `Duplicada` con el resultado guardado.
- **RN-SYN-03** El `op_id` se inserta en `sync.operaciones` en la misma transacción que el efecto
  de negocio (garantiza exactamente una vez).
- **RN-SYN-04** Rechazos de negocio no se reintentan automáticamente; errores de red o 5xx sí
  (con retroceso exponencial en la app).
- **RN-SYN-05** El dispositivo debe estar activo y asignado al usuario del token (`SYN-001`).
- **RN-SYN-06** `fecha_operativa` de una operación = fecha local (`America/Mexico_City`) de
  `creadaEn`, no de la recepción. Si la liquidación de esa ruta y fecha ya está cerrada, la operación
  se aplica igual y se crea incidencia `DiferenciaLiquidacion` `[SUP-10]`.
- **RN-SYN-07** Máximo 200 operaciones por lote; la app parte lotes más grandes.
- **RN-SYN-08** El consecutivo de folio lo lleva la app en SQLite; nunca se reinicia. Al reinstalar,
  la app pide al servidor el último consecutivo del dispositivo (`GET /sync/dispositivo`).

- **RN-SYN-09 (v0.2)** Alcance por carga asignada (ver "Bajada"). La ruta de cada operación la resuelve el servidor
  con la **fecha operativa de la operación** (RN-SYN-06): la ruta de la carga del chofer en esa fecha o, si no tuvo
  carga, su ruta predeterminada. Así, una venta de ayer en R51 que sube hoy se aplica a R51 aunque hoy el chofer
  esté en R52. Si el chofer no tiene ni carga ni ruta predeterminada para esa fecha, la operación se rechaza con
  `SYN-006`.
- **RN-SYN-10 (v0.2)** `CapturarPedido` requiere el permiso `ventas.pedidos_app` (si no, `SYN-008`). Sincronización
  valida antes de llamar a Ventas que el cliente pertenezca a la ruta de la operación (RN-SYN-09) con
  `ILogisticaService.ClientesDeRutaAsync` (`SYN-007`); Ventas no consulta rutas. Crea el pedido en `Capturado` con
  `origen = App`, `op_id`, `capturado_por` (usuario), `dispositivo_id` y `ruta_id` validada; el id del pedido lo
  genera la app. Otros rechazos:
  producto exclusivo (`VEN-019`), `fechaEntrega` anterior a la fecha operativa de `creadaEn` (`VEN-021`), pedido de
  ruta duplicado (`VEN-020`). Los precios los pone el servidor (lista o especial vigente); la app no los manda.
  *Implementación (T-8.6):* el reenvío con el mismo `op_id` **o** el mismo `pedidoId` responde `Duplicada` con el
  pedido original; `esRuta` es informativo (lo decide el servidor). La bajada de `pedidos` agrega `esRuta` y
  `partidas[].tipo`. La app guarda y captura pedidos en T-8.12.
- **RN-SYN-11 (v0.2)** *(implementado en T-8.9; la bajada es `cambiosPendientes` con `id, clienteId, productoId,
  cantidadBase, cantidadSaldadaBase, fechaOperativa, versionSync` y los saldados o cancelados llegan en
  `eliminados.cambiosPendientes`)* `RegistrarRetorno` con `CambioFisico`: partidas con `sentido`; las `Entregado` pueden traer
  `cambioPendienteId`; las `Retirado` traen `dejarPendiente` (por partida, por defecto `true`), que aplica RN-LOG-16.

**Errores nuevos (v0.2):** `SYN-006` el chofer no tiene carga ni ruta predeterminada en la fecha de la operación,
`SYN-007` cliente fuera de la ruta, `SYN-008` sin permiso para capturar pedidos desde la app.

## Sesión offline
- Access token de 15 min y refresh de 30 días ligado al dispositivo.
- Si el refresh expiró, la app sigue registrando localmente y pide iniciar sesión al sincronizar;
  las operaciones pendientes se conservan.

## Contrato de datos

Las formas exactas de cada colección de bajada, de `GET /sync/dispositivo` y de los `datos` de cada tipo de operación
están en `frontend/packages/shared/src/sync.ts` (tipos TypeScript y esquemas Zod). El backend las serializa igual
(camelCase). Notas:

- `rutasClientes` usa `clienteId` como id en `eliminados`.
- `saldosClientes` siempre viaja completo (no es incremental).
- Los `datos` de `RegistrarVenta`, `EntregarPedido` y `RegistrarRetorno` llevan `consecutivo`; el servidor lo guarda
  como `ultimo_consecutivo` del dispositivo (RN-SYN-08).
- `ReportarEstado` informa `pendientes`, que Logística usa para RN-LOG-09.

**(v0.2)** Contrato nuevo en `sync.ts`:

```ts
datosCapturarPedido = {
  pedidoId: uuid, clienteId: uuid, fechaEntrega: 'yyyy-MM-dd', ordenCompraCliente: string | null,
  esRuta: boolean,                       // pedido global de detalle (RN-VEN-16)
  partidas: [{ productoId: uuid, unidadId: uuid, cantidad: number > 0,
               tipo: 'Venta' | 'CambioFisico', notas: string | null }] (min 1),
  notas: string | null, gps: GpsSync | null,
}
datosRegistrarRetorno.tipo += 'CambioFisico'
datosRegistrarRetorno.partidas[] += { sentido: 'Retirado' | 'Entregado', cambioPendienteId: uuid | null,
                                      dejarPendiente: boolean }
datosRegistrarVenta.formaPago / datosEntregarPedido.formaPago += 'Cheque'
GET /sync/pull?desde&rutaId      // rutaId = ruta que la app tiene guardada (opcional)
RespuestaPull += { rutaCambiada: boolean }  // con desde > 0: sin cambios y cursor = 0
```
