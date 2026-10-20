# Grapas como en el modelo de referencia, y separación de 0 cm

Fecha: 20-oct-2026. Estado: **implementado, falta probarlo en Revit.**

## Contexto

Luciano probó la columna 781265 y aparecieron tres problemas:

1. La grapa en S quedaba muy en diagonal.
2. Las grapas en C y en S eran muy grandes: los ganchos se salían del
   estribo principal y se metían en el recubrimiento.
3. Aparecía este aviso: "tramo 3: el paquete de estribos menores mide
   2,9 cm y la separación es de 0 cm; se omiten los estribos menores".

## Qué se encontró

### Separación de 0 cm

En un tramo confinado de tres zonas, el grupo de abajo y el del centro se
crean con `includeLastBar: false`. Así el estribo del borde entre dos zonas
no se repite.

El problema es que `Rebar.NumberOfBarPositions` sigue contando esa última
posición, aunque ahí no haya estribo. `RegistrarPosiciones` la guardaba, y el
primer estribo del grupo siguiente tiene la misma altura. Con dos estribos a
la misma altura, la separación mínima daba 0 cm.

### Grapas: medido en el modelo de referencia (MCP de Revit)

Luciano tenía un modelo con grapas que considera buenas. Se midieron con el
conector MCP de Revit. En ese modelo, 448 grapas usan:

- forma "01", estilo Standard;
- gancho "Standard - 180 deg." en las dos puntas;
- barra #3 con 64 mm de doblez.

O sea, el gancho es **más grande** que el nuestro (57 mm). El tamaño del
gancho no era el problema; lo que cambia es dónde se pone la grapa.

Medidas de la grapa 152902 (columna con estribo de 428 × 438 mm por fuera):

| Medida | Valor | Qué significa |
|---|---|---|
| B (largo total) | 450 mm | Los ganchos quedan dentro de ese largo; la grapa termina a unos 0–12 mm de la cara exterior del estribo. |
| J (ancho) | 83 mm | Es el ancho del gancho (64 + 2 × 9,5). |

En el modelo de referencia, el tramo recto pasa por la línea de las barras
longitudinales, o pegado a ellas. Los ganchos se abren hacia un solo lado.

En cambio, el add-in ponía **el centro del gancho sobre la barra**. Eso tenía
dos efectos:

- la curva del gancho quedaba unos 3 cm más afuera que la barra, es decir,
  fuera del estribo;
- el tramo recto quedaba corrido un radio de gancho (unos 3,3 cm). En la S,
  ese corrimiento va hacia un lado en una punta y hacia el otro en la otra, y
  por eso quedaba tan en diagonal.

## Fix implementado

- `PaqueteEstribos.PosicionesDelGrupo`: no cuenta la última posición si el
  grupo no la incluye. Tiene la prueba `posiciones_sin_el_ultimo_excluido`.
  `GeneradorArmado.RegistrarPosiciones` la usa.
- Los avisos de estribos menores ahora numeran los tramos igual que la
  ventana (`tramo.Indice`). Antes sumaban 1.
- Las grapas se piden desde la cara exterior del estribo (el recubrimiento)
  hasta la del otro lado, sobre la línea de las barras.
- `MedirCorreccionDeGrapa` mide dos cosas en la grapa de prueba:
  - la **punta** del gancho (centro del arco + radio + medio diámetro);
  - la **línea del tramo recto**.

  Con eso corrige la línea para que la punta termine en la cara del estribo y
  el tramo recto pase por las barras.

### Ajuste del 20-oct: el gancho tiene que pasar sobre las varillas

Luciano pidió dos cosas:

- que la grapa no se salga del tamaño del estribo, y mucho menos del
  recubrimiento;
- que el gancho pase sobre la varilla longitudinal, porque si no, no sirve.

Además recordó que en los traslapos algunas varillas cambian de posición. Una
de las dos piezas del traslapo se corre un diámetro a lo largo de la cara:

- **+Y** en las caras izquierda y derecha;
- **+X** en las caras de abajo y arriba.

Lo que se cambió:

- **Punta del gancho:** queda en la cara exterior del estribo, nunca más
  afuera.
- **Línea de la grapa:** se pide por el **medio** de las dos posiciones
  posibles de la varilla enganchada. Es la posición sin traslapo más medio
  diámetro, hacia +Y o +X.
- **Centro del gancho:** se corre de costado hasta donde el arco pasa justo
  sobre esa línea, dentro de la profundidad de la barra. La cuenta está en
  `EstriboMenor.CorrimientoSobreBarra` y tiene la prueba
  `gancho_pasa_sobre_la_barra`. Con un #3 (radio de eje de unos 33 mm) y la
  barra a unos 11 mm por dentro de la punta, el arco pasa a 24,6 mm del centro
  del gancho. Ese arco cubre con margen las dos posiciones de la varilla (un
  diámetro de distancia).
