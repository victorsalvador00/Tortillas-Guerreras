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
