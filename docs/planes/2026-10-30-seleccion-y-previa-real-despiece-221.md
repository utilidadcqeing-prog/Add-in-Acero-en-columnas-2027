# Plan: elegir la viga en Revit y ver la vista previa real (#221)

> **Para quien lo ejecute:** se sigue tarea por tarea, en orden. Usar superpowers:executing-plans. Commit y push solo si Juan lo pide (CLAUDE.md), sin firma de Claude.

**Objetivo:** en el add-in de despiece, elegir vigas reales con la selección de Revit, agrupar las que llevan el mismo acero y mostrar en los pasos 2 y 3 **una imagen de la vista real de Revit**, no un dibujo del add-in.

**Arquitectura:** el add-in crea las vistas en Revit, las exporta a PNG y deshace todo, sin dejar nada en el proyecto. La página muestra esas imágenes. El agrupamiento vive en un proyecto sin Revit, con pruebas.

**Tecnología:** C#, Revit 2026 y 2027, WebView2, xUnit, Preact + htm.

**Referencia:** #221 y la sección "Cómo se hace" de #217.

## Contexto

- **Qué se pide:** Juan, 30-oct: "vamos con la 221, que es la de seleccionar, y que me salga en las previews las vistas de Revit de verdad".
- **Amplía #221:** la vista previa real adelanta parte de #222, #223 y #225. Lo que se crea es lo probado a mano en `PROY_JULIO_BETO`; **no crea vistas definitivas**: eso sigue en #222.
- **Fuera de alcance:** columnas (#226), botón "Crear vistas" real (#222), plano (#229).
- **Depende de:** PR #231 unido a `main`. La rama sale de `main`, para no apilar PR.
- **Rama:** `despiece-seleccion-221`. **Modelo de prueba:** `PROY_JULIO_BETO`, viga 392699.

## Restricciones

- Nunca editar tipos existentes del proyecto; crear tipos propios sí.
- Las vistas salen limpias: nada dibujado que imite otra cosa (ver #217).
- La vista previa no deja rastro: ni vistas, ni tipos, ni cambios en el proyecto.
- Nada de API interna de Revit. Probar contra Revit real antes de programar (CLAUDE.md).

## Hallazgos

- `addins/acero-vigas/Tablero/BarraSeleccion.xaml.cs` y `PuenteTablero.EmpezarSeleccion` ya resuelven la barra propia: el tablero se aparta y se usa la selección normal de Revit.
- `AccionesRevit` encola el trabajo en el hilo de Revit con un `ExternalEvent`.
- `doc.ExportImage` funcionó el 30-oct con vistas ya confirmadas. **No está probado** dentro de una transacción sin confirmar.
- **Las imágenes salen con el fondo que tenga Revit:** en este equipo salieron oscuras.

**Probado en Revit el 30-oct, `PROY_JULIO_BETO`, planta N+3.70 (25 vigas), con el servidor MCP:**

- **Exportar una vista sin confirmar funciona:** duplicar la planta, recortarla y exportarla a 1600 px tarda unos 2 s.
- **Fondo blanco:** `Application.BackgroundColor` se puede cambiar solo durante la exportación y devolver después. Salió blanco y volvió a su color (34,41,51).
- **La imagen no coincide con el recorte:** los círculos de los ejes la agrandan. Ni el recorte ni el recorte de anotaciones dan el encuadre exacto (0.519 y 0.6155 contra 0.6457 y 0.6297 reales).
- **Calibración que sí funciona:** dibujar dos cruces magenta en puntos conocidos de la vista temporal, ubicarlas en el PNG y sacar de ahí la escala y el desplazamiento: 40.36 px/pie en X y 40.37 en Y. Las líneas de las 25 vigas cayeron sobre las vigas de la imagen. Evidencia: `o4.png` de la prueba.
- **La línea de ubicación va por un borde de la viga, no por el centro:** para hacer clic hay que usar la caja de la viga, no esa línea.
- **Pendiente de probar:** `RollBack` de verdad, que el servidor MCP no permite porque ya tiene su propia transacción. En esta prueba se borró la vista temporal a mano. Y borrar las cruces del PNG, reemplazando los píxeles magenta por blanco.

**Cierre de Revit al cerrar la ventana (2026-10-30, Juan: «parece que si hay un error se cierra Revit»):** la traza (`%AppData%\CQEING\despiece\traza.log`) dio la causa: `Invalid call to Revit API! Revit is currently not within an API context`, lanzada desde un evento de WPF. Al cerrar la ventana (la X), `Soltar` quitaba `DocumentClosing` y soltaba el `ExternalEvent` **sin proteger**. Eso corre fuera del contexto de la API, lanza, y una excepción sin atender en el hilo de Revit lo cierra («error irrecuperable»). Pasó a las 15:42 y a las 16:04. Corrección: `try/catch` en `Soltar` (como en `PuenteTablero`), y un `cerrado` para que una suscripción viva no haga nada. Regla: **nada de API de Revit desde eventos de la ventana**; todo pasa por `AccionesRevit`. Revit 2026.4, diario `journal.0360`: excepción `0xe0434352`.

**Composición del paso 2, probada en Revit el 30-oct (viga 392699):**

- `GetCenterlineCurves(..., i)` devuelve la misma posición para todas las barras del conjunto. La real sale de `GetShapeDrivenAccessor().GetBarPositionTransform(i)`.
- `TextNote.AddLeader(TNLT_STRAIGHT_L).End = punto` da la etiqueta con flecha `1 #5`, como la captura de Santiago.
- El tipo de detalle sin etiqueta (el realista) sale parado con **rotación 0**. Corrige lo anotado en #217, donde se probó con otro tipo y 90°.
- Con el recorte justo, la sección se ve grande. Falta confirmar la vista larga con filas sin encimar: una prueba por MCP se quedó sin respuesta, así que se comprueba en el add-in.

## Decisiones

1. **Vista previa = PNG de la vista real** (2026-10-30, Claude): no hay forma soportada de meter una vista de Revit dentro de una página.
2. **Sin clic en las barras del despiece** (2026-10-30, Juan confirmó): en los pasos 2 y 3 la imagen no sabe qué barra se tocó, y la lista de barras queda solo como información.
3. **~~El paso 1 usa la selección de Revit con la barra propia~~** → **El paso 1 muestra las plantas reales y se elige ahí** (2026-10-30, Juan: "en la preview grande se muestren las diferentes plantas y poder seleccionar las vigas o columnas", "la idea es poder moverse entre plantas"): la imagen grande es la planta de Revit, con un selector de plantas (N+0.00, N+3.70…). Encima va una capa invisible con la caja de cada viga, calibrada, para hacer clic y resaltar. Cada planta se genera la primera vez que se abre (unos 2 s) y queda guardada mientras la ventana esté abierta.
4. **"Columnas" pasa a "pronto" hasta #226** (2026-10-30, Claude): sus datos eran de ejemplo. La capa de clic debe servir también para columnas.
5. **Elementos iguales: los detecta el add-in y se eligen solos** (2026-10-30, Juan: "si elijo una y hay varias iguales, automáticamente esas se harán"): al elegir una, se resaltan todas las iguales, también las de otras plantas, y se dice cuántas son. Son iguales si coinciden sección, tipo de barra, forma, cantidad y largos, en orden a lo largo de la viga.
5b. **En verde lo que ya tiene despiece** (2026-10-30, Juan: "al generar su despiece, en su preview se pondrá con un color verde indicando que esa ya está"): en #221 el verde dura solo mientras la ventana está abierta, porque "Crear vistas" sigue simulado. Que quede guardado en el modelo (con una marca como `MarcaTerminado` de vigas) se hace en #222, al crear vistas de verdad.
6. **Un grupo es la viga continua de un eje** (2026-10-30, Claude): vigas colineales y seguidas, como `V Cim A-B-C`. El nombre sale de los ejes y de la sección.
7. **Sin columnas en la vista previa por ahora** (2026-10-30, Claude): sigue pendiente en #219 cómo mostrar la real y dejarla corta.

12. **Cambios frente al plan, al programar** (2026-10-30, Claude):
    - Se borran `despiece.js`, `en-revit.js` y `datos-ejemplo.js` (ya no hay dibujo simulado). Se quita "Ver en Revit": la imagen ya es de Revit.
    - No hay carpeta `Vigas/` todavía; `Revit/` tiene `LectorDespiece`, `GeneradorPlanta`, `GeneradorPrevia` y `ExportadorImagen`.
    - `CalibracionImagen` se agrega al proyecto sin Revit, con pruebas.
    - `ElementoLeido` lleva `LargoMm`, para reconocer una viga dibujada al revés.
    - Las etiquetas de la sección y de los estribos quedan pendientes para #224 y #225: en esta vista previa no se ponen, para que no choquen.
    - "Crear vistas" dice "Marcada en verde. Crear las vistas de verdad llega con #222", porque decir "Vistas creadas" sería falso.

## Tareas

### Tarea 0: comprobar en Revit

Hecha con el servidor MCP, salvo lo anotado como pendiente en Hallazgos. El `RollBack` se prueba en la tarea 2, ya dentro del add-in.

### Tarea 1: agrupar por acero, sin Revit

**Archivos nuevos:** `addins/despiece/calculo/CQEING.Despiece.Calculo/` (`BarraLeida.cs`, `ElementoLeido.cs`, `AgrupadorIguales.cs`) y `addins/despiece/calculo/CQEING.Despiece.Calculo.Tests/AgrupadorIgualesTests.cs`. Sumarlos a la `.sln`, y excluir `calculo\**` del `.csproj` del add-in, como en vigas.

**Interfaces:**
- `record BarraLeida(string Tipo, string Forma, int Cantidad, int LargoMm, int PosicionMm)`
- `record ElementoLeido(string Id, string Ejes, int AnchoMm, int AltoMm, IReadOnlyList<BarraLeida> Barras)`
- `record GrupoIguales(string Nombre, IReadOnlyList<ElementoLeido> Elementos)`
- `static IReadOnlyList<GrupoIguales> AgrupadorIguales.Agrupar(IEnumerable<ElementoLeido>)`

- [ ] Escribir las pruebas y verlas fallar:
  - `DosVigasConElMismoAceroSalenJuntas`
  - `UnLargoDistintoLasSepara`
  - `UnaSeccionDistintaLasSepara`
  - `ElOrdenDeLasBarrasEnLaListaNoImporta`
  - `SinBarrasNoFormaGrupoConOtraSinBarras`
  - `ElNombreLlevaEjesYSeccion`: `A-B-C` y 500 × 900 dan `V A-B-C - (0.50x0.90)`
- [ ] Programar y ver que pasan: `dotnet test addins/despiece/calculo/CQEING.Despiece.Calculo.Tests`.

### Tarea 2: las plantas reales, para elegir en ellas

**Archivos nuevos:** `Revit/LectorDespiece.cs`, `Revit/GeneradorPlanta.cs`, `Interfaz/PuenteDespiece.cs`, `Interfaz/AccionesRevit.cs`.

**Interfaces:**
- `LectorDespiece.Plantas(doc)`: las plantas estructurales, ordenadas por nivel.
- `LectorDespiece.Leer(doc)`: todas las vigas con su `ElementoLeido`, uniendo las colineales y seguidas.
- `PlantaGenerada GeneradorPlanta.Generar(doc, planta, carpeta)`: la ruta del PNG, su ancho y alto, y la caja de cada viga en píxeles.

- [ ] `GeneradorPlanta`: duplicar la planta, recortarla, dibujar las dos cruces, poner fondo blanco, exportar, devolver el fondo en un `finally` y deshacer con `RollBack`. Comprobar en Revit que no queda rastro y que Deshacer no muestra nada.
- [ ] Ubicar las cruces en el PNG y reemplazar los píxeles magenta por blanco. Si falta una cruz, la planta se muestra sin clic y con un aviso, sin error.
- [ ] Puente: mensajes `plantas`, `verPlanta` y `grupos`, con `ExternalEvent` como en vigas.
- [ ] Página, paso 1: selector de plantas, imagen grande, capa de clic, iguales resaltadas con su cuenta (también las de otras plantas), verde para las ya hechas y aviso si la viga no tiene acero.
- [ ] Probar en Revit: moverse entre las 4 plantas, elegir una viga con iguales en otra planta, una sin iguales y una sin acero.

### Tarea 3: la vista previa real

**Archivo nuevo:** `Revit/GeneradorPrevia.cs`.

**Interfaz:** `ResultadoPrevia Generar(Document doc, GrupoIguales grupo, string carpeta)`, que devuelve las rutas de los dos PNG o el motivo del fallo.

- [ ] Crear la vista longitudinal y la sección con los pasos 1 a 12 de #217, exportar y deshacer, según lo hallado en la tarea 0.
- [ ] Servir la carpeta temporal con un segundo host virtual, `previa.cqeing`, con la ruta distinta en cada generación para no ver una imagen vieja.
- [ ] Página, pasos 2 y 3: "Generando vista previa…", las dos imágenes, y el error si falla.
- [ ] Borrar los PNG al cerrar la ventana.

### Tarea 4: prueba final

- [ ] Compilar 2026 y 2027 Release, y `dotnet test CQEING-Addins.sln`, sin fallos ni advertencias.
- [ ] Desplegar con Revit cerrado y recorrer "Cómo probarlo".

## Foco de revisión

1. **Viga sin acero, o que no es de concreto:** aviso claro, sin error.
2. **Cambiar de documento, o cerrarlo con la ventana abierta:** no se cae Revit; la lista se vacía.
3. **Fallo a mitad de la vista previa:** el proyecto queda como estaba, y el fondo de Revit vuelve a su color.
4. **El proyecto ya tiene una vista con ese nombre:** no falla; como se deshace, se usa un nombre temporal único.
5. **Vigas en diagonal o en Y:** la vista sale bien orientada. Solo se probó en X.

## Cómo probarlo

```
dotnet build addins/despiece/CQEING.Despiece.csproj -c Debug -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin despiece -Revit 2026
dotnet test CQEING-Addins.sln
```

1. Abrir `PROY_JULIO_BETO` y pulsar **Despiece**: sale la planta real, con el selector de plantas.
2. Moverse entre plantas. La primera vez tarda unos 2 s; la segunda es inmediata.
3. Hacer clic en la viga 392699: se resaltan ella y sus iguales, también en otras plantas, y se dice cuántas son.
4. Paso 2: la imagen es de Revit y coincide con la vista creada a mano.
5. Paso 3: "Crear vistas" y volver al paso 1: esas vigas salen en verde.
6. Cerrar la ventana y comprobar que en el navegador no quedó ninguna vista nueva, y que Deshacer no muestra nada del add-in.
7. Probar una viga en Y, y una viga sin acero.

## Estado

- [x] Tarea 0 hecha en Revit, salvo el `RollBack` y borrar las cruces, que se prueban en la tarea 2.
- [ ] Plan revisado con Juan.
- [ ] PR #231 unido a `main`.
