# Plan: etiquetas de estribos en la vista larga (#224)

> **Para quien lo ejecute:** commit y push solo si Juan lo pide (CLAUDE.md), sin firma de Claude.

**Objetivo:** cada zona de estribos con su etiqueta sobre la viga, con el formato del plano E-01 (`25 #4 @0.10`) y sin choques.

**Referencia:** #224. **Rama:** `despiece-vista-longitudinal-222`, sin PR propia (Juan, 30-oct). **Modelo de prueba:** `PROY_JULIO_BETO`, vista `V 3`, viga 392698; con grapas, viga 388501.

## Hallazgos (Revit, 30-oct)

- **Cada zona es un conjunto de Revit**, con `NumberWithSpacing`: cantidad y separación salen del propio conjunto. Viga 392698: `8 @100`, `7 @200`, `8 @100` por tramo.
- **Grapas:** en la viga 388501, cada estribo (`E 1-2`) tiene una grapa (`E1 1-2`) con la misma cantidad y separación, 2 cm al lado.
- **Medidas de la nota a 1:15:** 0.33 m de ancho y 0.12 m de alto. Desde su punto baja 0.09 m y sube 0.03 m.
- **Vigas generadas antes del arreglo de la separación:** dicen 187.48 mm (`@0.19`) en la 388501. La etiqueta muestra lo que tiene el modelo; hay que generarlas de nuevo.

## Decisiones

1. **`TextNote`, como en #223** (lo pide el issue): las etiquetas de Revit salen `8 -#3 CQEING @100.0 mm - 53`.
2. **Una etiqueta por zona; la grapa igual a su estribo no se repite** (2026-10-30, Claude, sin confirmar por Juan): el plano pide una etiqueta por zona y dos iguales en el mismo sitio solo ocuparían otra fila. No se usa el nombre de la forma: se comparan texto y posición. La grapa se sigue viendo en la sección.
3. **Las que no quepan suben a otra fila, y las barras sacadas se corren lo que haga falta** (2026-10-30, Claude).

4. **La sección también a 1:15** (2026-10-30, Juan: "la sección transversal no las da con el 1:15 que pedí"): estaba a 1:25. Probado cambiando la escala de `Sección V 3` y deshaciendo: los textos no se enciman.

## Tareas

### Tarea 1: etiquetas

**Archivos:** `addins/despiece/Revit/GeneradorPrevia.cs`, `addins/despiece/Revit/LectorDespiece.cs`. Hecho.

### Tarea 2: prueba en Revit y cierre

Cerrar Revit, correr `tools/desplegar-local.ps1 -Addin despiece -Revit 2026` y crear las vistas de las vigas 392698 y 388501. Luego marcar lo hecho en #224.
