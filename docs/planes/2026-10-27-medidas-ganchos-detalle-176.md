# Medidas de los ganchos en el detalle de plegado (#176)

Fecha: 27-oct-2026. Estado: **resuelto y probado por Luciano en Revit 2027 el 28-oct.**

## Contexto

En la reunión del 24-oct, el detalle de plegado de un estribo de columna
(0.25 × 0.25) mostraba la medida de los cuatro lados pero no la de los
ganchos. En el de vigas (tipo «02_Detalle_de_plegadO_LONG_con_etiqueta_VIGAS»)
sí aparecen: 0.08 en cada gancho.

## Qué se encontró

El add-in no crea los detalles de plegado: los pone el usuario a mano en la
vista. Lo que decide si se acotan los ganchos es una casilla del **tipo de
detalle de plegado**, no del estribo. En la API pública de Revit 2027
(`RevitAPI.xml`):

- `RebarBendingDetailType.SegmentLengthDimensionsForHooksEnabled` (desde 2024):
  "si se muestran las cotas de longitud de segmento de los ganchos". Para que
  se vean, también tiene que estar activa
  `RebarBendingDetailType.SegmentLengthDimensionsEnabled`.

Hipótesis: el tipo usado en la foto de columnas tiene esa casilla apagada y el
de vigas la tiene prendida. Los ganchos de columnas y de vigas se hacen igual
(`RebarHookType` en las dos puntas), así que no se espera diferencia por el lado
del estribo.

## Prueba en Revit (sin cambiar código)

1. Abrir la vista donde está el detalle de plegado del estribo de columna.
2. Seleccionar el detalle → «Editar tipo».
3. Activar la casilla de cotas de longitud de los ganchos (en inglés
   «Segment Length Dimensions for Hooks»), con las cotas de segmento activas.
4. Alternativa: cambiarle el tipo al de vigas
   («02_Detalle_de_plegadO_LONG_con_etiqueta_VIGAS») y ver si salen los 0.08.

## Comparación con vigas

El código de `acero-vigas` no toca los detalles de plegado (no hay ninguna
referencia a `RebarBendingDetail*`), así que si el de vigas acota los ganchos
es por la configuración de su tipo en la plantilla, no por el add-in.

Para comparar en el modelo real se agregó el comando temporal
`ComandoDiagnosticoDetallePlegado` (botón «Diagnóstico detalle plegado», solo
Debug). Solo lee: lista cada tipo de detalle de plegado con sus casillas
(cotas de segmento, cotas de ganchos, cotas como dimensión o texto, etiqueta),
cuál es el tipo por defecto, y cada detalle ya puesto con su tipo y la barra
que representa (forma, ganchos, si tiene el largo de gancho modificado).

## Fix implementado

Luciano decidió (27-oct) dar por buena la causa e implementarlo sin esperar el
diagnóstico. `Logica/DetallesPlegado.AcotarGanchos`, llamado desde
`GeneradorArmado` al generar (dentro de la misma transacción): en cada tipo
de detalle de plegado del modelo que ya acota los lados
(`SegmentLengthDimensionsEnabled`), enciende
`SegmentLengthDimensionsForHooksEnabled` (propiedad pública desde 2024). Un tipo
que no acota nada se deja como está. Si un tipo no se puede cambiar, sale un
aviso y la generación sigue.

Así cualquier detalle que pongan sobre un estribo de columna sale con los
ganchos acotados, sin configurar nada a mano. Ojo: el cambio es sobre los
tipos del modelo, así que también alcanza a los detalles de vigas (que ya lo
tenían encendido).

## Retest 28-oct: no alcanzó

