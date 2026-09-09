# Plan: selector visual de columnas + sincronización de selección + fix de longitudinal continua

## Contexto

En la reunión del 9-sep (`Meeting Transcription (3).txt`), William (compartiendo pantalla) mostró tres problemas/pedidos sobre el add-in de armado de columnas, todos relacionados con cómo se identifica y selecciona una columna, y con qué tan "completa" queda la barra longitudinal generada:

1. **Selector visual**: hoy es difícil saber qué columna se está eligiendo porque solo hay una lista de texto ("Nota 9", "C8"...). Pidió ver las columnas como en un plano de planta real, con las rejillas (grids) y sus burbujas de nombre cuando el modelo las tenga, norte siempre hacia arriba, y que si no hay rejillas (o no hay vista de planta) igual se vea, solo sin ellas — no debe depender de que el modelo tenga eso.
2. **Selección en ambos sentidos** (00:32-02:39): si se selecciona una columna directamente en Revit (clic en la vista, no en el add-in), el add-in debe reflejarlo también — hoy Luciano confirma que solo funciona al revés (elegir en el add-in resalta en Revit).
3. **Longitudinal continua por columna física completa** (09:14-14:24): la barra longitudinal debe recorrer SIEMPRE toda la columna física de punta a punta (incluyendo anclaje en la zapata a 7.5cm), sin importar qué tramos estén tildados en la UI — los tramos tildados solo deberían afectar la configuración de ESTRIBOS. Al revisar el código real esto **no se cumple hoy**: es un bug confirmado, no una percepción.

Luciano confirmó que la división por tramos (lista con checkboxes, usada para configurar estribos por tramo) se mantiene igual — no se toca esa lógica ni su UX, solo lo de arriba.

