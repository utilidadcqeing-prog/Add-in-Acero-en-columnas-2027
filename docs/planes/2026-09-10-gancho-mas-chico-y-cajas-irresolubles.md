# Cajas irresolubles y ganchos exagerados en los estribos menores

## Contexto

Después de implementar la regla nueva de cajas (ver
`docs/planes/2026-09-10-cajas-por-eje-y-diagnostico-ganchos.md`), la ventana
quedó bien pero la generación en Revit falló:

- ~17 avisos `no se pudo usar la forma «T1» para una caja (An internal error
  has occurred.); se usa el camino de respaldo`;
- **17 errores duros de Revit**: "Can't solve Rebar Shape", con "Delete Rebar"
  como única salida;
- en el modelo quedaban **lazos enormes que se salían del hormigón**;
- aparte, los ganchos de los menores salían **muy exagerados**.

Los ~17 avisos eran **una sola caja repetida en cada altura de estribo**, no 17
cajas distintas. La caja nueva, al ir de extremo a extremo, queda larga y muy
angosta cuando abarca dos filas pegadas — bastante más achatada que las cajas
del producto cruzado anterior. Por eso el problema apareció recién ahora.

Causa de fondo compartida por los dos síntomas: **grapas y cajas heredaban el
tipo de gancho del estribo principal del tramo**, dimensionado para el estribo
grande.

## Decisiones del usuario

- El gancho de grapas y cajas se elige **automáticamente: el más chico que
  entre**, sin agregar nada a la interfaz.
- Si no entra ninguno, la caja se genera **sin gancho** (que exista, aunque
  haya que ajustarla a mano).

## Fix implementado

Todo en `Logica/GeneradorLongitudinal.cs`.

**1. Elegir el gancho con la API nativa.** Nuevo `GanchoQueEntra`: ordena los
`RebarHookType` del documento por `RebarBarType.GetHookLength(hookTypeId)` —
la API nativa para esto, en vez de calcular la geometría del gancho a mano — y
devuelve el más chico que entre en el espacio disponible. Para una caja, ese
espacio es su **lado corto**, que es donde el gancho cierra.

**Filtro por ángulo**, deliberado: solo se consideran ganchos del **mismo
ángulo** que el del tramo. Sin ese filtro, "el más chico" podría bajar un
gancho sísmico de 135° a uno de 90°, que es un cambio estructural y no
estético.

Para las **grapas**, si no hay ninguno más chico que entre se sigue usando el
del tramo: una grapa sin gancho no sirve, así que es preferible el de antes a
que deje de generarse.

**2. Resolver el gancho una sola vez por caja.** La geometría en planta se
repite idéntica en todas las alturas de estribo del tramo, así que el gancho y
las coordenadas de cada caja se calculan una vez, fuera del bucle de alturas.
Antes se reintentaba y se fallaba una vez por altura — de ahí los 17 avisos
iguales.

**3. Nunca dejar armadura que Revit no pueda resolver.** `CrearCaja` ya **no
cae al respaldo de `Rebar.CreateFromCurves`**: ese camino era el que dejaba las
barras irresolubles y los lazos gigantes. El orden ahora es:

1. forma nativa con el gancho más chico que entra;
2. si falla, forma nativa **sin gancho**;
3. si también falla, no se crea nada y se avisa.

Además, si un paso intermedio falla después de haber creado la barra, esa barra
se borra (`Descartar`) en vez de quedar a medio hacer en el modelo.

**4. Aislar el paso que falla.** `CrearCajaDesdeForma` tenía sus tres pasos
(`CreateFromRebarShape`, `ScaleToBox`, `SetHookTypeId`) bajo un solo `try`, así
que el "internal error" de Revit no decía si el problema era el gancho o que la
forma no se deja achatar. Ahora cada paso tiene su propio `try` y el aviso
nombra el paso más las medidas reales: alto × ancho de la caja en cm, la barra,
y el gancho con su longitud.

## Estado

Compilado (0 errores) y desplegado a Revit 2027.

## Verificación pendiente (retest del usuario)

