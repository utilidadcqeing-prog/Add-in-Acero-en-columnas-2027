# Diagnóstico: formas y ganchos propios del add-in (#64, con los cuadros del #65)

Fecha: 8-oct-2026. Estado: **diagnóstico corrido en Revit 2027 (8-oct): todo lo propio funciona. Falta decidir el diámetro de doblado y escribir el diseño.**

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

## Resultado de la corrida (8-oct, columna de 30×30, modelo con 222 formas y 12 ganchos)

Todo lo que el #64 necesita funciona con API pública:

- **Gancho de cero.** `RebarHookType.Create` lo entrega con estilo `Standard`;
  basta con asignarle `Style = StirrupTie` después. Eso era lo que faltaba el
  10-sep. No hace falta duplicar un gancho del modelo.
- **Extensión del gancho.** Con multiplicador 6, el gancho de 135° da
  exactamente la E del cuadro en todas las barras (3,84 / 5,72 / 7,62 /
  9,53 cm para #2 a #5). Con multiplicador 4, el de 180° da la E del cuadro de
  la #6 a la #8; de la #2 a la #5 se llegó a los 6,5 cm exactos con
  `SetAutoCalcHookLengths(false)` + `SetHookLength`. Ese ajuste es por barra
  **y por gancho**: solo afecta a nuestro gancho, no a los demás del modelo.
  Ojo: `GetHookLength` de Revit no es la "B" del cuadro (#3 a 135°: 8,47 cm
  contra 11,3 cm); lo que hay que igualar es la extensión recta
  (`GetHookExtensionLength`).
- **Parámetros compartidos.** Se crearon cuatro nuevos desde un archivo
  temporal (`ExternalDefinitionCreationOptions` con
  `SpecTypeId.ReinforcementLength` +
  `GetOrCreateElementIdForExternalDefinition`) y las formas se armaron con
  ellos: el add-in no depende de que el modelo tenga formas.
- **Forma de estribo de cero.** Cuatro lados en sentido antihorario, cotas
  medidas por fuera del doblez, ganchos de 135° en las dos puntas. Funciona
  con `CreateFromRebarShape` + `ScaleToBox`: sin diagonales, Revit no la
  cambia por otra, y sirve igual para cajas de 15, 10 y 12×12 cm. Los ganchos
  quedan en la esquina de arranque (abajo a la izquierda); la «T1» del modelo
  los deja en la opuesta.
- **Forma de grapa de cero.** Un tramo con ganchos de 180°; funciona con
  `CreateFromCurvesAndShape` y las colas salen de 6,50 cm.

Comparación con los tipos del modelo, misma barra y misma caja:

| | Propios | Del modelo |
|---|---|---|
| Cola del gancho de 135°, #3 | 5,72 cm (= cuadro) | 10,25 cm |
| Cola del gancho de 135°, #2 | 3,84 cm (= cuadro) | 4,50 cm |
| Caja de 10 cm con #3 | queda dentro | el gancho se sale 1,79 cm |
| Cola del gancho de 180°, #3 | 6,50 cm (= cuadro) | 4,76 cm |
| Cola del gancho de 180°, #2 | 6,50 cm (= cuadro) | 3,20 cm |

(La línea "se SALE" de las grapas en el texto del diagnóstico no es un
problema real: mide el ancho del gancho contra una línea sin altura.)

La comparación usa el gancho de 135° del modelo tal cual; el generador hoy lo
achica para las cajas (`GanchoAMedidaParaMenores`), así que no reproduce
exactamente lo que sale en producción.

**Lo que los tipos propios NO arreglan: el diámetro de doblado.** Está en el
tipo de barra del modelo y no coincide con el cuadro:

| Barra | Estribo (cuadro: 4 d) | Gancho estándar (cuadro: 6 d) |
|---|---|---|
| #2 | 7,81 d (5 cm) | 7,81 d |
| #3 | 4 d | 6,67 d |
| #4, #5 | 4 d | 6 d |

Con la #2, las esquinas del estribo salen con radio de 2,8 cm en vez de
1,6 cm: en una caja de 10 cm quedan solo 3,7 cm de lado recto y se ve
ovalada. Además, tocar un tipo de barra hace que Revit regenere toda la
armadura del modelo que la usa (en la corrida reaparecieron avisos viejos del
modelo al fijar los largos de gancho).

No probado: un modelo con `RebarShapeDefinesHooks = True` (aquí es `False`).

## Pendiente

- Confirmar con el equipo el gancho de estribo de 135° para #6 a #8: el
  cuadro 2 solo llega a la #5. La regla de 6 d daría extensiones de 11,5 /
  13,3 / 15,2 cm; `CuadroVarillas.GanchoEstribo` conserva los largos del
  cuadro 1 (27,2 / 31,6 / 36,2 cm).
- Decidir si el add-in corrige el diámetro de doblado de las barras del modelo
  (#2 y #3 no coinciden con el cuadro).
- Diseño escrito del #64 y plan de implementación.