Lo demás de la reunión (traslapo — Carlos explicará el criterio en otra sesión, sin fecha; "aplicar selección redundante" — William mismo lo difirió explícitamente: *"ahorita vemos bien, después vemos en el camino"*; el testeo con la barra #10 que no confinaba, transitorio) queda fuera: son puntos ya diferidos por los propios participantes o dependen de información que aún no existe (criterio de Carlos). La segunda mitad de la reunión (parte comercial/presupuestos de Juan Felipe y Carlos) es de otro proyecto, sin relación con este add-in.

## Hallazgos del código (confirmados por exploración, no supuestos)

### A. Selector visual — falta todo
- No hay posición X/Y de columna expuesta a la UI (`LectorContexto.cs` la calcula para agrupar "pilas" vía `CentroPlanta`/`ElementoColumna.Centro`, pero nunca sale de ahí; `ColumnaResumen`/`ColumnaContexto` no tienen ese campo).
- No hay ninguna lectura de `Grid`/rejillas en el repo.
- No hay manejo de Norte de Proyecto/Verdadero.
- Sí hay precedente de dibujo con `Canvas` reutilizable: `VentanaConfiguradorEstribos.RedibujarElevacion` (elevación esquemática con overlays clicables que llaman `IrA(indice)`) y `VentanaEstribosMenores.Dibujar` (sección transversal).
- El selector actual: `Vistas/VentanaConfiguradorEstribos.xaml`(`.cs`), columna 0 de la ventana = `TabControl` con dos `ListBox` ("Columnas principales"/"Columnetas"), lista plana de `NodoLista` (clase de presentación en el code-behind). Clic en fila llama `Activar(nodo)`.

Ubicación decidida con el usuario: un botón alterna, en el mismo espacio de columna 0, entre la lista actual y la nueva vista de planta.

### B. Selección Revit→addin — no existe, addin→Revit sí
- `VentanaConfiguradorEstribos` ya expone `ElementosActivados` y `SeleccionMarcadaCambio`, que `ComandoEstribos.cs` conecta a dos `IExternalEventHandler`/`ExternalEvent` que llaman `uidoc.Selection.SetElementIds(...)` (`Logica/ManejadorSeleccionElemento.cs`). Esto es addin→Revit, ya funciona (issue #24).
- No existe ningún listener de la selección nativa de Revit (`UIApplication`/`UIDocument` selection-changed, `Idling`, polling) en ningún lado del repo — Revit→addin es trabajo nuevo.
- La ventana es modeless (`_ventana.Show()` en `ComandoEstribos.cs`), lo cual es justo lo que hace viable escuchar cambios de selección en vivo mientras la ventana sigue abierta.
- No existe hoy una tabla inversa ElementId→fila; `_nodos` (la lista plana que ya arma toda la UI) sí tiene `IdElementoHost` por tramo — alcanza para construir la búsqueda inversa sin rehacer nada, pero el índice en sí no existe todavía.

### C. Longitudinal continua — bug real confirmado
- `LectorContexto` YA agrupa correctamente la columna física completa (nudos + tramo contra zapata + tramos libres) en una sola "pila", detectada automáticamente por proximidad en planta, sin depender de qué tramo seleccionó el usuario (`AgruparPilas`, `LectorContexto.cs`). Esta parte ya está bien.
- El anclaje de 7.5cm en zapata (issue #26) también existe y está bien separado del recubrimiento lateral: `ColumnaContexto.ZInferiorLongitudinal`, constante `AnclajeSobreElFondo = 0.075` en `LectorContexto.cs`.
- **El bug estaba en `Logica/GeneradorLongitudinal.cs`**: `AgruparPorHost` armaba el rango de host donde sí iba a crear barra a partir de `columnaPlan.Tramos`, que es el mismo arreglo que ya viene con `null` en los tramos NO tildados (construido en `VentanaConfiguradorEstribos.ConstruirPlanArmado`). Es decir, la configuración de estribo (por tramo, correcto) y el rango de la barra longitudinal (debería ser toda la columna) compartían la misma señal de "tildado o no".
  - `zFinTotal = porHost[^1].ZSup` usaba el tope del **último host tildado**, no `contexto.ZSuperior` (tope real de la columna) — si el tramo de arriba no estaba tildado, la barra no llegaba hasta ahí.
  - `CrearBarraConTraslapos` buscaba el host de cada segmento dentro de `porHost` (solo tildados) y si no lo encontraba hacía `continue` **sin aviso** — si el tramo contra zapata no estaba tildado, ese pedazo de barra (el que debería anclar a 7.5cm) simplemente no se creaba, en silencio.
  - **Ya corregido** (ver Estado abajo): `AgruparPorHost` ahora recorre todos los tramos físicos de `contexto.Tramos`, sin filtrar por tildado.

## Alcance de la solución

### 1. Fix: longitudinal siempre cubre la columna física completa (independiente de tramos tildados)

- En `GeneradorLongitudinal.cs`: separar la señal de "qué tramos existen físicamente en esta columna" (siempre todos, viene de `contexto.Tramos`, ya completo gracias a `LectorContexto`) de "qué tramos tiene configuración de estribo tildada" (`columnaPlan.Tramos`, sigue siendo solo para estribos).
- `AgruparPorHost` debe recorrer **todos** los tramos físicos de `contexto.Tramos` (no filtrar por `columnaPlan.Tramos[i] is null`), para construir el rango real de hosts de la barra longitudinal.
- `zFinTotal` debe ser `contexto.ZSuperior` (tope real de la columna), no el tope del último host con estribo tildado.
- `CrearBarraConTraslapos` debe poder anclar en el tramo contra zapata y llegar hasta el tramo superior aunque ninguno de esos dos tenga estribo tildado — el gate de "¿corre esta columna en esta corrida?" (al menos un tramo tildado) se mantiene igual, solo cambia CUÁNTO abarca la barra una vez que corre.
- El resultado esperado: tildar un solo tramo (aunque sea el del medio) para configurar estribos ahí sigue generando una longitudinal continua de punta a punta de la columna física, incluido el anclaje en zapata si existe.
- La configuración de estribos sigue exactamente igual (por tramo, tildado = se genera con esa config; no tildado = no se genera estribo ahí) — no se toca `GeneradorArmado.cs` para nada de esto.

### 2. Sincronización de selección Revit → add-in

- Nuevo mecanismo en `ComandoEstribos.cs` (mismo patrón ya usado ahí: modeless + `ExternalEvent`) que detecte cambios de selección nativa de Revit mientras la ventana está abierta — vía el evento de selección de `UIApplication`/`UIDocument` si está disponible en la versión de Revit del proyecto, o vía suscripción a `Idling` como respaldo si no.
- Nuevo método público en `VentanaConfiguradorEstribos` (ej. `SincronizarDesdeRevit(IReadOnlyList<string> idsSeleccionados)`) que `ComandoEstribos` invoca cuando detecta el cambio.
- Dentro de la ventana: construir un índice `ElementId string → NodoLista` a partir de `_nodos` (ya tiene `IdElementoHost` por fila) para resolver rápido qué fila activar; llamar al mismo `Activar(nodo)` que ya usa un clic en la lista o en la futura vista de planta — mismo camino, sin duplicar lógica de activación.
- Si la selección de Revit no corresponde a ninguna columna conocida por el add-in (otro tipo de elemento), no hacer nada (no hay fila que activar).

### 3. Selector visual: vista de planta con rejillas

- **Datos — posición de columna**: en `LectorContexto.cs`, reusar `CentroPlanta`/`ElementoColumna.Centro` (y `LadoX`/`LadoY`, ya usados para el bounding en planta) para poblar X/Y (metros, coordenadas de proyecto) en `ColumnaResumen` y/o `ColumnaContexto`. Mantener la limitación ya conocida de columnas rotadas (solo bounding box, igual que el resto del generador). Actualizar `IProveedorContexto`/`ProveedorContextoFalso` en paralelo con datos falsos razonables.
- **Datos — rejillas**: nuevo método en `LectorContexto` (o `Logica/LectorRejillas.cs`) que colecta `FilteredElementCollector(doc).OfClass(typeof(Grid))` y expone nombre + extremos de cada una (asumir rectas). Expuesto vía `IProveedorContexto.ListarRejillas()`, lista vacía como caso válido y esperado (sin rejillas en el modelo, o sin vista de planta que las muestre — el dibujo es propio del add-in vía `Canvas`, no depende de que exista una vista de planta de Revit).
- **Norte**: usar directamente las coordenadas de proyecto (X/Y), sin transformar por Norte Verdadero — igual que cualquier vista de planta por defecto de Revit. Documentado como simplificación deliberada.
- **Nuevo control** `Vistas/PanelPlantaColumnas.xaml`(`.cs`): `Canvas` con auto-fit (bounding box de columnas+rejillas → escala), rejillas como líneas punteadas + burbuja (círculo+texto) si existen, columnas como rectángulos en su posición real, columna activa resaltada (mismo criterio de color que `RedibujarElevacion`), overlay clicable por columna que llama `Activar(nodo)`.
- **Integración**: botón/toggle arriba del `TabControl` de columna 0 ("Ver en planta" / "Ver lista"); alternar no toca `Marcado`/`_configPorTramo` para nada, solo cambia cómo se elige la columna activa. La vista de planta se arma a partir de los mismos `_nodos`/`ListarColumnas()`, filtrando solo filas `EsColumna`.
- Este mismo panel de planta es también el punto natural donde resaltar la fila activada por la sincronización Revit→addin del punto 2 (mismo `Activar(nodo)`).

## Archivos a tocar

- `Logica/GeneradorLongitudinal.cs` — fix de rango de longitudinal (punto 1).
- `Logica/LectorContexto.cs` — exponer X/Y de columna, nuevo método de rejillas (punto 3).
- `Modelo/IProveedorContexto.cs`, `Modelo/ProveedorContextoFalso.cs` — nuevos campos/métodos en paralelo (mantener el desacople de Revit).
- `Modelo/ColumnaContexto.cs` (o `ColumnaResumen`) — nuevo campo de posición.
- `Logica/ManejadorSeleccionElemento.cs` o un manejador nuevo, + `ComandoEstribos.cs` — listener de selección nativa de Revit (punto 2).
- `Vistas/VentanaConfiguradorEstribos.xaml`(`.cs`) — método de sincronización desde Revit, botón toggle, integración del panel de planta.
- Nuevo: `Vistas/PanelPlantaColumnas.xaml` + `.xaml.cs`.

## Verificación

- **Longitudinal continua**: en un modelo real, tildar solo un tramo del medio de una columna con zapata debajo y nudo arriba, generar, y confirmar en Revit que la barra longitudinal llega de punta a punta (incluido el anclaje de 7.5cm en la zapata) aunque esos tramos extremos no estén tildados — y que los estribos solo aparecen donde sí se tildó, sin cambios ahí.
- **Sincronización Revit→addin**: con la ventana abierta, seleccionar una columna directamente en una vista de Revit (no en el add-in) y confirmar que la fila correspondiente se activa en la UI (lista o vista de planta).
- **Selector visual**: con rejillas presentes, confirmar que aparecen con su nombre y las columnas quedan ubicadas donde corresponde visualmente. En un modelo o escenario sin rejillas, confirmar que no rompe (solo faltan las líneas). Clic en una columna en la vista de planta activa la misma fila que un clic en la lista, y el toggle entre lista/planta no pierde los tramos ya tildados.
- Todo esto con el flujo estándar del proyecto: build, cerrar Revit, `tools/deploy.ps1 -Versions 2027 -TargetFramework net10.0-windows`, reabrir y probar.

## Estado

- [x] Punto 1 (fix longitudinal continua) — implementado en `Logica/GeneradorLongitudinal.cs`, compilado, desplegado. **Primer retest del usuario encontró dos problemas adicionales, ya corregidos también:**
  - La barra seguía sin entrar en la zapata: `Rebar.CreateFromCurves` recorta la geometría a los límites Z del host, y el host era siempre la columna, nunca la zapata — el tramo de anclaje (7.5cm dentro de la zapata) caía fuera del host de la columna y se perdía en silencio. Fix: `LectorContexto` ahora expone `ColumnaContexto.IdZapata` (el elemento de la zapata detectada), y `GeneradorLongitudinal` lo agrega como un host más en `porHost` (con su propio bounding box), para que ese tramo de barra se hostee en la zapata en vez de en la columna.
  - En la "columnita" de la base (más ancha que la columna típica, apoyada en la zapata), las barras salían corridas hacia los bordes de esa sección más ancha en vez de seguir derechas — porque la posición X/Y se recalculaba por cada host con SU PROPIO footprint. Fix: ahora la posición de cada barra (esquina o cara) se calcula UNA sola vez con el footprint del host de más arriba de la pila (la sección típica de diseño) y esa misma X/Y se reutiliza en todos los hosts que la barra atraviesa, incluida la zapata.
  - Desplegado, pendiente de nuevo retest del usuario.
- [x] Punto 2 (sincronización Revit→addin) — implementado: `ComandoEstribos.cs` se suscribe a `UIApplication.SelectionChanged` (evento nativo de Revit desde 2023) y llama a `VentanaConfiguradorEstribos.SincronizarDesdeRevit(ids)`, que busca la fila (`NodoLista`) correspondiente y la activa igual que un clic en la lista, sin volver a mandarle la selección a Revit (bandera `_sincronizandoDesdeRevit` corta el bucle). El lado addin→Revit (`ManejadorSeleccionElemento`) ahora expone `SincronizacionEnCurso` para que el listener nuevo ignore los cambios de selección que el propio addin generó. Compilado, desplegado. Pendiente de retest del usuario: seleccionar una columna directo en una vista de Revit con la ventana abierta y confirmar que se activa la fila correcta.
- [x] Punto 3 (selector visual con rejillas) — implementado:
  - `ColumnaResumen` ahora lleva `X`/`Y` (metros, coordenadas de proyecto) y `IProveedorContexto` tiene un nuevo método `ListarRejillas()`. `LectorContexto` los llena leyendo `Grid` de Revit (se descartan las curvas, solo rectas) y el centro en planta ya calculado para agrupar pilas. `ProveedorContextoFalso` tiene posiciones y dos rejillas inventadas para poder iterar sin Revit.
  - Nuevo control `Vistas/PanelPlantaColumnas.xaml`(`.cs`): un `Canvas` con auto-fit que dibuja las rejillas (línea punteada + burbuja con nombre) y las columnas (cuadrado en su posición real, resaltado si es la activa, clic activa la misma columna que un clic en la lista), con norte de proyecto siempre hacia arriba. No depende de que el modelo tenga rejillas ni de que exista una vista de planta de Revit — es un dibujo propio del add-in a partir de la geometría leída.
  - Integrado en `VentanaConfiguradorEstribos` con un botón "Ver en planta"/"Ver lista" que alterna, en el mismo espacio de la columna izquierda, entre la lista de tramos (sin tocar su lógica de tildado) y el panel de planta.
  - Compilado, desplegado. Pendiente de retest del usuario: abrir el configurador, tocar "Ver en planta", confirmar que las columnas aparecen ubicadas razonablemente y (si el modelo tiene rejillas) que se ven con su nombre; clic en una columna del panel debe activar la misma fila/elevación que un clic en la lista.

## Todo lo del plan está implementado y desplegado

Los 3 puntos fueron confirmados funcionando por el usuario. Ajuste adicional pedido tras el retest: en la vista de planta, un clic en una columna ahora la activa Y la tilda entera (todos sus tramos) de un solo gesto — mismo efecto combinado que tildar la fila en negrita de la lista — con indicador visual azul (tildada) además del verde (activa). Implementado en `PanelPlantaColumnas`/`VentanaConfiguradorEstribos.ActivarYAlternarMarcadoDesdePlanta`, compilado y desplegado.

Issues creados en GitHub (`utilidadcqeing-prog/Add-in-Acero-en-columnas-2027`) documentando este trabajo:
- [#38](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/38) — sincronización Revit→addin.
- [#39](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/39) — selector visual de planta.

Ver también `docs/planes/2026-09-08-reunion-traslapos-ganchos-normativa.md` (análisis de la reunión del 8-sep) para el issue nuevo sobre el gancho de anclaje faltante en la zapata (#37).

Pendiente: commitear y pushear los cambios (regla del proyecto: nunca commitear sin pedido explícito).
