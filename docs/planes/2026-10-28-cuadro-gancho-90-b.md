# El cuadro de varillas muestra la B del gancho de 90°

## Contexto

Luciano notó (28-10) que el add-in mostraba en una tabla un valor y calculaba
con otro. La tabla «Cuadro de varillas» (#142) mostraba para el gancho de 90°
la E y la D del cuadro 2 (#4: 0,152 / 0,076), pero desde #177 las longitudinales
se generan para que la pata, medida por fuera del doblez, sea la B del cuadro
1 (#4: 0,222), con la D del cuadro 2.

Se revisó el resto: traslapo, gancho de 180° (grapas) y estribo de 135° usan
lo mismo que muestra la tabla.

## Qué se hizo

Decisión de Luciano: mostrar en la tabla lo que se usa (no cambiar el cálculo).

- `Vistas/VentanaCuadroVarillas`: la columna pasa a «Gancho 90° B / D»
  (`Varilla.Gancho90` / `Gancho90Cuadro.DiametroDoblado`), y la nota explica
  qué es la B.
- No cambia el acero que se genera.

## Pendiente

- Sigue abierto unificar con vigas (B o cola E), ver
  `2026-10-26-diagnostico-largo-longitudinales.md`.
