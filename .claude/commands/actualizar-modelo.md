---
description: Complementa el modelo de datos y las reglas con la información recibida del cliente
argument-hint: [ruta a los archivos del cliente]
---

Sigue el procedimiento de `docs/spec/10-supuestos-y-pendientes.md`, sección "Cómo complementar el
spec", usando los archivos en $ARGUMENTS (por defecto `docs/cliente/`).

1. Lee cada archivo (Excel, CSV, documentos) y genera `docs/cliente/mapeo.md` con el mapeo
   columna del cliente → `esquema.tabla.columna`, marcando: igual, otro nombre, falta, sobra.
2. Propón, sin editar todavía, la lista de cambios:
   - columnas o tablas nuevas en `03-modelo-datos.md`,
   - supuestos `[SUP-xx]` confirmados, cambiados o que siguen abiertos,
   - reglas `RN-…` nuevas o modificadas por módulo,
   - tareas nuevas o de ajuste para `09-plan-implementacion.md`.
3. Espera mi confirmación y luego aplica los cambios al spec en un solo commit
   `docs(spec): complementar modelo con información del cliente`.
4. No generes migraciones en este paso; quedan como tareas en el plan.
