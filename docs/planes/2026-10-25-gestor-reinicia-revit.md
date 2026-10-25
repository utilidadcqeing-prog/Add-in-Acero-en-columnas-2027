# Plan: el gestor reinicia Revit después de actualizar (continuación de #159)

## Contexto

- Pedido (2026-10-25, equipo del usuario, después de ver el aviso de #159): «que cuando actualizara y reiniciara el
  gestor, se cierre Revit y se vuelva a abrir ya actualizado».
- Revit no permite recargar un add-in con Revit abierto (plan `2026-10-22-gestor-en-revit-boton-y-aviso-159.md`). Lo
  que se automatiza es el **reinicio**: el gestor cierra Revit como si el usuario hiciera clic en la X y lo vuelve a
  abrir, con el modelo que estaba abierto.
- Vale para los dos casos (usuario, 2026-10-25): **actualizar un add-in** (también instalar o volver a una versión) y
  **actualizar el propio gestor** («Actualizar y reiniciar»), porque el gestor nuevo trae un componente nuevo para Revit.
- Fuera de alcance: cerrar Revit a la fuerza (nunca: se perdería el trabajo); guardar el modelo por el usuario; firmar
  los add-ins (pendiente aparte, ver hallazgo 3).
- Entorno: rama `gestor-reiniciar-revit`; Revit 2026 (.NET 8) y 2027 (.NET 10); gestor instalado 0.2.0.

## Criterios de aceptación

- [ ] Al terminar de instalar/actualizar un add-in en un año de Revit que está abierto, el gestor cierra ese Revit
      (Revit pregunta si guardar, como siempre) y, cuando termina de cerrarse, lo vuelve a abrir.
- [ ] Si el usuario cancela el cierre, Revit sigue abierto y el gestor lo dice; nunca se cierra a la fuerza.
- [ ] Se vuelve a abrir el modelo que estaba activo (si es un archivo normal en disco).
- [ ] «Actualizar todas» reinicia Revit una sola vez, al final.
- [ ] «Actualizar y reiniciar» del gestor cierra los Revit abiertos, actualiza el gestor y los vuelve a abrir.
- [ ] Probado en Revit real.

## Hallazgos del código y de Revit real

1. (2026-10-25, código) La app del gestor instala en `VistaGestor.InstalarAsync` (una tarjeta) y
   `ActualizarTodasAsync` (encadena varias). La autoactualización está en `ActualizadorVelopack.AplicarAsync`: descarga
   y llama `ApplyUpdatesAndRestart`, que cierra el gestor y abre el nuevo; el nuevo registra su componente en Revit al
   abrirse (`Program.Main` → `ComplementoEnRevit.Registrar`).
2. (2026-10-25, medido en el registro de Windows) `.rvt` → `Revit.Project` → `"C:\Program Files\Autodesk\Revit 2026\Revit.exe" /dde`:
   Windows abre los modelos por DDE, no pasando la ruta. Hay que medir si `Revit.exe "<modelo>.rvt"` abre el modelo.
3. (2026-10-25, medido con Revit 2026 lanzado como `Revit.exe "<copia de DynamoSampleModel.rvt>"`) Revit se detuvo en
   el diálogo de seguridad **«The publisher of this add-in could not be verified»** (`TaskDialog_Security_Unsigned_File_Loading`,
   botones Always Load / Load Once / Do Not Load, por defecto Do Not Load) antes de crear su ventana principal. Pasa
   con cada DLL nueva de un add-in sin firmar, o sea **con cada versión nueva que instala el gestor**. Consecuencia:
   después del reinicio automático el usuario tiene que responder ese diálogo (es de Revit y de seguridad: no se
   responde por él). Firmar los add-ins lo evitaría (ya estaba en pendientes a futuro). Esa medición en 2026 se
   detuvo ahí: se cerró el proceso (no había llegado a abrir el modelo).

## Decisión de prueba

- (2026-10-25, usuario: «yo uso Revit 2027… hazlo para los dos Revit, pero yo pruebo en el 2027») Se programa para 2026
  y 2027; las mediciones del paso 1 y la prueba completa se hacen en **Revit 2027**, con el usuario delante para
  responder el diálogo de seguridad. Si `Revit.exe "<modelo>"` no abriera el modelo, el gestor reabre solo Revit
  (se corrige antes de publicar).

## Decisiones

1. **Automático, sin botón extra** (2026-10-25, usuario: «así tal cual como me dijiste en el paso a paso»): al
   terminar de instalar en un año de Revit abierto, el gestor reinicia ese Revit. La protección del trabajo es la propia
   pregunta de Revit «¿Desea guardar?».
2. **Cerrar como con la X, nunca a la fuerza** (2026-10-25, Claude): `Process.CloseMainWindow()` (manda el cierre normal
   de Windows a la ventana principal). Se espera hasta **3 minutos** a que Revit termine (el usuario puede estar
   decidiendo si guarda). Si no se cierra (canceló, o Revit está ocupado en un comando), se deja abierto y el gestor
   avisa: «Revit 2027 sigue abierto. Reinícielo cuando pueda para usar la versión nueva». No se usa `Kill`.
3. **Solo el Revit del año actualizado** (2026-10-25, Claude). Se reconoce por la ruta del ejecutable
   (`...\Autodesk\Revit 2027\Revit.exe`). En la autoactualización del gestor, todos los Revit abiertos (el componente
   cambia en todos los años).
4. **Qué modelo reabrir** (2026-10-25, Claude): el componente de Revit anota en
   `%LOCALAPPDATA%\CQEING\Gestor\sesiones-revit\<id del proceso>.txt` la ruta del modelo activo cada vez que se activa
   una vista; la borra al cerrar ese modelo y al cerrar Revit. El gestor lee ese archivo **antes** de pedir el cierre.
   Solo se anota un archivo normal que existe en disco y **sin trabajo compartido** (abrir un modelo central por ruta
   puede ser un riesgo); con trabajo compartido, en la nube o sin guardar, se reabre solo Revit. Con un componente
   viejo (0.2.1 o anterior) no hay archivo: se reabre solo Revit.
5. **«Actualizar todas» reinicia una vez** (2026-10-25, Claude): instala todas y al final reinicia cada año tocado.
6. **Autoactualización del gestor** (2026-10-25, Claude): primero descarga (Revit sigue abierto mientras tanto), después
   cierra los Revit abiertos, anota cuáles reabrir en `%LOCALAPPDATA%\CQEING\Gestor\reabrir-revit.json` y aplica la
   actualización. El gestor nuevo, al abrirse, registra su componente y **después** reabre esos Revit y borra el archivo
   (se ignora si tiene más de 15 minutos). Si aplicar falla, el gestor viejo los reabre en el acto.
7. **La lógica en el núcleo, con pruebas** (2026-10-25, Claude): `ReinicioRevit` en `CQEING.Gestor.Core` con una
   interfaz `IProcesosRevit` (listar, pedir cierre, esperar, abrir) para probarla sin Revit; `ProcesosRevit` es la
   implementación real con `System.Diagnostics.Process`.

## Plan paso a paso

1. **Medir en Revit real** (antes de programar lo que depende de ello): que `Revit.exe "<modelo>"` abra el modelo y que
   `CloseMainWindow()` cierre Revit como la X — anotar en Hallazgos.
2. **Núcleo** — `ReinicioRevit.cs` (nuevo: `IProcesosRevit`, `ProcesosRevit`, `ReinicioRevit`, `RevitParaReabrir`),
   rutas nuevas en `RutasGestor.cs` — prueba: `ReinicioRevitTests` (solo ese año, con y sin modelo, sesión de otro
   proceso, no se cierra → sigue abierto y no se reabre, guardar/tomar pendientes, pendiente viejo se ignora).
3. **Componente** — `SesionRevit.cs` (nuevo) y `AppGestorRevit.cs` — prueba: compila 2026 y 2027; en Revit, el archivo
   aparece con la ruta del modelo y se borra al cerrar.
4. **App del gestor** — `VistaGestor.cs` (reiniciar tras instalar, una vez en «Actualizar todas», mensajes),
   `IActualizadorGestor.cs`/`ActualizadorVelopack.cs` (paso «antes de reiniciar» entre descargar y aplicar),
   `Program.cs` (reabrir pendientes después de registrar el componente) — prueba: `VistaGestorTests` con procesos falsos.
5. **Probar en Revit** con el gestor compilado en local (add-in) y luego con `gestor-v0.2.2` (autoactualización).

## Cómo probarlo

```powershell
dotnet test CQEING-Addins.sln
dotnet build addins/gestor-revit/GestorRevit.csproj -c Release -p:RevitVersion=2027
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin gestor-revit -Revit 2027 -Configuracion Release
```

1. Abrir Revit 2027 con un modelo; desde el gestor actualizar un add-in de 2027 → Revit pregunta si guardar, se cierra
   y se vuelve a abrir con el modelo (responder el diálogo de seguridad del add-in nuevo).
2. Repetir y elegir «Cancelar» en la pregunta de guardar → Revit sigue abierto y el gestor lo dice.
3. Con `gestor-v0.2.2` publicado: «Actualizar y reiniciar» con Revit abierto.

## Estado

- [x] Pedido confirmado con el usuario (automático, también para el gestor).
- ✅ Mediciones en Revit real (paso 1), hechas en la prueba completa en 2027 (ver abajo): `CloseMainWindow()` hace que
  Revit pregunte si guardar y se cierre como con la X; `Revit.exe "<modelo>.rvt"` abre el modelo (journal
  `journal.0037.txt`: `[Jrn.CommandLine] Rvt.Attr.ParentProcess: CQEING.Gestor.exe` y `"File Name" , "IDOK" , ...Snowdon Towers Sample Structural.rvt`).
- ✅ Núcleo: `ReinicioRevit`, `ProcesosRevit`, `SesionRevit`, rutas nuevas. 13 pruebas nuevas; núcleo 167/167.
- ✅ Componente: `ModeloActivo` anota el modelo activo; compila 2026 y 2027 sin advertencias.
- ✅ App: reinicio tras instalar, una vez en «Actualizar todas», cierre antes de autoactualizarse y reapertura en
  `Program.Main`. 6 pruebas nuevas; app 28/28.
- Hallazgo: la autoactualización la hace el gestor **viejo**. De 0.2.0/0.2.1 a 0.2.2 Revit no se cierra (ese gestor no
  sabe hacerlo); desde 0.2.2 en adelante sí. Para probar ese camino hace falta publicar después una 0.2.3.
- ✅ **Probado por el usuario en Revit 2027** (2026-10-25) con el gestor compilado en local
  (`gestor/CQEING.Gestor/bin/Release/net8.0-windows/CQEING.Gestor.exe`, que no registra componente porque no trae la
  carpeta `revit`) y el componente desplegado (`desplegar-local.ps1 -Addin gestor-revit -Revit 2027 -Configuracion Release`):
  - Revit 2027 abierto con `C:\Program Files\Autodesk\Revit 2027\Samples\Snowdon Towers Sample Structural.rvt` y un
    cambio sin guardar. El componente anotó `sesiones-revit\17856.txt` con esa ruta.
  - En el gestor, «Actualizar» en «Acero en vigas» (0.2.10 → 0.4.0): Revit preguntó si guardar, se cerró y se volvió a
    abrir solo con Snowdon Towers. El `.addin` de vigas apunta a `...\acero-vigas\0.4.0\2027\` y el journal muestra
    «Starting External Application: CQEING - Acero en vigas» desde esa carpeta. Salió el diálogo de seguridad del
    add-in nuevo (hallazgo 3).
  - El Revit nuevo (proceso 5628) anotó `5628.txt` con el mismo modelo y la anotación del proceso anterior se borró.
- ⏳ Sin probar todavía: cancelar en la pregunta de guardar (cubierto por pruebas), Revit 2026 (mismo código),
  «Actualizar todas» en Revit real, y la autoactualización del gestor cerrando Revit (necesita 0.2.2 instalado y luego
  una 0.2.3).