- **Lo reportado**: generar la misma columna con la misma caja y confirmar que
  ya no aparece el diálogo "Can't solve Rebar Shape" de Revit, y que no quedan
  lazos fuera del hormigón.
- **Ganchos**: mirar la sección desde arriba y confirmar que los de grapas y
  cajas quedan chicos y proporcionados, como en el modelo de referencia, y que
  siguen mirando hacia el centro (cambio del día anterior).
- **Si todavía falla**: el aviso ahora dice qué paso falló y con qué medidas.
  Si el que falla es `ScaleToBox`, el problema **no** era el gancho sino que la
  forma T1 no se deja achatar a una caja angosta, y hay que buscar otra forma
  (o resolver la caja angosta como grapa doble). Eso queda decidido con el dato,
  sin otra ronda de suposiciones.
- **Regresión**: el estribo principal y las cajas anchas (filas no pegadas)
  tienen que seguir saliendo igual que antes.
- Correr el "Diagnóstico de Rebar" y traer el catálogo de formas y ganchos, para
  confirmar qué ganchos había disponibles y si el elegido fue el esperado.

## Riesgos anotados

- Si en el modelo no hay ningún gancho chico **del mismo ángulo**, la caja sale
  sin gancho. Es lo que eligió el usuario, pero conviene verlo en el retest
  porque es una decisión estructural.
- `GetHookLength` devuelve la longitud por defecto (no la calculada) si el tipo
  de barra tiene `AutoCalcHookLengths` apagado, así que el orden podría no
  coincidir con lo que se ve. El catálogo del diagnóstico permite contrastarlo.

## Fuera de alcance

- La ventana de estribos menores: el usuario confirmó que el menú está bien.
- Buscar una forma nativa de **grapa** (`RebarShape` de barra recta con dos
  ganchos): sigue pendiente del catálogo del diagnóstico. Las grapas se siguen
  creando con `CreateFromCurves`; acá solo cambió su gancho.

