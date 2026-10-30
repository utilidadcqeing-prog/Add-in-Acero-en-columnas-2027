# Plan: interfaz web de Acero en columnas, más dibujo que texto (#205)

> **Para agentes:** usar `superpowers:subagent-driven-development` o `superpowers:executing-plans` para ejecutarlo tarea por tarea. Los pasos llevan casillas (`- [ ]`) para seguir el avance.

**Objetivo:** rehacer la ventana de Acero en columnas como página web, con la receta del tablero de vigas. Quien la abre por primera vez tiene que entender qué hacer sin leer instrucciones. La funcionalidad no cambia.

**Arquitectura:** un WebView2 dentro de una ventana WPF muestra una página (htm-preact, sin compilar) que se comunica con C# por mensajes JSON. La página **no calcula**: dibuja una `VistaColumnas` que arma C# con las cuentas de siempre (`SeccionPlanta`, `RepartoEstribos`, `PlanTraslapos`, `ChoquesSeccion`) y le devuelve lo que el usuario cambia. Los generadores de Revit no se tocan: reciben el mismo `OpcionesArmado` que hoy.

**Tecnología:** .NET 10 / WPF, Revit 2026 y 2027, Microsoft.Web.WebView2 (solo para compilar; Revit ya lo trae) y htm-preact standalone (copiado de vigas). Las pruebas son consolas sin Revit, con el mismo molde que `tools/PruebasVistaPrevia`.

**Especificación:** el issue #205 y sus sub-issues #206 a #212. Referencia de diseño y de código: el tablero de vigas (`addins/acero-vigas/Tablero/`, plan `docs/planes/2026-10-16-tablero-vigas-83.md`).

## Contexto

- **Qué se pide** (Luciano, 28/29-oct): hoy no se entiende qué hace cada cosa. La vista previa está separada de donde se configura: cambias las barras y no ves la sección, y las grapas se editan en otra ventana. Además, hay que aceptar tramo por tramo aunque casi todo es igual en la columna. Se pide algo "como vigas" visualmente, con más dibujo que texto, pocos datos y todo ordenado.
- **Proceso pedido:** primero una **maqueta como artifact** para que **Carlos la apruebe**. Recién con su aprobación se pasa a Revit.
- **Fuera de alcance:**
  - las **columnetas** salen de esta ventana (pedido de Luciano);
  - no se cambia ningún generador ni el cálculo de traslapos, ganchos o grapas;
  - no se agregan funciones nuevas.
- **Entorno:** Revit 2027 (y 2026 en CI), .NET 10. Rama `columnas/interfaz-web-205`, worktree `C:\Proyecto\addins-205`, creado desde `origin/main` en ec6267c sin upstream. Antes de pushear: `git push -u origin columnas/interfaz-web-205`.

## Criterios de aceptación

