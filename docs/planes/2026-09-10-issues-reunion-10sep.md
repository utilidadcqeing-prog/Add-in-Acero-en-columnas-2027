# Análisis: reunión 10-sep (add-in de columnas) — issues

## Contexto

Reunión del 10-sep-2026, 78 minutos (William Ramos, Carlos Quintero, Juan Felipe,
Luciano). La primera hora es del ERP — unidades con múltiplo, APU, concreto
dosificado — y **no toca el add-in**: no genera issues acá. La parte del add-in va
de 32:26 a 56:30, más un cierre en 1:04:10, y es una prueba en vivo que hizo
William sobre su propio modelo.

Todo lo que sigue está contrastado contra el código real, no contra lo que se
recuerda de la reunión. Donde el diagnóstico difiere de la primera impresión, se
dice explícitamente.

## Lo que la reunión CERRÓ (confirmaciones, no issues)

- **Grapas: gancho de 180°. Cajas: gancho de 135°** (William, 1:04:41). Luciano
  preguntó expresamente por la NSR-10 (135° en un extremo y 90° en el otro) y
  William lo descartó: "No, no, solamente en eso". Esto **cierra la pregunta que
  quedaba abierta para Carlos** en
  `docs/planes/2026-09-10-gancho-mas-chico-y-cajas-irresolubles.md`, y confirma
  que el trabajo del gancho de grapa de esa misma sesión apunta bien.
- **La longitudinal es fija para toda la columna**; lo que cambia por tramo son
  solo los estribos (William, 37:50: "ya hacer longitudinal fijo fijo fijo").
- **Cambiar de tramo con clic ya funciona** (41:13, confirmado en vivo): `IrA`
  se dispara tanto desde las flechas ▲/▼ como desde el clic en la elevación
  (`Vistas/VentanaConfiguradorEstribos.xaml.cs:388-402`).

## Los issues

### P1 — Barra longitudinal

