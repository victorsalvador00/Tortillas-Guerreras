---
description: Toma la siguiente tarea pendiente del plan de implementación y la ejecuta
---

1. Lee `docs/spec/09-plan-implementacion.md` y toma la primera tarea sin marcar `[ ]` que no
   esté bloqueada.
2. Lee los archivos del spec que la tarea indica (`Lee:`) y las reglas `RN-…` de sus criterios.
3. Muestra un plan breve: archivos a crear o cambiar, migraciones, pruebas por regla. Espera
   confirmación si la tarea toca más de un módulo o cambia el modelo de datos.
4. Crea la rama `feat/<modulo>-<tarea>` e implementa.
5. Cumple la definición de terminado de `CLAUDE.md` (build, pruebas, migración, OpenAPI y cliente TS).
6. Marca la tarea `[x]` en el plan y resume qué reglas quedaron cubiertas y qué supuestos `[SUP-xx]`
   se usaron.
