# Diagnóstico: formas y ganchos propios del add-in (#64, con los cuadros del #65)

Fecha: 8-oct-2026. Estado: **comando de diagnóstico escrito y compilado; falta correrlo en Revit real.**

## Contexto

El #64 pide que estribos, cajas y grapas dejen de depender de las formas y
ganchos que traiga cada modelo: que el add-in cree los suyos, con las medidas
del cuadro de la empresa, y que la ventana deje elegir entre "por defecto del
add-in" o uno del modelo (igual que los tipos de empalme `CQ Traslapo #N` del
#41). El motivo inmediato: las cajas y grapas hoy salen deformes.

Por la regla del `CLAUDE.md`, antes de diseñar hay que medir contra Revit real.

## Qué dice el cuadro 2 ("Dimensiones mínimas para ganchos estándar")

Las medidas siguen una regla por diámetro de barra (d):

| Gancho | Extensión recta (E) | Diámetro de doblado (D) |
|---|---|---|
| Estribo de 135° | 6 d | 4 d |
| 90° | 12 d | 6 d |
| 180° | 4 d, con mínimo de 6,5 cm | 6 d |

Consecuencias para Revit:

- Un gancho de Revit es ángulo + un multiplicador del diámetro. Para 135° y
  90° alcanza con **un** gancho propio para todas las barras.
- Para 180° el mínimo de 6,5 cm manda de la #2 a la #5, así que no es un
  múltiplo fijo: hay que fijar el largo **barra por barra**
  (`RebarBarType.SetAutoCalcHookLengths` + `SetHookLength`).
- El diámetro de doblado no está en el gancho sino en el **tipo de barra**
  (`StirrupTieBendDiameter`, `StandardHookBendDiameter`,
  `StandardBendDiameter`). Cambiarlo afecta a toda la armadura del modelo que
  use esa barra: es una decisión aparte, todavía sin tomar.

## API verificada (pública, `RevitAPI.xml` de 2027)

`RebarHookType.Create`, `RebarHookType.Style` (con setter),
`RebarShapeDefinitionBySegments`, `RebarShape.Create`,
`RebarShapeTerminationsData`, `RebarShapeParameters.GetAllRebarShapeParameters`
/ `GetOrCreateElementIdForExternalDefinition`, y en `RebarBarType`:
`Get/SetHookLength`, `Get/SetAutoCalcHookLengths`, `GetHookTangentLength`,
`GetHookPermission`.

Hallazgo que no estaba en el issue: **una forma necesita parámetros
compartidos** para sus medidas (`RebarShapeDefinition.AddParameter` solo
acepta parámetros compartidos). Se pueden reusar los de las formas del modelo
o crearlos desde un archivo de parámetros compartidos.

## El comando: `ComandoDiagnosticoFormasPropias.cs`

Botón "Diagnóstico formas propias" (solo en Debug). Se elige una columna
rectangular y prueba, cada paso en su propia transacción:

0. Inventario: si la forma define los ganchos (`RebarShapeDefinesHooks`) y el
   diámetro de doblado de cada barra del cuadro, en diámetros.
1. Ganchos de cero: 135° con estilo de estribo (el caso que falló el 10-sep) y
   180° estándar.
2. Largo de gancho por barra: lo que calcula Revit contra el cuadro, y si se
   puede fijar a mano el de 180°.
3. Parámetros compartidos: los que ya hay, y crear cuatro nuevos desde un
   archivo temporal (el caso "modelo sin nada").
4. Formas de cero: estribo rectangular de cuatro lados con ganchos de 135°, y
   grapa de un tramo con ganchos de 180°.
5. Estribo de toda la sección y tres cajas angostas (15, 10 y 12×12 cm), con
   los tipos propios y con los del modelo, para barras #3 y #2.
6. Grapas, propias y del modelo.

De cada barra creada mide: con qué forma quedó (si Revit la cambió), si tiene
diagonales, si se sale del rectángulo pedido, el diámetro de los dobleces y el
largo de las colas de los ganchos contra el cuadro.

Al terminar pregunta si deja las barras de prueba para mirarlas en 3D (se
sacan con un Ctrl+Z) o deshace todo; por defecto deshace.

## Pendiente

- Correr el comando en Revit 2027 y analizar el texto.
- Confirmar con el equipo el gancho de estribo de 135° para #6 a #8: el
  cuadro 2 solo llega a la #5. La regla de 6 d daría extensiones de 11,5 /
  13,3 / 15,2 cm; `CuadroVarillas.GanchoEstribo` conserva los largos del
  cuadro 1 (27,2 / 31,6 / 36,2 cm).
- Decidir si el add-in toca el diámetro de doblado de las barras del modelo.
- Con los resultados: diseño escrito del #64 y plan de implementación.
