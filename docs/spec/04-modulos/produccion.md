# Módulo Producción (PRO)

El "cerebro" del sistema: convierte materias primas en producto mediante recetas y registra lo que
realmente se consumió, produjo y mermó.

## Casos de uso
1. Crear y versionar recetas; publicar una versión como `Vigente`.
2. Planear órdenes de producción por día, turno y línea.
3. Liberar la orden (fija receta y lote), iniciarla, terminarla con cantidades reales, cancelarla.
4. Registrar mermas durante o al cierre de la orden.
5. Consultar variación teórico vs real por orden, producto y periodo.

## Recetas
- Una receta define cuánto producto sale (`rendimiento_cantidad`) a partir de qué componentes.
- Multinivel por inventario: la masa (Semiterminado) tiene su receta y su orden; la tortilla
  consume masa del inventario. No hay explosión automática de niveles `[SUP-12]`.

## Estados de la orden

| Estado | Pasa a | Efecto |
|---|---|---|
| Planeada | Liberada, Cancelada | editable |
| Liberada | EnProceso, Cancelada | fija `receta_id` y genera `lote`; calcula consumos teóricos |
| EnProceso | Terminada | se capturan mermas y consumos reales parciales |
| Terminada | — | genera movimientos de inventario |
| Cancelada | — | sin movimientos |

## Reglas
- **RN-PRO-01** Solo una versión `Vigente` por producto. Publicar una nueva pasa la anterior a
  `Obsoleta`. Una receta `Vigente` u `Obsoleta` no se edita: se crea nueva versión.
- **RN-PRO-02** Consumo teórico por componente =
  `componente.cantidad_base × cantidad_planeada / receta.rendimiento_cantidad`.
- **RN-PRO-03** Al terminar, si no se capturó consumo real, se usa el teórico recalculado con
  `cantidad_producida` en lugar de la planeada.
- **RN-PRO-04** Terminar genera, en una sola transacción:
  `SalidaConsumo` por cada componente (almacén de consumo, cantidad real),
  `EntradaProduccion` del producto (almacén destino, lote de la orden),
  `Merma` por cada merma registrada.
- **RN-PRO-05** Costo unitario del producto = suma de (consumo real × costo promedio del
  componente) / `cantidad_producida`. Sin costos indirectos `[SUP-06]`.
- **RN-PRO-06** Si un componente no tiene existencia suficiente al terminar, se rechaza con
  `PRO-002` salvo que `produccion.permitir_consumo_sin_existencia = true` `[SUP-12]`.
- **RN-PRO-07** Merma de producción requiere motivo con `aplica_produccion = true`.
- **RN-PRO-08** Variación = `(real − teórico) / teórico` por componente; se marca en el reporte si
  supera ±5 % (`produccion.umbral_variacion_pct`).
- **RN-PRO-09** `cantidad_producida` es obligatoria y mayor que cero para terminar.
- **RN-PRO-10** Lote = `AAMMDD-<folio sin guion>`; caducidad = `fecha_operativa + vida_util_dias`.

## Cambios v0.2 (reunión del 24-oct-2026)

### Casos de uso nuevos
1. Consultar y capturar el **estimado de producción** por canal: cuadrícula rutas × productos para una fecha de
   entrega, igual a los formatos en papel (detalle, autoservicio, institucional/CEDIS).
2. Ver el **plan sugerido**: demanda de la fecha de entrega contra existencia, y crear las órdenes de producción del
   faltante.
3. **Copiar receta** a otro producto (misma fórmula, distinta presentación o gramaje).
4. Volver a mostrar **Plan del día** en el menú (la pantalla existe en `/produccion/plan`, oculta desde `390c4d6`).

Dependencias nuevas: Producción lee pedidos por `IVentasService` y lo cargado por `ILogisticaService` (ninguno de
los dos depende de Producción, no hay ciclo).

### Reglas
- **RN-PRO-11** *Estimado*: es una vista sobre `ventas.pedidos` de la fecha de entrega y canal (decisión V2-02).
  Filas = rutas activas del canal; columnas = productos vendibles con demanda o marcados para el canal, encabezado con
  `nombre_corto`; celda = Σ `cantidad` de partidas `Venta` (en la unidad de venta predeterminada) de los pedidos no
  cancelados de la ruta. Columnas adicionales: observaciones (notas de las partidas de productos fuera de las
  columnas), y renglones **TOTAL**, **CAJAS / PAQUETES** (RN-CAT-09) y **REAL** (lo cargado en envíos despachados de
  la fecha, cargas iniciales + recargas). En canales `pedido_por = Ruta` y en pedidos de oficina la celda es
  editable y escribe en el pedido de la ruta (RN-VEN-16); en `pedido_por = Cliente` la celda es la suma de los
  pedidos por tienda y abre el detalle.
