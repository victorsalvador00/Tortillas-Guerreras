# Módulo Inventario (INV)

Único dueño de existencias y costos. Todo cambio entra por el kardex.

## Casos de uso
1. Administrar almacenes (incluye un almacén tipo `Ruta` por cada ruta).
2. Registrar movimientos (solo vía `IInventarioService`, usado por otros módulos).
3. Traspasos entre almacenes no-ruta.
4. Ajustes manuales con motivo (permiso `inventario.ajustar`).
5. Conteo físico: abrir, capturar, cerrar (genera ajustes por diferencia).
6. Consultar existencias por almacén, producto y lote; kardex por producto; valuación.

## Servicio público

```csharp
public interface IInventarioService
{
    Task<Result<IReadOnlyList<MovimientoRegistrado>>> RegistrarMovimientosAsync(
        RegistrarMovimientosRequest request, CancellationToken ct);
    Task<decimal> ObtenerExistenciaAsync(Guid almacenId, Guid productoId, string? lote, CancellationToken ct);
    Task<decimal> ObtenerCostoPromedioAsync(Guid productoId, CancellationToken ct);
}
// RegistrarMovimientosRequest: documento (tipo, id, folio), fecha_operativa, usuario, dispositivo?,
// lista de líneas (tipo, almacén, producto, lote, cantidad con signo, costo_unitario?, motivo?) y
// PermitirNegativo opcional (producción con produccion.permitir_consumo_sin_existencia, RN-PRO-06)
```

## Reglas
- **RN-INV-01** Los movimientos son inmutables. Corregir = registrar el movimiento contrario
  referenciando el mismo documento.
- **RN-INV-02** Un movimiento de salida en un almacén con `permite_negativo = false` que deje
  existencia negativa se rechaza con `INV-001`.
- **RN-INV-03** En almacenes `Ruta` se permite negativo (ventas offline) y se genera incidencia
  `ExistenciaNegativaRuta` `[SUP-10]`.
- **RN-INV-04** La existencia se actualiza en la misma transacción que el movimiento, con bloqueo
  de la fila (`SELECT … FOR UPDATE`) para evitar condiciones de carrera.
- **RN-INV-05** Costo promedio ponderado: en cada entrada con costo
  (`EntradaCompra`, `EntradaProduccion`, `AjusteEntrada` con costo),
  `nuevo = (exist_total × costo_actual + cant × costo_entrada) / (exist_total + cant)`, donde
  `exist_total` es la existencia del producto en todos los almacenes no-ruta y ruta. Salidas usan el
  costo promedio vigente. `[SUP-06]`
- **RN-INV-06** Traspasos y envíos generan `TraspasoSalida` y `TraspasoEntrada` con el mismo costo.
- **RN-INV-07** Para productos con lote, las salidas sin lote indicado consumen FEFO (primero el que
  caduca antes); si no hay caducidad, FIFO por fecha del lote.
- **RN-INV-08** Ajustes manuales requieren motivo y nota; se reportan en el tablero.
- **RN-INV-09** Cerrar un conteo genera `AjusteEntrada`/`AjusteSalida` por cada diferencia distinta
  de cero, con `documento_tipo = ConteoFisico`.
- **RN-INV-10** Mientras un conteo está `Abierto`, el almacén admite movimientos; la cantidad del
  sistema se toma al momento de capturar cada partida.

### Cambios v0.2
- **RN-INV-11** `SalidaCambio` es una salida (−) de un almacén `Ruta` con documento `Retorno` (cambio físico). Usa el
  costo promedio vigente, respeta FEFO (RN-INV-07) y, como venta en ruta, puede dejar negativo el almacén de ruta con
  incidencia `ExistenciaNegativaRuta` (RN-INV-03). Los reportes de mermas no la cuentan como merma; el reporte de
  cambios la muestra junto con lo retirado.

### Trazabilidad de lotes (T-8.16, guía: trazabilidad de panissimo)
Pantalla **Inventario → Trazabilidad**: se busca un lote, una orden de compra o el folio de cualquier documento que
movió inventario (recepción, orden de producción, carga, venta, retorno, liquidación, traspaso, ajuste, conteo) y se
dibuja el camino del producto de proveedor a cliente. Pestañas *Mapa* (grafo por carriles), *Resumen* (proveedores,
lotes, órdenes, rutas y clientes alcanzados) y *Bitácora* (movimientos del kardex), con exportación a CSV. El grafo se
arma **solo con el kardex** (`inventario.movimientos`): cada nodo "lote" es un lote en un almacén; los documentos lo
unen con los lotes que entran y salen. Los nombres (proveedor, OC, ruta, chofer, cliente) salen de las interfaces
`Publico/` de cada módulo. Permiso `inventario.trazabilidad`. Las consultas se registran en el log de la API (la
bitácora solo guarda altas, cambios y bajas).

- **RN-INV-14** Hacia el origen se siguen las entradas de cada lote (compra, producción, llegada de traspaso o carga);
  una orden de producción lleva a los lotes que consumió con la cantidad neta del documento (FEFO puede repartir un
  componente entre varios lotes). Hacia el destino se siguen las salidas: consumo en producción, carga a ruta,
  traspaso, venta (→ cliente). Por omisión una OC o recepción se rastrea hacia el destino, una venta hacia el origen
  y lo demás en ambas direcciones. `[SUP-02]` Materia prima y semiterminado manejan lote por omisión para que la
  cadena llegue al proveedor.
- **RN-INV-15** Lo que no sigue la cadena se dibuja como terminal: merma (nodo *Merma* con el folio que la
  generó), devolución de cliente (arista *Rechazo* hacia el retorno) y sobrante que regresa al almacén en la
  liquidación (nodo *Regresó a…*). Las entradas por retorno o liquidación no se tratan como origen.
- **RN-INV-16** Un componente sin control de lote no se puede rastrear: aparece en la orden de producción como
  "consumo sin lote" con su cantidad y el resultado lleva el aviso `componentes_sin_lote`.
- **RN-INV-17** El recorrido es por niveles con tope de 20 niveles y 2,000 nodos; al alcanzarlo se devuelve lo
  armado con `truncado = true` y el aviso `truncado`.

## Endpoints (`/api/v1/inventario`)
| Método | Ruta | Permiso |
|---|---|---|
| GET/POST/PUT | `/almacenes` | `inventario.ver` / `inventario.configurar` |
| GET | `/existencias?almacenId&productoId&soloConExistencia` | `inventario.ver` |
| GET | `/kardex?productoId&almacenId&desde&hasta` | `inventario.ver` |
| GET | `/valuacion?fecha` | `inventario.costos` |
| POST | `/ajustes` | `inventario.ajustar` |
| GET/POST | `/traspasos` | `inventario.traspasar` |
| GET/POST | `/conteos`, `/conteos/{id}/partidas`, `/conteos/{id}/cerrar` | `inventario.contar` |
| GET | `/trazabilidad?tipo&id&productoId&lote&direccion` | `inventario.trazabilidad` |
| GET | `/trazabilidad/buscar?q` | `inventario.trazabilidad` |

## Criterios de aceptación
- Dos solicitudes concurrentes que consumen la última existencia en `PT`: una pasa y la otra
  recibe `INV-001`.
- El saldo del kardex (`sum(cantidad)`) siempre coincide con `existencias.cantidad`
  (prueba de propiedad sobre 1,000 movimientos aleatorios).
