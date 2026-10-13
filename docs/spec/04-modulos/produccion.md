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