- **RN-PRO-12** Si una ruta no tiene pedido para la fecha de entrega, el estimado muestra en gris lo pedido para la
  misma ruta `ventas.dias_estimado_referencia` días antes `[SUP-23]`; el botón "Usar referencia" lo copia como pedido
  `Capturado`. El plan sugerido (RN-PRO-13) puede incluir la referencia marcada como "estimada".
- **RN-PRO-13** *Plan sugerido*: por producto `es_producible`,
  `demanda` = Σ partidas (`Venta` + `CambioFisico`) de pedidos no cancelados con esa fecha de entrega (+ referencias
  si se eligen) + cambios pendientes de los clientes que se visitan ese día (lo mismo que suma RN-LOG-14, vía
  `ILogisticaService`) − ya cargado; `disponible` = existencia en `PT` con caducidad ≥ fecha de entrega (o sin caducidad);
  `faltante = max(0, demanda − disponible − órdenes planeadas/liberadas/en proceso para la fecha)`. Productos `Reventa`
  aparecen como "surtir de compra" con su faltante y no generan orden. "Crear órdenes" planea una orden por producto
  con faltante (receta vigente, turno y línea elegidos por el usuario; RN-PRO-08 sin cambios). Sin receta vigente
  se marca y no se crea (`PRO-008`).
- **RN-PRO-14** *Copiar receta*: crea una receta `Borrador` para el producto destino con los mismos componentes
  y el rendimiento convertido proporcionalmente si el usuario indica el factor (p. ej. 800 g → 450 g). Se publica
  como cualquier receta (RN-PRO-01).

### Criterios de aceptación (v0.2)
- Con pedidos de R1 (3 tiendas: 24, 48 y 48 de T-800) y el pedido global de R51 (96), el estimado de AUTO muestra 120
  en R1 y el de DET 96 en R51; TOTAL y CAJAS/PAQUETES con 24 paquetes por caja muestran "5 cajas" y "4 cajas".
- Con 200 de demanda de T-800, 150 en `PT` vigentes y 0 órdenes, el plan sugiere 50; con una orden planeada de 30
  sugiere 20.

**Notas de implementación (T-8.7).**
- Sin tablas nuevas: el estimado y el plan leen `IVentasService.DemandaPorFechaEntregaAsync`,
  `ILogisticaService.CargadoPorFechaAsync` (envíos `EnRuta`/`Liquidado` de la fecha) e
  `IInventarioService.ExistenciasDeAlmacenAsync("PT")`. Producción referencia `Ventas` y `Logistica` (solo `Publico/`).
- La fila de un pedido es `pedido.ruta_id` o, si no tiene, la ruta activa del cliente (`RutasDeClientesAsync`).
- Columnas: productos vendibles con demanda (o en la referencia) + los de las listas de precio activas del canal
  `[SUP-30]`; celdas en la unidad de venta predeterminada.
- Solo los canales `pedido_por = Ruta` son editables desde el estimado (`PRO-015` en otro canal); el PUT crea o
  corrige el pedido del genérico `GEN-{ruta}` (cantidad 0 quita la partida; sin partidas se cancela) y solo mientras
  esté `Capturado` (`VEN-012`). Los pedidos de oficina a tiendas se capturan en Pedidos.
- "Usar referencia" (`PRO-017` si la ruta ya tiene pedido o no hay referencia) copia los pedidos de la referencia
  con la nueva fecha de entrega como `Capturado`.
- Plan sugerido: la demanda incluye todos los pedidos de la fecha (tengan ruta o no); las órdenes abiertas cuentan
  si su fecha operativa es ≤ la fecha de entrega. Los **cambios pendientes** se suman en T-8.9 (todavía no existen).
  "Crear órdenes" redondea la cantidad hacia arriba a los decimales de la unidad base y devuelve creadas y omitidas.
