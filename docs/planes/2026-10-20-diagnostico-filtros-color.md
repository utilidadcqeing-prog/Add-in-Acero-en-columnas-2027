# Diagnóstico: filtros de vista para colorear las barras por diámetro

Contexto: Tarea 6 de `2026-10-20-plan-vista-previa-y-colores.md`. Comando
temporal `ComandoDiagnosticoFiltrosColor` (botón «Diagnóstico colores», solo
Debug), corrido por Luciano en Revit 2027 real.

## Qué se encontró

Vista «3D ACEROS» (ThreeD), sin plantilla de vista, `AreGraphicsOverridesAllowed: True`:

- 250 barras: 179 de #3, 55 de #4 y 16 de #5. Los diámetros reales
  coinciden con la tabla.
- Los filtros "CQEING #3".."#11" y "CQEING otro" se crearon sin errores.
  Cada uno atrapa exactamente las barras de su diámetro (179 / 55 / 16 / 0…),
  y "CQEING otro" atrapa las 250.
- **Hallazgo importante:** el proyecto ya trae sus propios filtros:
  - por diámetro: "ACERO #2" a "ACERO #8";
  - por uso: Acero_Vigas, Acero_Columnas, Acero_Zapatas, etc.

  En la lista de la vista están **antes** que los nuestros, y en Revit gana
  el filtro de más arriba. Por eso, donde un filtro de la empresa ya pinta
  la barra, nuestro color no se ve.
- La API pública de Revit 2027 no tiene forma de reordenar filtros.
  `AddFilter` y `SetFilterOverrides` siempre agregan al final, y no existe
  `InsertFilter` en RevitAPI.xml. Las únicas formas de que gane nuestro color
  son:
  - quitar los filtros de la empresa de la vista y volver a ponerlos
    después, conservando sus ajustes;
  - desactivarlos en la vista con `SetIsFilterEnabled(false)`;
  - o cambiar el color de los "ACERO #N" existentes.

Pendiente de correr: planta normal, planta con plantilla de vista, corte.

## Estado

Esperando decisión de Luciano sobre qué hacer con los filtros "ACERO #N" de
la empresa antes de implementar la Tarea 7.