Luciano generó una columna (#4) y puso el detalle de plegado: salen los cuatro
lados, los ganchos siguen sin medida. El diagnóstico muestra que los 10 tipos
de detalle del modelo tienen `SegmentLengthDimensionsEnabled = True` y
`SegmentLengthDimensionsForHooksEnabled = True` (cotas como texto), también
los de vigas. **La casilla no es la causa**: la diferencia tiene que estar en
el estribo (forma propia «CQ Estribo» y gancho «CQ Estribo 135°» en columnas
vs. forma de la plantilla en vigas). La búsqueda de detalles puestos dio 0
(no los encontró por tipo), así que se amplió el diagnóstico: ahora pide
elegir a mano estribos y/o detalles y lista forma, ganchos (ángulo, estilo,
largo), orientación y los parámetros de gancho de cada barra.

`DetallesPlegado.AcotarGanchos` queda (no estorba), pero no resolvió el caso.

## Comparación estribo de columnas vs. de vigas (28-oct)

| | Columnas (814594) | Vigas (815560) |
|---|---|---|
| Forma | «CQ Estribo» (propia, creada por API), 4 segmentos | «T1» (de la plantilla), 4 segmentos |
| Tipo de barra | «#3 CQEING» | «#3» |
| Ganchos | «CQ Estribo 135°», 135°, StirrupTie, 8,45 cm | «CQEING Estribo 135° #3», 135°, StirrupTie, 8,45 cm |
| Orientación | Left / Left | Left / Left |
| Largo de gancho modificado | No | No |

`RebarShapeDefinesHooks = False` en el modelo. Ganchos idénticos en ángulo,
estilo, largo y orientación: lo que cambia es la **forma** (y el nombre del
tipo de barra, que no debería importar). Sospechoso principal: la forma
propia «CQ Estribo».

### Parámetros de las dos barras (leídos con el conector MCP de Revit, 28-oct)

Luciano prefiere no cambiar de forma (por los problemas que dieron otras
formas): hay que ver qué tiene «T1» que le falta a «CQ Estribo». Comparando
los parámetros de las dos barras: «T1» trae A = 0.08, B = 0.12, C = 0.27,
D = 0.12, E = 0.27, G = 0.08, H = 0.06 (y F, J, K, O, R en 0). **A y G valen
0.08, lo mismo que el detalle de vigas acota en los ganchos.** «CQ Estribo»
solo tiene CQ A-D (los cuatro lados), así que el detalle no tiene qué acotar
en los ganchos.

La API pública deja agregar a la forma parámetros atados al gancho:
`RebarShapeDefinition.AddFormulaParameter(id, fórmula)`, con fórmulas que
pueden usar `REBAR_SHAPE_START_HOOK_LENGTH` / `REBAR_SHAPE_END_HOOK_LENGTH`.
`GetParameterFormula` lee la fórmula de una forma existente. Para copiar la
fórmula exacta de «T1» se amplió el diagnóstico: lista cada parámetro de la
forma con su valor por defecto y su fórmula.

(Aparte: el tipo «#3 CQEING» tiene peso por metro 0, así que la masa de los
estribos de columna sale 0; «#3» tiene 0.56 kg/m. Afecta cantidades.)

Prueba propuesta, sin código: cambiarle a mano la forma al estribo de columna
a «T1» (Propiedades → Forma) y ver si el detalle de plegado acota los
ganchos.

## Pendiente de retest

- Generar una columna, poner (o mirar) el detalle de plegado de un estribo y
  comprobar que salen las medidas de los dos ganchos.
- Si no aparecen: comparar el estribo de vigas con el de columnas (forma
  «CQ Estribo» propia vs. la forma del documento que usa vigas) con un comando
  de diagnóstico.

## Peso por metro de las varillas propias (28-oct)

Encontrado de paso al comparar las barras: «#3 CQEING» tenía 0 kg/m
(`RebarBarType.Create` la entrega así) y la masa de los estribos de columna
salía 0. Fix: `VarillasPropias.CompletarPeso` pone el `KgPorMetro` del cuadro
en cada «#N CQEING». Se llama al crear las varillas y al generar el armado
(así se corrigen las que ya estaban en los modelos).

- Todas las versiones: cualquier parámetro editable del tipo con dato
  `SpecTypeId.MassPerUnitLength`. En las plantillas del equipo es «kg/m»
  (vigas: «#3» con kg/m = 0.56; «#3 CQEING» tenía 0). Es lo que leen las
  tablas y `cuadro-cantidades` (`MassPerLengthReader`, mismo criterio), y el
  único peso que hay en Revit 2026, cuya API no tiene peso propio de la barra.
- Solo 2027: además `RebarBarType.BarMassPerUnitLength` (pública desde 2027).

Pendiente de retest: generar y ver que «kg/m» (y en 2027 «Bar Mass per Unit
Length») de «#3 CQEING» dé 0,56.

## Causa encontrada y fix (28-oct)

Diagnóstico ampliado sobre la «T1» de vigas (barra 814613): A tiene la fórmula
«Start Hook Length», G «End Hook Length» y H «Start Hook Offset Length». El
detalle de plegado acota los ganchos con esos parámetros atados al gancho.
«CQ Estribo» (barra 814644) solo tiene CQ A-D, sin fórmulas.

Fix en `Logica/TiposPropios.cs`, sin cambiar la geometría de las formas:

- Dos parámetros compartidos nuevos, «CQ Gancho inicio» y «CQ Gancho fin»
  (mismo mecanismo que CQ A-D), agregados con
  `RebarShapeDefinition.AddFormulaParameter` a todas las formas propias
  (estribo y grapas). La fórmula es el nombre de `REBAR_SHAPE_START/END_HOOK_LENGTH`
  en el idioma de Revit en uso (`LabelUtils.GetLabelForBuiltInParameter`), para
  que funcione también con Revit en español.
- Una forma ya creada no se puede cambiar (la API no tiene setter de la
  definición). Si el modelo tiene la forma propia sin esas cotas, se le cambia
  el nombre a «… (anterior)» y se crea la nueva con el nombre de siempre. Las
  barras viejas la conservan hasta que se regeneren.
- `DetallesPlegado.AcotarGanchos` queda: asegura la casilla del tipo de
  detalle, que también hace falta.

Pendiente de retest: regenerar una columna, poner el detalle de plegado del
estribo y ver la medida de los dos ganchos; revisar que el estribo y las
grapas salgan igual que antes (misma forma y largo).

## Retest 28-oct: los ganchos del estribo ya se acotan

Luciano regeneró una columna: el detalle de plegado del estribo muestra las
medidas de los ganchos (0.08 en la #3 = largo de gancho de Revit, 8,45 cm, el
mismo que vigas). Quedó así por ahora.

## Texto exagerado: tipo de detalle esquemático propio (28-oct)

Los textos no son un problema de fuente: todos los tipos usan «Flecha - Arial
2.5 mm», que es tamaño de papel, mientras que el modo realista dibuja la barra
a tamaño real. En la planta «N+7.00 Cubierta» a 1:100, 2,5 mm de texto = 25 cm,
más que el estribo de 22 cm (la foto de vigas estaba a 1:5).

`DetallesPlegado.AsegurarTipoPropio`: crea, si no existe, el tipo
**esquemático** «CQ Detalle de plegado» (`RebarBendingDetailType.CreateSchematic`):
dibuja la barra en un recuadro de tamaño fijo en el papel, así el texto queda
proporcionado en cualquier escala. Cotas de segmento y de ganchos como texto,
la etiqueta de algún tipo del modelo, y queda como predeterminado. Todo eso
**solo al crearlo**: si el equipo lo edita o elige otro predeterminado, se
respeta. `AcotarGanchos` tampoco lo toca.

## Longitudinales: cota del gancho (28-oct)

Con el conector MCP: las longitudinales (p. ej. 814617, #4, «CQ Gancho 90°»
abajo) usan la «00» de la plantilla (la elige Revit en `CreateFromCurves`), que
tiene A…R pero con A = 0 aunque haya gancho: no ata nada al gancho.

Fix (coordinado con la sesión del #177, que no estaba en `BarraContinua.cs`):

- `TiposPropios.FormaRecta(grados inicio, grados fin)`: «CQ Recta 90-0»,
  «CQ Recta 0-90», «CQ Recta 90-90»… (una por combinación, como las grapas),
  un tramo «CQ A» + la cota «CQ Gancho inicio/fin» solo en las puntas con gancho.
  Sin ganchos no hay forma: la barra va como siempre.
- `BarraContinua.CrearEntera`: con gancho, `Rebar.CreateFromCurvesAndShape`
  con esa forma, misma línea y mismos ganchos; si no hay forma o Revit no la
  acepta, la llamada de siempre. `Pedido` lleva `Propios` (lo pasa
  `GeneradorLongitudinal`).

Pendiente de retest (pedido por la sesión del #177): que las piezas después de
partir en los traslapos conserven la forma; que en
`%TEMP%\<guid>\practicacq-longitudinal.log` las líneas «final:» sigan diciendo
«pata exterior del gancho 0,222» (#4) y REBAR_ELEM_LENGTH siga igual a la
pestaña (3,5003 / 6,0003 / 7,0003 / 2,5003 en la columna de prueba); y que el
detalle de plegado de una longitudinal muestre la medida del gancho.

### Corrección 28-oct: la forma se pone después de partir

Retest: el tipo «CQ Detalle de plegado» se creó y quedó como predeterminado
esquemático (id 814889). Pero la pieza longitudinal 815298 seguía con «00»:
crear la varilla con `CreateFromCurvesAndShape` no sirve, porque al partir en
los traslapos (`RebarSpliceUtils.SpliceRebar`) Revit les vuelve a elegir la
forma a las piezas. Se revirtió ese cambio en `BarraContinua.cs` /
`GeneradorLongitudinal.cs` (`Pedido.Propios` ya no existe).

Ahora: `DetallesPlegado.PonerFormaRecta`, llamada una sola vez en
`GeneradorLongitudinal` justo después de `BarraContinua.Acomodar` (que solo
mueve las piezas, no las recrea), le pone a cada pieza con gancho la «CQ Recta
a-b» de sus ganchos con `RebarShapeDrivenAccessor.SetRebarShapeId` (pública
desde 2018: "elige los parámetros para conservar la forma lo más parecida
posible"). Las líneas «final:» del log del #177 se escriben antes de este paso:
el largo después del cambio se verifica con REBAR_ELEM_LENGTH.

## Retest final (28-oct): resuelto

Luciano regeneró: el detalle de plegado del estribo y el de una longitudinal
muestran las medidas de los ganchos (estribo #3: 0.22 los lados y 0.08 los
ganchos; longitudinal #4: 0.22 el gancho y 2.31 el tramo). Verificado con el
conector MCP: 16 piezas #4 con «CQ Recta 90-0» / «CQ Recta 0-90», ganchos «CQ
Gancho 90°» conservados, largos 3.50 / 7.00 / 6.00 / 2.50 sin cambios. El
estribo mide 0.22 por fuera = columna 0.30 − 2 × recubrimiento 0.04 del modelo.

Nota para el equipo: sumar las cotas del dibujo da ~3 cm más que el largo de
corte en una #4 con un gancho (2.31 + 0.22 = 2.53 contra 2.50), porque cada
tramo se acota hasta la esquina de afuera del doblez y la barra sigue la
curva. Es la convención de los planos; el largo de corte es el correcto.

## Pendiente (fuera de este cambio)

- Mostrar el largo total en la etiqueta del detalle: la API no puede crear
  textos (labels) en una familia de etiqueta. Opciones: usar un tipo de
  «M_Etiqueta de armadura» que ya tenga el largo, o hacer a mano una vez una
  etiqueta «CQ … - largo» y que el add-in la cargue. Luciano lo dejó para más
  adelante (28-oct).
- Sacar el comando temporal `ComandoDiagnosticoDetallePlegado` (solo Debug)
  cuando ya no haga falta.
- Retest en Revit 2026: compila, pero no se probó (ahí el peso solo se llena
  en «kg/m»).
