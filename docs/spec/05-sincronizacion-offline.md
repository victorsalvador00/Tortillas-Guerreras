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

| Colección | Filtro |
|---|---|
| productos (+ conversiones, unidades) | `es_vendible` y activos |
| clientes | clientes de la ruta + genérico |
| rutas_clientes | de la ruta |
| listas_precio_partidas | listas usadas por los clientes de la ruta |
| envios + envios_partidas | de la ruta, estado `EnRuta`, últimos 2 días |
| pedidos + partidas | asignados a la ruta, no entregados |
| saldos_clientes | saldo de crédito por cliente `[SUP-08]` |
| motivos_merma | `aplica_ruta` o `aplica_retorno` |
| parametros | claves `gps.*`, `ruta.*` |

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
| `RegistrarRetorno` | `RegistrarRetornoHandler` | Logistica |
| `RegistrarCobro` | `RegistrarCobroHandler` | Ventas `[SUP-08]` |
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