- Copiar receta: `PRO-005` si el destino no es producible; `PRO-018` si el destino es componente de la receta.
- Errores nuevos: `PRO-014` canal inexistente, `PRO-015`, `PRO-016` ruta o producto inválido, `PRO-017`, `PRO-018`.

## Endpoints (`/api/v1/produccion`)
| Método | Ruta | Permiso |
|---|---|---|
| GET/POST | `/recetas` | `produccion.ver` / `produccion.recetas` |
| POST | `/recetas/{id}/nueva-version`, `/publicar` | `produccion.recetas` |
| GET/POST/PUT | `/lineas` | `produccion.recetas` |
| GET/POST | `/ordenes` | `produccion.ver` / `produccion.planear` |
| POST | `/ordenes/{id}/liberar`, `/iniciar`, `/cancelar` | `produccion.planear` |
| POST | `/ordenes/{id}/mermas` | `produccion.operar` |
| PUT | `/ordenes/{id}/consumos` | `produccion.operar` |
| POST | `/ordenes/{id}/terminar` | `produccion.cerrar` |
| GET | `/reportes/variacion?desde&hasta&productoId` | `produccion.ver` |
| GET/PUT | **(v0.2)** `/estimado?fechaEntrega&canalId` (cuadrícula RN-PRO-11; el PUT escribe pedidos vía `IVentasService`) | `produccion.ver` / `ventas.pedidos` |
| POST | **(v0.2)** `/estimado/confirmar?fechaEntrega&canalId` (confirma los pedidos `Capturado` de la cuadrícula) | `ventas.pedidos` |
| POST | **(v0.2)** `/estimado/usar-referencia` (`{ fechaEntrega, canalId, rutaId }`, RN-PRO-12) | `ventas.pedidos` |
| GET | **(v0.2)** `/plan-sugerido?fechaEntrega&incluirReferencia` | `produccion.ver` |
| POST | **(v0.2)** `/plan-sugerido/crear-ordenes` (`{ fechaOperativa, turno, lineaId?, partidas: [{ productoId, cantidad }] }`) | `produccion.planear` |
| POST | **(v0.2)** `/recetas/{id}/copiar` (`{ productoDestinoId, factorRendimiento? }`) | `produccion.recetas` |

## Criterios de aceptación
- Con la receta semilla "Tortilla v1" (100 KG masa → 88 KG), planear 176 KG calcula consumo teórico
  de 200 KG de masa.
- Terminar con 170 KG producidos y 4 KG de merma genera los movimientos de RN-PRO-04 y deja la
  existencia de tortilla del lote en +170.
- Publicar "Tortilla v2" deja "v1" en `Obsoleta` y las órdenes ya liberadas siguen usando v1.

## Decisiones de implementación
- **Almacenes por defecto:** consumo `MP`; destino `PT` para terminados y `MP` para semiterminados (la masa queda
  donde la consumen las órdenes del siguiente nivel, multinivel por inventario `[SUP-12]`). Ambos se pueden cambiar
  al planear; no se aceptan almacenes de ruta (`PRO-011`).
- **Receta al liberar:** si la receta planeada dejó de ser Vigente antes de liberar, se toma la Vigente actual; ya
  liberada, la orden conserva su versión (criterio de RN-PRO-01).
- **Mermas al terminar (RN-PRO-04):** la merma del producto de la orden se registra como `EntradaProduccion` por
  `producida + merma` y `Merma` del mismo lote, de modo que el lote queda con la cantidad producida (buena) y el costo
  de la merma se absorbe en el costo unitario (RN-PRO-05). La merma de un componente sale del almacén de consumo
  (FEFO) con el costo promedio vigente.
- **Variación (RN-PRO-08):** el teórico se ajusta a la cantidad producida
  (`teórico planeado × producida / planeada`) antes de compararlo con el real.
- **RN-PRO-06:** con `produccion.permitir_consumo_sin_existencia = true` el kardex permite dejar negativo el almacén
  de consumo (`RegistrarMovimientosRequest.PermitirNegativo`).
- Códigos: `PRO-001` transición inválida, `PRO-002` sin existencia, `PRO-003` receta, `PRO-004` receta no editable,
  `PRO-005` producto no producible, `PRO-006` orden, `PRO-007` motivo, `PRO-008` sin receta vigente,
  `PRO-009` producida inválida, `PRO-010` línea, `PRO-011` almacén, `PRO-012` producto ajeno a la orden,
  `PRO-013` componentes inválidos.
