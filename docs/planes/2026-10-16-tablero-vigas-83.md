# Plan: el tablero de vigas, idéntico a la maqueta (#83)

> **Para agentes:** SUB-SKILL REQUERIDA: superpowers:subagent-driven-development (recomendada) o superpowers:executing-plans, tarea por tarea. Los pasos usan casillas (`- [ ]`).

**Objetivo:** abrir en Revit la ventana del tablero tal como la pinta la opción 1 de la maqueta, píxel a píxel, con los pórticos y los cálculos reales del modelo.

**Arquitectura:**
- Una ventana WPF lleva dentro un **WebView2** que muestra el HTML de la maqueta, portado a una página propia que no depende de claude.ai.
- La página guarda lo que el usuario escribe, igual que el `state` de la maqueta, y se lo manda a C# por mensajes JSON.
- En C#, `SesionTablero` (en la librería sin Revit, con pruebas) arma con el motor una `VistaTablero` lista para pintar y se la devuelve a la página.
- Lo que toca Revit (leer el modelo, seleccionar, mostrar, 3D) va por un `ExternalEvent`.

**Tecnología:**
- C# (`net8.0-windows` para Revit 2026) y WebView2 1.0.2478.35, el que ya trae Revit 2026.
- Preact + htm sin compilar (un archivo `.js` fijo, guardado en el repo).
- Fuentes IBM Plex guardadas en el repo.
- Pruebas con xUnit.

**Fuente de las vistas:** la maqueta, versión 19 (https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh):
- tablero: `project/Main.dc.html`;
- alzado: `project/Beam.dc.html`;
- estados: `Reglas`, `Cimentacion`, `Aviso`, `Varias` y `Vacio`, que son `Main` con `estado=…`.

Las opciones 2 y 3 (`Panel`, `Minima`) **no** se hacen.