- **S:** queda casi recta. Cada punta se corre solo unos 8 mm, hacia el lado
  contrario de donde se abre su gancho.

### Segunda prueba (columna 781265 de 30×30): las grapas ocupan mucho

Mediciones con el MCP en la columna generada:

- **Estribo:** 21 cm por fuera.
- **Grapas en C:** 22 cm de largo, y el gancho de 7,6 cm de ancho. Una C,
  cerca de la cara derecha, abría el gancho hacia afuera y se pasaba 11 mm
  del estribo.
- **Ojo con las medidas:** las cajas que da Revit no son exactas al
  milímetro, así que los 5 mm que se pasan por las puntas no son seguros.
- **Traslapos:** bien. La pieza de abajo queda corrida 12,7 mm hacia +X o +Y.
- **Estribos de los tramos libres:** salió un solo grupo cada 19 cm, no tres
  zonas confinadas. Se le preguntó a Luciano qué configuró.

Luciano: allá (en el modelo de referencia) las grapas se ven más chicas y
acá toman mucho espacio. La explicación es el tamaño de la columna: allá el
estribo mide 43 cm y acá 21 cm, con casi el mismo gancho.

**Decisión de Luciano:** gancho de 180° doblado como el estribo (4 d = 3,8
cm en la #3, en vez de 5,7 cm).

Cambios:

- **Tipos propios nuevos:** `CQ Grapa 180° estribo` (gancho) y `CQ Grapa C` /
  `CQ Grapa en S` (formas), todos con estilo StirrupTie. Con ese estilo Revit
  dobla con `StirrupTieBendDiameter`.
- **No probado todavía:** que Revit acepte una forma StirrupTie de un solo
  tramo. Si no la acepta, sale un aviso y se usan las grapas Standard de
  antes.
- **Dirección de los ganchos:** todos se abren hacia el centro de la sección,
  para que no se salgan del estribo por el costado.

### Tercera prueba (columna en X = 3,5 m): grapas dobladas como el estribo

Revit aceptó la forma StirrupTie de un solo tramo:

- **Doblado:** "CQ Grapa C" y "CQ Grapa en S" salen con 0,04 de doblez
  (3,8 cm).
- **Ancho del gancho:** 5,7 cm.
- **Largo (CQ A):** 0,22, igual que el estribo (CQ A a D: 0,22 / 0,21).
- **Estribos confinados:** bien en el tramo de arriba:
  - 8 estribos cada 6,3 cm en los 50 cm de abajo;
  - 15 cada 13,6 cm en el centro;
  - 9 cada 6,5 cm en los 50 cm de arriba;
  - una caja en cada paquete.

Hallazgo: en un traslapo la pieza de ARRIBA queda corrida un diámetro hacia
**−X / −Y**. La de abajo está en la posición teórica.

- Medido en las dos columnas: en esta, la línea de X = 3469 tiene la pieza de
  arriba en 3456,5.
- La grapa C confirmó que la posición teórica es 3469: su tramo recto quedó
  exactamente donde da la cuenta con esa posición.
- La línea de la grapa se pedía en teórica + d/2. Se corrigió a teórica − d/2.
- El comentario viejo del código decía "+X / +Y" (medido el 7-oct, otro
  caso). Hay que ver si con tres piezas la tercera vuelve a la teórica.

Respuestas de Luciano y cambios:

- **Contra zapata (de −0,65 a 0):** "creo que allí no se debería generar", y
  poder ponerle confinado es un error.
  - El generador salta el tramo contra zapata en estribos y estribos
    menores, con un aviso. El acero longitudinal sí pasa por ahí.
  - La ventana no deja poner confinado en ese tramo y explica que no lleva
    estribos.
  - La vista previa (de la otra sesión) todavía dibuja estribos ahí.
- **Tramo de 0 a 3,5 m de la columna nueva:** no tenía estribos.
  Probablemente no se aceptó en la ventana.
- **Cajas:** "debería rodear/abrazar la varilla". Antes las medidas de afuera
  del lazo (ScaleToBox) iban de eje a eje de las barras. Ahora:
  - **En su eje:** los lados tocan por dentro el lado de afuera de las barras
    (`EstriboMenor.LadosQueAbrazan`, prueba `cerrado_abraza_las_barras`). El
    lado de abajo deja lugar a la pieza corrida del traslapo.
  - **En el otro sentido:** el lazo va hasta donde va el estribo principal.

## Pendiente de retest

- Que las grapas C y S queden dentro del estribo, como en el modelo de
  referencia.
- Que la S quede recta, o casi.
- Que en los tramos de tres zonas ya no aparezca el aviso de 0 cm y se
  generen los estribos menores.
- Revisar en "Detalles" la línea "gancho de grapa medido en Revit": cuánto se
  corrigió a lo largo y de costado.
