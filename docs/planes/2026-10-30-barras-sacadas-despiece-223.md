# Plan: barras sacadas con etiqueta, cotas y color (#223)

> **Para quien lo ejecute:** commit y push solo si Juan lo pide (CLAUDE.md), sin firma de Claude.

**Objetivo:** que las barras sacadas de la vista larga muestren la medida de cada traslapo, como el plano de Santiago.

**Referencia:** #223 y "Cómo se hace" de #217. **Rama:** `despiece-vista-longitudinal-222`, sin PR propia (Juan, 30-oct: "sin abrir PR"). **Modelo de prueba:** `PROY_JULIO_BETO`, vista `V 3`.

## Hallazgos (Revit, 30-oct)

- **Un detalle de plegado no da referencias:** `get_Geometry` con `ComputeReferences` devuelve null (vista `V 3`, detalles 402326 y 402327). No se puede poner una cota de Revit sobre él.
- Lo que ya estaba del issue: orden por filas, traslapo corrido 0.08 m, metros y color por número (#222).

## Decisiones

1. **La medida del traslapo es texto** (2026-10-30, Claude): sin referencias no hay cota real, y líneas de detalle o planos de referencia solo para acotar irían contra "nada que imite otra cosa" (#217). El valor sale de los tramos rectos reales de las dos piezas. Pega: no se actualiza solo, y no lleva las flechas del plano.
2. **Propuestas sin confirmar por Juan:** dejar la piel para cuando exista (el add-in de vigas no la crea) y aceptar que los `TextNote` no se actualicen solos; si cambia el acero, se borran las vistas y se crean de nuevo.

## Tareas

### Tarea 1: medida de cada traslapo

**Archivo:** `addins/despiece/Revit/GeneradorPrevia.cs`. Al colocar cada línea, medir el cruce de los tramos rectos de cada par de piezas seguidas y escribirlo sobre el traslapo, en el color de la barra. Si cae sobre una etiqueta, sube al espacio libre.

**Prueba en Revit:** en `V 3`, comparar cada medida con los datos de las barras.

### Tarea 2: cierre

Marcar lo hecho en #223 y compilar con `tools/desplegar-local.ps1 -Addin despiece -Revit 2026`.
