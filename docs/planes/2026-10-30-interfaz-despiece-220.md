# Plan: add-in de despiece con la interfaz aprobada (#220)

> **Para quien lo ejecute:** se sigue tarea por tarea, en orden, con casillas `- [ ]`. Usar superpowers:subagent-driven-development o superpowers:executing-plans. Commit y push solo si Juan lo pide (CLAUDE.md), sin firma de Claude.

**Objetivo:** un add-in nuevo, `addins/despiece`, cuyo botón abre la interfaz **igual a la opción 2 "Paso a paso"** de la maqueta aprobada.

**Arquitectura:** igual que el tablero de vigas: ventana WPF con un WebView2 que sirve una página local (Preact + htm, sin compilar nada), a 1440 × 900 y con zoom para encajar. Los tres archivos de la maqueta se portan uno a uno a tres archivos JS.

**Tecnología:** C# (.NET de `addins/Directory.Build.props`), Revit 2026 y 2027, WebView2, Preact + htm (`vendor/htm-preact-standalone.umd.js`).

**Referencia:** https://claude.ai/artifact/PxNgLkS5sJ9zEiuq3PjKmC. Issue #220, épica #217.

## Contexto

- **Qué se pide:** Juan, 30-oct: "lo quiero igual que el artifact, que sea igual las vistas ya que esas fueron las aprobadas".
- **Fuera de alcance:**
  - Leer elementos reales del modelo (#221, #226).
  - Crear vistas en Revit (#222 a #225, #227, #228).
  - Dejar las vistas listas (#229).
  - Publicar una versión.
- **Entorno:** Revit 2026 (2027 no está instalado en este equipo). Rama `despiece-interfaz-220`, saliendo de `main`.

## Restricciones

- **Las vistas de la maqueta no se cambian:** mismos textos, colores, medidas, posiciones y comportamiento. Si algo no se puede igualar, se anota en Hallazgos y se le pregunta a Juan antes de cambiarlo.
- **Tamaño de página:** 1440 × 900, como la maqueta.
- **Colores de barra:** los de `ColoresDiametro`, que ya usa la maqueta.
- **Nada del add-in de vigas cambia.** La compilación de `CQEING.Vigas` debe seguir igual.
- **Nada de API interna de Revit** (CLAUDE.md).
- **Datos:** los de ejemplo de la maqueta, en un solo archivo, `datos-ejemplo.js`, para reemplazarlo en #221.

## Hallazgos del código

- `addins/acero-vigas/Tablero/VentanaTablero.xaml.cs` ya resuelve la ventana: WebView2 creado en código (en Revit 2027 falla desde el XAML), host virtual, encaje al monitor y zoom.
- `ComandoTablero.cs` ya resuelve el caso de una sola ventana y el aviso si falta WebView2.
- `CalculadoraZoom` y `Caja` viven en `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Tablero/CalculadoraZoom.cs` y no dependen de Revit.
- `addins/Directory.Build.props:35` compila `compartido\*.cs` en cada add-in: `Licencia`, `BloqueoPorVersion` y `CintaCqeing`.
- `CintaCqeing.CrearBoton` busca `Resources/<base>16.png` y `<base>32.png`.
- El publicador recorre `addins/*/addin.json`, así que el add-in nuevo entra solo a `compilar.yml`. Falta sumarlo a `CQEING-Addins.sln`.
- La maqueta tiene tres archivos: `Asistente.dc.html`, `Despiece.dc.html` y `EnRevit.dc.html`. Sus plantillas usan `<sc-for>` y `<sc-if>`, que en Preact son `map` y condicionales.
- **El `<text>` de SVG no se pintaba en la maqueta**, por eso sus textos son `<span>` encima del dibujo. Se porta igual.

## Decisiones

1. **Carpeta y nombres** (2026-10-30, Claude): `addins/despiece`, id `despiece`, ensamblado `CQEING.Despiece`, botón "Despiece" en un panel "Despiece". Es un solo add-in para todos los elementos.
2. **`CalculadoraZoom` se enlaza, no se copia** (2026-10-30, Claude): `<Compile Include="..\acero-vigas\calculo\CQEING.Vigas.Calculo\Tablero\CalculadoraZoom.cs" Link="Tablero\CalculadoraZoom.cs" />`. Así no hay código duplicado ni se toca vigas.
3. **Icono provisional** (2026-10-30, Claude): `iconoCQ16.png` e `iconoCQ32.png`, copiados de vigas.
4. **"Crear vistas" es simulado en esta issue** (2026-10-30, Claude): la pantalla es la aprobada, con el mensaje "Vistas creadas. Arrástralas a tu plano.", pero no crea nada en Revit. **Por eso no se publica versión hasta #222.**
5. **El paso 1 muestra el modelo simulado de la maqueta** (2026-10-30, Claude): se porta igual. En #221 hay que decidir con Juan cómo convive con la selección real en Revit, con la barra propia (Todas, Terminar, Cancelar).
6. **Carpetas por elemento** (2026-10-30, Claude): `web/` para la interfaz común; `Vigas/` y `Columnas/` se crean en #221 y #226, cuando tengan código. No se crean carpetas vacías.

7. **El botón "Cerrar" del paso 1 sí cierra** (2026-10-30, Claude, al implementar): en la maqueta no hacía nada. Se agrega un mensaje de la página a C#, porque un botón muerto en el add-in sería un error. Es el único cambio de comportamiento frente a la maqueta.
8. **Se porta desde la copia local de la maqueta** (2026-10-30, Claude, al implementar): los archivos de la versión 10 que se publicó. Son los mismos que los del artifact.
9. **`Pasos` acepta un estado inicial** (2026-10-30, Claude, al implementar): prop `inicial`, solo para comprobar cada paso con capturas sin tener que hacer clic. La interfaz no la usa.

10. **La página llena la ventana** (2026-10-30, Juan, probando en Revit: "no se está usando el 100% de la página, parece que fuera una ventana dentro de otra"): cambia la restricción de 1440 × 900 fijos. En ventanas más grandes, el paso 1 agranda el modelo, el 2 el dibujo y el 3 ocupa todo el ancho. En más chicas se sigue achicando con el zoom. Lo demás de las vistas no cambia.

11. **Los pasos 2 y 3 dejan de imitar a Revit** (2026-10-30, Juan, al ver el paso 3): se quitan la barra "Revit", la barra inferior y el navegador falso. El paso 3 muestra una vista previa y la lista de las dos vistas que se crearán. Las vistas reales aparecen en el navegador de Revit con #222. El modelo simulado del paso 1 se resuelve en #221, y "Ver en Revit" del paso 2 sigue simulado hasta entonces.

## Pruebas: lo que no se automatiza

Este repo no tiene pruebas para el JS de los tableros, y esta issue solo agrega pantalla, sin lógica de cálculo. La comprobación es compilar, abrir en Revit y comparar con la maqueta (memoria del proyecto: se prueba en Revit, no con la demostración en Chrome).

## Foco de revisión

Situaciones que la maqueta no muestra y un usuario sí encontrará:

1. **Pantalla de 1366 × 768:** todo se ve, sin scroll ni cortes, porque la página se achica entera.
2. **Pulsar el botón con la ventana abierta:** no abre otra; trae al frente la que está.
3. **Cerrar y volver a abrir a mitad de camino:** arranca limpio, en el paso 1.
4. **Falta WebView2 Runtime:** sale el aviso con el enlace, sin error de Revit.
5. **Sin licencia vigente o con versión nueva publicada:** no aparece el botón, como en vigas.

## Tareas

### Tarea 1: el add-in abre una ventana en Revit

**Archivos, todos nuevos en `addins/despiece/`:**
- `CQEING.Despiece.csproj`, `CQEING.Despiece.addin`, `addin.json`, `AppDespiece.cs`
- `Resources/iconoCQ16.png`, `Resources/iconoCQ32.png`
- `Interfaz/ComandoDespiece.cs`, `Interfaz/VentanaDespiece.xaml`, `Interfaz/VentanaDespiece.xaml.cs`
- `web/index.html`, `web/vendor/` y `web/fonts/` (copias de los de vigas)

**Modificar:** `CQEING-Addins.sln`, `README.md` (tabla de add-ins).

**Interfaces que produce:** `CQEING.Despiece.AppDespiece`; `CQEING.Despiece.Interfaz.ComandoDespiece`; la página carga desde `https://despiece.cqeing/index.html` y monta en `<div id="app">`.

- [ ] **Paso 1:** crear la rama: `git switch -c despiece-interfaz-220`.
- [ ] **Paso 2:** crear el `.csproj` como `CQEING.Vigas.csproj`, sin `calculo`, con el enlace de la decisión 2 y `web\**` copiado a `web\`.
- [ ] **Paso 3:** crear `addin.json`:
  ```json
  { "id": "despiece", "nombre": "Despiece", "descripcion": "Vistas de despiece de vigas y columnas, listas para llevar al plano.", "proyecto": "CQEING.Despiece.csproj", "revit": ["2026", "2027"] }
  ```
- [ ] **Paso 4:** crear el `.addin` como el de vigas, con `CQEING.Despiece.dll`, `CQEING.Despiece.AppDespiece` y ClientId `5c1f3e72-8a4d-4b6e-9d21-7f0c3ae6b814`.
- [ ] **Paso 5:** crear `AppDespiece` como `AppVigas`, con el panel "Despiece", el botón "Despiece", la ayuda "Abre el despiece: elige un elemento, revísalo y crea sus vistas." y el icono `iconoCQ`.
- [ ] **Paso 6:** crear `ComandoDespiece` y `VentanaDespiece` adaptando `ComandoTablero` y `VentanaTablero`:
  - Título "Despiece", host `despiece.cqeing`, datos en `%LOCALAPPDATA%\CQEING\despiece\WebView2`.
  - Sin `PuenteTablero` ni mensajes a C#, que aún no hacen falta. Quitar `MensajeRecibido`, `Enviar` y `Diagnostico`.
- [ ] **Paso 7:** crear `index.html` con el mismo `<style>` base de vigas, que incluye `html,body{overflow:hidden}`.
- [ ] **Paso 8:** compilar.
  ```
  dotnet build addins/despiece/CQEING.Despiece.csproj -c Debug -p:RevitVersion=2026
  ```
  Esperado: 0 errores.
- [ ] **Paso 9:** comprobar que vigas sigue compilando.
  ```
  dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2026
  ```
  Esperado: 0 errores.
- [ ] **Paso 10:** con Revit cerrado (comprobar con `Get-Process -Name Revit -ErrorAction SilentlyContinue`), desplegar.
  ```
  powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin despiece -Revit 2026
  ```
- [ ] **Paso 11:** abrir Revit. Esperado: panel "Despiece" en la pestaña CQEING; el botón abre una ventana de 1440 × 900 en blanco; pulsarlo otra vez la trae al frente (foco 2).

### Tarea 2: los dos dibujos

**Archivos nuevos:** `web/despiece.js` (de `Despiece.dc.html`), `web/en-revit.js` (de `EnRevit.dc.html`).

**Interfaces que produce:**
- `window.Despiece`, componente con props `{ tipo, el, w, h, sel, onPick, aplica, esc, conSeccion, conEtiquetas, conCotas, conColor }` y los valores por defecto de la maqueta (`esc` = `'1:50'`).
- `window.EnRevit`, componente con props `{ tipo, modo: 'elegir'|'previa'|'hoja', el, aplica, w, h, onEl, onTodas, onTerminar, onCancelar }`.

- [ ] **Paso 1:** bajar los archivos de la maqueta con la acción `read` de Artifact (`project/Despiece.dc.html`, `project/EnRevit.dc.html`).
- [ ] **Paso 2:** portar `Despiece`. El cuerpo de `renderVals()` se copia sin cambios; solo cambia la plantilla, que pasa a `html` de htm.
- [ ] **Paso 3:** portar `EnRevit` de la misma forma. En modo `hoja` usa `Despiece`.
- [ ] **Paso 4:** montar los dos a prueba en `index.html` y desplegar.
- [ ] **Paso 5:** comparar en Revit contra la maqueta: viga `AB`, viga `1`, columna `A2`, y `EnRevit` en sus tres modos. Esperado: iguales. Anotar diferencias en Hallazgos.

### Tarea 3: los tres pasos

**Archivos nuevos:** `web/datos-ejemplo.js`, `web/pasos.js` (de `Asistente.dc.html`).

**Interfaces:**
- Consume: `window.Despiece`, `window.EnRevit`.
- Produce: `window.DATOS_EJEMPLO = { elementos: { viga, columna }, filas: { viga, viga1, columna } }` y la pantalla completa montada en `#app`.

- [ ] **Paso 1:** pasar `DATA` y `FILAS` de `renderVals()` a `datos-ejemplo.js`, sin cambiar un valor.
- [ ] **Paso 2:** portar el resto de `Asistente` a `pasos.js`: estado `{ paso: 1, tipo: 'viga', el: 'AB', sel: null, conRevit: false, hecho: false }`, con los mismos textos, iconos y estilos.
- [ ] **Paso 3:** cargar en `index.html`, en este orden: vendor, `datos-ejemplo.js`, `despiece.js`, `en-revit.js`, `pasos.js`. Quitar el montaje de prueba.
- [ ] **Paso 4:** compilar y desplegar como en la tarea 1.
- [ ] **Paso 5:** recorrer en Revit la lista de "Cómo probarlo". Esperado: todo igual a la maqueta. Anotar el resultado en Estado.

## Cómo probarlo

```
dotnet build addins/despiece/CQEING.Despiece.csproj -c Debug -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin despiece -Revit 2026
```

En Revit 2026, con la maqueta abierta al lado para comparar:

1. **Paso 1:** tipos de elemento con Viguetas y Zapatas como "pronto" y desactivados; cambiar a Columnas cambia la lista; clic en el modelo o la lista resalta el elemento; Todas y Terminar avanzan.
2. **Paso 2:** el dibujo sale en la ventana estilo Revit; clic en una barra del dibujo o de la lista la resalta y atenúa el resto; "Ver en Revit" muestra y oculta la vista pequeña.
3. **Paso 3:** se ven las dos vistas en el navegador simulado; "Crear vistas" muestra el mensaje y cambia a "Despiezar otro", que vuelve al paso 1.
4. **Navegación:** los números de arriba y Atrás/Siguiente llevan al paso correcto.
5. **Foco de revisión:**
   - Probar a 1366 × 768 (1).
   - Doble pulsación del botón (2).
   - Cerrar y volver a abrir (3).
   - Los puntos 4 y 5 se comprueban leyendo el código: es el mismo de vigas.

## Criterios de aceptación

- [ ] El botón "Despiece" aparece en la pestaña CQEING y abre la interfaz.
- [ ] Los tres pasos se ven y se comportan igual que la opción 2 de la maqueta.
- [ ] Viguetas y zapatas aparecen como "pronto".
- [ ] `despiece` está en `CQEING-Addins.sln` y tiene `addin.json`.
- [ ] Sumar otro elemento no obliga a tocar vigas ni columnas: agregar una entrada a `tipos` y a los datos.
- [ ] `CQEING.Vigas` compila sin cambios.
- [ ] Juan da el visto bueno comparándolo con la maqueta.

## Estado

- [x] Plan revisado con Juan (30-oct).
- [x] Tareas 1 a 3 programadas, en la rama `despiece-interfaz-220`, sin commit. Compila sin errores ni advertencias, `CQEING.Vigas` también, y quedó desplegado en Revit 2026.
- [x] Capturas de Edge sin Revit (1440 × 900) de los pasos 1 a 3, viga `AB`, viga `1` y columna `A2`: la página pinta sin errores y coincide con el código de la maqueta.
- [ ] Probado en Revit: lista de "Cómo probarlo", pendiente de Juan.
- [ ] Revisión final de la rama, después de la prueba en Revit.

**Defectos que ya traía la maqueta y se portaron igual** (no se cambian sin el visto bueno de Juan, por la restricción de vistas aprobadas):

- Columna: el texto `N+0.00` queda encima del título de la vista.
- "Ver en Revit" en el paso 2: el aviso "Vista previa temporal" tapa casi todo el modelo pequeño.
- Paso 1: los círculos de los ejes caen encima de las vigas del modelo simulado.
