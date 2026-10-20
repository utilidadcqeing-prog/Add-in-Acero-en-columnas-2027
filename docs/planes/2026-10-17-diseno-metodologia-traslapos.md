# Diseño: nueva metodología para ubicar los traslapos

Aprobado en conversación por Luciano el 17-oct-2026. Reemplaza las reglas
de "dónde va un traslapo" que el diseño del #41
(`docs/planes/2026-10-07-diseno-barra-continua-41-42.md`) había dejado
fuera de alcance.

## Contexto: qué pasa hoy

- `GeneradorLongitudinal.PlanificarAlturas` pone cada traslapo con su
  **centro** en la mitad de un tramo libre, redondeada hacia arriba a 10 cm.
  Revit lo extiende medio solape hacia arriba y medio hacia abajo
  (`RebarSplicePosition.Middle`), así que en un tramo corto el traslapo se
  mete en el nudo (dentro del canto de la viga).
- El solape es fijo: el del cuadro de la empresa (`CuadroVarillas.Traslapo`).
- Los largos de las varillas no se redondean.
- La ventana no muestra nada del cálculo: solo se elige 6 o 12 m y el tipo
  de empalme.

## Qué se quiere lograr

1. Ningún traslapo toca un nudo.
2. Los traslapos quedan lo más centrados posible en su tramo, sin que sea
   obligatorio.
3. El solape del cuadro es un **mínimo**: el traslapo puede ser más largo.
4. Cada varilla mide un múltiplo exacto de un paso de redondeo elegido por
   el usuario.
5. El cálculo se hace mientras se configura, y la ventana dibuja los tipos A
   y B con el largo de cada varilla y de cada traslapo. Cualquier cambio
   recalcula y redibuja.
6. El diámetro de las barras longitudinales se elige por tipo (A y B), no
   por cara (X, Y, esquinas).

