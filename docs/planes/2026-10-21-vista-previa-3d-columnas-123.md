# Plan: vista previa 3D del armado de columnas, con filtros (#123)


## Contexto

- **Qué se pide:** issue [#123](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/123).
  Ver en 3D, dentro de Revit, el armado de la columna que se está configurando: longitudinales,
  estribos, estribos menores, ganchos y traslapos, cada barra del color de su diámetro. Tiene que
  actualizarse solo con cada cambio de la ventana y no dejar nada en el modelo. Lleva los
  **filtros** que pidió Carlos (tipo A/B, diámetro, clase de pieza, solo ganchos, etc.).
- **Fuera de alcance:** ver varias columnas a la vez (solo se dibuja la columna activa de la
  ventana), tocar la vista previa 2D actual, cambiar cómo genera «Generar», y **tocar el add-in
  de vigas o `addins/compartido`** (decisión 2).
- **Entorno:** Revit 2027 / net10.0-windows. Rama `columnas/vista-previa-3d-123`, en el worktree
  `C:\Proyecto\addins-main`, creada desde `origin/main` (b960d59).

## Criterios de aceptación (del issue)

- [ ] Al cambiar diámetro, cantidad, espaciado o recubrimiento, el 3D se actualiza (meta: ~1 s; se mide en el paso 1).
- [ ] Lo dibujado coincide con lo que después crea «Generar».
- [ ] No crea armadura en el modelo ni deja entradas en el Ctrl+Z (la vista 3D propia sí se crea una vez, como en vigas).
- [ ] El dibujo desaparece al cerrar la ventana o cambiar de documento.
- [ ] Los filtros dejan ver solo tipo A, solo tipo B, uno o varios diámetros y cada clase de pieza por separado (incluido «estribos + estribos menores»), y se pueden combinar.
- [ ] «Solo ganchos» muestra únicamente los dobleces y las colas, en su posición real.
- [ ] Cambiar un filtro actualiza el 3D sin volver a generar.
- [ ] «Todo» vuelve a mostrar el armado completo.

## Hallazgos del código

1. **Cómo dibuja vigas** (`addins/acero-vigas/Revit/PreviaAcero3D.cs`): un servidor
   `IDirectContext3DServer` (API pública) que pinta tubos de 8 lados, coloreados por diámetro,
   solo en la vista "CQEING Vigas 3D". Arma los búferes en bloques de 60 000 vértices y no deja
   escapar excepciones de `RenderScene`. `AccionesRevit.Abrir3d` crea esa vista: sombreada,
   aislada en las vigas y sus apoyos, transparente, con caja de sección y zoom.
   `PuenteTablero.Atender` hace `Mostrar(...)` + `RefreshActiveView()` en cada cambio y
   `Quitar()` al cerrar o al cambiar de documento.
2. **Por qué en vigas sirve calcular a mano:** su «Generar» crea las barras desde un plan de
   cuentas puras (`PlanAcero`), y la vista previa dibuja ese mismo plan.
3. **Por qué en columnas no:** parte de la forma la decide Revit mientras genera.
   `GeneradorLongitudinal.MedirCorreccionDeGrapa` crea grapas de prueba para medir dónde cae el
   gancho, `GanchoLongitudinal` ajusta el gancho de 90° midiendo, y `BarraContinua` parte la
   varilla con empalmes nativos que corren la pieza (`GeneradorLongitudinal.cs:379-397`, 690-697).
   **Decisión 1.**
4. `addins/Directory.Build.props` compila **dentro de cada add-in** todo `addins/compartido/*.cs`.
   Por eso, cualquier cambio ahí llega también a vigas y a los demás add-ins (decisión 2).
5. La generación es `GeneradorArmado.Generar` + `GeneradorLongitudinal.Generar`, dentro de una
   transacción (`Logica/ManejadorGeneracionArmado.cs`). Las dos marcan todo lo que crean con
   `MarcaEstribos`: estribos, piezas longitudinales, grapas y cajas.
6. La ventana no puede referenciar Revit (también corre en modo simulado). Habla con Revit por
   eventos que `ComandoEstribos` engancha a un `ExternalEvent`, como `GenerarSolicitado`.
   `ConstruirPlanArmado()` (`VentanaConfiguradorEstribos.xaml.cs:1807`) arma el plan.
   `PedirVistaPrevia()` (`VentanaConfiguradorEstribos.VistaPrevia.cs`) ya junta todos los cambios
   en un solo aviso.
7. Hay cuentas puras reutilizables para clasificar: `SeccionPlanta.Calcular` (posición y orden en
   el anillo → A/B y esquina/cara), `RepartoEstribos.Calcular` (zonas) y
   `PlanTraslapos.Calcular` (dónde traslapa). `ColoresVarilla.ColorDe` da el color.
   `tools/PruebasVistaPrevia` es la consola de pruebas sin Revit.

## Decisiones

1. **Forma de las barras: «generar de prueba y deshacer»** (2026-10-21, Luciano). Se corre el
   «Generar» de siempre, solo para la columna activa, dentro de una transacción que se revierte.
   Antes de revertir se copia la forma real de cada barra (`Rebar.GetCenterlineCurves` +
   `GetBarPositionTransform`, API pública). Lo que se ve es exactamente lo que se generaría.
2. **El dibujo es el de vigas, copiado dentro de columnas; no se mueve a `compartido`**
   (2026-10-21, Luciano: "no quiero problemas con el otro ingeniero"). Vigas y `addins/compartido`
   no se tocan. Unificar las dos copias queda para más adelante, hablándolo con quien mantiene
   vigas. El issue #123 decía lo contrario: se le deja un comentario aclarándolo.
3. **Vista propia "CQEING Columnas 3D"**, como la de vigas: la columna con sus vigas y su zapata,
   transparente y con caja de sección. Además se **oculta la categoría de armadura** en esa vista,
   para que el acero ya generado no se mezcle con la vista previa (2026-10-21, Luciano: aprobado).
4. **Filtros en la ventana**, en la pestaña «Vista previa», junto al botón «Ver en 3D». Cambiar un
   filtro vuelve a pintar lo que ya se tenía, sin volver a generar.
5. **Tramos:** se usa la configuración que tiene cada tramo en la ventana, esté aceptado o no.
   Si un tramo nunca se configuró, va la configuración por defecto, la misma que usa
   `ConstruirPlanArmado` (2026-10-21, Luciano: aprobado).

6. **Se implementa todo de una; la prueba de velocidad del paso 1 se corre después** (2026-10-21,
   Luciano: "hazlo de una que tengo que mostrar, luego probamos lo de la velocidad y todo"). El
   comando de diagnóstico del paso 1 queda listo para esa medición. Si después resulta lento, se
   aplica lo de Riesgos.
7. **Detalles de los filtros, al implementar** (2026-10-21):
   - «Solo el tramo activo» recorta todo a la altura de ese tramo (las longitudinales pasan por
     varios tramos, así que se cortan, no se ocultan enteras).
   - Los filtros de tipo A/B y de posición solo aplican a las longitudinales; los de zona, solo a
     estribos y estribos menores. Una barra a la que un filtro no aplica no se oculta por él.
   - «Ganchos» y «traslapos» se eligen como modo: todo / solo ganchos / solo traslapos.
   - El panel va como un desplegable «Vista 3D en Revit» arriba de la pestaña «Vista previa».
   - A/B y posición se sacan de las propias barras simuladas: se agrupan las piezas por posición
     en planta y se recorren en el orden del anillo (esquina de abajo a la izquierda, sentido
     antihorario), igual que `GeneradorLongitudinal`. Las pares son A y las impares B.

## Plan paso a paso

1. **Prueba de velocidad (spike), antes que nada.**
   - Crear `Logica/SimuladorArmado.cs`: arma un `OpcionesArmado` con una columna, corre los dos
     generadores en una `Transaction`, junta los `Rebar` nuevos (los ids que no estaban antes),
     lee sus curvas por posición y hace `RollBack()`. Lleva un `IFailuresPreprocessor` que borra
     las advertencias, para que no salgan diálogos.
   - Probarlo con un botón temporal de diagnóstico (Debug, con el patrón `ComandoDiagnostico*`)
     sobre la columna seleccionada, con la configuración por defecto. Muestra: tiempo, cantidad
     de barras, que el Ctrl+Z no cambió y que no salieron diálogos.
   - Se prueba en Revit, con una columna de 1 piso y otra de varios pisos. **Si tarda más de ~3 s,
     se para y se decide con Luciano** antes de seguir (ver Riesgos). El resultado se anota en
     Hallazgos.
2. **Dibujo 3D propio de columnas: `addins/armado-columnas/Logica/Tubos3D.cs`.**
   - Copia adaptada de `addins/acero-vigas/Revit/PreviaAcero3D.cs`: malla de tubos de 8 lados,
     bloques de 60 000 vértices, `RenderScene` con try/catch, registro del servidor.
   - Lleva un **Guid propio**, distinto del de vigas, y su nombre: "CQEING Columnas: vista previa
     del armado". Solo pinta en la vista "CQEING Columnas 3D".
   - La entrada es `Tubo(IReadOnlyList<XYZ> Puntos, double Radio, ColorWithTransparency Color)`.
   - El comentario del archivo dice de dónde se copió, para unificar las dos copias más adelante.
   - Se prueba compilando columnas. En Revit, con un tubo de prueba en la vista 3D.
3. **Barras clasificadas, sin Revit: `Modelo/BarraPrevia.cs`.**
   - Cada barra lleva sus puntos (en metros), su diámetro y su clase: longitudinal, estribo o
     estribo menor. Las longitudinales llevan además grupo A/B y posición (esquina, cara X o
     cara Y). Todas llevan su tramo y su zona (confinada o no confinada).
   - Cada barra se parte en pedazos marcados como recto, gancho o traslapo.
   - `SimuladorArmado` clasifica así:
     - longitudinal = estilo `Standard` y vertical; su A/B y su posición salen de la posición más
       cercana de `SeccionPlanta.Calcular`;
     - estribo principal = estilo `StirrupTie` que rodea toda la sección;
     - el resto, estribo menor;
     - ganchos = las curvas que desaparecen al pedir `suppressHooks: true`;
     - traslapos = la parte donde se pisan dos piezas de la misma posición;
     - tramo y zona = por la altura (`RepartoEstribos`).
4. **Filtros, sin Revit: `Modelo/FiltroPrevia3D.cs`.**
   - Guarda qué está marcado en cada grupo: A/B, diámetros, clases, posiciones, tramo (todos o
     el activo) y zona, más «solo ganchos» y «solo traslapos». Dentro de un grupo las opciones se
     suman; entre grupos se cruzan.
   - `Aplicar(barras)` devuelve los pedazos a pintar. Atajos: «Todo», «Solo longitudinales»,
     «Solo estribos (con menores)», «Solo ganchos» y «Solo traslapos».
   - Se prueba con casos nuevos en `tools/PruebasVistaPrevia` (`dotnet run`).
5. **Manejador: `Logica/ManejadorVistaPrevia3D.cs`** (`IExternalEventHandler`).
   - Guarda el último pedido: plan de la columna, filtros y si hay que abrir la vista.
   - Si el plan cambió (se compara su JSON), simula de nuevo; si solo cambiaron los filtros,
     reutiliza las barras que ya tiene.
   - Filtra, convierte a tubos (color `ColoresVarilla.ColorDe`, radio = medio diámetro), llama a
     `Tubos3D.Mostrar` y refresca la vista.
   - «Abrir vista» crea o reutiliza "CQEING Columnas 3D", con el mismo código que `Abrir3d`
     (decisión 3).
   - Devuelve a la ventana un resumen: tiempo, barras y avisos.
6. **Ventana.**
   - Un `UserControl` nuevo, `Vistas/PanelFiltros3D`, dentro de la pestaña «Vista previa»: botón
     «Ver en 3D», casillas, atajos y una línea de estado.
   - Un evento `VistaPrevia3DSolicitada(OpcionesArmado, FiltroPrevia3D, bool abrirVista)`.
   - `ConstruirPlanDeColumnaActiva()` sale de `ConstruirPlanArmado` (decisión 5).
   - Una vez activado «Ver en 3D», `RecalcularVistaPrevia` también pide el 3D, y cada casilla lo
     pide con el mismo plan.
7. **Conexión en `ComandoEstribos`.**
   - `ExternalEvent` y callbacks contra `_ventana`, como en «Generar».
   - Al cerrar la ventana y en `DocumentClosing`: `Tubos3D.Quitar()` + refrescar.
   - Ningún manejador deja escapar excepciones.
8. **Medir contra Revit real** (regla del CLAUDE.md).
   - Generar de verdad una columna y comparar con la vista previa: misma cantidad de barras y la
     misma forma encima.
   - Medir el tiempo por cambio.
   - Quitar el botón temporal del paso 1, o dejarlo como `ComandoDiagnostico*` solo de Debug.

## Cómo probarlo

```
cd C:\Proyecto\addins-main
dotnet build addins/armado-columnas/practicacq.csproj -c Debug -p:RevitVersion=2027
dotnet run --project tools/PruebasVistaPrevia
Get-Process -Name Revit -ErrorAction SilentlyContinue   # tiene que estar cerrado
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027
```

En Revit:
1. Columnas → elegir una columna → «Ver en 3D»: se abre la vista y aparece el armado.
2. Cambiar diámetro, cantidad de barras, espaciado y recubrimiento: el 3D cambia (anotar el tiempo).
3. Probar cada filtro y sus combinaciones; «Todo» vuelve a todo.
4. Ctrl+Z: no hay entradas de la vista previa. Guardar y reabrir: no queda armadura de más.
5. Cerrar la ventana: el dibujo desaparece.
6. «Generar» esa columna y comparar con lo que mostraba la vista previa.
7. Con el tablero de vigas abierto a la vez: las dos vistas previas 3D no se mezclan (cada una
   pinta solo en su vista). `git diff --stat origin/main` no muestra cambios en `addins/acero-vigas`
   ni en `addins/compartido`.

## Riesgos

- **Velocidad:** generar una columna con traslapos y grapas puede tardar varios segundos, y Revit
  queda congelado mientras tanto. Por eso el paso 1 va primero. Si es lento, se puede generar solo
  al soltar el campo (no con cada tecla) o con un botón «Actualizar».
- **Diálogos de Revit** durante la prueba: se tapan con el `IFailuresPreprocessor`; se verifica
  en el paso 1.

## Estado

- [x] Plan revisado con Luciano (2026-10-21): aprobado, sin mover nada a `compartido`.
- [x] Copiar el plan a `docs/planes/` y comentar en el #123 que el dibujo va copiado en columnas.
- [x] Pasos 1 a 7 implementados (2026-10-21, sin commit todavía):
  - `Logica/SimuladorArmado.cs`: genera de prueba y revierte. Usa `GetTransformedCenterlineCurves`,
    que da las curvas ya en su posición final (documentación oficial), en lugar de
    `GetCenterlineCurves` + transformación.
  - `Logica/ClasificadorPrevia3D.cs`, `Modelo/BarraPrevia.cs`, `Modelo/FiltroPrevia3D.cs`,
    `Modelo/ResumenPrevia3D.cs`.
  - `Logica/Tubos3D.cs` (copia del dibujo de vigas, Guid `C3A1F0D2-…`) y `Logica/Vista3DColumna.cs`.
  - `Logica/ManejadorVistaPrevia3D.cs`, `Vistas/PanelFiltros3D.xaml(.cs)` y
    `Vistas/VentanaConfiguradorEstribos.Vista3D.cs`, conectados en `ComandoEstribos.cs`.
  - `ComandoDiagnosticoPrevia3D.cs` + botón «Diagnóstico vista 3D» (solo Debug), para el paso 1.
  - `ConfigPorDefecto` / `ConfigLongitudinalPorDefecto` de la ventana pasaron de `private` a
    `internal`, para que los use el diagnóstico.
  - `tools/PreviewUI/PreviewUI.csproj` suma los archivos nuevos de la ventana.
- [x] Compila: columnas Debug y Release para 2027, y Debug para 2026; `tools/PreviewUI` también.
- [x] `dotnet run --project tools/PruebasVistaPrevia`: 0 fallas, con 11 pruebas nuevas de
  filtros y recorte por altura.
- [x] `git diff --stat origin/main -- addins/acero-vigas addins/compartido`: vacío.
- [x] Desplegado en local (`desplegar-local.ps1 -Addin armado-columnas -Revit 2027`), con Revit
  cerrado y comprobado.
- [x] Puesta al día con `origin/main` (2026-10-23, 454540d): la rama estaba 296 commits atrás
  (#130, #131, #133, #145...). Conflictos resueltos en `VentanaConfiguradorEstribos.VistaPrevia.cs`
  (el pedido del 3D va al final de `RecalcularVistaPrevia`, que ahora usa `BarraPorNombre`) y en
  `tools/PruebasVistaPrevia/Program.cs` (quedan las pruebas de los dos lados). `SimuladorArmado`
  usa `BorrarArmadoAjeno = false`: desde #145 «Generar» siempre reemplaza lo que generó antes,
  y en la simulación ese borrado también se revierte. Compila 2027/2026 y PreviewUI; pruebas: 0 fallas.
- [ ] **#137: en la reunión del 21-oct el 3D mostró solo los estribos, sin longitudinales.**
  Revisado el código (2026-10-23): el plan que se simula trae la longitudinal, la transacción es
  la misma de «Generar», y ni el filtro ni el panel ni el manejador las descartan. Quedan dos
  hipótesis sin medir: (a) `GeneradorLongitudinal` no crea nada dentro de la simulación y deja un
  aviso (el panel solo dice «N aviso(s)», no cuáles); (b) se crean pero se pierden al leerlas:
  si todas las curvas de una barra quedan marcadas como gancho, el clasificador la descarta.
  No había registro en `%TEMP%`. El «Diagnóstico vista 3D» ahora también dice cuántas barras
  tienen todo como gancho y cuántas quedan de cada clase, para saber cuál de las dos es.
- [ ] **Probado en Revit.** Pendiente, en este orden:
  1. «Diagnóstico vista 3D» sobre una columna de 1 piso y otra de varios: tiempo, que la cantidad
     de Rebar no cambie, sin diálogos y sin entrada en el Ctrl+Z.
  2. Que las curvas de gancho se reconozcan bien: «Solo ganchos» tiene que mostrar solo dobleces
     y colas, no barras enteras (supuesto sin medir: con `suppressHooks` las demás curvas no
     cambian).
  3. Que A/B y la posición coincidan con la pestaña «Traslapos» (supuesto sin medir: el orden
     antihorario desde la esquina de abajo a la izquierda es el del generador).
  4. La lista de «Cómo probarlo».