**Fuente de las reglas:** lo que dice Carlos (las dos transcripciones del 16-oct y `Desktop\addin vigas\traslapos-vigas-notas.md`), ya metido en el motor (#80, #88, #82, #89 y #90).

## Contexto

- **Qué se pide:**
  - la issue [#83](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/83);
  - Juan, 16-oct: *"las vistas deben ser idénticas a las confirmadas en el artifact, ya que eso está decidido"*, *"idénticas al píxel, mete el HTML de la maqueta en Revit"*.
- **Hecho cuando** (issue): *"La ventana funciona en Revit con datos reales y ningún texto se corta ni se parte en dos líneas."*
- **Fuera de alcance**, cada cosa con su issue. Los botones se ven igual que en la maqueta, pero al pulsarlos muestran el aviso *"Esto llega con la #NN."* (decisión 7):
  - **#84 edición:** Subir, Bajar, Automático, Aplicar traslapo y Aplicar bastón. Las tarjetas sí muestran sus datos reales.
  - **#91 corte:** la vista tipo por defecto, la profundidad y elegir barras para los estribos adicionales (+ Cerrado, + Abierto, Agregar). El corte sí se pinta con la sección real, y la línea del corte se mueve y se pone roja en un traslapo, como en la maqueta.
  - **#85 generar:** el botón "Generar en Revit".
- **Entorno:**
  - Revit 2026, el que hay instalado en este equipo;
  - `RevitVersion` por defecto 2026 (`addins/Directory.Build.props`);
  - rama `vigas-tablero-83`, que sale de `main` (ya trae #80, #88, #81, #82, #89 y #90).

## Criterios de aceptación

- [ ] El botón "Tablero" de la pestaña CQEING › Vigas abre la ventana en Revit 2026.
- [ ] Con los datos de demostración, cada estado se ve igual que su tablero en la maqueta: normal, reglas, cimentación, aviso, varias y vacío. Se comprueba con la comparación de capturas de la tarea 3.
- [ ] Con un modelo real, la lista muestra sus pórticos y tramos, y al elegir uno se ven sus barras, traslapos, bastones, estribos, kilos y avisos, calculados por el motor.
- [ ] Cambiar algo en los pasos 1 a 3 (barras, recubrimiento, tipo, bastones, estribos) recalcula al momento.
- [ ] La pantalla Reglas lee y guarda las reglas del PC (`AlmacenReglas`), con el candado de la maqueta.
- [ ] "En vivo con Revit", "Seleccionar en Revit", "Ver en Revit" y "3D" funcionan.
- [ ] Ningún texto se corta ni se parte en dos líneas, ni en pantallas más chicas que 1440 × 900 ni con escala de Windows al 125 % o al 150 %.
- [ ] `rtk dotnet test CQEING-Addins.sln -c Release` en verde.

## Hallazgos del código y de Revit real

- **WebView2 en Revit 2026** (medido en este equipo, 16-oct):
  - `C:\Program Files\Autodesk\Revit 2026\` trae `Microsoft.Web.WebView2.Core.dll`, `.Wpf.dll` y `.WinForms.dll` 1.0.2478.35, y `WebView2Loader.dll`;
  - Dynamo usa esa misma versión;
  - el runtime de Edge WebView2 instalado es el 155.0.4283.45.
  - Por eso el add-in compila contra el NuGet 1.0.2478.35 **sin copiar las DLL** y usa las que ya cargó Revit.
  - Revit 2027 no está instalado: su versión de WebView2 queda por medir (tarea 1, paso 6).
  - **Probado en Revit 2026** (Juan, 16-oct, tarea 1): el botón Tablero abre la ventana y el eco va y vuelve entre la página y C#. No se copió la línea de diagnóstico (versión y DLL cargada); queda por anotar en la tarea 5.
- **La maqueta** es un "Design Component":
  - la plantilla va en `<x-dc>`, con `{{huecos}}`, `<sc-if>`, `<sc-for>` y `<dc-import>`;
  - la lógica es una clase tipo React (`state`, `setState`, `renderVals()`);
  - el runtime es el de claude.ai (`support.js`) y no se puede llevar a Revit.
  - Por eso se porta a Preact + htm, que tiene la misma forma (componentes de clase, `setState`) y acepta `style` como texto. Así los estilos se copian sin tocarlos.
- **Datos escritos a mano en la maqueta**, que pasan a salir del motor:
  - `Main.dc.html`: `PORS`, `TRAMOS`, `C`/`F` (barras), `CL`/`FL` (traslapos), `BAST0`, `LUCES`, `RULES0`, `sumIzq`/`sumDer` (kg por cara), `layerInfo`, `place`, `nEst`, `lenMain`, `planOf`…;
  - `Beam.dc.html`: `C`, `F`, `mkLap`, `mkB`, `st` (zonas de estribos) y `g` (columnas, ejes y voladizo), con lugares fijos para 4 luces.
- **El alzado** usa `X(m) = 30 + 52·(m + 0.20)` (52 px por metro, desde la cara izquierda del pórtico de la maqueta, en −0.20) y un alto fijo de 204, y se ajusta con zoom.
- **El motor** ya da lo que hace falta:
  - `MotorVigas.Calcular(EntradaVigas)` da `ResultadoVigas`: barras `S1`…, traslapos `S1–S2`, bastones `BS1`/`BI1`, `CaraInicio`/`CaraFinal` con sus kg, `CaraMasEconomica`, `Avisos` y `ZonasTraslapo`;
  - `MotorEstribos.Calcular(EntradaEstribos)` da `ResultadoEstribos`: `E`, `E1`…, la sección con las barras movidas y `EnSeccion`;
  - `RepartidorFilas` da `CabenPorFila`, `Filas`, `Error` y `Maximo`;
  - `AlmacenReglas` lee y guarda en `%AppData%\CQEING\vigas\reglas.json`;
  - `LectorModelo.Leer(doc)` + `AgrupadorPorticos.Agrupar` dan `ResultadoLectura`, con los `PorticoLeido` y sus `TramoLeido` (sección en mm).
  - Los ids del motor son los mismos que pinta la maqueta.
- **Repo:**
  - `AppVigas.cs` hoy pone un botón temporal "Pórticos (diagnóstico)", con la nota de quitarlo cuando el tablero tenga "Seleccionar en Revit";
  - todos los add-ins usan WPF (`UseWPF` en `Directory.Build.props`);
  - en este equipo hay Node 22, Python 3.14 con Pillow y Edge.

## Decisiones

1. **Idéntico al píxel = el mismo DOM y los mismos estilos que la maqueta** (Juan, 16-oct). La plantilla de `Main` y `Beam` se copia elemento por elemento con sus `style` tal cual; solo cambia la sintaxis de la plantilla (decisión 3).
   - Donde la maqueta tiene lugares fijos (4 luces, `C1`…`C4`, `T1`…`T4`, `a1`…`v2`), se pinta un elemento igual por cada dato real con un bucle. Con los datos de la maqueta, el resultado es el mismo DOM.
2. **WebView2 dentro de una ventana WPF** (Juan, 16-oct: "mete el HTML de la maqueta en Revit"):
   - la página se sirve de una carpeta `web\` junto a la DLL, con `SetVirtualHostNameToFolderMapping("tablero.cqeing", …)`;
   - los datos de usuario de WebView2 van en `%LocalAppData%\CQEING\vigas\WebView2`, porque la carpeta de Revit no se puede escribir;
   - nada se carga de internet: las fuentes y Preact van en el repo.
3. **Reglas para portar la plantilla** (mi decisión; mantiene la identidad visual):
   - `<x-dc>…</x-dc>` pasa a ser lo que devuelve `render()` con `html\`…\``;
   - `{{a.b}}` pasa a `${v.a.b}`, donde `v = this.valores()` (el `renderVals()` de la maqueta);
   - `<sc-if value="{{x}}">…</sc-if>` pasa a `${v.x ? html\`…\` : null}`;
   - `<sc-for list="{{xs}}" as="r">…</sc-for>` pasa a `${v.xs.map((r) => html\`…\`)}`;
   - `<dc-import name="Beam" sel="{{barSel}}" …>` pasa a `<${Alzado} sel=${v.barSel} … />`, con las mismas props en camelCase;
   - `onChange` en `<input>` pasa a `onInput`, porque Preact usa el evento nativo. En `<select>` se queda `onChange`;
   - el `<style>` del `<helmet>` va tal cual al `index.html`, y el `<link>` de Google Fonts se cambia por `fonts.css` local, con las mismas familias y pesos (Sans 400/500/600 y Mono 400/500).
4. **Unidades de la vista: las de la maqueta**, para no reescribir sus cuentas de pantalla. El alzado y las longitudes van en m (`double`), la sección en cm y los kilos en kg. `ArmadorVista` convierte desde los mm del motor.
5. **La página no calcula acero.**
   - Las cuentas de la maqueta que inventan datos (`C`, `F`, `CL`, `FL`, `BAST0`, `nEst`, `lenMain`, `layerInfo`, `place`, `sumIzq`, `kgTotal`…) se borran y se leen de `props.vista`.
   - Se quedan las de pantalla: formatear con `f2`, los textos de los mensajes, los colores de selección, el pan y el zoom.
6. **Tamaño:** la página mide 1440 × 900 CSS px, como la maqueta.
   - Si el área de trabajo de la pantalla es más chica, `ZoomFactor = min(1, ancho / 1440, alto / 900)`, calculado sobre píxeles independientes del dispositivo. Todo se achica junto y nada se corta (criterio "ningún texto se corta").
   - La ventana se puede cambiar de tamaño y el zoom se recalcula.
7. **Botones de otras issues:** se ven y se colocan igual que en la maqueta, pero muestran el aviso de la maqueta (`say`) con *"Esto llega con la #84."*, *"… #85."* o *"… #91."* (mi decisión; ver "Fuera de alcance"). Cuando llegue cada issue, se cambia el aviso por la acción.
8. **Datos de demostración** (`web/demo.js`): una `VistaTablero` escrita a mano con los números que hoy pinta la maqueta (Pórtico 1, P2, P3a, P3b, PC1; S1 3.00, F1 6.00…). Se usa solo fuera de Revit (sin `window.chrome.webview`), con `?estado=normal|reglas|cimentacion|aviso|varias|vacio`, y sirve para comparar capturas contra la maqueta. No se muestra en Revit.
9. **Botón de la cinta:** "Tablero" abre una sola ventana sin modo, que no bloquea Revit; si ya está abierta, la trae al frente. El botón temporal "Pórticos (diagnóstico)" se quita, como decía su nota (#81).
10. **En vivo con Revit:** con el enlace activo, si en Revit se selecciona una viga de un pórtico, el tablero abre ese pórtico (`UIApplication.SelectionChanged`); elegir un pórtico en el tablero lo selecciona en Revit. Sin enlace, no hace ninguna de las dos cosas.
11. **"3D"** abre la vista 3D `CQEING Vigas 3D`, que se crea la primera vez, aislada temporalmente en las vigas del pórtico y con caja de sección ajustada. Es una transacción corta.

## Lo que más puede fallar y ninguna prueba "normal" toca

1. **Pantalla chica o escala de Windows al 125–150 %:** 1440 × 900 no entra y se cortaría. Debe achicarse todo (decisión 6). Prueba manual en la tarea 5 (cambiar la escala de Windows) y prueba de `CalculadoraZoom` en la tarea 4.
2. **Un pórtico con muchas luces** (8 luces y 40 m): el alzado, la lista de tramos y la lista de barras deben seguir sin encimarse; el ajuste y el zoom resuelven el alzado. Prueba `UnPorticoDeOchoLuces_ArmaTodosSusTramosYZonas` en la tarea 2, y revisión visual con demo `?estado=largo` en la tarea 3.
3. **Un modelo sin pórticos, o con avisos de lectura** (`SinApoyos`, `SinEje`): se ve el estado "vacío" de la maqueta y no revienta. Prueba `SinPorticos_LaVistaVaVacia` en la tarea 2.
4. **El motor no tiene solución o falta una regla** (`SinSolucion`, `SinTraslapoMinimo`, `DiametroSinReglas`, `NoCaben`): el panel muestra el mensaje, "Generar en Revit" queda desactivado y no hay excepción. Prueba `UnPorticoSinSolucion_DaAvisoYNoGenera` en la tarea 2.
5. **Cerrar o cambiar de documento en Revit con la ventana abierta:** la ventana no puede usar un documento cerrado. Al cerrarse o activarse otro documento, se recarga la lista desde el documento activo, o queda vacía si no hay. Prueba manual en la tarea 5.

---

### Tarea 1: una ventana con WebView2 que abre en Revit y habla con C#

**Archivos:**
- Modificar:
  - `addins/Directory.Build.props`, para agregar `WebView2Version` por año (2026 → `1.0.2478.35`; 2027 → el mismo, hasta medirlo en el paso 6);
  - `addins/acero-vigas/CQEING.Vigas.csproj`, para agregar `PackageReference Microsoft.Web.WebView2` `Version="$(WebView2Version)"` con `ExcludeAssets="runtime;native"` y `PrivateAssets="all"`, y `Content Include="Tablero\web\**"` con `CopyToOutputDirectory=PreserveNewest` y destino `web\`;
  - `addins/acero-vigas/AppVigas.cs`, para cambiar el botón temporal por "Tablero" (`CQEING.Vigas.Tablero.ComandoTablero`, icono `iconoCQ`).
- Crear:
  - `addins/acero-vigas/Tablero/ComandoTablero.cs`;
  - `addins/acero-vigas/Tablero/VentanaTablero.xaml` y `.xaml.cs`;
  - `addins/acero-vigas/Tablero/web/index.html`, por ahora una página que responde "eco".
- Borrar: `addins/acero-vigas/Revit/ComandoDiagnosticoPorticos.cs` (decisión 9).

**Interfaces:**
- Produce:
  - `public sealed class ComandoTablero : IExternalCommand` (`TransactionMode.Manual`), que abre o activa la única `VentanaTablero`;
  - `internal sealed partial class VentanaTablero : Window`, con:
    - `public event Action<string>? MensajeRecibido`;
    - `public void Enviar(string json)`, que llama a `CoreWebView2.PostWebMessageAsJson`;
    - `public static string CarpetaWeb`, la carpeta `web` junto a la DLL;
    - `public static string CarpetaDatos`, `%LocalAppData%\CQEING\vigas\WebView2`.

- [ ] **Paso 1:** `ComandoTablero`, `VentanaTablero` (con dueño = la ventana principal de Revit, `WindowInteropHelper.Owner = UIApplication.MainWindowHandle`) y un `index.html` mínimo: al recibir `{tipo:"eco", texto}` responde `{tipo:"eco", texto}`.
  - Si `CoreWebView2Environment.GetAvailableBrowserVersionString()` lanza (no hay runtime), se muestra un `TaskDialog` con *"Falta Microsoft Edge WebView2 Runtime. Instálelo desde https://go.microsoft.com/fwlink/p/?LinkId=2124703 y vuelva a abrir el tablero."* y no se abre la ventana.
- [ ] **Paso 2:** `rtk dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2026` → compila sin advertencias nuevas. `bin\Debug\2026\` tiene `web\index.html` y **no** tiene `Microsoft.Web.WebView2.*.dll` ni `WebView2Loader.dll`.
- [ ] **Paso 3:** `powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026`, abrir Revit 2026 y pulsar CQEING › Vigas › Tablero.
  - Se espera: abre la ventana y el eco vuelve.
  - Anotar en "Hallazgos", con captura en `docs/planes/imagenes/tablero-83/`, la versión de WebView2 que reporta `CoreWebView2Environment.BrowserVersionString`, si cargó la DLL de Revit (`typeof(CoreWebView2).Assembly.Location`) y si se creó la carpeta de datos.
- [ ] **Paso 4:** cerrar la ventana y volver a pulsar Tablero (abre de nuevo); pulsarlo con la ventana abierta (la trae al frente, sin abrir otra); cerrar Revit con la ventana abierta (no queda Revit colgado).
- [ ] **Paso 5:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde. Commit `feat(vigas): ventana del tablero con WebView2 en Revit (#83)`.
- [ ] **Paso 6 (si hay un Revit 2027 a mano):** medir lo mismo del paso 3 y ajustar `WebView2Version` de 2027. Si no lo hay, anotarlo como pendiente en "Estado".

### Tarea 2: la vista que pinta la página, armada con el motor

**Archivos:**
- Crear:
  - `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Tablero/OpcionesTablero.cs`;
  - `.../Tablero/VistaTablero.cs`;
  - `.../Tablero/ArmadorVista.cs`;
  - `.../Tablero/ProtocoloTablero.cs`;
  - `.../Tablero/SesionTablero.cs`.
- Prueba:
  - `.../CQEING.Vigas.Calculo.Tests/ArmadorVistaTests.cs`;
  - `.../ProtocoloTableroTests.cs`;
  - `.../SesionTableroTests.cs`.

**Interfaces:**
- Consume:
  - `PorticoLeido`/`ResultadoLectura` (#81);
  - `MotorVigas.Calcular`, `MotorEstribos.Calcular`, `RepartidorFilas.Repartir`;
  - `Reglas`, `AlmacenReglas`, `ConfigEstribos`, `ArmadoBastones`, `CambiosManuales`.
- Produce (todo `public`, en el namespace `CQEING.Vigas.Calculo.Tablero`; medidas como dice la decisión 4):
  - `sealed record OpcionesTablero(TipoViga? Tipo, IReadOnlyList<GrupoBarras> Superior, IReadOnlyList<GrupoBarras> Inferior, double RecubrimientoCm, double? RecArribaCm, double? RecAbajoCm, double? RecLadosCm, double? SeparacionFilasCm, int MaximoFilas, Cara? Cara, ArmadoBastones Bastones, ConfigEstribos Estribos, IReadOnlyList<string> TramosMarcados, ConfigEstribos? EstribosMarcados, CambiosManuales? Cambios)`:
    - `static OpcionesTablero PorDefecto` es lo del `state` inicial de la maqueta: 3 #5 arriba y abajo, recubrimiento 4, filas pegadas (null), 3 filas, bastones por defecto y estribos `new ConfigEstribos()`;
    - `EstribosMarcados` se aplica a los `TramosMarcados` como `PorTramo`;
    - unidades: los recubrimientos y la separación de filas van en cm (como los escribe el paso 1); `ArmadoBastones` y `ConfigEstribos` van en mm, como en el motor. La página convierte al mandar: `'0.80'` m pasa a 800, `'#3'` a 3 y `'135°'` a `De135`.
  - `sealed record VistaTablero(string Proyecto, IReadOnlyList<PorticoEnLista> Porticos, VistaPortico? Actual, VistaReglas Reglas, bool EnVivo)`.
  - `sealed record PorticoEnLista(string Id, string Nombre, string Sub, string Tipo, IReadOnlyList<TramoEnLista> Tramos)`:
    - `Sub` como `"Eje 1 · A a D + voladizo"` o `"Cimentación · eje 1"`;
    - `Tipo` es `"Aérea"` o `"Cimentación"`.
  - `sealed record TramoEnLista(string Id, string Nombre, double Desde, double Hasta)`: `Id` = el nombre del tramo del motor (`"A-B"`, `"Voladizo D"`); `Nombre` = `"Viga A-B"` o `"Voladizo D"`.
  - `sealed record VistaPortico(...)`, con:
    - `Id`;
    - `Inicio` y `Fin` (m, las puntas del concreto);
    - `IReadOnlyList<ApoyoVista> Apoyos`, con `ApoyoVista(string Eje, double X, double CaraIzquierda, double CaraDerecha)`;
    - `IReadOnlyList<BarraVista> Barras`, con `BarraVista(string Id, string Capa, double Desde, double Hasta, bool GanchoInicio, bool GanchoFin, double Largo, string Recta, bool Manual, bool LlegaAlVoladizo, string Cantidad, double Kg)`, donde `Capa` es `"arriba"`/`"abajo"`, `Recta` es `"5.80 + gancho"` o `"recta"` y `Cantidad` es `"3 #5"`;
    - `IReadOnlyList<TraslapoVista> Traslapos`, con `TraslapoVista(string Id, string BarraIzquierda, string BarraDerecha, string Capa, double Desde, double Hasta, double Minimo, string Diametro, bool Manual)`;
    - `IReadOnlyList<BastonVista> Bastones`, con `BastonVista(string Id, double Eje, double Desde, double Hasta, bool GanchoInicio, bool GanchoFin, double Largo, string Donde, string Cantidad, double Kg, bool Cortado)`, donde `Donde` es `"Centrado en la columna B"` o `"Centro de la luz A-B"`;
    - `IReadOnlyList<ZonaEstribos> Zonas`, con `ZonaEstribos(double Desde, double Hasta, bool Junta)`, donde `Junta` = zona de columna (en la maqueta, dasharray `1 4.2`) y no `Junta` = centro (`1 9.4`);
    - `CaraVista Superior` e `Inferior`, con `CaraVista(int CabenPorFila, IReadOnlyList<int> PorFila, string Error, int Maximo)`, donde `Error` es `"Ninguno"`, `"MenosDeDosBarras"`, `"NoCaben"` o `"NoCabenEnElAlto"`;
    - `OpcionCaraVista Izquierda` y `Derecha`, con `OpcionCaraVista(double Kg, bool Existe)`;
    - `string CaraUsada` y `string CaraMasEconomica`, que valen `"izq"`/`"der"`;
    - `SeccionVista Seccion`, con `SeccionVista(double AnchoCm, double AltoCm, IReadOnlyList<BarraSeccion> Barras, IReadOnlyList<EstriboEnSeccion> Figuras)` y `BarraSeccion(string Id, string Nombre, bool Arriba, int Fila, double X, double Y, double D, bool Esquina)`, en cm, con la y desde arriba como la maqueta y `Id` `"a1"`/`"b3"`;
    - `IReadOnlyList<FilaEstribos> ListaEstribos`, con `FilaEstribos(string Id, string Tipo, int Cantidad, string Diametro, double Largo, string Detalle, double Kg)`;
    - `IReadOnlyList<AvisoVista> Avisos`, con `AvisoVista(string Tipo, string Elemento, string Texto)`;
    - `IReadOnlyList<(double Desde, double Hasta)> ZonasTraslapo`;
    - `double Kg` y `int Estribos`;
    - `bool SePuedeGenerar`.
  - `sealed record VistaReglas(IReadOnlyList<ReglaVista> PorDiametro, double Multiplo, double DistanciaLibreCm, bool Varilla12, string? Problema)`, con `ReglaVista(string D, string T, string G, string E, string K)` en m y kg/m con los decimales de `RULES0`.
  - `static class ArmadorVista`:
    - `VistaPortico Armar(PorticoLeido p, OpcionesTablero o, Reglas r)`;
    - `PorticoEnLista EnLista(PorticoLeido p)`;
    - `VistaReglas Reglas(ReglasCargadas c)`.
  - `abstract record MensajeTablero` y sus casos:
    - `Listo()`;
    - `ElegirPortico(string Id)`;
    - `Calcular(string Id, OpcionesTablero Opciones)`;
    - `SeleccionarEnRevit()`;
    - `VerEnRevit(string Id)`;
    - `Abrir3d(string Id)`;
    - `CambiarEnVivo(bool Activo)`;
    - `GuardarReglas(VistaReglas Reglas)`: llega con los textos de la pantalla (m y kg/m) y `ArmadorVista.ReglasDe(VistaReglas v, Reglas base)` lo pasa a `Reglas` en mm. Un texto que no es número deja el valor de `base` y hace fallar `Validar()`;
    - `Desconocido(string Tipo)`.
  - `static class ProtocoloTablero`:
    - `MensajeTablero Leer(string json)`: el `tipo` es el discriminador en camelCase (`"listo"`, `"elegirPortico"`…); un JSON roto da `Desconocido("")`;
    - `string Vista(VistaTablero v)`, que escribe `{"tipo":"vista","vista":…}`;
    - `string Aviso(string texto)`, que escribe `{"tipo":"aviso","texto":…}`;
    - JSON en camelCase y los enums como texto.
  - `sealed class SesionTablero(Func<ResultadoLectura> leer, AlmacenReglas almacen, string proyecto)`:
    - `IReadOnlyList<Salida> Manejar(MensajeTablero m)`, donde `abstract record Salida` es `AJs(string Json)` o `ARevit(AccionRevit Accion, IReadOnlyList<string> Vigas)`, con `enum AccionRevit { Seleccionar, Mostrar, Abrir3d, PedirSeleccion }`;
    - `void Recargar()`, que vuelve a leer el modelo y conserva las opciones por pórtico mientras su nombre exista;
    - `string? PorticoDeViga(string idViga)`, para "En vivo".

Datos de prueba: un `PorticoLeido` "Pórtico 1" del eje "1", aérea, con `Datos.PorticoMaqueta()`, tramos de 300 × 500 y vigas `["101","102","103","104"]`.

- [ ] **Paso 1: pruebas que fallan (`ArmadorVistaTests`):**
  - `LaListaNombraElPorticoComoLaMaqueta`: `Sub` = `"Eje 1 · A a D + voladizo"` y tramos `["Viga A-B","Viga B-C","Viga C-D","Voladizo D"]`, con `Desde`/`Hasta` en m (A-B de 0 a 4.40, como `TRAMOS` de la maqueta: de eje a eje, y el voladizo hasta la punta, 14.00).
  - `LasBarrasVanEnMetrosConSusGanchos`: la `BarraVista` `S1` tiene `Desde`/`Hasta` = los del motor / 1000, `GanchoInicio` true, `Cantidad` `"3 #5"` y `Kg` = el del motor.
  - `LasZonasDeEstribosSalenDelMotor`:
    - A-B da 3 zonas: (0.25, 0.95, junta), (0.95, 3.45, centro) y (3.45, 4.15, junta), redondeadas a cm desde las posiciones de `MotorEstribos`;
    - el voladizo D da (12.55, 13.25, junta) y (13.25, 13.95, centro).
  - `LaSeccionVaEnCmConLasEsquinasMarcadas`: con 5 #5 arriba, las x son 5.745, 10.3725, 15, 19.6275 y 24.255 cm. `a1` y `a5` tienen `Esquina` true. La y de la fila 1 de arriba es 5.745.
  - `LasDosOpcionesDeLaCaraTraenSusKilos`: `Izquierda.Kg` = `CaraInicio.Kg` y `Derecha.Kg` = `CaraFinal.Kg` del motor; `CaraMasEconomica` coincide con la del motor.
  - `UnTraslapoLargoDaElAvisoConSuTexto`: con `TipoAviso.TraslapoLargo`, `Causa VecinaDeFrontera` y una alternativa que lo evita y ahorra 7 kg, el texto es *"El traslapo I3–I4 queda en 1.40, 0.40 por encima del mínimo. I4 termina en el voladizo y solo puede crecer. Si el traslapo va al inicio de la luz no pasa, y además se ahorran 7 kg."* (el de la maqueta). Se arma con `Aviso` hecho a mano y `ArmadorVista.TextoAviso(Aviso, string capaDeLaCara)`, un `internal static string`.
  - `LaListaDeEstribosTraeElCerradoYSusKilos`: la fila `E` tiene 91 estribos de 1.468 m, `Detalle` `"zonas 0.80 cada 0.10 · centro cada 0.20"` y kg = el de `ResultadoEstribos` (#90).
  - `UnPorticoDeOchoLuces_ArmaTodosSusTramosYZonas`: 9 apoyos cada 5 m da 8 tramos en la lista, 8 × 3 zonas y ningún índice fuera de rango.
  - `UnPorticoSinSolucion_DaAvisoYNoGenera`: con `Reglas.Generales` sin traslapo del #5 (`PorDiametro[5] with { Traslapo = null }`) sale un aviso `SinTraslapoMinimo`, `SePuedeGenerar` false y ninguna excepción.
  - `LasReglasVanConLosDecimalesDeLaMaqueta`: `Reglas.Generales` da `#3`: T `"0.55"`, G `"0.166"`, E `"0.113"`, K `"0.560"`.
- [ ] **Paso 2:** `rtk dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests --filter ArmadorVistaTests` → FALLA porque no existe `ArmadorVista`.
- [ ] **Paso 3:** implementar `OpcionesTablero`, `VistaTablero` y `ArmadorVista`.
  - `Armar` arma `EntradaVigas` y `EntradaEstribos` desde `PorticoLeido` + `OpcionesTablero`:
    - recubrimientos en mm (cm × 10);
    - `SeparacionFilas` = `SeparacionFilasCm × 10` o 0;
    - la sección de `Tramos[0]`.
  - Las zonas salen de las posiciones de `EstriboEnPortico` "E" de cada tramo: la zona junto a la columna va hasta el último estribo a la separación de zona, y el centro es el resto.
- [ ] **Paso 4:** `--filter ArmadorVistaTests` → PASA.
- [ ] **Paso 5: pruebas que fallan (`ProtocoloTableroTests` y `SesionTableroTests`):**
  - `LeeCadaMensajeDeLaPagina`: un JSON por tipo da su record. `{"tipo":"calcular","id":"P1","opciones":{…}}` da `Calcular("P1", opciones)` con sus `GrupoBarras`.
  - `UnJsonRotoEsDesconocido`: `"{"` da `Desconocido("")` y no lanza.
  - `LaVistaSaleEnCamelCaseConLosEnumsComoTexto`: `Vista(v)` contiene `"tipo":"vista"`, `"porticos":` y `"caraUsada":"izq"`.
  - `ListoMandaLaVistaVaciaSiNoHayPorticos` (`SinPorticos_LaVistaVaVacia`): con `leer` → `ResultadoLectura([], [])`, `Manejar(Listo())` da un `AJs` cuya vista tiene `Porticos` vacío y `Actual` null.
  - `ListoAbreElPrimerPortico`: con un pórtico, `Actual.Id` = el suyo.
  - `CalcularGuardaLasOpcionesDelPortico`: después de `Calcular("Pórtico 1", o)` y `ElegirPortico` de otro y de vuelta, la vista usa `o`.
  - `ElegirPorticoConEnVivoLoSeleccionaEnRevit`: con `CambiarEnVivo(true)`, `ElegirPortico` también da `ARevit(Seleccionar, ["101","102","103","104"])`; con false, no.
  - `GuardarReglasEscribeElArchivoYRecalcula`: con un `AlmacenReglas` en una carpeta temporal, `GuardarReglas(r)` escribe `reglas.json` y la vista nueva trae `r`.
  - `GuardarReglasInvalidasAvisa`: con el múltiplo en `"0"` da `AJs` de aviso con el primer error de `Reglas.Validar()` (*"El múltiplo de corte debe ser mayor que 0."*) y no escribe.
  - `LasReglasDeLaPantallaPasanAMm` (en `ArmadorVistaTests`): `#5` con T `"0.95"` da `Traslapo` 950; múltiplo `"0.50"` da `Modulo` 500; separación `"2.5"` cm da `DistanciaLibre` 25; `Varilla12` true da `Varilla` 12000.
  - `RecargarConservaLasOpcionesPorNombre`.
- [ ] **Paso 6:** `--filter "ProtocoloTableroTests|SesionTableroTests"` → FALLA.
- [ ] **Paso 7:** implementar `ProtocoloTablero` (System.Text.Json con un conversor polimórfico por `tipo`) y `SesionTablero`.
- [ ] **Paso 8:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde. Commit `feat(vigas): la vista del tablero armada con el motor (#83)`.

### Tarea 3: la página, portada de la maqueta e idéntica al píxel

**Archivos:**
- Crear, en `addins/acero-vigas/Tablero/web/`:
  - `index.html`: el `<style>` del helmet de `Main` tal cual, más `fonts.css`, Preact, `alzado.js`, `tablero.js` y, solo fuera de Revit, `demo.js`;
  - `vendor/htm-preact-standalone.umd.js` (htm 3.1.1, `preact/standalone.umd.js` del paquete npm `htm@3.1.1`) y `vendor/LICENCIAS.txt` (MIT de Preact y htm);
  - `fonts/`: IBM Plex Sans 400, 500 y 600 e IBM Plex Mono 400 y 500, en woff2 (de `@fontsource/ibm-plex-sans@5.1.0` y `@fontsource/ibm-plex-mono@5.1.0`, solo `latin` y `latin-ext`), más `fonts.css` y `OFL.txt`;
  - `alzado.js`: `class Alzado extends Component`, el port de `Beam.dc.html`;
  - `tablero.js`: `class Tablero extends Component`, el port de `Main.dc.html`, más el puente con `window.chrome.webview`;
  - `demo.js`: `window.VISTA_DEMO(estado)`, que devuelve la `VistaTablero` con los números de la maqueta (decisión 8), más `?estado=largo` (8 luces) para la revisión del punto 2 de "Lo que más puede fallar".
- Crear: `tools/comparar-capturas.py`, que recibe `referencia.png actual.png salida-diferencia.png` y escribe el % de píxeles distintos (tolerancia 16 por canal) y una imagen con las diferencias en rojo.
- Capturas: `docs/planes/imagenes/tablero-83/referencia-<estado>.png` y `actual-<estado>.png`.

**Interfaces:**
- Consume: la forma JSON de `VistaTablero` (tarea 2, en camelCase) y los mensajes de `ProtocoloTablero` (`{tipo:"listo"}`, `{tipo:"calcular", id, opciones}`, etc.).
- Produce:
  - `Alzado` recibe las props de `<dc-import name="Beam">` de la maqueta (`w`, `h`, `sel`, `selLap`, `lapsManual`, `side`, `invert`, `warn`, `showStirrups`, `cutX`, `cutBad`, `rangeFrom`, `rangeTo`, `onPick`, `onPickLap`), más `portico` (la `VistaPortico`). `bastLen`, `bastDef`, `bastHook` y `bastOff` se quitan, porque los bastones ya vienen calculados.
  - `Tablero` recibe `vista` y `enviar(mensaje)`.

- [ ] **Paso 1: capturas de referencia.** En el navegador, abrir la maqueta (versión 19) en pantalla completa con cada tablero de la opción 1 al 100 %: Main, Reglas, Cimentación, Aviso, Varias y Vacío. Capturar cada uno a 1440 × 900 como `referencia-<estado>.png`.
  - Si el lienzo no deja 1:1, anotarlo en "Hallazgos" y comparar lado a lado midiendo con la herramienta de capturas (anchos de columnas, alturas de barras y tamaños de letra).
- [ ] **Paso 2:** portar `Beam.dc.html` a `alzado.js` con las reglas de la decisión 3.
  - Las listas fijas se cambian por bucles sobre `portico`:
    - `C`/`F` → `portico.barras` por capa, con la y alternando 52/58 (arriba) y 118/112 (abajo) según el índice par o impar, como la maqueta;
    - `mkLap` → `portico.traslapos`;
    - `mkB` → `portico.bastones` (arriba y = 70, abajo y = 100);
    - `st` → `portico.zonas`;
    - `g` → `portico.apoyos`.
  - `X(m) = 30 + 52·(m − portico.inicio)`. Con la maqueta `inicio` = −0.20, así que da lo mismo.
  - La etiqueta del voladizo pasa a ser `"voladizo " + f2(largo)`.
  - El texto de aviso (`warnText`) sale del `AvisoVista` del traslapo largo.
- [ ] **Paso 3:** portar `Main.dc.html` a `tablero.js` con la decisión 3.
  - Se borra lo que lista la decisión 5 y se lee de `props.vista`.
  - Cada `setState` que cambia algo de `OpcionesTablero` también llama a `enviar({tipo:"calcular", id, opciones})`, con un debounce de 150 ms en los campos de texto.
  - `opciones` se arma del `state` con la forma de `OpcionesTablero` (tarea 2), en una función `opcionesDe(state)`. Las filas `{n, d:'#5'}` pasan a `{cantidad, diametro: 5}`.
  - Un texto que no es número no se manda: se queda en el campo y el último cálculo válido sigue en pantalla, igual que el `num(x, d)` de la maqueta.
  - "Guardar y bloquear" manda `{tipo:"guardarReglas", reglas}` con la forma de `VistaReglas`.
  - Los botones de la decisión 7 llaman a `this.say("Esto llega con la #NN.")`.
  - `[Nombre del proyecto]` pasa a ser `vista.proyecto`.
- [ ] **Paso 4:** servir la carpeta y capturar los 6 estados con el mismo motor de WebView2, que es Chromium. Comandos:
  - `python -m http.server 8083 -d addins/acero-vigas/Tablero/web`;
  - por cada estado, `msedge --headless=new --hide-scrollbars --force-device-scale-factor=1 --window-size=1440,900 --screenshot=docs/planes/imagenes/tablero-83/actual-<estado>.png "http://localhost:8083/index.html?estado=<estado>"`.
- [ ] **Paso 5:** `python tools/comparar-capturas.py referencia-<estado>.png actual-<estado>.png diferencia-<estado>.png` por estado.
  - Se espera: menos de 1 % de píxeles distintos en cada uno, y en las imágenes de diferencia solo bordes de letras (suavizado), sin cajas ni textos movidos.
  - Si no se cumple, corregir el port, nunca la referencia. Anotar en "Hallazgos" el % de cada estado.
- [ ] **Paso 6:** revisar a ojo `?estado=largo`: el alzado entra con el ajuste, la lista de tramos tiene barra de desplazamiento y ningún texto se parte en dos líneas.
- [ ] **Paso 7:** commit `feat(vigas): la página del tablero, portada de la maqueta (#83)`, con las capturas.

### Tarea 4: puente entre la página, la sesión y Revit

**Archivos:**
- Crear:
  - `addins/acero-vigas/Tablero/PuenteTablero.cs`;
  - `addins/acero-vigas/Tablero/AccionesRevit.cs`;
  - `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Tablero/CalculadoraZoom.cs`.
- Modificar:
  - `VentanaTablero.xaml.cs`, para el zoom de la decisión 6 y para cargar `index.html` de verdad;
  - `ComandoTablero.cs`.
- Prueba: `.../CQEING.Vigas.Calculo.Tests/CalculadoraZoomTests.cs`.

**Interfaces:**
- Consume: `SesionTablero`, `ProtocoloTablero` y `Salida`/`AccionRevit` (tarea 2), y `VentanaTablero.MensajeRecibido`/`Enviar` (tarea 1).
- Produce:
  - `public static double CalculadoraZoom.Factor(double anchoDisponible, double altoDisponible)`, que devuelve `min(1, ancho/1440, alto/900)`, con unidades WPF (DIP);
  - `internal sealed class PuenteTablero`, que une `VentanaTablero` ↔ `SesionTablero`: los `AJs` se mandan con `Enviar` y los `ARevit` se encolan en `AccionesRevit`;
  - `internal sealed class AccionesRevit : IExternalEventHandler`, una cola de `(AccionRevit, vigas)` que corre en el hilo de Revit:
    - `Seleccionar`: `Selection.SetElementIds`;
    - `Mostrar`: `ShowElements`;
    - `Abrir3d`: decisión 11;
    - `PedirSeleccion`: `PickObjects` de vigas estructurales; luego `SesionTablero.PorticoDeViga` y `ElegirPortico`.

- [ ] **Paso 1: pruebas que fallan:**
  - `EnUnaPantallaGrandeNoSeAchica`: `Factor(1920, 1040)` = 1.
  - `EnUnaPantallaChicaSeAchicaParaEntrar`: `Factor(1280, 680)` = 680/900.
  - `Con150PorCientoEn1080p`: `Factor(1280, 690)` = 690/900. Una pantalla de 1920 × 1080 al 150 % da 1280 × 720 DIP, con el área de trabajo cerca de 690.
- [ ] **Paso 2:** `--filter CalculadoraZoomTests` → FALLA. Implementar. → PASA.
- [ ] **Paso 3:** `PuenteTablero` + `AccionesRevit`, y en `VentanaTablero`:
  - `ZoomFactor = CalculadoraZoom.Factor(ActualWidth, ActualHeight)` al cargar y al cambiar de tamaño;
  - tamaño inicial 1440 × 900 más el borde, recortado al área de trabajo.
- [ ] **Paso 4:** "En vivo":
  - suscribirse a `UIApplication.SelectionChanged`; si el pórtico de la viga elegida es otro, mandar `ElegirPortico`;
  - suscribirse a `ViewActivated` y `DocumentClosing` para `Recargar()` (punto 5 de "Lo que más puede fallar").
- [ ] **Paso 5:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde. Commit `feat(vigas): el tablero habla con Revit (#83)`.

### Tarea 5: prueba en Revit real y documentación

- [ ] **Paso 1:** desplegar (`tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026`) y probar en un modelo con:
  - un pórtico aéreo con voladizo;
  - uno de cimentación;
  - uno con la sección de 2 filas;
  - uno con un traslapo largo, para el estado de aviso.
- [ ] **Paso 2:** recorrer y anotar en "Estado", con captura en `docs/planes/imagenes/tablero-83/revit-*.png`:
  - la lista;
  - elegir pórtico y tramo;
  - "Ver: tramo";
  - pan y zoom;
  - pasos 1 a 4;
  - Reglas (desbloquear, cambiar, guardar y ver que recalcula);
  - las dos opciones de la cara;
  - la lista de barras;
  - En vivo en los dos sentidos;
  - Seleccionar en Revit;
  - Ver en Revit;
  - 3D;
  - los botones de #84, #85 y #91 con su aviso.
- [ ] **Paso 3:** con la escala de Windows al 125 % y al 150 %, y con la ventana achicada: ningún texto cortado ni partido en dos (criterio de la issue). Cerrar el documento con la ventana abierta y abrir otro (punto 5).
- [ ] **Paso 4:** en `Desktop\addin vigas\traslapos-vigas-notas.md`, agregar en §0 una línea: *"Tablero (#83): HTML de la maqueta dentro de Revit con WebView2; idéntico al píxel."*
- [ ] **Paso 5:** marcar los criterios de aceptación con su evidencia y hacer el commit `docs(vigas): tablero probado en Revit (#83)`.

## Cómo probarlo

```
rtk dotnet test CQEING-Addins.sln -c Release
rtk dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026
python -m http.server 8083 -d addins/acero-vigas/Tablero/web     # demo fuera de Revit: http://localhost:8083/index.html?estado=aviso
python tools/comparar-capturas.py <referencia.png> <actual.png> <diferencia.png>
```

## Estado

- [ ] Plan revisado con el usuario.
- [ ] Pasos implementados.
- [ ] Probado en Revit.