**[#41](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/41) — Una sola barra de punta a punta, con empalmes nativos, múltiples y reversibles**

Lo que se vio (48:11–49:35): la barra se corta en cada cambio de columna aunque
ahí no haya traslapo — "no es un empalme, esto es otra barra que continuó para
abajo; acá no debería haber empalme, debería ser la misma barra que sigue para
arriba". El orden de trabajo que pidió William es explícito: **primero que la
barra vaya completa y derecha de punta a punta**, y recién después aplicarle los
empalmes; y que borrar el empalme devuelva la barra entera.

Hoy hay un corte OBLIGADO en cada límite de host
(`Logica/GeneradorLongitudinal.cs:1103-1108`), justificado con "Revit no permite
un mismo Rebar en dos FamilyInstance". **Eso es lo primero a verificar contra
Revit real**, con un comando de diagnóstico temporal (regla del `CLAUDE.md`): un
`Rebar` hosteado en la columna de abajo cuya geometría abarque toda la pila.
Revit suele aceptarlo con un aviso de "armadura fuera de su hospedador". Si lo
acepta, desaparecen todos los cortes obligados.

El empalme nativo ya existe (`IntentarPartirConSplice`,
`GeneradorLongitudinal.cs:998-1047`) pero **solo cuando hay exactamente UN corte
real dentro del mismo host**; con dos o más se cae al método viejo de dos barras
sueltas con corrimiento a mano. El Dynamo que nombró William (50:19) está en el
repo y muestra el camino que sí funciona —
`Dymanos de referencia/definitions/Empalme_una_barra.dyf` — con tres detalles que
el código actual no tiene:

- usa la sobrecarga **`RebarSpliceGeometry(barra.Id, punto)`**; nuestro código usa
  `RebarSpliceGeometry(punto, normal)`, que es la que falló en el diagnóstico del
  9-sep devolviendo 0 barras;
- **proyecta el punto sobre la curva de la barra** (`curva_guia.Project(punto)`)
  antes de crear la geometría. El propio script avisa que, si el punto no toca la
  barra perfectamente, Revit "acepta los datos pero no corta" — exactamente el
  síntoma que tuvimos;
- pasa **varias geometrías en una sola llamada**, que es lo que hace falta para
  una barra con más de un traslapo.

**[#42](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/42) — La posición de la barra la manda la sección más chica de la pila**

Lo que se vio: al pasar de una columna grande a una más chica "se corta raro" y
las posiciones no coinciden entre las dos.

La X/Y ya se calcula una sola vez (bien), pero con el footprint del host de **más
arriba** (`GeneradorLongitudinal.cs:264-269`), asumiendo que el ensanche siempre
está en la base. Si la sección chica no es la de arriba, la barra queda fuera del
hormigón y `Rebar.CreateFromCurves` devuelve null.

Decidido con Luciano: la referencia pasa a ser la **intersección de las huellas de
todos los hosts de la pila** — la sección más chica, esté donde esté. En el mismo
movimiento hay que revisar `diametroEstribo` (`GeneradorLongitudinal.cs:161-167`),
que se toma como el máximo entre tramos **tildados**, mientras que la barra
recorre todos los hosts físicos.

**[#43](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/43) — El gancho de la zapata tiene que mirar hacia adentro**

Lo que se vio (39:51–40:22): "Los de acá se hicieron 90 grados por un mismo lado.
Debería ser siempre para dentro". Es un requisito normativo que Carlos ya había
marcado en la reunión del 8-sep.

Hoy la orientación es la constante `RebarTerminationOrientation.Left`
(`GeneradorLongitudinal.cs:1205-1210`), con un comentario que admite que nunca se
verificó contra Revit. **El patrón ya está resuelto** en `IntentarGrapa`
(`GeneradorLongitudinal.cs:1362-1369`): calcula el lado con el vector real hacia
el centro de la sección. Hay que aplicarlo a la longitudinal en vez de la
constante.

**[#44](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/44) — Faltó acero longitudinal encima de una viga**

Lo que se vio (40:35–41:11): "viene la viga, simplemente puso el acero aquí abajo,
pero no aquí arriba".

El filtro `DentroDeViga` **no** es la causa: la longitudinal no lo aplica (recorre
todos los tramos físicos, tildados o no). La causa verificable es otra: un tramo
con `IdElementoHost` nulo sale de `porHost` **sin ningún aviso**
(`GeneradorLongitudinal.cs:734`), y después su segmento se descarta en silencio
(`:844-845`) con un comentario que dice "ya avisado", cuando ese aviso solo existe
para hosts que sí entraron en `porHost`. Primer paso: que ese caso avise, y
reproducirlo sobre el modelo de William.

### P2 — Configuración por tramo

**[#45](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/45) — "Aplicar a selección" pisa la configuración individual de cada tramo**

Lo que se vio (42:25–43:58): William cargó tramo por tramo #4, #4, #3, #3, #4,
#3, #4, #4, #3, y #2 en los dos últimos. Al generar: "las puso de inicio a fin
número cuatro" y "puso número dos donde ni siquiera la había dicho... este tramo
de aquí es el que tiene número dos".

**No es el generador.** `GeneradorArmado` respeta `ConfigTramo.TipoBarraNombre`
tramo por tramo (`GeneradorArmado.cs:172-176`, dentro del bucle), y la ventana
guarda una configuración por tramo en `_configPorTramo`
(`VentanaConfiguradorEstribos.xaml.cs:42`) que se conserva al cambiar de tramo o
de columna.

La causa está en que **el tilde significa dos cosas a la vez**:

- `BotonAplicarSeleccion_Click` (`:708-773`) copia el panel activo a **todos** los
  tramos tildados;
- `ConstruirPlanArmado` (`:975-1009`) genera **solo** los tramos tildados.

Para que se genere la columna entera hay que tildarla entera — y tildarla entera
es justamente lo que hace que un "Aplicar a selección" borre la configuración
individual de cada tramo. Encima, un clic en la vista en planta tilda la columna
completa en cascada (`ActivarYAlternarMarcadoDesdePlanta`, `:335-346`): no hay
forma de solo activar una columna desde la planta sin tildar todos sus tramos.
Hay que separar los dos significados.

**[#46](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/46) — Barras que no están en el cuadro de la empresa**

En la demo salieron dos avisos: "la barra número … no está en el cuadro de la
empresa, diámetro fuera de normalizado" y "no se puede ubicar el gancho".
`Modelo/EstandarCQ.cs` tiene el cuadro de la #2 a la #8, buscando por diámetro con
tolerancia de 0,6 mm; una barra métrica de 10 mm no cae en ninguna fila. Decidir
con William: se amplía el cuadro (hacen falta los valores de gancho y traslapo) o
la ventana deja de ofrecer tipos de barra que no estén en él.

**[#47](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/47) — El confinamiento se aplica donde no entra, y sin feedback**

Lo que se vio (36:15): "está confinando incluso esto aquí y ahí no cabe el
confinamiento". Tres cosas verificadas, las tres reales:

- **`Validar` no bloquea**. `BotonGenerar_Click` (`:1029-1048`) nunca la consulta:
  el texto rojo es informativo. Si L1×2 ≥ altura, el generador arma el tramo
  confinado de punta a punta (`GeneradorArmado.cs:251-259`), gastando más acero
  del pedido, con el aviso enterrado al final de la generación.
- **Desfase de 10 cm entre la UI y el generador**: la UI compara L1 contra la
  altura bruta del tramo y el generador contra la altura ya recortada 5 cm por
  lado (`SeparacionExtremosMetros`). Hay una banda donde la UI dice que está bien
  y el generador igual colapsa a "punta a punta".
- **"Aplicar a selección" saltea el chequeo de nudo**: llama
  `Validar(plantilla, tramo: null)` (`:714`), y ese chequeo exige un tramo. Así se
  puede cargar configuración de confinamiento en un tramo dentro de viga sin
  aviso; después ese tramo genera **cero estribos y cero feedback**
  (`GeneradorArmado.cs:151-153` devuelve `true` sin avisar). Candidato a explicar
  parte del "puso el acero abajo pero no arriba".

### P3 — Interfaz

**[#48](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/48) — Panel de tramos al lado de la planta**

Hoy planta y lista son **excluyentes**: un solo botón las intercambia
(`VentanaConfiguradorEstribos.xaml.cs:311-320`) y en modo planta se pierden los
checkboxes y la lista de tramos. Por eso William dijo (36:39–37:03) que en modo
lista se pierde de cuál columna se trata, y en modo planta no puede editar un
tramo puntual.

Decidido con Luciano: la planta se queda como está, para ubicar la columna, y
**al lado** aparece un panel nuevo con los tramos de la columna seleccionada y las
opciones de cada uno. William pidió además ventana **más grande y más espaciosa**,
sacar las etiquetas con el nombre de cada columna (hoy un `TextBlock` con
`c.Marca`, `PanelPlantaColumnas.xaml.cs:205-215`, que además se superponen entre
sí cuando las columnas están juntas) y darle más protagonismo a la parte gráfica.
El lienzo hoy es de tamaño fijo 220×460 con cuadraditos de 10×10 px
(`PanelPlantaColumnas.xaml:5`, `.cs:187`), que no escalan con la sección real.

**[#49](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/49) — Selección: un solo clic, Ctrl y arrastre**

Lo que se vio (35:40–36:15, 44:05–45:36): hay que hacer clic dos veces para que la
selección se aplique, y no se puede seleccionar de a varias.

El evento de Revit ya existe (`UIApplication.SelectionChanged`,
`ComandoEstribos.cs:122`), pero `SincronizarDesdeRevit`
(`VentanaConfiguradorEstribos.xaml.cs:364-386`) se queda con **un solo nodo**
(`FirstOrDefault`) y **no tilda nada**: si se seleccionan cinco columnas en Revit,
se activa una arbitraria — la primera en el orden del modelo, no en el orden de
selección. Pedido de William: selección inmediata en los dos sentidos,
**Ctrl+clic** para varias, y **arrastre estilo Revit** (hacia la izquierda toma
todo lo que toque, hacia la derecha solo lo que quede completamente dentro).
Seleccionar dos posiciones de la misma columna se muestra una sola vez.

**[#50](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/50) — Ganchos por defecto, y filtrar los combos por estilo**

William (34:21): "esto aquí es innecesario, puedes ocultarlo y ponerlo por
defecto para que funcione, porque siempre va a ser así". Por defecto:
**Stirrup/Tie 135° para el estribo** (el no sísmico, el que dice "estribo
tirante") y **Standard 90° para el gancho de anclaje en la zapata**.

Hoy el valor por defecto es `ListarTiposGancho().FirstOrDefault()` — **el primero
alfabéticamente** (`VentanaConfiguradorEstribos.xaml.cs:562` y `:641`). Peor: la
lista no filtra por `RebarStyle` (`Logica/ProveedorTiposReal.cs:31-37`), así que
los dos combos ofrecen la unión de los ganchos de los dos estilos y es fácil
elegir uno incompatible — en Revit un `RebarHookType` solo se puede aplicar a
barras de su mismo estilo, y el fallo aparece recién al generar, como
`CreateFromCurves` devolviendo null. El criterio correcto ya está escrito en
`GeometriaSeccion.BuscarGanchoGrapa` (`GeometriaSeccion.cs:151-156`): filtrar por
`Style` y por ángulo. Hay que aplicarlo a los combos y después ocultarlos.

**[#51](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/51) — Sacar el botón "Exportar"**

William (34:48): "eso de exportar se puede quitar porque no tiene realmente
utilidad". Confirmado por el código: `BotonExportar_Click`
(`VentanaConfiguradorEstribos.xaml.cs:1011-1027`) vuelca un JSON de una sola vía —
**no existe ninguna importación** que lo lea de vuelta, así que no sirve ni para
guardar y retomar la configuración.

### P4 — Retest y arrastre

**[#52](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/52) — Estribos que se salen y siguen con dobladura**

William, 32:40: "todavía se sigue doblando esto aquí y algunos estribos se salen",
con barra #3, sobre el primer documento que había pasado. Puede ser un modelo
anterior a los arreglos de `RebarShape`
(`docs/planes/2026-09-09-dobladura-estribo-rebarshape.md`). Reproducir antes de
abrir un arreglo nuevo.

**[#53](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/53) — El espaciado no confinado se pisa en cada tecla**

No salió en la reunión; apareció al revisar el código para este análisis.
`CampoEspaciadoConfinado_TextChanged` (`VentanaConfiguradorEstribos.xaml.cs:527-531`)
reescribe el espaciado no confinado con el doble en **cada pulsación**, aunque su
propio comentario dice que respeta lo que el usuario haya tecleado. No hay ningún
flag que implemente eso: si el usuario ajusta el no confinado a mano y después
vuelve a tocar el confinado, pierde su valor sin aviso.

## Issues creados

| Issue | Prioridad | Tema |
|---|---|---|
| [#41](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/41) | P1 | Una sola barra de punta a punta con empalmes nativos múltiples y reversibles |
| [#42](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/42) | P1 | La posición de referencia sale de la sección más chica de la pila |
| [#43](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/43) | P1 | El gancho de zapata debe mirar hacia el centro |
| [#44](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/44) | P1 | Un tramo sin host se descarta en silencio |
| [#45](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/45) | P2 | "Aplicar a selección" pisa la configuración de cada tramo |
| [#46](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/46) | P2 | Barras fuera del cuadro de la empresa (`EstandarCQ`) |
| [#47](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/47) | P2 | El confinamiento se aplica donde no entra y sin feedback |
| [#48](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/48) | P3 | Panel de tramos al lado de la planta |
| [#49](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/49) | P3 | Selección: un clic, Ctrl+clic y arrastre |
| [#50](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/50) | P3 | Ganchos por defecto y filtrado por estilo |
| [#51](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/51) | P3 | Sacar el botón "Exportar" |
| [#52](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/52) | P4 | Retest de estribos que se salen y dobladura |
| [#53](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/53) | P4 | El espaciado no confinado se pisa en cada tecla |

## Prioridad sugerida

1. **[#41](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/41) y [#42](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/42)** (barra continua y posición de referencia):
   son el pedido central de la reunión y arreglan de paso buena parte del "se
   corta raro al pasar de una columna a otra".
2. **[#43](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/43)** (gancho de zapata hacia adentro): requisito normativo,
   arreglo chico y con el patrón ya resuelto en el mismo archivo.
3. **[#45](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/45), [#50](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/50) y [#51](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/51)**: lo que más ruido hizo en
   la demo y lo más barato de arreglar.
4. El resto, por orden de la tabla.

## Estado

Documento de análisis. **No se tocó código en este trabajo.**
