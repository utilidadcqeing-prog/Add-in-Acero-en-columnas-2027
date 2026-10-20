# Diseño: panel visual de estribos menores, paquetes de estribos y formas propias (#64 revisado)

Conversación con Luciano del 20-oct-2026. Reemplaza en parte a
`2026-10-09-diseno-formas-ganchos-propios-64.md` (#64): lo que no se dice acá
sigue valiendo de ese documento; lo que se contradice, manda este.

Las decisiones de Luciano van marcadas **(Luciano)**. Las que tomé yo, sin
consultarlas, van marcadas **(decisión mía)** para que se revisen.

## Por qué

1. **El panel de estribos menores es poco visual.** Hoy se tildan casillas a
   los costados de la sección y no se ve qué sale hasta generar. Se quiere
   ver las piezas dibujadas y ponerlas a mano sobre la sección.
2. **Estribos menores a la altura del estribo.** Hoy se generan a la misma
   altura que el estribo principal, cruzándose con él. En obra van justo
   encima o justo debajo **(Luciano)**, y eso cambia cómo se reparten los
   estribos a lo largo del tramo.
3. **El #64 está diseñado pero sin programar.** Cajas y grapas siguen
   dependiendo de las formas y los ganchos que traiga cada modelo. El último
   retest todavía mostraba el aviso "el gancho mide 7,5 cm y el cuadro pide
   11,3 cm" (`2026-10-19-retest-metodologia-traslapos.md`).

## Qué se quiere lograr

- Un panel donde se ve la sección de la columna desde arriba y se ponen las
  piezas arrastrándolas o haciendo clic, con una vista previa
  semitransparente antes de soltarlas.
- Tres formas de estribo menor: **grapa en C**, **grapa en S** y **estribo
  cerrado** (caja).
- Cada pieza con su propio número de barra.
- En cada posición de estribo, el principal y sus menores forman un
  **paquete** apilado. El reparto a lo largo del tramo tiene en cuenta lo
  que ocupa el paquete.
- Estribos, cajas y grapas con las formas y los ganchos del cuadro de la
  empresa, sin depender del modelo (#64).

Fuera de alcance:

- Grapas que no vayan de borde a borde: siempre cruzan la sección entera
  **(Luciano)**.
- Estribos menores en los nudos (tramos `DentroDeViga`): hoy no se generan y
  sigue igual.
- Selector "por defecto del add-in / del modelo" para formas y ganchos:
  sigue descartado, como decía el #64.

Sería un fracaso, aunque funcione:

- que la vista previa del panel muestre algo distinto de lo que se genera;
- que un paquete se meta en un nudo, o que dos paquetes se pisen.

## 1. Panel de estribos menores

### Distribución (opción A, elegida por Luciano)

Tres columnas:

- **Izquierda, paleta:** tres dibujitos con su nombre: "Grapa en C",
  "Grapa en S" y "Estribo cerrado".
- **Centro, sección:** la columna vista desde arriba, con el estribo
  principal, las barras de esquina e intermedias, y las piezas ya puestas.
- **Derecha, pieza seleccionada:** qué es y dónde está (por ejemplo,
  "Estribo cerrado, columnas 1 a 2"), el combo "# de varilla", el botón
  "Voltear" (solo para C y S) y el botón "Eliminar (Supr)".

Abajo, como hoy: Aceptar y Cancelar. La ventana sigue abriéndose desde
"Editar estribos menores…" del panel principal, para la columna activa, con
la cantidad de barras por cara ya cargada.

Boceto: `.superpowers/brainstorm/567-1792498326/content/panel-menores.html`
(opción A).

### Cómo se pone cada pieza

**Grapa en C y grapa en S (un clic) (Luciano).** La grapa siempre va de
borde a borde, así que la define una sola barra intermedia:

- Se arrastra la forma desde la paleta y se suelta sobre una barra
  intermedia, **o** se elige la forma en la paleta y se hace clic en la
  barra.
- Una barra de la cara izquierda o derecha da una grapa en esa **fila**, de
  la cara izquierda a la derecha. Una de la cara de abajo o de arriba da una
  grapa en esa **columna**, de abajo hacia arriba. Da lo mismo cuál de las
  dos barras enfrentadas se elija.
- Sobre una barra de esquina no se puede **(decisión mía)**: ahí la grapa
  quedaría encima del estribo principal. Al pasar por encima de una esquina
  no hay vista previa y no pasa nada al soltar.

**Estribo cerrado (dos clics) (Luciano).**

- Con la forma elegida en la paleta, o arrastrada y soltada sobre la
  primera barra, se marca una barra intermedia y después otra.
- Las dos tienen que ser del mismo eje: dos de las caras izquierda/derecha
  (la caja abarca esas **filas** y cruza la sección de lado a lado en X), o
  dos de las caras de abajo/arriba (abarca esas **columnas** y cruza en Y).
  Puede ser de la misma cara o de la de enfrente. Es la misma geometría que
  `CajaMenor` hoy: rango en un eje y de extremo a extremo en el otro.
- Si el segundo clic cae en una barra del otro eje, en la misma barra o en
  una esquina, no pasa nada: se sigue esperando el segundo clic
  **(decisión mía)**.
- La tecla Esc o un clic fuera de la sección cancelan una caja a medio
  poner.

**Vista previa (Luciano).** Mientras se arrastra, o entre el primer y el
segundo clic de una caja, la pieza se dibuja semitransparente donde
quedaría, y la barra bajo el mouse se resalta. Si en esa posición la pieza
no se puede poner, no se dibuja.

**Seleccionar, voltear y eliminar.**

- Un clic sobre una pieza puesta la selecciona y la resalta. A la derecha
  aparecen sus datos.
- "# de varilla": las barras aptas para estribo (sección 3), con
  "Igual al estribo" como primera opción y valor por defecto
  **(decisión mía)**. "Igual al estribo" usa la barra de estribo de cada
  tramo, como hoy.
- "Voltear" cambia hacia qué lado miran los ganchos. En la C, los dos pasan
  al otro lado. En la S, cada uno pasa al lado contrario.
- "Eliminar" y la tecla Supr la quitan.
- Un clic en un lugar vacío deselecciona.

### Reglas que el panel hace cumplir

- **Sin repetidos:** no se puede poner una segunda grapa (de cualquier
  forma) en la misma fila o columna; la vista previa no aparece.
- **Grapa tapada por una caja:** si una grapa queda dentro del rango de una
  caja del mismo eje, gana la caja y la grapa no se genera (regla actual,
  confirmada por Luciano). El panel la muestra en gris con una nota debajo
  de la sección, para que se vea antes de generar.
- **Cajas que se tocan:** dos cajas del mismo eje que se superponen o
  comparten una barra se dejan poner, porque van en niveles distintos del
  paquete (sección 2) **(decisión mía)**. Ya no se fusionan solas: con el
  panel nuevo cada caja es la que el usuario dibujó.
- **Cambio en la cantidad de barras:** al abrir el panel, las piezas que
  apuntan a una fila o columna que ya no existe se descartan, con un aviso
  en el panel principal.

### Configuraciones existentes

Se pasan solas al formato nuevo al cargarlas (configuraciones guardadas y lo
que ya tenga cada columna): `GrapasFila` → grapa en C en esa fila,
`GrapasColumna` → grapa en C en esa columna, `Cajas` → estribo cerrado con
el mismo eje y rango. Todas con "Igual al estribo" y sin voltear.

## 2. Paquetes: alturas de los estribos menores

### Niveles

En cada posición de estribo de un tramo libre, el principal y sus estribos
menores forman un **paquete** de niveles apilados **(Luciano)**:

- El estribo principal es el nivel central.
- Cada estribo menor va en un nivel justo encima o justo debajo de otro,
  tocándose: la distancia entre ejes de dos niveles vecinos es la suma de
  sus medios diámetros (en cada nivel, el de la barra más gruesa). **Lo más
  cerca posible del principal, sin ningún espacio de más, para que el
  paquete ocupe lo mínimo (Luciano, 20-oct).**
- **Dos piezas pueden compartir nivel si en planta no se cruzan ni tocan una
  misma barra longitudinal (Luciano).** Por ejemplo, dos grapas de filas
  distintas comparten nivel. Una grapa de fila y una de columna no. Una caja
  de columnas no puede compartir nivel con una grapa de fila que la
  atraviesa. Ningún estribo menor comparte nivel con el principal: todos
  tocan sus barras de esquina o lo cruzan.
- Se usa la menor cantidad de niveles posible. Los niveles se alternan:
  el primero debajo del principal, el segundo encima, el tercero debajo,
  etc. **(decisión mía)**. Con una cantidad par de niveles de menores, el
  principal queda en el centro. Con una impar, sobra uno abajo.
- Las cajas van en los niveles más cercanos al principal y las grapas, más
  lejos **(decisión mía, sin un motivo de obra detrás: si la empresa tiene
  una costumbre, manda esa)**. Dentro de eso, el orden es fijo (por eje y
  por índice), para que el resultado sea siempre el mismo.
- Todos los paquetes de un mismo tramo son iguales.

### Reparto a lo largo del tramo (Ing. Carlos Quintero)

- La separación (confinada y no confinada) se mide entre el **punto medio**
  de un paquete y el del siguiente: el punto a mitad de camino entre la cara
  de abajo de la pieza más baja y la cara de arriba de la más alta. Si el
  principal queda en el centro, es lo mismo que medir de principal a
  principal, como hoy.
- Los 5 cm de los extremos (`SeparacionExtremosMetros`), que hoy se miden
  desde la cara del tramo hasta el borde del estribo, pasan a medirse hasta
  el **borde del paquete** **(decisión mía)**. Así ningún paquete se mete
  en el nudo, y el largo donde se reparten los estribos se achica en cada
  punta en lo que el paquete sobresale del principal.
- **Si los paquetes se pisan** (separación menor que el alto del paquete),
  ese tramo no lleva estribos menores. Se avisa con la marca de la columna,
  el tramo, el alto del paquete y la separación. Los estribos principales
  se generan igual **(decisión mía)**.
- Un tramo sin estribos menores mantiene el reparto actual.

Consecuencia para el código: hoy `GeneradorArmado` reparte los estribos sin
saber nada de los menores, y `GeneradorLongitudinal` usa después esas
mismas alturas (`ResultadoArmado.PosicionesEstribosPorTramo`). Con los
paquetes, el reparto del principal necesita conocer de antemano el paquete
de la columna (sus niveles y diámetros). El cálculo del paquete va en
`Modelo/` sin Revit, para que el panel y el generador usen el mismo.

## 3. Formas y ganchos propios (#64, revisado)

Sigue valiendo `2026-10-09-diseno-formas-ganchos-propios-64.md`, con estos
cambios:

### Una forma más: la grapa en S

| Nombre | Qué es | Definición |
|---|---|---|
| `CQ Estribo 135°` | gancho de estribos y cajas | sin cambios |
| `CQ Grapa 180°` | gancho de grapas en C y en S | sin cambios |
| `CQ Estribo` | estribo principal y cajas | sin cambios |
| `CQ Grapa` | grapa en C | sin cambios: 1 segmento, 180° en las dos puntas, ganchos hacia el mismo lado |
| `CQ Grapa S` | grapa en S, **nueva** | igual que `CQ Grapa`, pero con los ganchos hacia lados opuestos **(Luciano: "la misma C con un gancho para cada lado")** |

Hay que probar con un comando de diagnóstico, antes de programar la
generación (regla del `CLAUDE.md`):

- si la orientación de cada gancho se fija en la forma (`RebarShape`) o al
  crear la barra, y cómo se crea la S con la API pública;
- que "Voltear" se pueda resolver solo con la orientación de los ganchos,
  sin cambiar la geometría;
- que la medición de la grapa de prueba (`MedirCorreccionDeGrapa`) siga
  sirviendo para la S.

### Barras propias "#N CQEING"

El #64 descartaba las barras propias. Desde el 19-oct existen ("#2 CQEING" a
"#8 CQEING", `2026-10-19-varillas-propias-cqeing.md`), creadas con el doblado
del cuadro: 4 d en estribos y 6 d en ganchos. Cambios:

- El filtro "barra apta para estribo" del #64 se mantiene igual. Las CQEING
  quedan aptas de la #2 a la #5. De la #6 a la #8 quedan no aptas, porque
  el cuadro no les da estribo de 135° (pendiente con el equipo, como antes).
  Las barras del modelo siguen evaluándose como decía el #64.
- El filtro se aplica también al combo "# de varilla" del panel de estribos
  menores.
- La #2 deja de quedar siempre afuera: la #2 CQEING es apta.

### Generación de cajas y grapas

Lo que dice el #64 (formas `CQ Estribo` y `CQ Grapa`, gancho del cuadro, se
eliminan `GanchoAMedidaParaMenores` y `GanchoQueEntra`) más:

