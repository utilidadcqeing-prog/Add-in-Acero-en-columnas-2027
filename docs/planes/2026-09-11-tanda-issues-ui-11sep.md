# Tanda de interfaz: los 8 issues de la reunión del 11-sep (#56 a #63)

## Contexto

En la reunión del 11-sep William pidió un **cambio de fondo en el flujo** de la
ventana: menos clics, sin paso de "aplicar". El análisis quedó en
`docs/planes/2026-09-11-issues-reunion-11sep.md`, con 8 issues (#56 a #63). Esta
tanda los implementa.

Dos diferencias con la tanda anterior:

- **Revit volvió a abrir** (se destrabó la licencia), así que por primera vez se
  puede verificar contra un modelo real lo que toca los generadores.
- Parte de lo del 10-sep se **revierte a pedido**: el botón "Aplicar a selección"
  con su flechita (#45) y el panel de tramos (#48) desaparecen. No estaban mal
  resueltos; William quiere otra solución para el mismo problema.

El resultado: se elige la columna en la planta, se configura tramo por tramo,
cada tramo aceptado queda con un **chulito verde**, y "Generar armado en Revit"
arma exactamente lo que tiene chulito.

## Lo que se hizo

### [#56](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/56) — fuera el paso de "aplicar"; se genera lo aceptado

El problema de fondo era que en el código "tiene configuración" significaba
**"fue visitado"**: `GuardarPanelEnTramoActivo` escribe en `_configPorTramo` cada
vez que se activa un tramo, justamente para no perder lo tecleado al navegar.
Usar eso como criterio del chulito habría marcado la columna entera con solo
recorrerla con las flechitas.

Por eso ahora son **dos estados separados**:

- `_configPorTramo` — lo que el panel tiene cargado para cada tramo. Se sigue
  escribiendo solo, igual que antes.
- `_tramosAceptados` (nuevo) — lo que lleva chulito y lo que se genera. Solo se
  entra por un gesto explícito: el botón **Aceptar**, o un "aplicar al resto
  de…" que lo incluya.

Desaparecieron: los botones "Aplicar a selección" (con su flechita) y "Limpiar
selección", el panel de tramos del medio, las casillas de tildar, y el evento
`SeleccionMarcadaCambio` con su `ExternalEvent` en `ComandoEstribos`.

Ese último **no se recableó** a lo aceptado a propósito: si lo estuviera, cada
Aceptar le mandaría una selección nueva a Revit y movería lo que el usuario está
mirando. La sincronización con Revit queda solo por activación.

El chulito se dibuja en la elevación (y en la planta, sobre las columnas que
tienen alguno). **Se clickea para sacarlo**: William no quiere una casilla que
haya que tildar, pero tiene que haber forma de deshacer uno puesto por error. La
configuración del tramo no se borra al sacarlo.

Como ya no hay casillas que contar a ojo, el pie dice en números qué se va a
armar: *"Listo para generar: 1 columna(s), 4 tramo(s) con ✓"*.

La ventana se achicó de 1320×780 a **1180×780** (mínimo 1000×660): se fue un
panel entero de 255 px, y el ancho que sobraba se repartió entre la elevación y
el panel de configuración.

### [#57](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/57) — extender al resto del mismo tipo, desde el tramo activo

Tres botones al pie del panel derecho, **fuera del `ScrollViewer`** para que la
acción principal no dependa de hasta dónde scrolleó el usuario:

| Botón | Cuándo aparece |
|---|---|
| ✓ Aceptar este tramo | siempre |
| ✓ Aceptar y aplicar al resto de *los tramos libres / los nudos / los tramos contra zapata* (N) | según el TIPO del tramo activo, y si N > 0 |
| ✓ Aceptar y aplicar a toda la columna (N) | si la columna tiene más de un tramo y no son todos del mismo tipo |

Los conteos son del **resto** (sin contar el activo, que se acepta igual) y
salen del mismo helper que después aplica (`TramosDeLaColumnaActiva`), así que no
pueden discrepar. Con 0 el botón no se muestra: una columna con un solo tramo
libre no ofrece "aplicar al resto" como si hiciera algo.

### [#58](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/58) — los nudos llevan estribos uniformes (cierra el #55)

- **Generador**: `GeneradorArmado.GenerarTramo` salteaba el nudo con un
  `return true` sin avisar. Se sacó: un tramo `DentroDeViga` genera estribos por
  el mismo camino que cualquier otro. Las grapas y cajas siguen yendo solo en los
  tramos libres, eso no cambió.
- **Ventana**: con un nudo activo, "Confinado" queda **deshabilitado** y aparece
  la explicación debajo de los radios (un control gris sin explicación se lee
  como un error de la ventana). Si el tramo venía en confinado de antes, se
  corrige a uniforme al cargarlo.
- Se sacó el error de validación "este tramo ya es un nudo…": deja de tener
  sentido cuando la combinación es imposible de elegir.

### [#59](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/59) — gancho de 90° también arriba, hacia adentro

Dos cosas:

1. **Que exista el gancho arriba.** `CrearBarraVertical` ahora recibe un gancho
   por extremo. Quien llama decide: el de abajo va en el segmento que llega al
   arranque (y solo si la columna apoya en zapata), el de arriba en el que llega
   al remate. De paso se corrigió que el de abajo se ponía en **cualquier**
   segmento alojado en el host de la zapata: si ese host quedaba partido por un
   traslapo, la pieza de arriba también se llevaba un gancho de anclaje.

2. **Que mire hacia adentro** (esto es también el #43). Ya no se usa
   `RebarTerminationOrientation.Left` fijo: `OrientacionesHaciaAdentro` calcula
   hacia dónde dobla el gancho —la pata va a lo largo de `tangente × normal`, o
   sea en ±Y para las barras de normal X y en ∓X para las de normal Y— y elige el
   signo que apunta al centro de la sección del host.

Lo único que **no se puede deducir de la documentación** es la correspondencia
entre ese signo y `Left`/`Right`. Queda aislada en dos constantes
(`OrientacionPositiva` / `OrientacionNegativa`) y **se mide**, no se asume: ver
más abajo.

También se repone el gancho sobre las piezas que devuelve
`RebarSpliceUtils.SpliceRebar`, que son elementos nuevos — igual que ya pasaba
con la marca de `MarcaEstribos`, no está confirmado que conserven las
terminaciones de la barra original.

### [#60](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/60) — estribo #3, longitudinal #4, elegidos por diámetro

El default era `ListarTiposBarra().FirstOrDefault()`: el primero alfabéticamente,
que en el modelo de William es el #10. Por nombre no se puede elegir — "#3",
"#3 COL", "3" cambian de modelo a modelo — así que se elige por **diámetro**,
igual que ya se había hecho con los ganchos el 10-sep.

- `Modelo/TipoBarra.cs` (nuevo): `record TipoBarra(Nombre, DiametroMm)` con
  `PorDefecto(barras, numeroVarilla)`.
- `IProveedorTipos.ListarTiposBarra()` → **`ListarBarras()`**, que devuelve esos
  records. Los combos siguen mostrando nombres, que es lo que se guarda como
  texto.
- `Modelo/CuadroVarillas.cs` (nuevo): la tabla de varillas de la empresa se mudó
  ahí desde `EstandarCQ`, **sin `using Autodesk.Revit`**. `EstandarCQ` quedó solo
  con `Buscar(RebarBarType)` (convierte el diámetro y delega) y `APies`. Hacía
  falta porque `tools/PreviewUI` excluye `EstandarCQ.cs` justamente por
  depender de Revit, y la ventana necesita los diámetros.

Si el documento no tiene ninguna barra de ese diámetro se cae a la primera de la
lista, **avisando** en la barra de estado — mismo criterio que ya usaba
`AvisarSiFaltaAlgunGanchoPorDefecto`, que ahora es
`AvisarSiFaltaAlgoPorDefecto` y cubre las dos cosas.

### [#62](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/62) — planta con la marca de ubicación, sin la pestaña "Lista"

La coordenada **no se calcula**: la arma Revit del cruce de rejillas.

`BuiltInParameter.COLUMN_LOCATION_MARK`, verificado antes de usarlo (regla del
`CLAUDE.md`): está en la metadata de `RevitAPI.dll` de 2027 y es público y
documentado, sin marca de uso interno. El equivalente moderno sería
`ParameterTypeId.ColumnLocationMark`; se usó el `BuiltInParameter` por
consistencia con el resto de `LectorContexto`.

- `ColumnaResumen` suma `MarcaUbicacion`. Es **otra cosa** que `Marca`, que sigue
  siendo la marca de instancia ("C-12") y la que se lee en la elevación y en los
  avisos del generador.
- `PanelPlantaColumnas` vuelve a dibujar etiquetas, ahora con esa marca. Las
  etiquetas se habían sacado el 10-sep porque se encimaban: acá se resuelve
  explícito — se dibujan después de todos los cuadraditos, se prueban dos
  posiciones (debajo y a la derecha) y se omite la que se pise con algo ya
  dibujado. Una columna sin etiqueta no pierde nada: el nombre sigue en el
  tooltip.
- Se fue la pestaña "Lista". **Se queda** la de "Columnetas" (decisión de
  Luciano): son delgadas y suelen estar juntas, y pincharlas en la planta es
  incómodo. Se sigue ocultando sola si el modelo no tiene ninguna.

Sin la lista de columnas principales, la selección múltiple ya no puede vivir en
los `SelectedItems` de un ListBox: ahora hay un `_columnasElegidas` propio, que
la planta y la lista de columnetas reflejan.

### [#63](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/63) — avisar cuando las columnas no comparten estructura

`MismaEstructura` compara cantidad de tramos y la secuencia de tipos. **La altura
no entra**: William fue explícito en que una columna más larga o más corta no es
problema, lo que importa es "el mismo nudo, los mismos tramos".

Se avisa en dos momentos: apenas se eligen varias columnas que no coinciden, y al
aceptar —donde además se aplica **solo a las compatibles** y el estado nombra las
que quedaron afuera con su cantidad de tramos.

Aceptar con varias columnas elegidas copia también el **acero longitudinal** del
panel a todas (con su propia copia de grapas y cajas, para que editar una no
cambie las otras). Sin eso, las columnas que no eran la activa se habrían
generado con el longitudinal por defecto en vez de con el configurado, que es
justo lo contrario de lo que pedía el issue.

## Lo que NO se hizo, a propósito

**[#61](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/61)
(sacar el checkbox "Borrar TODO el armado existente") queda pendiente.** Decisión
de Luciano: es lo que permite regenerar una columna sin acumular duplicados
mientras se prueba. Hay que sacarlo antes de mostrárselo a William, que lo pidió
explícitamente ("es muy buena, pero muy peligrosa"). El issue sigue abierto.

## Verificado en la vista previa

Con `dotnet run --project tools/PreviewUI`, manejando la ventana por
automatización (no a ojo):

- **Recorrer la columna entera con las flechitas sin aceptar nada deja el
  contador en cero** ("Todavía no hay nada aceptado"). Es la trampa concreta del
  #56 y es lo primero que había que comprobar.
- Con un **nudo** activo (5/10): el botón dice "Aceptar y aplicar al resto de los
  nudos (3)" —C-12 tiene 4 nudos— y "Confinado" está **deshabilitado**, con su
  explicación visible.
- Aceptar por ese botón: "Aceptado: 4 tramo(s) de C-12", 4 chulitos en la
  elevación exactamente en los 4 nudos, ✓ en la columna en la planta, y el pie en
  "Listo para generar: 1 columna(s), 4 tramo(s) con ✓".
- Clic en un chulito: lo saca, uno por clic, y el contador baja.
- Con un tramo **contra zapata** activo (1/10) el botón del mismo tipo no
  aparece (el resto es 0) y queda solo "a toda la columna (10)".
- Combos: estribo en **#3 COL**, cara X / cara Y / esquinas en **#4 COL**.
- Planta: "A-1", "B-1", "A-2 (150, 0)" visibles y sin encimarse; sin pestaña
  "Lista".
- Ctrl+clic sobre dos columnas de distinta estructura: *"2 columnas elegidas,
  pero C-12 (10 tramos) no tiene la misma estructura que C-1 (1 tramo): queda
  afuera de lo que aceptes."*

## Pendiente de retest en Revit

Compilan el add-in (`-p:RevitVersion=2027`) y la vista previa, 0 errores, y el
add-in quedó desplegado. Falta comprobar en el modelo real:

1. **#62** — que `COLUMN_LOCATION_MARK` devuelva la coordenada de rejilla de
   verdad, y que una columna fuera del cruce traiga el desfase. Sin rejillas la
   etiqueta no se dibuja, y eso es lo correcto.
2. **#58** — que un nudo aceptado genere estribos uniformes dentro del canto de
   la viga, con el espaciado pedido, y que no choquen con el acero de la viga.
3. **#59 / #43 — la medición de la orientación del gancho.** Es lo que no se
   puede dar por bueno porque compile (regla del `CLAUDE.md`, y el precedente del
   corrimiento de traslapos que se dio por bueno y salió mal). Para eso se
   agregó al `ComandoDiagnosticoRebar`:

   > generar el armado de una columna → seleccionar una barra longitudinal →
   > correr el comando de diagnóstico.

   Reporta, para cada extremo, cuánto se corre la punta del gancho y si eso es
   **ADENTRO** o **AFUERA** respecto del centro del host. Si dice AFUERA, hay que
   invertir `OrientacionPositiva` / `OrientacionNegativa` en
   `Logica/GeneradorLongitudinal` — están las dos juntas y es el único lugar
   donde se decide.

   Ojo con que los dos extremos pueden no comportarse igual con el mismo
   `Left`/`Right`: el diagnóstico los reporta por separado a propósito.
4. **#59 con traslapo** — una columna alta, donde el host de arriba se parte con
   `SpliceRebar`: confirmar que la pieza superior conserva el gancho (se repone
   por las dudas, pero hay que ver si hacía falta).
5. **Sincronización con Revit** — que seleccionar columnas en Revit siga
   activándolas en la ventana, y al revés. Es lo que queda del cableado después
   de sacar `SeleccionMarcadaCambio`.
6. **#60** — que en el modelo de William los combos arranquen en #3 y #4 de
   verdad, con sus nombres reales ("#3 COL" y compañía), y que si falta alguno
   salga el aviso.

## Estado

Implementado y compilado. Interfaz verificada en la vista previa; los tres
puntos que tocan geometría real (#58, #59, #62) esperan el modelo de William.
