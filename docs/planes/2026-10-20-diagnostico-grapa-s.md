# Diagnóstico: grapa en S

Fecha: 20-oct-2026. Estado: **corrido en Revit 2027 el 20-oct por Luciano: todo funciona. Conclusión abajo.**
Tarea 4 de `2026-10-20-plan-estribos-menores-visual-y-64.md`.

## Qué se quiere saber

La grapa en S es "la misma C con un gancho para cada lado" (Luciano). Antes
de programar cómo se genera hay que medir en Revit real:

1. cómo se crea con la API pública una grapa con los ganchos hacia lados
   opuestos;
2. si "Voltear" se resuelve solo con la orientación de los ganchos;
3. de qué lado queda cada gancho en la barra creada, y si Revit le cambia
   la forma por su cuenta.

## API usada (pública, verificada en `RevitAPI.xml` de 2027)

- `RebarShapeTerminationsData.TerminationOrientationAtStart` / `AtEnd`
  (desde 2026): orientación de cada gancho en la **forma**.
- `BarTerminationsData` (orientación de cada gancho al **crear la barra**) y
  `Rebar.CreateFromCurvesAndShape`, que ya usa el add-in para las grapas.
- `Rebar.SetTerminationOrientation(int, RebarTerminationOrientation)`:
  orientación de una punta **después** de crear la barra.
- `RebarShape.Create`, `RebarShapeDefinitionBySegments`,
  `RebarHookType.Create`: ya verificados el 8-oct.

## El comando: `ComandoDiagnosticoGrapaS.cs`

Botón "Diagnóstico grapa en S" (solo en Debug). Se elige una columna y se
crean, cada una en su transacción y a otra altura, grapas de un tramo con
180° en las dos puntas, con barra #3:

1. forma C + barra Left/Left (control);
2. forma C + barra Left/Right (¿S sin forma propia?);
3. forma C + barra Right/Right (¿C volteada?);
4. forma S (Left/Right en la forma) + barra Left/Right;
5. forma S + barra Right/Left (¿S volteada?);
6. forma C Left/Left y después `SetTerminationOrientation(1, Right)`.

De cada una informa con qué forma quedó (y si Revit la cambió), de qué lado
del eje queda cada gancho (+Y / −Y) y dónde quedan las puntas de las colas
respecto de los puntos pedidos. Deja el informe en una ventana y en
`%TEMP%\practicacq-diagnostico-grapa-s.txt`, y al final pregunta si deja
las grapas en el modelo para mirarlas en 3D.

## Resultados (columna 782745 de 30×30, barra #3)

| Caso | Forma con la que quedó | Gancho inicio / fin | Resultado |
|---|---|---|---|
| 1. Forma C + barra Left/Left | C (sin cambio) | +Y / +Y | C |
| 2. Forma C + barra Left/Right | C (sin cambio) | +Y / −Y | **S** |
| 3. Forma C + barra Right/Right | C (sin cambio) | −Y / −Y | **C volteada** |
| 4. Forma S + barra Left/Right | S (sin cambio) | +Y / −Y | S |
| 5. Forma S + barra Right/Left | S (sin cambio) | −Y / +Y | **S volteada** |
| 6. Forma C L/L y después `SetTerminationOrientation(1, Right)` | C (sin cambio) | +Y / −Y | S |

Captura en 3D de Luciano: las seis grapas salen con la forma esperada.

En todos los casos: colas de **3,81 cm** (4 d, lo que da el multiplicador 4
del gancho) con doblez de radio 3,65 cm; el cuadro pide E = **6,5 cm** para
el 180° de la #3. Es lo que corrige `TiposPropios.AjustarExtension` (#64). La
recta central mide 11,75 cm para una grapa pedida de 20 cm: Revit mete los
ganchos dentro del largo pedido (lo que ya corrige `MedirCorreccionDeGrapa`).

## Conclusión

- **La orientación de cada gancho la decide la barra** (`BarTerminationsData`
  al crearla), y Revit la respeta sin cambiar la forma, sea cual sea la
  orientación guardada en la forma.
- **S** = ganchos Left/Right; **C** = Left/Left. **Voltear** = invertir los
  dos (C: Right/Right; S: Right/Left).
- Se crea igual la forma propia `CQ Grapa S` (Left/Right en la forma), como
  dice el diseño, para que en planillas de despiece la S figure aparte de la
  C. Funciona igual que la C (casos 4 y 5).
