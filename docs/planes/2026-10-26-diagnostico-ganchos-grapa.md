# Diagnóstico: grapas con gancho de 135° y 90°

Fecha: 26-oct-2026. Estado: **corrido por Luciano en Revit 2027 el 26-oct; conclusión abajo.**
Tarea 1 de `2026-10-26-plan-grapas-angulo-por-punta.md`. Comando temporal
`ComandoDiagnosticoGanchosGrapa` (botón «Diagnóstico ganchos grapa», solo Debug).

## API usada (pública, verificada en `RevitAPI.xml` de 2027)

- `RebarShapeTerminationsData.HookAngleAtStart/AtEnd`: 0 a 180; "valores comunes 0, 90, 135 y 180".
- `BarTerminationsData.HookTypeIdAtStart/AtEnd`, `Rebar.CreateFromCurvesAndShape`,
  `Rebar.CreateFromCurves`, `Rebar.GetShapeId`, `GetHookTypeId`, `GetTerminationOrientation`.
- `CreateFromCurvesAndShape`: "las curvas y las terminaciones tienen que coincidir
  con la forma"; solo con `ReinforcementSettings.RebarShapeDefinesHooks = false`
  los ganchos pueden ser distintos a los de la forma.

## Resultados (columna 387984, barra #3 CQEING, doblado de estribo 38 mm)

`RebarShapeDefinesHooks = False` en ese modelo.

| Caso | Se creó | Forma final | Centro del doblez (Axial / Lateral) | Cola |
|---|---|---|---|---|
| 1. C 180/180, forma 180/180 | sí | CQ Grapa C | 2,85 / 2,38 cm | 6,50 cm, hacia adentro |
| 2. C 135/135 con forma 180/180 | sí | CQ Grapa C | 2,85 / 2,38 | 5,70 cm, diagonal (0,707 / 0,707) |
| 3. C 135/135, forma 135-135 | sí | CQ Grapa C 135-135 | 2,85 / 2,38 | 5,70 cm, diagonal |
| 4. C 90/90, forma 90-90 | sí | CQ Grapa C 90-90 | 2,85 / 2,38 | 5,70 cm, perpendicular (0 / 1) |
| 5. C 135/90, forma 135-90 | sí | CQ Grapa C 135-90 | 2,85 / 2,38 en las dos | 135: diagonal; 90: perpendicular |
| 6. S 135/180, forma S 135-180 | sí | CQ Grapa en S 135-180 | 2,85 / +2,38 inicio, −2,38 fin | 135: diagonal; 180: hacia adentro |
| 7. C 135/135 con CreateFromCurves | sí | CQ Grapa C 135-135 (la ya creada) | 2,85 / 2,38 | diagonal |

Radio del doblez al eje: 23,8 mm en todos (doblez de estribo 38 mm + medio
diámetro). Extensiones: 135° = 5,70 cm (la E del cuadro para la #3, 6 d);
90° = 5,70 cm (6 d); 180° = 6,50 cm (cuadro).

## Conclusión

- Revit acepta grapas con 135° y 90° en cualquier combinación, en C y en S,
  con la orientación de cada punta respetada.
- **El centro del doblez no depende del ángulo**: Axial y Lateral son los
  mismos con 180°, 135° y 90°. La medida que ya hace `MedirGanchoDeGrapa`
  (con el 180° doblado como estribo) sirve para todas las puntas.
- Las colas salen exactamente como las dibuja `PlantaArmado.Grapa` (135°
  en diagonal hacia el núcleo, 90° perpendicular hacia el lado que abre).
- El caso 2 funcionó solo porque el modelo tiene `RebarShapeDefinesHooks =
  False`. Para que funcione en cualquier modelo, **una forma por combinación**
  (`CQ Grapa C 135-90`, etc.), como los casos 3 a 6.
