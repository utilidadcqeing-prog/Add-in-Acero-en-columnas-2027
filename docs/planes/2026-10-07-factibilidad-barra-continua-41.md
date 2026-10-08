# Factibilidad del #41: una sola barra de punta a punta, medida contra Revit real

## Contexto

El issue [#41](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/41)
pide una sola barra longitudinal por posición, de la zapata al tope, con los
traslapos aplicados encima como empalmes nativos y reversibles.

`Logica/GeneradorLongitudinal.cs` corta la barra en cada límite entre
columnas apiladas dando por hecho que "Revit no permite un mismo Rebar en dos
FamilyInstance" y que "Revit no aloja un Rebar cuya curva se sale del sólido
de su host". Esa suposición venía de la sesión del 9-sep, donde se concluyó
buscando en documentación y en add-ins de referencia, sin medirla. Antes de
diseñar nada se midió con un comando temporal (regla del `CLAUDE.md`).

## La prueba

Comando temporal `ComandoDiagnosticoBarraContinua` (botón "Diagnóstico barra
continua", `ComandoDiagnosticoBarraContinua.cs` + alta en `App.cs`). Se
seleccionan las columnas apiladas de una pila y el comando crea barras de
prueba que quedan en el modelo (se sacan con Ctrl+Z).

Pila usada (Revit 2027, 7-oct): dado 781598 (0,40x0,40, Z -0,65 a 0), columna
782104 (0,30x0,30, Z 0 a 3,5) y columna 782735 (0,30x0,30, Z 3,5 a 7). Barra
#4, tipo de empalme "Empalme solapado 60D".

## Qué se midió

1. **Una barra hosteada en una sola columna puede recorrer toda la pila.**
   `Rebar.CreateFromCurves` con host = el dado de 65 cm y una línea de 7,45 m
   (Z -0,55 a 6,9) devolvió la barra completa, sin recortar y sin ningún aviso.
   Lo mismo con host = la columna de más arriba. La suposición del código era
   falsa.
2. **Varios empalmes en una sola llamada funcionan, también fuera del host.**
   `RebarSpliceUtils.SpliceRebar(doc, id, opciones, lista)` con dos
   `RebarSpliceGeometry(punto, BasisZ)` (Z 1,75 y Z 5,25, los dos fuera del
   host) devolvió 3 piezas, y `GetSpliceChain` las reconoce como una cadena de
   3. `CanRebarBeSpliced` dio `Success` para los dos. El punto se arma con la
   X/Y leída de la barra ya creada.
3. **El corrimiento lateral lo pone Revit y va en zigzag**: la pieza del medio
   se corre un diámetro (1,3 cm con #4) y la de arriba vuelve a la X original.
   El solape también lo pone Revit (76 cm).
4. **Después de partir hay que reasignar el host de cada pieza.** Revit dejó la
   pieza de arriba hosteada en la columna del medio, que no la contiene, con el
   aviso "Rebar is placed completely outside of its host". Con
   `Rebar.SetHostId` a la columna que contiene el punto medio de cada pieza, el
   aviso desaparece, la geometría no cambia y la cadena de empalme sigue siendo
   de 3.
5. **Es reversible.** `RebarSpliceUtils.UnifyRebarsIntoOne` aplicado de abajo
   hacia arriba devolvió una sola barra idéntica a la original. Devuelve un
   elemento nuevo y borra las dos piezas: los ids hay que leerlos antes.

## Correcciones a lo que decía el issue

- La sobrecarga `RebarSpliceGeometry(barra.Id, punto)` que usa el Dynamo
  `Empalme_una_barra.dyf` **no existe** en la API de Revit 2027: el único
  constructor documentado es `(XYZ origen, XYZ normal)`, el que ya usa el
  código. Lo aprovechable del Dynamo es pasar varias geometrías en una llamada
  y que el punto toque la barra exacto.

## Sin probar todavía

- Ganchos en las dos puntas de una barra con varios empalmes (hoy existe
  `ReponerGancho` para el caso de un solo empalme).
- Barra de cara Y (normal `BasisY`): la prueba usó solo `BasisX`.
- Qué host conviene para la barra entera cuando no lleva ningún empalme.

## Estado

Prueba cerrada: el #41 es factible con API pública. Sigue el diseño del
cambio en `GeneradorLongitudinal`. El comando de diagnóstico es temporal y se
saca cuando el #41 quede implementado. Cambios sin commit.

## Segunda medición (tipos propios y sentido del corrimiento)

Misma pila, barra #4, cuatro varillas con normales +X, -X, -Y y +Y.

- **Solape del tipo propio: exacto.** `CQ Traslapo #4` (multiplicador 59,055)
  dio 0,75 m de solape medido en los ocho empalmes. El multiplicador con
  decimales funciona.
- **El corrimiento NO sigue el sentido de la normal.** La pieza corrida salió
  siempre hacia el lado positivo del eje de la normal:

  | Varilla | Normal | Corrimiento de la pieza del medio |
  |---|---|---|
  | A, izquierda-abajo | +X | +1,27 cm en X (hacia el centro) |
  | B, derecha-abajo | -X | +1,27 cm en X (hacia AFUERA) |
  | C, derecha-arriba | -Y | +1,27 cm en Y (hacia AFUERA) |
  | D, izquierda-arriba | +Y | +1,27 cm en Y (hacia AFUERA) |

  La normal decide el eje del corrimiento, pero no su sentido. La compuerta de
  la tarea 2 del plan no pasó: hace falta otra forma de mandar el corrimiento
  hacia el centro. Se prueban cuatro en la tercera medición: invertir la
  normal de la cara de empalme, dibujar la barra de arriba hacia abajo, tipo
  sin corrimiento + mover las piezas a mano, y corregir a mano el corrimiento
  de Revit.
- Sin avisos de Revit al confirmar, y el cambio de host de cada pieza volvió a
  funcionar en las cuatro.

## Tercera medición (cómo mandar el corrimiento hacia el centro)

Cuatro varillas del lado derecho (X=3,58; hacia el centro es -X), normal +X.

| Variante | Resultado |
|---|---|
| A: `RebarSpliceGeometry` con normal -Z | Sin efecto: la pieza del medio siguió en +1,27 cm |
| B: barra dibujada de arriba hacia abajo | Sin efecto: la pieza del medio siguió en +1,27 cm |
| C: tipo con `ShiftOption.None` + `MoveElement` de la pieza del medio (-1,27 cm) | Se movieron LAS TRES piezas juntas a X=3,567; siguen alineadas, sin corrimiento entre sí |
| D: `BarPlane` + `MoveElement` de la pieza del medio (-2,54 cm) | Se movieron LAS TRES piezas -2,54 cm; la del medio sigue 1,27 cm hacia +X respecto de las otras |

Conclusiones:

- **El corrimiento relativo entre piezas lo fija Revit y no se puede invertir**
  con nada de lo probado: la segunda pieza de cada empalme queda siempre un
  diámetro hacia el lado positivo del eje de la normal.
- **Mover una pieza mueve toda la cadena de empalme, rígida.** Las piezas
  están atadas entre sí; no se puede reubicar una sola. En las dos variantes
  la cadena siguió reconocida (3 barras) y sin avisos de Revit.
- Lo que sí se puede elegir es **dónde queda la cadena entera**. Si el lado
  positivo es hacia afuera, corriendo toda la cadena un diámetro hacia el
  centro las piezas pares (la de abajo, la tercera...) quedan un diámetro
  hacia adentro y las impares en la posición de diseño: el mismo zigzag
  entre "posición de diseño" y "un diámetro hacia el centro", con los turnos
  cambiados.
