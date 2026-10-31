# Plan: sección transversal con cotas y ganchos del estribo (#225)

> **Para quien lo ejecute:** commit y push solo si Juan lo pide (CLAUDE.md), sin firma de Claude.

**Objetivo:** que la sección tenga sus cotas y recubrimiento, y que el estribo aparte muestre la medida de su gancho, como la captura de Santiago.

**Referencia:** #225. **Rama:** `despiece-vista-longitudinal-222`, sin PR propia (Juan, 30-oct). **Modelo de prueba:** `PROY_JULIO_BETO`, `Sección V 3`, viga 392698.

## Hallazgos (Revit, 30-oct)

- **Caras de la viga:** con `ComputeReferences` y la vista, una cota real da 0.25 y 0.35.
- **Estribo en una cota:** Revit ignora su referencia; la cadena sale de un solo tramo. Por eso el recubrimiento no puede ser cota.
- **Gancho:** el tipo de detalle tiene `Longitudes de gancho = Sí`, pero con la forma `CQ Estribo` no aparece ninguna medida, ni con 180° ni con 135° (probado). Santiago sí la ve (`0.08`), así que parece venir de la forma de su modelo: sin confirmar.
- **Pata recta del gancho:** 0.065 m a 180° y 0.057 m a 135°, en el estribo #3.
- **Barras corridas:** el add-in de vigas usa `MoveBarInSet`, que `GetBarPositionTransform` no incluye. La flecha caía en -7.480 y la barra está en -7.503. Se mide con `GetTransformedCenterlineCurves`.

## Decisiones

1. **Ancho y alto con cotas de Revit; recubrimiento en texto** (2026-10-30, Claude): `0.04 · 0.17 · 0.04`, medido por la cara exterior del estribo. Pega: el texto no se actualiza solo.
2. **La pata del gancho en texto, una vez, dentro del estribo** (2026-10-30, Claude, sin confirmar por Juan): como `0.15` en la maqueta aprobada. La captura de Santiago la repite en cada gancho.
3. **El bastón no se etiqueta** (Santiago, min 37:36: "el bastón no se marca").
4. **Solo se dibuja el estribo cerrado aparte** (2026-10-30, Claude): la grapa de la misma zona no sale al lado. Falta verlo con la viga 388501.

## Tareas

### Tarea 1: cotas, gancho y flechas

**Archivo:** `addins/despiece/Revit/GeneradorPrevia.cs`. Hecho.

### Tarea 2: prueba en Revit y cierre

Cerrar Revit, correr `tools/desplegar-local.ps1 -Addin despiece -Revit 2026` y crear las vistas de las vigas 392698 y 388501. Luego marcar lo hecho en #225.