Fuentes de la API:
[RebarBarType.GetHookLength](https://www.revitapidocs.com/2015/abcf48ad-052f-40bd-ee5f-b5d59d607123.htm),
[RebarHookType](https://www.revitapidocs.com/2022/3c7a78d5-f92b-e58b-e7c9-7927537498fd.htm).

## Retest del 10-sep: el gancho NO era el problema de las cajas

El aviso separado por pasos dio la respuesta de una: `la forma «T1` no se pudo
**ajustar** a la caja de 18,5×3,7 cm, barra «#3 COL», **sin gancho**`. Dos datos
en una línea:

- el paso que falla es **`ScaleToBox`**, no el gancho;
- el intento que falla ya iba **sin gancho**, así que el gancho nunca estuvo
  involucrado.

La caja mide **3,7 cm de alto** con una barra **#3**: una barra de ese diámetro
no se puede doblar en un lazo cerrado tan bajo, el radio de doblado no entra.
Revit tiene razón en rechazarla; no es un bug del addin. Sale a 3,7 cm porque la
sección tiene 4 filas de barras repartidas en ~18,5 cm y la caja abarcaba dos
filas **contiguas**.

### Ajustes a partir de eso

- **Aviso claro en vez de "internal error"**: ahora dice que la caja es
  demasiado angosta para doblar esa barra, con el diámetro, y sugiere elegir
  casillas no contiguas o una barra más delgada.
- **Un solo aviso por caja**: si una caja falla, se saca de la lista y no se
  reintenta en las demás alturas de estribo (antes: 17 avisos idénticos).
- **No reintentar sin gancho cuando falla `ScaleToBox`**: sacarle el gancho no
  cambia nada ahí y solo duplicaba el aviso. El reintento sin gancho queda solo
  para cuando el paso que falla es el del gancho.

## Segundo hallazgo del retest: las grapas iban de cara a cara del hormigón

Los lazos que se salían del hormigón no eran las cajas (que ni se creaban): eran
las **grapas**. Se creaban de `xMin` a `xMax` — las caras del hormigón, **sin
descontar el recubrimiento** — así que el gancho de cada punta quedaba por fuera
del concreto.

**Fix**: la grapa ahora va de barra a barra, con el mismo `insetEsquinas` que
usan las barras de esquina, igual que en el modelo de referencia (donde la grapa
engancha la barra del perímetro, no el borde del concreto).

## Verificación pendiente (segundo retest)

- Las grapas tienen que quedar **dentro** del hormigón, enganchando las barras
  del perímetro, sin lazos asomando.
- La caja de dos filas contiguas debe dar **un solo aviso** claro y no generarse.
- Probar una caja con casillas **no contiguas** (más alta) y confirmar que sí se
  genera y que ahí el gancho entra.

## Tercera vuelta: gancho a medida y grapas que pasan por detrás

Con las grapas ya dentro del hormigón, el usuario mostró la comparación contra
el modelo de referencia: la forma del gancho seguía mal (una curva enorme en vez
de un retorno corto que abraza la barra), y señaló además que **la grapa tiene
que pasarse un poco de la longitudinal, para engancharla por detrás** -- si
termina justo en el eje de la longitudinal, el gancho no la abraza y la grapa no
cumple ninguna función.

### Gancho a medida en vez de heredar o buscar

Elegir "el más chico del modelo" dependía de que el modelo tuviera cargado uno
chico. Ahora el addin **crea el suyo** con `RebarHookType.Create(doc, ángulo,
multiplicador)` -- API pública y documentada, verificada antes de usarla:

- mismo **ángulo** que el gancho del tramo (bajarlo sería un cambio estructural,
  no estético);
- tramo recto de **6 diámetros de barra**, el gancho sísmico estándar
  (`MultiplicadorGanchoMenores`). **PENDIENTE de confirmar el valor con Carlos**;
- se crea una sola vez por documento, con el nombre
  `CQEING - gancho de menores`, y en las corridas siguientes se encuentra por
  nombre en vez de duplicarse;
- si Revit no lo deja crear, se sigue usando el del tramo (comportamiento
  anterior), no se rompe la generación.

Para las cajas se prefiere ese gancho y, si ni ese entra en el lado corto, se
busca el más chico del modelo que sí (`GanchoQueEntra`); si no hay ninguno, sin
gancho.

### La grapa se pasa de la longitudinal

El extremo de la grapa ya no cae en `insetEsquinas` (el **eje** de la barra
longitudinal) sino en `recubrimiento + diámetro del estribo + medio diámetro de
la grapa`: el eje de la grapa queda a medio diámetro de la cara interna del
estribo, que es más afuera que el eje de la longitudinal, así que la cruza por
detrás y el gancho puede abrazarla.

## Regla nueva en CLAUDE.md

Se agregó al `CLAUDE.md` del repo la regla de **no usar API interna de Revit**:
verificar en la documentación oficial que el miembro sea público antes de usarlo,
porque los internos compilan sin error y recién fallan en tiempo de ejecución
dentro de Revit -- el build en verde no alcanza como comprobación.

## Verificación pendiente (tercer retest)

- La grapa tiene que verse como en el modelo de referencia: recta, con un
  retorno **corto** en cada punta que abraza la barra longitudinal por detrás.
- Confirmar que el gancho ya no es una curva que se come media sección.
- Confirmar que en el modelo aparece un tipo de gancho nuevo llamado
  `CQEING - gancho de menores`, y que al generar por segunda vez **no** se crea
  otro duplicado.
- El tramo recto de 6 diámetros es el valor a validar con Carlos.

## Cuarta vuelta: el gancho creado de cero rompía TODAS las grapas

El retest anterior falló peor: ninguna grapa se creó, con el aviso "Revit
rechazó la armadura sin dar motivo" repetido.

Causa: `RebarHookType.Create(doc, ángulo, multiplicador)` crea el gancho **sin
control sobre su `Style`**, y `RebarHookType.Style` define a qué estilo de barra
se le puede aplicar ese gancho ("The hook may only be applied to shapes of the
specified style"). La grapa se crea como `RebarStyle.StirrupTie`, así que el
gancho nuevo no le servía y Revit rechazaba la armadura entera.

La documentación no aclara si `Style` tiene setter, así que no se fuerza por ahí.

### Fix: duplicar en vez de crear de cero

`GanchoAMedidaParaMenores` ahora hace `ganchoDelTramo.Duplicate(nombre)` —
`ElementType.Duplicate`, público y documentado — y solo le cambia
`StraightLineMultiplier`. El estilo (y todo lo demás) viene bien del gancho que
el tramo ya usa sin problemas; lo único que cambia es el largo del tramo recto.

### Fix: un gancho que Revit no acepte no puede dejar la columna sin grapas

`CrearGrapa` se partió en `IntentarGrapa` (un intento, devuelve el motivo del
fallo) más la orquestación: se prueba con el gancho de los menores y, si Revit
no lo acepta, **se reintenta con el del tramo**, que ya se sabe que funciona. El
aviso sale solo si fallan los dos, y si funcionó el respaldo se avisa cuál se
usó. Una grapa con el gancho grande de antes es mejor que ninguna grapa.

### Guarda contra un gancho inservible ya cargado

La versión rota alcanzó a crear un tipo de gancho con el `Style` equivocado,
pero el usuario **no guardó el modelo**, así que no quedó nada. Igual se dejó la
guarda: la búsqueda por nombre ahora **valida el `Style`** contra el del tramo
antes de reusar cualquier gancho que encuentre, así que un tipo inservible con
ese nombre nunca se reutiliza.

## Verificación pendiente (cuarto retest)

- Que las grapas **se generen** (era lo que estaba roto).
- Que el gancho sea un retorno corto que abraza la longitudinal por detrás,
  como en el modelo de referencia.
- Que aparezca `CQEING - gancho de menores` y que al generar dos veces no
  se duplique.
- Si algún aviso dice "se generó con «...», el del estribo", significa que el
  gancho a medida todavía no le sirve a Revit y hay que mirar por qué.

## Quinta vuelta: la forma nativa de grapa (T9)

El diagnóstico ampliado dio los números y cerró la discusión:

```
Barra: «#3 COL», diámetro 9,5 mm
Extremo 0: «CQEING - gancho de menores» | 135° | Style=StirrupTie |
           multiplicador=6 | largo real=8,5 cm | esperado por multiplicador=5,7 cm
[posición 0]  (14,037, -2,731, 6,950) -> (14,229, -2,731, 6,950)
```

- El gancho a medida **sí se creó y sí se aplicó** (Style correcto, 135°,
  multiplicador 6): el `Duplicate` funcionó.
- Pero **el multiplicador no manda**: largo real 8,5 cm contra 5,7 cm
  esperados. El tipo de barra `#3 COL` tiene su propio largo de gancho y pisa
  al `RebarHookType`. Achicar el multiplicador no cambia nada.
- La grapa mide 19,2 cm de parte recta con 8,5 cm de gancho por punta.

La hipótesis de que se veía mal "porque la columna es chica" **quedó
descartada por el usuario**: en columnas de 40×40 sale igual.

### La causa real

La grapa se armaba pasándole a `Rebar.CreateFromCurves` una **línea suelta con
estilo `StirrupTie`** -- un "estribo" de un solo lado, que no es una forma
real. Revit tenía que sintetizar la forma y sacaba ganchos enormes y deformes.

El catálogo mostró que el modelo tiene la forma nativa que faltaba:
**`T9 | StirrupTie | 1 segmento`** -- una barra recta con estilo de estribo. Es
a la grapa lo que T1 es al estribo cerrado.

### Fix

- `GeometriaSeccion.BuscarFormaGrapa`: busca la forma por **estructura**
  (estilo `StirrupTie` + un solo segmento), no por nombre, prefiriendo "T9" si
  está. Mismo patrón que `BuscarFormaEstribo`.
- `IntentarGrapa` usa `Rebar.CreateFromCurvesAndShape`: la posición la damos
  nosotros con la línea, y la geometría del gancho la resuelve la definición de
  la forma. Si no hay forma o falla, cae al `CreateFromCurves` de antes.

**Nota de API**: la sobrecarga de `CreateFromCurvesAndShape` que documenta
revitapidocs para 2022/2025 (con `RebarHookOrientation`) **quedó obsoleta y ya
no existe en 2027**; el tipo `RebarHookOrientation` tampoco. La que sirve es
`(doc, forma, barra, host, normal, curvas, BarTerminationsData)`, igual que el
`CreateFromCurves` que ya se usaba.

## Verificación pendiente (quinto retest)

- Que la grapa tenga la forma del modelo de referencia: recta, con un retorno
  corto en cada punta.
- Sigue pendiente que **pase por detrás de la longitudinal**: hoy termina justo
  en el eje de esa barra, porque la grapa usa el mismo tipo de barra que la
  longitudinal de cara y el inset da igual. Falta decidir la referencia (medir
  el modelo de la empresa vs. calcularla).
- Sigue pendiente decidir qué hacer con el largo de gancho de 8,5 cm que
  impone el tipo de barra.

## Sexta vuelta: la respuesta salió de MEDIR la grapa del modelo de referencia

Cinco intentos ajustando la grapa a mano no convergieron. La respuesta llegó
cuando el usuario corrió el diagnóstico sobre una grapa del **modelo de la
empresa**:

```
--- Rebar 153247 (host 152865) ---
  Barra: «#3», diámetro 9,5 mm
  Extremo 0: «Standard - 180 deg.» | ángulo 180° | Style=Standard |
             multiplicador=4 | largo real=12,7 cm
  [posición 0]  (-4,623, -4,868, 15,260) -> (-4,623, -4,418, 15,260)
```

Tres cosas que dan vuelta todo lo que se venía haciendo:

1. **El gancho es de 180°, no de 135°.** Un gancho de 180° da la vuelta
   completa alrededor de la barra longitudinal -- eso es lo que produce los
   semicírculos de la imagen de referencia y lo que hace que la grapa la agarre
   "por detrás". Un gancho de tirante de 135° no lo hace.
2. **El `Style` del gancho es `Standard`, no `StirrupTie`.** Como un
   `RebarHookType` solo se puede aplicar a barras de su mismo estilo, esa grapa
   es una barra **`RebarStyle.Standard`** -- **no un estribo**. En ese
   documento, además, todas las formas de un solo segmento son Standard; no
   existe ninguna T9.
3. **El largo del gancho nunca fue el problema**: el de la empresa mide
   **12,7 cm**, MÁS que los 8,5 cm que salían acá. Toda la vuelta de achicar el
   gancho (multiplicador, `GetHookLength`, gancho a medida) apuntaba al lado
   equivocado.

La causa real: se armaba la grapa como un estribo de un solo lado
(`RebarStyle.StirrupTie` sobre una línea suelta, con gancho de tirante de
135°). Eso no es una forma real, así que Revit tenía que sintetizarla y sacaba
esos lazos enormes -- también en columnas de 40x40, que fue lo que descartó la
explicación de "la columna es chica".

### Fix

- `GeometriaSeccion.BuscarFormaGrapa` ahora busca una forma de **un segmento
  con estilo `Standard`** (antes buscaba `StirrupTie`, guiada por el "T9" del
  otro documento).
- `GeometriaSeccion.BuscarGanchoGrapa`: nuevo, devuelve el gancho de **180° con
  estilo `Standard`**.
- `IntentarGrapa` crea la grapa con `RebarStyle.Standard` (antes `StirrupTie`),
  tanto por `CreateFromCurvesAndShape` como en el respaldo.
- Las **cajas no cambian**: son lazos cerrados y siguen con estilo de estribo y
  el gancho de menores, como el estribo principal.

## Verificación pendiente (sexto retest)

- Que la grapa se vea como la del modelo de referencia: recta, con un retorno
  de 180° ajustado en cada punta que abraza la longitudinal.
- Con eso debería resolverse también lo de "pasar por detrás de la
  longitudinal", que es justamente lo que hace un gancho de 180°.
- Que las cajas y el estribo principal no hayan cambiado.

## Séptima vuelta: el gancho de la grapa, proporcional a la grapa

Con el estilo ya corregido (barra Standard + gancho de 180°), la grapa quedó
con la forma correcta pero se veía como un lazo cerrado. Los números lo
explican:

| | Parte recta | Gancho | Gancho / barra |
|---|---|---|---|
| Modelo de la empresa | 45 cm | 12,7 cm | **28%** |
| El addin | 19,2 cm | 12,1 cm | **63%** |

El gancho es prácticamente el mismo (12,1 vs 12,7 cm). Lo que cambia es el
largo de la barra: con un retorno de 180° de 12,1 cm sobre una grapa de
19,2 cm, los dos retornos se cruzan en el medio y desde arriba se ve cerrada.

De dónde sale ese largo: el multiplicador del gancho dice 4,8 cm pero el real
es 12,1 cm; **lo impone el tipo de barra**, igual que en el modelo de la
empresa (4,8 → 12,7).

### Fix: gancho propio de grapa, con largo proporcional

`RebarBarType.SetHookLength(hookId, largo)` define el largo por **par (tipo de
barra, tipo de gancho)**. Aprovechando eso, el addin duplica el gancho de 180°
en uno propio y le fija el largo solo a esa combinación: **ninguna armadura
existente cambia**, porque nada más usa ese gancho.

- `ProporcionGanchoGrapa = 0.28` -- el 28% medido en el modelo de la empresa.
  **Provisional**: falta que Carlos confirme el criterio normativo, porque el
  gancho de 180° tiene mínimos de código que esta proporción no mira.
- Se crea un tipo de gancho por cada largo distinto (el nombre lleva la
  medida, ej. `CQEING - grapa 180° 5,4cm`), así se reusa entre columnas de la
  misma sección en vez de acumular uno por corrida.
- El largo se fija en cada corrida, no solo al crear el tipo: el largo vive en
  el tipo de barra, así que otra barra con el mismo gancho necesita su propia
  entrada.
- Si algo de esto falla, se avisa y queda el gancho que impone el tipo de
  barra (comportamiento anterior), no se rompe la generación.

## Verificación pendiente (séptimo retest)

- Que la grapa se vea abierta, con un retorno corto en cada punta, en la misma
  proporción que la del modelo de referencia.
- Probar en una columna chica **y** en una de 40x40: la gracia de que sea
  proporcional es que se vea parejo en las dos.
- Que aparezcan los tipos `CQEING - grapa 180° ...cm` y que al generar dos
  veces sobre la misma sección no se dupliquen.
- **Pendiente de Carlos**: confirmar el 28%, o el criterio normativo que
  corresponda para el gancho de 180°.

## Octava vuelta: el mínimo normativo es también el límite físico

El retest de la vuelta anterior falló con **"Can't solve Rebar Shape"** (17
errores, opción única "Delete Rebar"). Causa: la proporción sola daba **5,4 cm**
de gancho en la columna chica, y Revit no puede resolver un gancho de 180° tan
corto en una barra #3 -- el arco del doblez ya no entra.

Coincide exactamente con la NSR-10, que el usuario trajo en la misma sesión:
para el gancho de 180° de una grapa el mínimo es **4db o 65 mm, el mayor** --
6,5 cm para una #3. O sea que ese mínimo normativo es además el límite físico
de fabricación, y Revit lo hace cumplir a su manera.

**Fix**: `GanchoProporcionalDeGrapa` ahora toma
`max(28% del largo de la grapa, max(4db, 65mm))`. La proporción sigue mandando
en columnas grandes (donde da más que el mínimo) y el piso protege las chicas.

## Lo que quedó pendiente de Carlos

De la NSR-10 que trajo el usuario salen dos preguntas que **no se resuelven sin
él**, porque contradicen el modelo de referencia de la empresa:

1. **Ganchos distintos en cada punta.** El texto dice que una grapa
   reglamentaria lleva un extremo a 135° y el otro a 90°, alternando la
   posición entre grapas consecutivas en altura. Pero la grapa medida del
   modelo de la empresa tiene **180° en los dos extremos**. Hoy el addin pone
   el mismo gancho en las dos puntas y no alterna. Si vale la regla del
   135°+90°, cambia el diseño.
2. **Qué mide `SetHookLength`.** El mínimo normativo de 65 mm es la **extensión
   recta** posterior al doblez, sin el arco. El gancho de la empresa da 12,7 cm
   por `GetHookLength`, que parece incluir el arco. Si la API espera el total,
   fijarle 6,5 cm dejaría la parte recta por debajo de la norma. Hasta
   confirmarlo, el número que se fija es el que Revit acepta, no
   necesariamente el que pide la norma.

## Verificación pendiente

- Que no vuelva a aparecer "Can't solve Rebar Shape".
- Que la grapa se vea abierta en columna chica y en 40x40.
- Las dos preguntas de arriba, con Carlos.

## Novena vuelta: el gancho de 180° NO se puede acortar -- revertido

Con el piso normativo de 6,5 cm el error empeoró: **34 errores** "Can't solve
Rebar Shape" en vez de 17.

La aritmética explica por qué, y cierra el tema:

- un gancho de 180° tiene un **arco de semicírculo obligatorio**;
- con el diámetro interior mínimo de `4db`, el eje de la barra gira con radio
  `2,5db` = 2,4 cm en una #3;
- ese arco **solo él** mide `π × 2,4` ≈ **7,5 cm** -- ya más que los 6,5 cm que
  se le estaban fijando como largo TOTAL;
- sumando el tramo recto mínimo de 6,5 cm, el piso real ronda los **14 cm**.

La grapa de la empresa mide **12,7 cm** y la nuestra medía **12,1 cm**: las dos
ya estaban en ese piso. **No había margen para achicar nada**, y toda la línea
de trabajo de acortar el gancho estaba condenada desde el principio.

**Revertido**: se quitó el largo a medida (`GanchoProporcionalDeGrapa`, la
proporción del 28% y el piso normativo). Las grapas vuelven a usar el gancho de
180° estándar del documento, que es el estado que sí generaba.

### Lo que queda como única salida real

Si la grapa tiene que verse más compacta, no puede ser con 180° en las dos
puntas. La NSR-10 que trajo el usuario describe justamente otra configuración:

- **un extremo a 135°** y **el otro a 90°** (nunca 90° en ambos);
- **alternando** la posición del gancho entre grapas consecutivas en altura.

Un gancho de 90° gira un cuarto de círculo y uno de 135° tres octavos: los dos
son bastante más cortos que el semicírculo de 180°. Esa configuración daría una
grapa mucho más compacta **y** sería la reglamentaria.

**Contradice la grapa medida del modelo de la empresa**, que tiene 180° en los
dos extremos. Es la decisión que hay que llevarle a Carlos antes de
implementarla -- ver "Lo que quedó pendiente de Carlos" más arriba.

## CERRADO en la reunión del 10-sep: 180° en las grapas, 135° en las cajas

La pregunta que quedaba para Carlos ("Ganchos distintos en cada punta", la regla
NSR-10 de 135°+90° alternando) la contestó **William** en la reunión del 10-sep
(1:04:41). Luciano preguntó expresamente si los ganchos de los estribos menores
son siempre de 180°, y si los de las cajas son de 135°:

> — ¿Los ganchos son siempre de 180 grados?
> — En este caso son gancho... son en 180 grados.
> — ¿Y los de cajas son, o sea, como un estilo normal, cierto, de 135?
> — Sí, a 135 sí.

Y sobre la NSR-10 que traía Luciano ("aquí decía que se ponían unos en 135, en 90
y así"), la respuesta fue: **"No, no, solamente en eso"**.

O sea: **la configuración actual es la correcta** — grapas con gancho de 180° en
las dos puntas, cajas con el gancho de 135° del estribo. La regla de 135°+90°
alternando no aplica acá y no hay que implementarla. Queda abierta solo la
segunda pregunta de esa sección (qué mide exactamente `SetHookLength`), que hoy
no bloquea nada porque el largo del gancho ya no se toca.

Ver `docs/planes/2026-09-10-issues-reunion-10sep.md` para el resto de la reunión.