- [x] Carlos aprueba la maqueta (#206). Aprobada el 29-oct (versión 6 del artifact).
- [ ] La ventana tiene tres franjas: columnas a la izquierda (planta, buscador y «Seleccionar en Revit»), dibujo al centro y pasos 1 Barras, 2 Estribos, 3 Grapas y 4 Traslapos a la derecha. Arriba van «Configuraciones», «Cuadro de varillas» y «Generar en Revit».
- [ ] Cada dato que se cambia se ve dibujado en el momento, en el dibujo del centro.
- [ ] Los estribos se configuran una vez para toda la columna: en los tramos libres, en los nudos y en la zapata. Un tramo distinto es una excepción que se marca en el dibujo.
- [ ] No quedan ✓ por tramo ni botones de aceptar. «Generar» arma las columnas listas.
- [ ] El paso Grapas hace lo mismo que la ventana actual de estribos menores, con los mismos gestos.
- [ ] El acero generado es igual al de la versión 1.4.1 con la misma configuración. La única diferencia buscada es que los nudos nunca toman el espaciado confinado (decisión 5).
- [ ] Las configuraciones guardadas con la versión 1.4.1 se siguen pudiendo aplicar.
- [ ] Ninguna parte abre otra ventana, salvo el aviso de choques antes de generar.

## Hallazgos del código y de Revit real

1. **La ventana actual** (`Vistas/VentanaConfiguradorEstribos.*`, unas 2 900 líneas entre XAML y C#) concentra estado, validación y dibujo. Lo que la página nueva necesita sin Revit vive ahí y hay que sacarlo:
   - `Validar(ConfigTramo, Tramo)`;
   - `ColumnasCompatibles()` (#63: «la misma configuración» solo cae en columnas de la misma estructura);
   - `LeerLongitudinalConEstribosMenores()`;
   - el estado aceptado y pendiente de cada tramo (#168), que con esta interfaz desaparece.
2. **Tramos:** `ColumnaContexto.Tramos` alterna `TipoTramo.Libre` y `DentroDeViga` (el nudo), y abajo puede haber un `ContraZapata`.
   - En el nudo, el estribo va **siempre uniforme** (#58, la ventana desactiva «Confinado»).
   - Contra la zapata **no se ponen estribos**: el generador se salta el tramo (`Logica/GeneradorArmado.cs:180`), aunque el longitudinal sí pasa.
3. **`PlanArmado` / `ColumnaPlan.Tramos`** es `List<ConfigTramo?>` con el mismo índice que `Contexto.Tramos`: `null` quiere decir «no se genera». Los generadores leen esto, y **no se cambia**.
4. **La conexión con Revit** está en `ComandoEstribos.cs`: hay un `ExternalEvent` para activar la selección, generar, guardar configuraciones y la vista 3D, y la ventana avisa con los eventos `ElementosActivados`, `GenerarSolicitado` y `VistaPrevia3DSolicitada`. Los manejadores (`ManejadorGeneracionArmado`, `ManejadorVistaPrevia3D`, `ManejadorPersistirConfiguraciones`, `ManejadorSeleccionElemento`) se reusan tal cual.
5. **Las configuraciones guardadas** (`ConfiguracionGuardada`: `Nombre`, `Tramo` y `Longitudinal`) viven en el modelo (`AlmacenConfiguracionesRevit`, Extensible Storage). Guardan **un solo** `ConfigTramo`.
6. **El tablero de vigas** resuelve lo mismo:
   - el WebView2 se crea en código, porque en Revit 2027 el XAML no lo resuelve (`VentanaTablero.xaml.cs`);
   - usa el host virtual `tablero.cqeing`, los datos de usuario en `%LocalAppData%\CQEING\vigas\WebView2`, `web\**` copiado junto a la DLL y `PackageReference Microsoft.Web.WebView2` con `ExcludeAssets="runtime;native;build;buildTransitive"`;
   - fuera de Revit, `tablero.js` carga `demo.js` (`?estado=…`), y así la página se puede ver en un navegador cualquiera.
7. **Vista sin Revit:** `tools/PreviewUI` abre la ventana actual con `ProveedorContextoFalso` y `ProveedorTiposFalso` (las columnas C-1, C-7 y C-12). Las capturas de la ventana de hoy, para comparar, salen de ahí.
8. **Barras por cara** (29-oct, maqueta): en `ConfigLongitudinal`, `BarrasPorCaraX` e `BarrasPorCaraY` cuentan las barras de las **dos** caras juntas («4 (2 + 2)», `Anillo()` las divide por 2). La página muestra y manda las de **cada** cara. `SesionColumnas` (tarea 3) tiene que multiplicar por 2 al recibir y dividir al armar la vista. Prueba: `CambiarBarrasX_CambiaLasBarrasDeLaSeccion`.
9. **Traslapos por número de varilla** (29-oct, pedido de Luciano): `PlanTraslapos` da un `PlanTipo` por grupo (A o B, según la alternancia del anillo) **y** por diámetro, y el panel de hoy dibuja todos («A · #5», «A · #4», «B · #5»…). La vista lleva un plan por cada uno, con `cantidad` de barras y su `solape`; la página dibuja un carril por plan, con desplazamiento horizontal si no entran.
10. **Formas reales en la sección** (29-oct, pedido de Luciano): grapas, estribos cerrados y el estribo principal se dibujan con `PlantaArmado` (`Menor`, `EstriboCerrado`), como hoy en la vista previa. La vista manda los `puntos` de cada pieza ya calculados por C#; `demo.js` tiene un port de esas funciones solo para la maqueta.
11. **Cuadro de varillas** (29-oct, pedido de Luciano): en el gancho de 90° se muestra **solo la B** (`CuadroVarillas.Varilla.Gancho90`), sin la D.
12. **Una columna son varios elementos de Revit** (29-oct, prueba de Luciano en Revit 2027: «cuando las seleccionaba no pasaba nada ni se iluminaba el botón de terminar»). `LectorContexto.ListarColumnas` junta en una pila los elementos de cada piso, y el `Id` de la columna es solo el del elemento de **abajo** (`pila[0]`). Al hacer clic en una planta de arriba se selecciona el elemento de ese piso, que la sesión no reconocía: la barra contaba 0 y «Terminar» seguía gris. Arreglo: `ColumnaResumen.Elementos` lleva los ids de todos los elementos de la pila, y `SesionColumnas.ColumnaDe(id)` traduce cualquiera de ellos a la columna. Lo usan la barra (contar), «Terminar» y «Todas» (selecciona todos los elementos). Prueba: `SeleccionarEnRevit_ElementoDeArriba_EligeLaColumna`.
13. **Los «números raros» de la planta** (29-oct, Luciano: «en el otro ya aparecían con sus etiquetas y todo del modelo pero acá otra vez aparecen con sus números raros»). La página rotulaba cada columna con `marca`, y `LectorContexto.Marca` da el Id entre corchetes («[123456]») cuando la columna no tiene «Marca» en Revit, que es lo normal en el modelo de prueba. La ventana vieja (`PanelPlantaColumnas.DibujarEtiquetasDeUbicacion`) rotulaba con la marca de ubicación del pilar («A-1»). Arreglo (decisión 17).
14. **«Se armó» aunque hubo avisos** (29-oct, Luciano: «aunque me saliera el error después, el addin dice que se armó la columna»). `SesionColumnas.ResultadoGeneracion` metía `ResultadoArmado.Avisos` en el mismo recuadro verde que «Se armó la columna». La ventana vieja los mostraba aparte («Avisos de la generación»). Arreglo: `ResultadoVista.Avisos` va aparte, la página muestra solo una línea naranja («Hay 3 avisos de la generación. Ver los avisos») y la lista va en una ventana aparte que se abre con ese enlace (Luciano, 29-oct: «que sea una ventana aparte que se pueda abrir, porque está tomando mucho espacio»); y la primera línea dice «Se armó la columna X, pero con avisos: revísalos abajo». Prueba: `Generar_ConAvisos_NoLosMezclaConElExito`. Los ganchos que salían en el traslapo en esa misma prueba son otro problema, del generador: `docs/planes/2026-10-29-gancho-en-traslapo.md`.
15. **Lo que le falta a la ventana nueva respecto de la vieja** (30-oct, al empezar la tarea 7, comparando control por control `VentanaConfiguradorEstribos.xaml` con `columnas.js`):
    - Configuraciones (aplicar, guardar cambios, guardar como nueva, eliminar con confirmación), cuadro de varillas, choques con «Volver a revisar» / «Generar igual», «Borrar también el acero que no generó el add-in» y el resultado: **ya están**, se hicieron en las tareas 3 y 4.
    - Los combos de gancho del estribo y de anclaje en zapata (`ComboTipoGancho`, `ComboGanchoLongitudinal`) están **ocultos** en la vieja desde la reunión del 10-sep: no se agregan.
    - «Quitar todos los ✓» y «Deshacer» (#168) eran de los ✓ por tramo, que ya no existen (decisión 7).
    - **Falta: los filtros de la vista 3D** (`Vistas/PanelFiltros3D`). La nueva solo tiene los 5 atajos. La vieja, además, deja marcar qué se ve: longitudinales / estribos / estribos menores; barras enteras, solo ganchos o solo traslapos; grupo A / B; esquinas / cara X / cara Y; cada número de varilla (las casillas aparecen después de ver la 3D, con lo que hay de verdad: `ResumenPrevia3D.Numeros`); zona confinada / no confinada; y «solo el tramo activo». Debe ser lo que Luciano pidió el 29-oct («recuerda poner lo de los filtros a las varillas», dicho justo después de probar «Ver en 3D»), que la decisión 16 tomó por el filtro de barras aptas para estribo.
16. **Elegir una columna no la seleccionaba en Revit** (30-oct, Luciano: «en la vieja cuando se seleccionaba una columna salía seleccionada en el modelo de una, en la nueva se va a esa columna pero no se selecciona/resalta»). La vieja, al activar una columna, llamaba a `ManejadorSeleccionElemento` (`CentrarVista = true`): selecciona en Revit **todos** los elementos de la pila (la zapata incluida, #28) y acerca la vista con `ShowElements`. La nueva no lo hacía. Arreglo (decisión 19).

## Decisiones

1. **Página web como vigas** (Luciano, 28-oct): WebView2 y htm-preact, con la misma estructura de archivos que `acero-vigas/Tablero`. Se copia `vendor/htm-preact-standalone.umd.js` y las fuentes IBM Plex.
2. **La página no calcula acero** (como la decisión 5 de vigas). Todo número que se dibuja sale de `VistaColumnas`. La página solo formatea, maneja el pan y el zoom, y guarda qué paso está abierto.
3. **Columnetas fuera** (Luciano, 28-oct). La planta muestra solo columnas (`ColumnaResumen.EsColumneta == false`). La ventana `VentanaColumnetas` y su botón en la cinta **no** se tocan en este trabajo.
4. **Estribos por columna** (Luciano, 28-oct). La parte de la columna a la vista, de viga a viga, se llama **«Tramos libres»** en la página (Luciano, 29-oct; antes, «Entre vigas»). Hay un modelo nuevo, `ConfigEstribosColumna`, con:
   - `TramosLibres` (`ConfigTramo`: barra, uniforme o confinado, y espaciados);
   - `NudoEspaciado` (m) y `NudoBarraNombre`, siempre uniforme;
   - `Excepciones` (`Dictionary<int, ConfigTramo>`, por índice de tramo).

   La zapata no tiene datos. Un traductor lo convierte a la `List<ConfigTramo?>` de siempre, así que los generadores no cambian.
5. **Nudos: siempre uniformes, y al elegir «Confinado» se pide su espaciado** (Luciano, 29-oct; ~~antes, mi decisión: el espaciado confinado si «tramos libres» era confinado~~):
   - **Uniforme:** los nudos usan el mismo espaciado que la columna. No hay un dato aparte, y el dibujo lo muestra.
   - **Confinado:** junto a L1, «confinado» y «centro» aparece un cuarto dato, **«En los nudos, un estribo cada ___»**. Así se elige en el mismo momento y no queda escondido. Arranca con el espaciado del centro (nunca el confinado), y el usuario lo cambia ahí.

   La barra de los nudos es la de la columna. No hay un bloque «En los nudos» separado: el nudo se configura dentro de «Tramos libres».
6. **Zapata** (mi decisión): en el traductor recibe la misma config que los nudos (uniforme). El generador se la salta igual (hallazgo 2), y no queda `null` porque `null` quiere decir «no se genera» y podría afectar al longitudinal.
7. **«Lista» en lugar de ✓** (Luciano, 28-oct). Una columna está **lista** cuando el usuario cambió algún dato con ella elegida o le aplicó una configuración guardada. «Generar en Revit» arma todas las listas. Cada columna lista tiene una ✕ en la planta para sacarla. Confirmada: Carlos aprobó la maqueta tal cual (29-oct).
8. **Varias columnas elegidas** (lo que se acordó en la conversación): lo que se cambia cae en todas las **compatibles** (misma estructura, regla #63). Las otras se nombran en un aviso, como hoy.
9. **Configuraciones guardadas** (mi decisión): `ConfiguracionGuardada` suma `Estribos` (`ConfigEstribosColumna?`). Si viene `null`, porque se guardó con la 1.4.1 o antes, se arma desde `Tramo` con los valores por defecto de la decisión 5. Al guardar se escriben los dos campos (`Tramo = TramosLibres`), para que una 1.4.1 que lea el modelo siga funcionando.
10. **Grapas fieles a `VentanaEstribosMenores`** (Luciano, 28-oct):
    - la paleta de formas (C, S y cerrado);
    - clic sobre las barras;
    - «Sección normal / Zona de traslapo» y «Ocultar barras de esquina»;
    - la pieza seleccionada con su # de varilla, «Voltear» y «Eliminar (Supr)».

    La validación de cada pieza (`CajaMenor`, `EstriboMenor`, `ChoquesSeccion`) la hace C#, y la página muestra el resultado.
11. **Pruebas sin Revit:** una consola nueva, `tools/PruebasTableroColumnas`, con el molde de `tools/PruebasVistaPrevia` (enlaza archivos y devuelve código 1 si algo falla). La agrega `CQEING-Addins.sln` **solo si** ya lo están las otras consolas de pruebas. Si no, se corre a mano (ver «Cómo probarlo»).
12. **La maqueta es la página real** (Luciano, 28-oct: «primero visualmente en un artifact»): `index.html`, `columnas.js`, `dibujos.js` y `demo.js` se publican como artifact con sus archivos, y lo que Carlos apruebe es exactamente lo que después entra a Revit.
13. **Maqueta aprobada** (Luciano, 29-oct: «carlos aprobó así que podemos seguir»). Carlos aprobó la versión 6 del artifact sin pedir cambios. Se sigue con la tarea 2.

14. **Una columna nueva arranca confinada** (Luciano, 29-oct: «que abra con los confinados»): estribo #3 confinado con L1 0.50, 0.07 en las puntas y 0.14 en el centro, y los nudos cada 0.14; longitudinal #4 con 1 barra por cara. El resto sale de `ValoresPorDefecto` (los de la ventana vieja, que no cambia).

15. **Recubrimiento de 4 cm por defecto** (Luciano, 29-oct: «el recubrimiento que sea 4 por defecto»). Una columna nueva arranca con `ValoresPorDefecto.Recubrimiento = 0.04`, tenga lo que tenga asignado en Revit (antes se usaba el de Revit, y 4 cm solo si no tenía). El aviso de `LectorContexto` cuando el de Revit es distinto sigue saliendo, con el texto corregido en la tarea 5 (decía «Se usa igual», que en la ventana nueva ya no es cierto): «En Revit tiene un recubrimiento lateral de 2.5 cm, distinto del valor normal (4 cm). Revisa cuál corresponde: se usa el de «Recubrimiento».», cierto en las dos ventanas. Una columna ya generada arranca con el recubrimiento con que se generó, y si no quedó guardado, con 4 cm. La 3D y «Generar» mandan siempre el valor explícito. Prueba: `ColumnaNueva_Recubrimiento4cm`.
    - **Lo de Revit, a un clic** (Luciano, 29-oct: «que aparezca un texto con las otras opciones de la columna, como un texto azul resaltado y que al hacer clic se ponga automáticamente ese valor»). `ColumnaVista.RecubrimientosRevit` lleva los recubrimientos que la columna tiene en Revit (el de la columna y el de cada tramo libre o nudo, sin el de contra zapata, redondeados al mm y sin repetir). Debajo del campo «Recubrimiento», la página muestra «En Revit: 2.5 cm» con cada valor distinto del elegido como enlace azul; el clic manda `cambiarRecubrimiento` con ese valor. Prueba: `RecubrimientosRevit_LosDeLaColumna`.
16. **Varillas: las CQEING primero** (Luciano, 29-oct: «poner lo de los filtros a las varillas y que aparezcan las cqeing primero»). Las listas de varillas de la página siguen el orden de `ProveedorTiposReal.ListarBarras` (las «#N CQEING» primero y por número, después las demás por nombre), como la ventana vieja; antes la sesión las reordenaba por diámetro. El filtro del #64 (en estribos y grapas solo las barras aptas para estribo) se mantiene. Prueba: `Catalogos_CqeingPrimero`.
17. **El nombre de una columna sin marca es su eje** (por el hallazgo 13, mi decisión sobre lo que pidió Luciano):
    - En la planta, cada columna se rotula con su marca de ubicación («A-1»), como la ventana vieja; sin ubicación, con su marca.
    - `LectorContexto.Marca`: si la columna no tiene «Marca» en Revit, usa la marca de ubicación y solo al final el Id entre corchetes. Así los títulos, los chips de «listas», los avisos y los mensajes del generador también dicen «A-1» en vez de «[123456]». La marca no se guarda en el modelo ni en el registro de generadas (solo va en textos), así que no cambia nada de lo ya generado.
    - El encabezado de la columna no repite el eje cuando la marca ya es el eje.
18. **Los filtros 3D, los mismos que la ventana vieja** (por el hallazgo 15, mi decisión). El menú «Ver en 3D» pasa a ser un panel: arriba los 5 atajos, abajo las casillas de `PanelFiltros3D` con los mismos nombres y el mismo efecto (`FiltroPrevia3D`), y el estado de la última vista («Actualizando…», o el texto del manejador).
    - La vista lleva `vista3D: { abierta, filtro: { clases, grupos, posiciones, zonas, numerosOcultos, partes, soloTramo }, numeros, estado, fallo }`. La página no guarda el filtro: lo pinta como viene.
    - Mensajes: `ver3D {filtro}` (un atajo: pone las casillas, abre la vista) y `filtro3D {filtro}` (lo marcado). Con la 3D ya abierta, cada cambio la redibuja (como hoy); sin abrir, solo queda guardado.
    - «Solo el tramo activo» pasa a **«Solo el tramo elegido»**: el que está elegido en el paso 2. La sesión pone `SoloAltura` con sus cotas. Si no hay tramo elegido, la casilla está desactivada.
    - Las casillas de número de varilla salen de `ResumenPrevia3D.Numeros` (lo que de verdad se dibujó), como hoy; antes de ver la 3D dicen «aparecen al ver en 3D».
19. **Elegir en la planta selecciona en Revit** (por el hallazgo 16): con `elegirColumnas`, la sesión deja pedido `SeleccionEnModelo` (los elementos de todas las columnas elegidas) y el puente llama a `ManejadorSeleccionElemento` tal cual, con `CentrarVista = true`, como la vieja. «Seleccionar en Revit» → «Terminar» no lo pide (ya están seleccionadas en Revit).
20. **El nombre del modelo arriba** (Luciano, 30-oct: «que se cambie por el nombre del modelo»). La barra de arriba decía «Edificio de ejemplo» también en Revit: era el texto de la maqueta, fijo en `columnas.js`. Ahora la vista trae `modelo` (el `Document.Title` del modelo, que el puente le pasa a la sesión) y la página lo muestra; en la maqueta sigue «Edificio de ejemplo». Prueba: `Vista_TraeElNombreDelModelo`.

## Lo que más puede fallar y ninguna prueba "normal" toca

1. **Configuración guardada vieja** (1.4.1, sin `Estribos`): debe aplicarse sin error y dar los mismos `ConfigTramo` que antes. Prueba `ConfiguracionVieja_SeTraduceIgualQueAceptarTodaLaColumna`, en la tarea 2.
2. **Columna sin nudos ni zapata** (un solo tramo libre, como C-1): los bloques «En los nudos» y «En la zapata» no aparecen y nada se rompe. Prueba `ColumnaDeUnTramo_SinNudosNiZapata`, en la tarea 2, y estado demo `?estado=simple`, en la tarea 1.
3. **Elegir varias columnas de estructura distinta:** el cambio cae solo en las compatibles, y la vista avisa cuáles quedaron fuera. Prueba `VariasColumnas_SoloCambianLasCompatibles`, en la tarea 3.
4. **Una excepción en un tramo que deja de existir** (se aplica una configuración a una columna con menos tramos): la excepción se descarta y no revienta. Prueba `ExcepcionFueraDeRango_SeDescarta`, en la tarea 2.
5. **Pantalla chica o escala de Windows al 125–150 %:** la página se achica entera (`CalculadoraZoom`, como en vigas) y nada se corta. Prueba manual en la tarea 8.

---

### Tarea 1: la maqueta, publicada como artifact para Carlos (#206)

**Archivos:**
- Crear en `addins/armado-columnas/Tablero/web/`:
  - `index.html`, el mismo esqueleto que el de vigas, con `<div id="tablero">`;
  - `columnas.js`, la página: barra de arriba, panel izquierdo, dibujo del centro y pasos;
  - `dibujos.js`, los SVG de planta, sección, elevación de estribos y traslapos A/B;
  - `demo.js`, con `VistaColumnas` de ejemplo y las respuestas a los mensajes;
  - `vendor/` y `fonts/`, copiados de `acero-vigas/Tablero/web/`.
- Crear `docs/planes/imagenes/2026-10-29-interfaz-columnas/`, con capturas de la ventana actual (de `tools/PreviewUI`) y de la maqueta.

**Interfaces:**
- Produce el **contrato de `VistaColumnas`**: la forma de los datos que lee la página. La tarea 3 la arma en C# con estos mismos nombres. Medidas en m; la sección, en cm.
  - `planta`: `{ rejillas: [{nombre, x1, y1, x2, y2}], columnas: [{id, marca, ubicacion, x, y, ladoX, ladoY, estado: 'sinConfigurar'|'lista'|'generada'}] }`.
  - `elegidas`: `[id]`; `descartadas`: `[marca]` (compatibilidad, decisión 8).
  - `columna`: `null` o `{ id, marca, baseX, baseY, recubrimiento, niveles: [{z, nombre}], tramos: [{indice, tipo: 'Libre'|'DentroDeViga'|'ContraZapata', zInf, zSup, excepcion}], seccion: {barras: [{x, y, diametroMm, color, posicion}], estribo: {x1, y1, x2, y2}, menores: [{indice, forma, puntos: [[x, y]], valida, motivo}]}, zonasEstribos: [{zInf, zSup, zona: 'confinada'|'central'|'uniforme'|'nudo'|'zapata', cada}], traslapos: {planes: [{nombre: 'A'|'B', numero, varillas: [{zInf, zSup, largo}], traslapos: [{zInf, zSup, largo}], sinSolucion}]}, avisos: [texto] }`.
  - `opciones`: `{ longitudinal: ConfigLongitudinal, estribos: ConfigEstribosColumna, recubrimiento }`, con los nombres de propiedad de C# en camelCase.
  - `catalogos`: `{ barrasLongitudinales: [{nombre, diametroMm, color}], barrasEstribo: [...], largosComerciales: [6, 12], redondeos: [m], configuraciones: [nombre] }`.
  - `generacion`: `{ choques: [texto], resultado: null|{detalles: [texto]}, error: null|texto }`.
- Produce los **mensajes de la página a C#** (`{tipo, ...}`):
  - `elegirColumnas {ids}`, `quitarDeLista {id}`, `seleccionarEnRevit`;
  - `cambiarLongitudinal {config}`, `cambiarEstribos {config}`, `cambiarRecubrimiento {valor}`;
  - `menor {accion: 'agregar'|'voltear'|'eliminar'|'varilla', ...}`;
  - `ver3D {filtro}`;
  - `aplicarConfiguracion {nombre}`, `guardarConfiguracion {nombre, comoNueva}`, `borrarConfiguracion {nombre}`;
  - `generar {borrarAjeno, aunConChoques}`.
- Respuesta de C#: `{tipo: 'vista', vista}`, que es siempre la vista entera.

- [ ] **Paso 1:** cargar la skill `artifact-design` antes de escribir. Abrir `tools/PreviewUI` (`dotnet run --project tools/PreviewUI`) y guardar capturas de la ventana actual: los pasos de tramos, la vista previa, los estribos menores y los traslapos. Son el material para comparar y para mostrarle a Carlos el antes.
- [ ] **Paso 2:** `demo.js` con tres estados (`?estado=normal|simple|varias`), con datos tomados de `ProveedorContextoFalso`:
  - `normal`: C-12, con zapata, tres pisos y nudos;
  - `simple`: C-1, un solo tramo;
  - `varias`: C-7 y C-12 elegidas, con una descartada.

  `demo.js` responde a los mensajes con cuentas simplificadas, igual que en vigas (decisión 14 de vigas): +/− barras mueve los puntos, cambiar espaciados redibuja las zonas, y el clic de grapas agrega la pieza.
- [ ] **Paso 3:** la página.
  - Arriba: título, «Configuraciones ▾», «Cuadro de varillas» y el botón azul «Generar en Revit».
  - Izquierda: buscador, «Seleccionar en Revit», la planta con colores por estado y la ✕ de las listas.
  - Centro: el dibujo del paso abierto (decisión 2). Pasos 1 y 3: la sección grande. Paso 2: la elevación con las zonas y «cada 0.07», más la sección chica al lado. Paso 4: la columna con las varillas A y B. Además, el botón «3D» con el menú de filtros.
  - Derecha: los pasos 1 a 4, con pocos datos por paso y botones +/−. En el paso 2: el bloque «Tramos libres» (con «En los nudos, cada ___» cuando está en confinado, decisión 5), «En la zapata (sin estribos)» y «Este tramo distinto» al hacer clic en un tramo. Al lado va el dibujo de la columna de costado, con las vigas, los nudos y la zapata rotulados, para que se entienda qué es cada parte. El paso 3 sigue la decisión 10.
- [ ] **Paso 4:** verlo en Chrome sirviendo la carpeta (`python -m http.server` dentro de `web/`) en los tres estados, a 1440 × 900 y a 1280 × 720. No debe haber textos cortados ni errores en la consola.
- [x] **Paso 5:** publicarlo como artifact y pasarle el enlace a Luciano para que se lo mande a Carlos.
  - Publicado el 29-oct: https://claude.ai/artifact/Xt9USpbA2zMh9z8C3DfoAa (privado: Luciano tiene que compartirlo con Carlos desde «Compartir»).
  - **Cambio respecto de lo planeado:** se publica **un solo HTML** con el CSS y los cuatro scripts adentro, armado desde `web/` con un script de Python (en el artifact, las hojas de estilo solo pueden venir de Google Fonts). Las fuentes salen de Google Fonts y no de `fonts/`. El código es el mismo de `web/`, sin cambios.
  - Probado en Chrome (servido con `python -m http.server`), en una ventana de 1536 × 639: los cuatro pasos, clic en un tramo, grapa en C, estribo cerrado con dos clics, tiritas de ángulo, los ejemplos «Varias elegidas» (C-7 descartada) y «Generar». Sin errores en la consola. **Falta** probar a 1440 × 900 y a 1280 × 720 (paso 4).
- [x] **Paso 6:** anotar los comentarios de Carlos en «Decisiones», con fecha, corregir y volver a publicar en el mismo artifact hasta que la apruebe. **Sin la aprobación no se sigue con la tarea 2.** Commit: `Columnas: maqueta de la interfaz web (#206)`.

### Tarea 2: estribos por columna y su traducción a tramos (#209, parte sin Revit)

**Archivos:**
- Crear `addins/armado-columnas/Modelo/ConfigEstribosColumna.cs`.
- Modificar `addins/armado-columnas/Modelo/ConfiguracionGuardada.cs` (agregar `Estribos`, decisión 9).
- Crear `addins/armado-columnas/Modelo/ValidacionTramo.cs`, sacando `Validar` de `VentanaConfiguradorEstribos.xaml.cs` sin cambiar sus mensajes. La ventana vieja pasa a llamarlo.
- Crear `tools/PruebasTableroColumnas/PruebasTableroColumnas.csproj` y `Program.cs`, con el molde de `tools/PruebasVistaPrevia` y enlazando `Modelo/*.cs` menos los cuatro que referencian Revit (la misma exclusión que `tools/PreviewUI`).

**Interfaces:**
- Produce:
  - `public sealed class ConfigEstribosColumna { ConfigTramo TramosLibres; double NudoEspaciado; string? NudoBarraNombre; Dictionary<int, ConfigTramo> Excepciones; ConfigEstribosColumna Copiar(); }`;
  - `public static ConfigEstribosColumna ConfigEstribosColumna.Desde(ConfigTramo tramo)` (valores por defecto de la decisión 5);
  - `public List<ConfigTramo?> ATramos(ColumnaContexto contexto)`: un elemento por tramo, nunca `null`;
  - `public static List<string> ValidacionTramo.Validar(ConfigTramo config, Tramo? tramo)`;
  - `public ConfigEstribosColumna ConfiguracionGuardada.EstribosDeLaColumna()`: `Estribos` (copia) o, si es `null`, `Desde(Tramo)` (decisión 9).

- [x] **Paso 1:** escribir las pruebas que fallan (29-oct; además de estas, `ExcepcionEnNudo_SiempreUniforme`, `Copiar_NoComparteNada` y `Validar_MismosMensajesQueLaVentana`):
  - `TodaLaColumna_DaLoMismoQueAceptarTodaLaColumna`: para C-12 y un `cfg` **uniforme** (#3, cada 0.20), `Desde(cfg).ATramos(ctx)` es igual, tramo por tramo (`ConfiguracionGuardada.Igual`), a lo que produce hoy «Aceptar y aplicar a toda la columna» con `cfg` en un tramo libre. Esa lista se calcula a mano en la prueba, siguiendo `Aceptar` (`VentanaConfiguradorEstribos.xaml.cs:1757`).
  - `Nudos_SinConfinamiento`: con `TramosLibres` confinado (L1 0.50, confinado 0.07, no confinado 0.14), todo índice `DentroDeViga` sale `Modo == Uniforme` con `EspaciadoUniforme == 0.14`, nunca 0.07 (decisión 5).
  - `Nudos_CambiadosAMano`: confinado con `NudoEspaciado = 0.10`, todo nudo sale uniforme cada 0.10.
  - `Nudos_EnUniforme_IgualQueLaColumna`: con `TramosLibres` uniforme cada 0.20, todo nudo sale cada 0.20, aunque `NudoEspaciado` traiga otro valor de antes.
  - `Excepcion_SoloCambiaSuTramo`: con `Excepciones[2]`, solo cambia el índice 2.
  - `ExcepcionFueraDeRango_SeDescarta`: `Excepciones[99]` no lanza y no agrega elementos.
  - `ColumnaDeUnTramo_SinNudosNiZapata`: para C-1, la lista tiene 1 elemento.
  - `ConfiguracionVieja_SeTraduceIgualQueAceptarTodaLaColumna`: un JSON de `ConfiguracionGuardada` sin `Estribos`, con el mismo `cfg` uniforme, se lee y da lo mismo que `TodaLaColumna_…`.
- [x] **Paso 2:** `dotnet run --project tools/PruebasTableroColumnas` → FALLA (no compila: falta `ConfigEstribosColumna`).
- [x] **Paso 3:** implementar `ConfigEstribosColumna`, `ValidacionTramo` y el campo `Estribos` de `ConfiguracionGuardada`.
- [x] **Paso 4:** `dotnet run --project tools/PruebasTableroColumnas` → todo `OK`, código 0 (11 pruebas, 29-oct). `CQEING-Addins.sln` no incluye las otras consolas de pruebas, así que esta tampoco (decisión 11). `dotnet build addins/armado-columnas/practicacq.csproj -c Debug -p:RevitVersion=2027` → compila, y la ventana vieja sigue andando en `tools/PreviewUI`.
- [ ] **Paso 5:** commit `Columnas: estribos configurados por columna, con su traducción a tramos (#209)`.

### Tarea 3: la sesión y la vista, sin Revit (#208–#211, datos)

**Archivos:**
- Crear `addins/armado-columnas/Tablero/SesionColumnas.cs`: el estado de la ventana (columnas elegidas, listas, opciones por columna y configuraciones). Atiende los mensajes de la tarea 1 y devuelve la `VistaColumnas`.
- Crear `addins/armado-columnas/Tablero/VistaColumnas.cs`: records con la forma exacta del contrato de la tarea 1, serializados con `System.Text.Json` en camelCase.
- Crear `addins/armado-columnas/Tablero/ArmadorVistaColumnas.cs`: arma la vista con `SeccionPlanta`, `RepartoEstribos`, `PlanTraslapos`, `ChoquesSeccion` y `ColoresVarilla`. No escribe ninguna cuenta nueva.
- Crear `addins/armado-columnas/Tablero/CompatibilidadColumnas.cs`, sacando `ColumnasCompatibles` de la ventana vieja.
- Modificar `tools/PruebasTableroColumnas/Program.cs`.

**Interfaces:**
- Consume la tarea 2.
- Produce:
  - `public sealed class SesionColumnas(IProveedorContexto contexto, IProveedorTipos tipos, IAlmacenConfiguraciones? almacen = null, IRegistroGenerados? registro = null)`: los mismos parámetros que el constructor de la ventana vieja (`VentanaConfiguradorEstribos.xaml.cs:210`). Las interfaces ya existen en `Modelo/ConfiguracionGuardada.cs:36` y `Modelo/ColumnaGenerada.cs:94`;
  - `public VistaColumnas Atender(string json)` y `public VistaColumnas Vista()`;
  - `public OpcionesArmado? OpcionesParaGenerar(bool borrarAjeno)`: el `PlanArmado` de las columnas listas, con `Tramos = Estribos.ATramos(ctx)`; `null` si no hay ninguna lista;
  - `public FiltroPrevia3D? Pedido3D`, que se llena con el mensaje `ver3D`.

- [x] **Paso 1:** pruebas (todas con `ProveedorContextoFalso`; 29-oct: se escribieron junto con el código, así que no se las vio fallar antes. Además de estas: `DesdeTramos_VuelveATraducirIgual`, `Generar_DejaElPedidoYLuegoLaMarcaGenerada`, `Generar_SinListas_Avisa`, `Grapa_TraeSuFormaYSusPuntas`, `GuardarConfiguracion_EscribeLosDosCampos`, `Ver3D_DejaElPedido`, `Excepcion_SeMarcaEnElTramo` y `VistaC12_SeGuardaParaLaMaqueta`):
  - `ElegirColumna_LaVistaTraeSuSeccionYSusTramos`;
  - `CambiarBarrasX_CambiaLasBarrasDeLaSeccion` (de 2 a 4 por cara: `seccion.barras` pasa de 8 a 12);
  - `CambiarEspaciado_CambiaLasZonas` (los `cada` de `zonasEstribos`);
  - `PrimerCambio_MarcaLaColumnaLista`, y `QuitarDeLista_LaVuelveSinConfigurar`;
  - `VariasColumnas_SoloCambianLasCompatibles`;
  - `Generar_ArmaElMismoPlanQueLaVentanaVieja`: mismas opciones → mismo `PlanArmado` (comparado en JSON) que el que arma hoy la ventana al aceptar toda la columna;
  - `MenorInvalido_SaleMarcadoConSuMotivo`;
  - `AplicarConfiguracionVieja_MarcaListaYTraduce`.
- [x] **Paso 2:** ~~`dotnet run --project tools/PruebasTableroColumnas` → FALLA~~ (ver paso 1).
- [x] **Paso 3:** implementar. El JSON de `Vista()` para C-12 se guarda como `tools/PruebasTableroColumnas/Datos/vista-c12.json` (lo escribe la prueba `VistaC12_SeGuardaParaLaMaqueta`). **Cambio (29-oct):** `demo.js` no lo carga, porque la maqueta tiene que seguir respondiendo a los cambios y un JSON fijo no lo hace. En su lugar, `node tools/PruebasTableroColumnas/comparar-forma.js addins/armado-columnas/Tablero/web/demo.js tools/PruebasTableroColumnas/Datos/vista-c12.json` compara campo por campo la vista de la maqueta con la de C#: hoy C# solo trae campos de más (`motivo` de los traslapos, `nudoBarraNombre`, `tipoGanchoNombre`), ninguno de menos.
  - También salieron de la ventana vieja `Modelo/ValoresPorDefecto.cs` (barra y gancho por defecto, barras aptas para estribo) y `ConfigEstribosColumna.DesdeTramos` (para que una columna ya generada abra con sus valores, #180). La ventana vieja llama a los dos y a `CompatibilidadColumnas`.
- [x] **Paso 4:** pruebas → todo `OK` (28, 29-oct). En Chrome, la página con `vista-c12.json` en lugar de la maqueta recorre los cuatro pasos sin errores en la consola (8 barras, 86 estribos, traslapos A y B #4). No se pudieron sacar capturas: Chrome no respondía al pedido de captura, con el equipo corto de memoria.
  - Arreglos en la página que salieron de esto: la zapata se dibuja desde su cota real (`zapata.zSup`) y no desde la cota 0, y el rango de la elevación arranca en el primer tramo; el cuadro de varillas marca lo usado por **número** (en Revit los tipos se llaman «#3 COL» o «#3 CQEING»).
- [ ] **Paso 5:** commit `Columnas: sesión y vista de la interfaz web, sin Revit (#208-#211)`.

### Tarea 4: la ventana con WebView2 en Revit (#207)

**Archivos:**
- Modificar `addins/armado-columnas/practicacq.csproj`:
  - `PackageReference Microsoft.Web.WebView2` `Version="$(WebView2Version)"`, con los mismos `ExcludeAssets` y `PrivateAssets` que vigas;
  - `Content Include="Tablero\web\**"` a `web\`.
- Crear `addins/armado-columnas/Tablero/VentanaColumnas.xaml` y `.xaml.cs` (copia adaptada de `VentanaTablero`: host `columnas.cqeing`, datos en `%LocalAppData%\CQEING\columnas\WebView2`, zoom con `CalculadoraZoom`).
- Crear `addins/armado-columnas/Tablero/PuenteColumnas.cs`: recibe mensajes, los pasa a `SesionColumnas` **por un `ExternalEvent`** (la sesión lee el modelo) y manda la vista.
- Modificar `addins/armado-columnas/ComandoEstribos.cs`: abre `VentanaColumnas` en vez de `VentanaConfiguradorEstribos`. En Debug, la ventana vieja queda disponible por un botón «Columnas (vieja)» hasta la tarea 8.

**Interfaces:**
- Consume `SesionColumnas` (tarea 3).
- Produce `VentanaColumnas.Enviar(string json)` y `event Action<string> MensajeRecibido`, igual que en vigas.

- [x] **Paso 1:** implementar la ventana y el puente. Si falta el runtime de WebView2, se muestra el mismo `TaskDialog` que en vigas (29-oct). Cambios respecto de lo de arriba:
  - **Comando nuevo** `Tablero/ComandoColumnas.cs` en lugar de modificar `ComandoEstribos.cs`: el botón «Acero en columnas» de la cinta apunta a él, y `ComandoEstribos` queda entero para el botón «Columnas (vieja)» (solo en Debug). `ComandoEstribos.OfrecerVarillasPropias` pasó a `internal` para usarlo desde el nuevo.
  - **Una sola cola** (`Tablero/ColaRevit.cs`, como `AccionesRevit` de vigas) en vez de un `ExternalEvent` por cosa: dentro de la cola la API ya es válida, así que generar, la vista 3D y guardar configuraciones llaman al `Execute` de sus manejadores de siempre ahí mismo. Por eso generar, 3D y configuraciones ya quedan conectados (eran de la tarea 7; se prueban en Revit con ella).
  - «Seleccionar en Revit» usa la barra de vigas (`Tablero/BarraSeleccion.xaml`, con «Todas», «Terminar» y «Cancelar»).
  - La vista 3D se vuelve a dibujar sola con cada cambio después de abrirla (`SesionColumnas.Pedido3D`, prueba `Ver3D_CadaCambioLoVuelveAPedir`).
  - `CalculadoraZoom` es una copia de la de vigas: los add-ins no se referencian entre sí.
- [x] **Paso 2:** (29-oct, los dos años compilan sin errores) `dotnet build addins/armado-columnas/practicacq.csproj -c Debug -p:RevitVersion=2027` → compila. `bin\Debug\2027\web\index.html` existe y **no** está `Microsoft.Web.WebView2.*.dll`. Repetir con `-p:RevitVersion=2026`.
- [ ] **Paso 3:** `Get-Process -Name Revit -ErrorAction SilentlyContinue` → nada. Después, `powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027`. Abrir Revit con el modelo de prueba y la ventana:
  - se ve la planta;
  - al elegir una columna, llega su sección;
  - «Seleccionar en Revit» la elige;
  - cerrar y volver a abrir funciona, y pulsar el botón con la ventana abierta la trae al frente.

  Captura en `docs/planes/imagenes/2026-10-29-interfaz-columnas/`.

  **Primera prueba (Luciano, 29-oct, Revit 2027):** la ventana abre y «Ver en 3D» anda. «Seleccionar en Revit» no: la barra no contaba las columnas elegidas y «Terminar» quedaba gris (hallazgo 12, ya corregido). No se generó. De esa prueba salieron también las decisiones 15 y 16. Falta repetir la prueba con el arreglo (Revit seguía abierto y no se pudo desplegar) y las capturas.
- [x] **Paso 4:** commit `Columnas: ventana web en Revit con WebView2 (#207)`, con los arreglos de la primera prueba. Pedido por Luciano el 29-oct, antes de repetir la prueba.

### Tarea 5: pasos Barras y Estribos conectados (#208, #209)

**Archivos:** modificar `Tablero/web/columnas.js` y `dibujos.js` (lo que falte respecto a la vista real), y `SesionColumnas.cs` si aparece un mensaje que no estaba.

- **Antes de Revit (29-oct):** lo que se puede comprobar sin Revit ya tiene prueba en `tools/PruebasTableroColumnas`: `CambiarBarrasX_CambiaLasBarrasDeLaSeccion`, `CambiarBarraEsquinas_CambiaSuColor` y `CambiarRecubrimiento_MueveLasBarras` (paso 1); `ArrancaConfinada_YLosNudosConElCentro`, `CambiarEspaciado_CambiaLasZonas`, `Excepcion_SeMarcaEnElTramo` y `Excepcion_SoloCambiaSuTramo` (paso 2). Las dos nuevas pasaron a la primera, porque el código ya estaba. También se corrigió el texto del aviso de recubrimiento (decisión 15).
- [x] **Paso 1:** en Revit, con una columna de tres pisos y zapata (Luciano, 29-oct: «todo bien»):
  - +/− en X e Y cambia la sección al momento;
  - cambiar el diámetro de las esquinas cambia el color de esas barras;
  - «Más opciones → Recubrimiento» mueve las barras.
- [x] **Paso 2:** en Revit, en el paso 2 (Luciano, 29-oct: «todo bien»):
  - las tres zonas se ven en la elevación con sus «cada»;
  - pasar «Tramos libres» a confinado dibuja las zonas confinadas en cada tramo libre;
  - clic en un tramo y «Este tramo distinto» lo marca, y cambiar su espaciado cambia solo ese tramo.
- [x] **Paso 3:** generar esa columna y la misma con la ventana vieja, en dos copias del modelo, y comparar el acero (cantidad de barras y estribos por tramo, en una tabla de planificación). Anotar el resultado en «Hallazgos».

  **Resultado (29-oct, leído con el MCP de Revit):** Luciano generó 1-6 con la ventana nueva y 6-7 con la vieja, en el mismo modelo. Son dos columnas iguales: 0.40 × 0.40 bajo el piso 1 (Z −0.65 a 0), 0.30 × 0.30 en los pisos 1 y 2 (0 a 3.50 y 3.50 a 7.00). El acero de las dos es **idéntico**:

  | | 1-6 (nueva) | 6-7 (vieja) |
  |---|---|---|
  | Estribos #3 «CQ Estribo» cada 0.20 | 5 grupos: 3 + 17 + 3 + 17 + 3 = 43 | 5 grupos: 3 + 17 + 3 + 17 + 3 = 43 |
  | Largo de cada estribo | 2.97 / 16.83 m por grupo (iguales) | iguales |
  | Longitudinales #4 | 16 piezas (8 varillas partidas en 2), largos 3.50, 6.00, 7.00 y 2.50 m, cuatro de cada uno | lo mismo |
  | Ganchos | cada pieza: «CQ Gancho 90°» solo en la punta de afuera (fin), ninguno en el traslapo | lo mismo |
  | Traslapo | «CQ Traslapo #4 · 1,195 m» en el inicio de cada pieza | lo mismo |

  Ids: 1-6 = 816452…816564, 6-7 = 817203…817315. De paso confirma el arreglo de `docs/planes/2026-10-29-gancho-en-traslapo.md`: ninguna pieza tiene gancho en el traslapo.

  Observación: todas las longitudinales (de las dos ventanas) quedaron con la forma «CQ Recta 0-90», la que dejó el #176 en el modelo. `DetallesPlegado.BorrarFormasRectas` no la puede borrar mientras alguna columna la use, y Revit la sigue eligiendo sola. Esta vez los traslapos salieron bien igual. Para sacarla del modelo hay que regenerar **juntas, en una sola generación,** todas las columnas que la usan: primero se borra su acero anterior y recién entonces la forma queda libre para borrarse. Una por una no alcanza, porque las demás la siguen usando (también sirve cambiar esas barras a «00»); queda anotado, no se arregla en esta tarea.
- [x] **Paso 4:** commit `Columnas: pasos Barras y Estribos en la ventana web (#208, #209)`.

### Tarea 6: paso Grapas (#210)

**Archivos:** `Tablero/web/columnas.js` y `dibujos.js` (paso 3), y `SesionColumnas.cs` (mensaje `menor`), reusando la lógica de `Vistas/VentanaEstribosMenores.xaml.cs` que no sea de dibujo (sacarla a `Modelo/` si hace falta, sin cambiarla).

- **Lo que ya había (29-oct):** la página del paso 3 y los mensajes `menor` (agregar, eliminar, voltear, varilla, ángulo) se hicieron en la tarea 3. Comparando con `VentanaEstribosMenores` faltaban sus reglas, y la página tenía dos errores:
  - en C#: una sola grapa (C o S) por fila o columna (`PiezaPropuesta`); una grapa va en una sola barra; un cerrado va entre dos barras distintas; un cerrado no se voltea (`BotonVoltear_Click`). Lo que no se puede poner no se pone y sale un aviso que dice por qué;
  - en la página: al poner una pieza quedaba elegida la anterior (`largo - 1` de la lista vieja; ahora es `largo`), y el segundo clic de un cerrado en la misma barra se mandaba igual (ahora se sigue esperando, como en la vieja).
- [x] **Paso 1:** escribir la prueba que falla, `Menores_MismosGestosMismoResultado`: la secuencia «grapa C entre barras 1 y 2, voltear, estribo cerrado 0–3, eliminar el primero» deja la misma `ConfigLongitudinal.EstribosMenores` que la ventana vieja con esos mismos pasos (escrita a mano desde su código).
- [x] **Paso 2:** `dotnet run --project tools/PruebasTableroColumnas` → `Menores_MismosGestosMismoResultado` pasó a la primera (esos gestos ya andaban). Fallaron las cuatro de las reglas, escritas junto con ella: `Grapa_UnaPorFilaOColumna`, `Grapa_VaEnUnaSolaBarra`, `Cerrado_EnLaMismaBarra_NoSePone` y `VoltearCerrado_NoHaceNada`.
- [x] **Paso 3:** implementar los mensajes de `menor` y el paso 3 de la página (decisión 10). `SesionColumnas.MenorRechazado` y el `voltear` de `CambiarMenor`; `poner` de `columnas.js`. `MenorInvalido_SaleMarcadoConSuMotivo` armaba un cerrado en una sola barra para probar el dibujo en rojo; ahora usa uno de dos barras vecinas con una #8 y 6 barras por cara, que no se puede doblar.
- [x] **Paso 4:** pruebas → `OK` (44). En Revit (30-oct, Luciano: «Todo bien»): grapa C, grapa en S y estribo cerrado; segunda grapa en la misma barra → aviso; voltear y eliminar con el botón y con Supr. Comparado con el MCP: 5-7 (ventana vieja) y 1-6 (nueva) llevan en cada estribo un cerrado (CQ Estribo), una CQ Grapa en S y una CQ Grapa C, #3 CQEING, en la misma posición dentro de la sección (diferencia < 1 mm). Las dos ventanas ponen las grapas en cada estribo del tramo libre (ni en el tramo contra zapata ni en los grupos del nudo); la cantidad difiere solo porque los estribos se configuraron distinto (5-7: 3 · 16 · 3 · 16 · 3; 1-6: 3 · 8+15+9 · 3 · 8+15+9 · 3). Un armado que apareció en la A-1 salió en la misma corrida que la 5-7 (ids 816908-816915 seguidos), o sea de la ventana vieja con las dos columnas elegidas; no es un error de la nueva.
- [x] **Paso 5:** commit `Columnas: paso Grapas en la ventana web (#210)`.

### Tarea 7: Traslapos, 3D, configuraciones, cuadro, choques y generar (#211)

**Archivos:** `Tablero/web/columnas.js` y `dibujos.js`, `SesionColumnas.cs` y `PuenteColumnas.cs` (el 3D y la generación por los manejadores de siempre: `ManejadorVistaPrevia3D`, `ManejadorGeneracionArmado` y `ManejadorPersistirConfiguraciones`).

- **Lo que ya había (30-oct):** el paso 4, el menú de configuraciones, el cuadro, los choques y generar se hicieron en las tareas 3 y 4 (hallazgo 15). Lo que falta son los filtros 3D (decisión 18).
- [x] **Paso 1:** los filtros 3D (decisión 18). Hecho el 30-oct: las 6 pruebas fallaron primero (no compilaban) y ahora pasan, junto con todas las demás; 2026 y 2027 compilan sin advertencias. En Chrome, con la maqueta, el panel muestra los atajos y las casillas, «Solo traslapos» deja marcados solo longitudinales y solo traslapos, y desmarcar la #4 la oculta. `comparar-forma.js`: `vista3D` es igual en C# y en la maqueta. Falta probarlo en Revit (paso 4). Pruebas en `tools/PruebasTableroColumnas`, escritas antes que el código:
  - `Filtro3D_LasCasillasLleganAlPedido`: con la 3D abierta, `filtro3D` con solo longitudinales, la #4 oculta, solo ganchos y solo el grupo A deja un `Pedido3D` con eso mismo;
  - `Filtro3D_SinAbrir_NoPide`: sin abrir la 3D no hay pedido, pero la vista trae lo marcado;
  - `Filtro3D_SoloTramoElegido_RecortaASuAltura`: `soloTramo = i` da `SoloAltura = (ZInferior, ZSuperior)` del tramo i; un índice que no existe se ignora;
  - `Ver3D_Atajo_PoneLasCasillas`: «Solo traslapos» deja en la vista solo longitudinales y `partes = SoloTraslapos`;
  - `Resultado3D_TraeLosNumerosYElEstado`: el resumen del manejador llega a `vista3D.numeros` y `vista3D.estado`.
  Después, `SesionColumnas`, `VistaColumnas`, `PuenteColumnas` (manda el resumen a la sesión), `columnas.js` (el panel) y `demo.js` (la maqueta lo atiende).
  - Y la selección en Revit (decisión 19), con la prueba `ElegirColumna_LaSeleccionaEnElModelo`: elegir C-12 deja pedidos todos sus elementos; «Terminar» de «Seleccionar en Revit» no pide nada.
- [ ] **Paso 2:** menú «Configuraciones»: aplicar, guardar como nueva, guardar cambios y eliminar; para eliminar, la página pide confirmación. «Cuadro de varillas», de solo consulta, con lo mismo que `VentanaCuadroVarillas`.
- [ ] **Paso 3:** «Generar en Revit». Si hay choques, se muestra en la página el aviso con «Volver a revisar» y «Generar igual» (como `VentanaChoques`), más la casilla «Borrar también el armado que no generó el add-in». Al terminar, las columnas pasan a «generada» y se muestra el resultado.
- [x] **Paso 4:** en Revit: aplicar una configuración guardada con la 1.4.1 (antes, guardar una en el modelo de prueba con la versión publicada), generar y ver el 3D con cada filtro. **30-oct, Luciano: «Todo bien»** con el nombre del modelo arriba, la selección en Revit al elegir en la planta y los filtros 3D. No confirmó por separado lo de la configuración guardada con la 1.4.1: queda para la lista completa de la tarea 8 (punto 7 de «Cómo probarlo»).
- [x] **Paso 5:** commit `Columnas: traslapos, 3D, configuraciones y generar en la ventana web (#211)`.

### Tarea 8: prueba en Revit, retirar la ventana vieja y publicar (#212)

**Archivos:**
- Borrar `Vistas/VentanaConfiguradorEstribos.*`, `VentanaEstribosMenores.*`, `VentanaChoques.*`, `VentanaCuadroVarillas.*`, `PanelPlantaColumnas.*`, `PanelTraslapos.*`, `PanelVistaPrevia.*` y `PanelFiltros3D.*`, solo si nada más los usa (comprobar con grep). También el botón «Columnas (vieja)».
- Actualizar `tools/PreviewUI` para que abra la página nueva o, si no, borrarlo y anotarlo.

- **Orden cambiado (Luciano, 30-oct: «Borra la ventana vieja y abre el PR»):** los pasos 3 y 4 van antes que el 1 y el 2. La prueba completa en Revit y la de alguien que nunca usó el add-in se hacen con el PR abierto y antes de publicar; si sale algo, se arregla en la misma rama.
- **Qué se borra de verdad** (grep del 30-oct, fuera de comentarios):
  - los ocho de arriba, más `Vistas/Cotas.cs`, `DibujoArmado.cs` y `ConvertidorBarrasPorCara.cs`, que solo usaba la ventana vieja;
  - `ComandoEstribos.cs` (el comando de «Columnas (vieja)»). Su `OfrecerVarillasPropias` pasa a `Tablero/ComandoColumnas.cs`, que es quien lo usa;
  - `tools/PreviewUI` entero: solo abría la ventana vieja. La página nueva se ve fuera de Revit con la maqueta (`python -m http.server`, ver «Cómo probarlo»), así que no se reemplaza.
- **Qué se queda:** `Vistas/Paleta.cs` y `Estilos.xaml` (los usa `VentanaColumnetas`, que no se toca, decisión 3). `ComandoDiagnosticoPrevia3D` (Debug) tomaba los valores por defecto de la ventana vieja; pasa a `ValoresPorDefecto.Tramo` y `.Longitudinal`, que es lo que la vieja llamaba por dentro.

- [ ] **Paso 1:** la lista de «Cómo probarlo», completa en Revit 2027, con capturas.
- [ ] **Paso 2:** una persona que nunca usó el add-in (a elegir con Luciano) arma una columna sin ayuda. Anotar dónde dudó.
- [x] **Paso 3:** borrar la ventana vieja, compilar 2026 y 2027 y repetir el punto 1 de «Cómo probarlo». 30-oct: borrado; 2026 y 2027 (Debug) y 2027 (Release) compilan con 0 advertencias y 0 errores. `dotnet test CQEING-Addins.sln`: 1 352 pruebas, ninguna falla. `PruebasTableroColumnas` y `PruebasTraslapos`: todo OK. `PruebasVistaPrevia`: falla `lindero_alcance_gancho_90` (esperado 0.25375, real 0.22835), **igual en `origin/main`** (comprobado en un worktree aparte), así que no es de este trabajo: viene del cambio del gancho del cuadro (#177). El punto 1 de «Cómo probarlo» en Revit queda con el paso 1.
- [x] **Paso 4:** completar «Estado». Commit `Columnas: se retira la ventana WPF vieja (#212)`. PR a `main` (30-oct). Publicar con la skill `publicar-version-addin-cqeing` cuando Luciano lo pida, después de los pasos 1 y 2.

## Cómo probarlo

```
dotnet run --project tools/PruebasTableroColumnas
dotnet build addins/armado-columnas/practicacq.csproj -c Debug -p:RevitVersion=2027
Get-Process -Name Revit -ErrorAction SilentlyContinue   # tiene que no devolver nada
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027
```

Maqueta fuera de Revit: `python -m http.server 8765` dentro de `addins/armado-columnas/Tablero/web/`, y abrir `http://127.0.0.1:8765/index.html?estado=normal` (también `simple` y `varias`).

En Revit, con el modelo de prueba:

1. Abrir la ventana, buscar una columna por marca y elegirla en la planta. Elegir otra con «Seleccionar en Revit».
2. Paso 1: cambiar las barras X e Y, los diámetros y el recubrimiento; la sección cambia en el momento.
3. Paso 2: los tramos libres en confinado, cambiar el espaciado de los nudos y hacer un tramo distinto; la elevación lo muestra.
4. Paso 3: poner una grapa C, una S y un estribo cerrado; voltear y eliminar.
5. Paso 4: cambiar el largo comercial a 6 m; el dibujo de A y B cambia.
6. Elegir dos columnas iguales y una distinta, y cambiar un dato: cae en las dos iguales y la distinta se avisa.
7. Guardar una configuración, aplicarla a otra columna y eliminarla.
8. Generar con un choque: sale el aviso. «Generar igual» genera, y las columnas pasan a «generada».
9. Comparar con la 1.4.1 sobre una copia del modelo: el mismo acero.
10. Escala de Windows al 150 %: nada se corta.

## Estado

- [x] Plan revisado con Luciano.
- [x] Tarea 1: maqueta publicada y **aprobada por Carlos** (29-oct). Queda pendiente probarla a 1440 × 900 y 1280 × 720 (paso 4).
- [x] Tareas 2 a 7 implementadas y probadas en Revit 2027 por Luciano (29 y 30-oct).
- [ ] Tarea 8: ventana vieja retirada y PR abierto (30-oct). Falta: la lista completa de «Cómo probarlo» en Revit (con la configuración guardada con la 1.4.1 y la escala al 150 %), la prueba con alguien que nunca usó el add-in y publicar.

| Tarea | Sub-issue |
|---|---|
| 1 | #206 |
| 2 | #209 (parte sin Revit) |
| 3 | #208, #209, #210 y #211 (datos) |
| 4 | #207 |
| 5 | #208 y #209 |
| 6 | #210 |
| 7 | #211 |
| 8 | #212 |