Fuera de alcance: estribos, grapas y cajas; las barras fuera del cuadro de
la empresa (sigue siendo el #46); el corrimiento lateral de los traslapos
(lo sigue poniendo Revit como hoy).

## Vocabulario

- **Tipo A / tipo B**: los dos grupos de varillas que hoy se llaman
  "par/impar". Se reparten "uno sí, uno no" dando la vuelta al anillo del
  perímetro (regla del 11-sep, sin cambios): posición 0 del anillo = A,
  posición 1 = B, etc.
- **Varilla** (o pieza): un trozo que se corta de una barra comercial. Una
  posición longitudinal de la columna está hecha de una o varias varillas
  unidas por traslapos.
- **Largo de corte**: el largo de la varilla estirada, ganchos incluidos.
- **Nudo**: un tramo `TipoTramo.DentroDeViga`.
- **Tramo libre**: un tramo `Libre` o `ContraZapata`.

## Comportamiento

### 1. Datos de entrada del cálculo (por columna y por tipo)

- Los tramos de la columna, con su Z inferior, Z superior y tipo
  (`ColumnaContexto.Tramos`).
- Z inicial de la barra: `ColumnaContexto.ZInferiorLongitudinal` (anclaje
  en la zapata, o la base si no hay zapata). Z final:
  `ColumnaContexto.ZSuperior`. Los mismos valores que usa hoy el generador.
- Largo de los ganchos para el largo de corte: el gancho de 90° del cuadro
  (`CuadroVarillas.Gancho90`) del diámetro del tipo, en las dos puntas si
  la columna arranca sobre zapata y en ninguna si no. Es lo que hace hoy el
  generador: el gancho de anclaje y el de remate son el mismo y solo se
  ponen cuando hay zapata.

  Para que el largo que informa Revit sea el mismo, la longitudinal usa un
  gancho propio, «CQ Gancho 90°», ajustado en cada corrida para que cada
  punta sume exactamente el `Gancho90` del cuadro. El «Estándar - 90°» del
  modelo sumaba 3,4 cm menos en una #4 y lo usan otras barras, así que no se
  toca (decisión de Luciano, 17-oct; ver
  `docs/planes/2026-10-17-diagnostico-traslapos-variables.md`).
- Largo comercial (6 o 12 m), paso de redondeo y solape mínimo (sección 3).

### 2. Reglas obligatorias

Un plan de un tipo es válido solo si cumple todas:

1. **Fuera de los nudos.** Cada traslapo queda entero dentro de un tramo
   libre: `[Z inferior del traslapo, Z superior del traslapo]` contenido en
   `[ZInferior, ZSuperior]` del tramo. Puede tocar la zona de estribos
   confinados (decisión de Luciano, 17-oct).
2. **Alternancia.** El tipo A traslapa solo en los tramos libres de ordinal
   par, y el B solo en los impares. Es el mismo ordinal que hoy: se cuentan
   los tramos libres de toda la columna de abajo hacia arriba, empezando en
   1. Nunca se ponen A y B en el mismo tramo, **salvo** que un tipo no
   tenga ningún tramo propio (por ejemplo, el A en una columna de un solo
   piso, que solo tiene el tramo 1). Solo en ese caso ese tipo traslapa en
   los tramos del otro, a otra altura y sin pisar los traslapos del otro:
   se calcula primero el tipo que sí tiene tramos y después el otro, en lo
   que queda libre (decisión de Luciano, 17-oct).
3. **Redondeo.** El largo de corte de cada varilla es un múltiplo exacto
   del paso de redondeo. Vale también para la de abajo (con su gancho de
   anclaje) y la de arriba (con su gancho de remate).
4. **Largo comercial.** Ninguna varilla supera el largo comercial elegido.
   Puede ser tan corta como haga falta: no tiene que aprovechar la barra
   entera. El redondeo de la regla 3 se respeta siempre, incluso en el
   peor caso: es para que en obra sea fácil de cortar (confirmado por
   Luciano, 17-oct).
5. **Solape mínimo.** Cada traslapo mide como mínimo el solape elegido
   (sección 3). Lo que sobra porque las varillas son redondas lo absorbe el
   traslapo. Los traslapos se ubican en pasos de 1 cm; el de más arriba
   absorbe además los milímetros sueltos de la altura real de la columna.
   Nunca baja del mínimo; solo puede agrandarse (confirmado por Luciano,
   17-oct).

Una columna va en una sola varilla, sin traslapos, solo si su largo de
corte es **exactamente** múltiplo del redondeo y no supera el comercial. Si
entra en una barra pero su largo no es múltiplo, se fuerza un traslapo para
que el redondeo se cumpla (decisión de Luciano, 17-oct).

### 3. Solape mínimo de cada tipo

Opción nueva en la ventana, "Solape":

- **"El mayor de los dos tipos"**: A y B usan el solape más grande del
  cuadro entre la barra A y la barra B.
- **"El de cada diámetro"**: A usa el de su barra y B el de la suya.

Por defecto: "El de cada diámetro".

### 4. Preferencias (entre planes válidos)

Se comparan en este orden y gana el primero que desempata:

1. Menos traslapos.
2. Menos exceso de solape: la menor suma de (largo del traslapo − solape
   mínimo). El traslapo se alarga solo lo que obliga el redondeo.
3. Traslapos más centrados: la menor suma de distancias entre el centro de
   cada traslapo y el centro de su tramo.

Orden cambiado por Luciano el 17-oct, después de ver un ejemplo: tramo de 0
a 2,60 m, barra #4, redondeo 0,50. Con "centrado" antes que "exceso" salía
un traslapo de 2,40 m (de 0,10 a 2,50); con este orden sale uno de 0,90 m
(de 0,60 a 1,50).

Si aun así empatan, gana el que tiene los traslapos más abajo, para que el
resultado sea siempre el mismo.

### 5. Búsqueda exhaustiva antes de declarar "sin solución"

Pedido de Luciano (17-oct): antes de concluir que no se puede, hay que
probar todo lo posible. El cálculo recorre **todas** las combinaciones que
cumplen las reglas de la sección 2:

- cualquier cantidad de traslapos (no se limita a uno por tramo libre
  disponible ni se corta en la primera solución "voraz");
- cualquier largo de varilla en el redondeo elegido, desde el más corto
  hasta el largo comercial, no solo el más largo que entra;
- cualquier ubicación de cada traslapo dentro de su tramo, incluido
  pegado a una punta del tramo (lejos del centro) y con el solape estirado,
  si eso es lo único que funciona.

El espacio es chico (pocos tramos, largos discretos en pasos del
redondeo), así que se puede recorrer entero con programación dinámica sobre
la altura donde termina cada varilla; no hace falta heurística. El método
exacto queda para el plan de implementación, con la condición de que sea
exhaustivo: si existe un plan válido, lo encuentra.

### 6. Sin solución

Solo si la búsqueda de la sección 5 no encuentra ningún plan válido:

- la pestaña "Traslapos" marca ese tipo en rojo con el motivo en lenguaje
  llano (por ejemplo, "ningún tramo libre par entre 3,20 y 9,20 m tiene
  lugar para un traslapo de 0,75 m sin tocar un nudo");
- **si el largo comercial elegido es 6 m**, el cálculo se repite con 12 m
  y, si con 12 m hay solución, el mensaje lo sugiere: "Con barras de 12 m
  sí se puede". No se cambia solo: la elección sigue siendo del usuario;
- **esa columna no genera su acero longitudinal**, ni el tipo A ni el B. Al
  generar se avisa con la marca de la columna y el mismo motivo (y la
  sugerencia de 12 m, si corresponde). Tampoco se generan sus grapas ni
  sus cajas, que abrazan las barras longitudinales y sin ellas quedarían
  sueltas. Los estribos principales de esa columna sí se generan.
- Si un tipo sin tramos propios depende del otro (regla 2) y el otro no
  tiene solución, los dos quedan sin solución, con el mismo motivo.

No se rompe la alternancia ni se genera "lo más parecido" (decisión de
Luciano, 17-oct).

### 7. Configuración (`ConfigLongitudinal`)

- **Nuevos**: `TipoBarraANombre`, `TipoBarraBNombre`, `RedondeoVarillas`
  (metros, por defecto 0,50), `SolapeMayorDeLosDos` (bool, por defecto
  false). Se copian en `Copiar()`.
- **Se dejan de usar**: `TipoBarraEsquinasNombre`, `TipoBarraCaraXNombre`,
  `TipoBarraCaraYNombre` y `TipoEmpalmeNombre`. Se conservan como
  propiedades solo para leer configuraciones guardadas viejas.
- **Configuraciones guardadas viejas** (sin A/B): A = la barra de
  esquinas, B = la de cara X (o la de cara Y si no había de cara X).
  Redondeo 0,50 y solape "de cada diámetro". Se avisa una vez al cargarla.
- `BarrasPorCaraX` y `BarrasPorCaraY` no cambian.

### 8. Ventana (`VentanaConfiguradorEstribos`)

Panel "Acero longitudinal":

- "Tipo barra A" y "Tipo barra B" reemplazan a "cara X", "cara Y" y
  "esquinas". Mismas listas y mismo valor por defecto que hoy (elección por
  diámetro, #60).
- "Redondeo de varillas": 0,10 / 0,20 / 0,25 / 0,50 / 1,00 m; por defecto
  0,50.
- "Solape": las dos opciones de la sección 3.
- Se quita el combo "Tipo de empalme": un tipo de empalme del modelo tiene
  un largo fijo y no puede crecer, así que choca con las reglas 3 y 5.

Panel de elevación: dos pestañas.

- **"Tramos"**: la elevación de hoy, sin cambios.
- **"Traslapos"**: la columna a escala, con los nudos y la zapata pintados
  igual que en "Tramos", y al lado dos barras dibujadas, A y B. Cada
  varilla tiene su largo de corte rotulado, y cada traslapo, su largo y su
  cota. Un tipo sin solución se dibuja en rojo con el motivo (y la
  sugerencia de 12 m) debajo.

Se recalcula y se redibuja al cambiar cualquiera de estos: columna activa,
barra A, barra B, largo comercial, redondeo, solape. (El recubrimiento no
cambia las alturas, así que no participa.)

El dibujo usa la misma lectura de la columna que ya tiene la ventana
(`ColumnaContexto`); no necesita Revit, así que funciona igual en
`tools/PreviewUI`.

### 9. Generación

- `GeneradorLongitudinal` pide el plan a `PlanTraslapos` (el mismo cálculo
  que usa la ventana) en lugar de calcular por su cuenta. Lo que se ve en la
  pestaña es lo que se genera.
- A cada posición del anillo le toca la barra de su tipo.
- `BarraContinua` recibe, por traslapo, su altura central y su largo, en vez
  de solo la altura.
- `TiposEmpalme` crea o reutiliza un tipo de empalme por diámetro y largo,
  con nombre `CQ Traslapo #N · L m` (por ejemplo `CQ Traslapo #4 · 0,85 m`),
  multiplicador = largo / diámetro nominal y corrimiento `BarPlane`, igual
  que hoy. Se vuelven a fijar en cada corrida.

### 10. Verificación previa en Revit (antes de programar el punto 9)

Regla del `CLAUDE.md`: no asumir cómo se comporta la API. Hoy una varilla
se parte en **una sola** llamada a `RebarSpliceUtils.SpliceRebar` con **un
solo** tipo de empalme. Con largos distintos hacen falta varios tipos, y
eso no está medido. Un comando de diagnóstico temporal tiene que medir, en
el modelo de referencia (dado 781598 + columnas 782104 y 782735):

1. Una varilla con dos traslapos de **largos distintos**: si se puede
   partir con varias llamadas a `SpliceRebar` (una por tipo, sobre la pieza
   que corresponda) o con alguna otra forma documentada.
2. Que cada pieza resultante mida lo calculado, y que el traslapo quede
   centrado donde se pidió.
3. Que el largo de corte que informa Revit (parámetro de largo de barra)
   coincida con el calculado con el gancho del cuadro. El diseño del #64
   midió que Revit calcula el largo del gancho distinto que el cuadro; si
   no coincide, se decide ahí si el cálculo usa el valor de Revit.
4. Que el corrimiento lateral y la reasignación de hosts sigan funcionando
   con piezas partidas en varias llamadas.

Si el punto 1 sale mal, se vuelve a esta conversación antes de seguir: la
alternativa (varillas sueltas sin empalme nativo) cambia el diseño.

## Cómo se reparte el código

- **`Modelo/PlanTraslapos.cs` (nuevo, sin Revit)**: el cálculo de las
  secciones 1 a 6. Entrada: tramos, Z inicial y final, si hay zapata,
  diámetro y solape mínimo del tipo, largo comercial, redondeo, paridad (A
  o B). Salida: lista de varillas (Z desde, Z hasta, largo de corte) y de
  traslapos (Z centro, largo), o el motivo de por qué no hay solución (con
  la sugerencia de 12 m si aplica).
- **`Modelo/ConfigLongitudinal.cs`**: sección 7.
- **`Vistas/VentanaConfiguradorEstribos.xaml(.cs)`**: sección 8. El dibujo
  de la pestaña va en un control propio (`Vistas/PanelTraslapos`), igual que
  `PanelPlantaColumnas`, para no engordar más el code-behind.
- **`Logica/GeneradorLongitudinal.cs`**: pierde `PlanificarAlturas`,
  `PlanificarSplicesReales` y `RedondearArriba10Cm`; arma el anillo con
  barras A/B.
- **`Logica/BarraContinua.cs`**: `Pedido.ZTraslapos` pasa a ser una lista
  de (Z centro, largo); el partido sigue lo que salga de la sección 10.
- **`Logica/TiposEmpalme.cs`**: un tipo por diámetro y largo; se elimina la
  rama del tipo elegido del modelo.
- **`Modelo/IProveedorTipos.cs`, `Logica/ProveedorTiposReal.cs`,
  `Modelo/ProveedorTiposFalso.cs`**: se quita `ListarEmpalmes()` si no lo
  usa nadie más.
- **Comando de diagnóstico temporal** para la sección 10, que se borra al
  cerrar.

## Cómo se comprueba

El repo no tiene pruebas automáticas de Revit. `PlanTraslapos` no depende
de Revit, así que se puede probar con casos armados a mano (en
`tools/PreviewUI` o con un proyecto de pruebas, a decidir en el plan):

- columna que entra en una varilla → sin traslapos;
- tramo libre más corto que el solape → no traslapa ahí; si era el único
  posible, sin solución con motivo;
- caso que solo tiene solución con una varilla más corta que la comercial,
  o con un traslapo lejos del centro → la encuentra (búsqueda exhaustiva);
- caso sin solución con 6 m y con solución con 12 m → sugiere 12 m;
- todos los largos de corte son múltiplos del redondeo y ninguno supera el
  comercial;
- ningún traslapo toca un nudo;
- A y B nunca en el mismo tramo;
- "mayor de los dos" iguala los solapes mínimos de A y B.

En Revit 2027, modelo de referencia:

1. La sección 10 completa, antes de programar la generación.
2. La pestaña "Traslapos" coincide con lo generado: largos de varilla (los
   informados por Revit) y alturas de traslapo.
3. Ningún traslapo entra en un nudo.
4. Una columna sin solución no genera longitudinal, y el aviso lo explica.
5. Una configuración guardada vieja se carga con A/B y el aviso.
6. Regenerar una columna ya armada no deja piezas de la corrida anterior.

## Pendiente fuera de este diseño

- Qué pasa con las barras fuera del cuadro de la empresa (sin solape
  mínimo conocido): hoy quedan enteras con aviso; con este diseño ese tipo
  quedaría sin solución si necesita traslapos. Sigue en el #46.