- la grapa en S con `CQ Grapa S`;
- cada pieza con su barra ("Igual al estribo" = la del tramo);
- cada pieza a la altura de su nivel en el paquete, no a la del principal;
- la medición de la grapa de prueba, una vez por tramo, forma y barra (hoy
  es una por tramo).

## Cómo se reparte el código

- `Modelo/EstriboMenor.cs` (nuevo, sin Revit): una pieza con forma (C, S,
  cerrado), eje (fila o columna), desde y hasta (iguales en una grapa),
  volteada (sí o no) y nombre de barra (null = igual al estribo).
- `Modelo/ConfigLongitudinal.cs`: lista `EstribosMenores`. `GrapasFila`,
  `GrapasColumna` y `Cajas` quedan solo para leer configuraciones viejas,
  con su migración (mismo patrón que `MigrarBarrasPorCara`). Se copian en
  `Copiar()`.
- `Modelo/PaqueteEstribos.cs` (nuevo, sin Revit): quién choca con quién, la
  asignación de niveles, el alto del paquete y la altura de cada pieza
  respecto del punto medio. Lo usan el panel (para la nota de "paquetes que
  se pisan", si se quiere mostrar) y los dos generadores.
- `Vistas/VentanaEstribosMenores.xaml(.cs)`: se rehace entero.
- `Logica/GeneradorArmado.cs`: el reparto usa el alto del paquete en los
  extremos y registra el punto medio de cada paquete.
- `Logica/GeneradorLongitudinal.cs` (`GenerarMenores`): genera las piezas
  de la lista, cada una en su nivel y con su barra y su forma.
- `Logica/TiposPropios.cs` (nuevo, ver #64): con `CQ Grapa S` agregada.
- Lo demás del #64 (`CuadroVarillas`, `TipoBarra`, filtro en la ventana)
  como allá.
- Comando de diagnóstico temporal para la grapa en S, que se borra al
  cerrar.

## Cómo se comprueba

Sin Revit (`tools/PruebasTraslapos` o un proyecto de pruebas hermano):

- migración de grapas y cajas viejas a piezas;
- niveles: dos grapas de filas distintas comparten nivel; una de fila y una
  de columna no; una caja y una grapa que la atraviesa no; la cantidad de
  niveles es la mínima;
- alternancia abajo/arriba y principal en el centro con niveles pares;
- alto del paquete y extremos de 5 cm contra el borde del paquete;
- paquetes que se pisan → sin menores en ese tramo.

`tools/PreviewUI`: el panel abre con el proveedor falso, se pueden poner,
voltear, seleccionar y eliminar piezas, y la vista previa aparece solo donde
se puede soltar.

En Revit 2027:

1. Diagnóstico de la grapa en S (sección 3), antes de programar su generación.
2. Lo que pedía el #64 (colas, dobleces, sin duplicar tipos en una segunda
   corrida, archivo de parámetros compartidos intacto).
3. Columna con una C, una S y una caja: cada una en su nivel, tocando a la
   de al lado, con el principal en el centro.
4. Los 5 cm de los extremos medidos al borde del paquete; ningún paquete en
   un nudo.
5. Una pieza con barra propia (#2 CQEING) y otra con "Igual al estribo".
6. Una configuración guardada vieja se carga con sus grapas y cajas.

## Pendiente con el equipo

- Si se usan las #6 a #8 como estribo, caja o grapa (viene del #64).
- Si hay una costumbre para el orden de los niveles (cajas cerca del
  principal y grapas afuera es decisión mía).
- En qué esquina van los ganchos de la caja (viene del #64).
