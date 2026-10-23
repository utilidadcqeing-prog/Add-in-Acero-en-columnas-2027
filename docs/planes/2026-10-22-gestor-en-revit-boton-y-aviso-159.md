# Plan: botón y aviso del gestor dentro de Revit (#159)

## Contexto

- Pedido del jefe (2026-10-21): que el gestor pudiera actualizar los add-ins «dentro de Revit, sin cerrarlo».
- **Lo que Revit no permite:** un add-in se carga una sola vez, al abrir Revit, y Revit no tiene forma oficial de
  descargarlo y volver a cargarlo con Revit abierto. Las herramientas que lo fuerzan sirven solo para comandos
  sencillos, no para add-ins con cinta propia, eventos y ventanas fijas como los nuestros, y pueden cerrar Revit
  de golpe. No se hace. El gestor ya actualiza con Revit abierto (cada versión va a su carpeta); la versión nueva se
  ve al reiniciar Revit.
- Lo que sí se puede, y es lo que se hace aquí: que el usuario **se entere dentro de Revit** de que hay una
  versión nueva y **llegue al gestor en un clic**, sin buscarlo en el menú Inicio.
- Fuera de alcance: recargar add-ins con Revit abierto; cambiar los 4 add-ins existentes; instalar desde Revit.
- Entorno: monorepo, rama `gestor-revit-boton-159`; Revit 2026 (.NET 8) y 2027 (.NET 10); gestor instalado con
  Velopack en `%LOCALAPPDATA%\CQEING.Gestor`.

## Criterios de aceptación

Copiados de la issue #159:

- [ ] Día 1: componente `addins/gestor-revit` con el panel «Gestor» y su botón; el gestor lo registra en cada
      Revit instalado; `publicar-gestor.yml` lo compila para 2026 y 2027; probado en los dos Revit.
- [ ] Día 2: aviso discreto dentro de Revit cuando hay versiones nuevas de los add-ins instalados; el botón
      muestra que hay novedades; probado en los dos Revit.

## Hallazgos del código

1. (2026-10-22) La cinta ya tiene una pieza compartida, `addins/compartido/CintaCqeing.cs`, que se compila dentro de
   cada proyecto de `addins/` y crea o reutiliza la pestaña CQEING y cada panel. El componente nuevo la reutiliza.
2. (2026-10-22) `addins/Directory.Build.props` decide framework y API de Revit por año (`RevitVersion=2026|2027`).
   Poner el componente en `addins/gestor-revit/` le da esas reglas sin repetirlas.
3. (2026-10-22) `publicar.yml` publica solo por etiqueta `<id>-vX.Y.Z`, y el gestor muestra solo lo que tiene
   release. El componente no tendrá etiqueta propia: no puede aparecer como tarjeta en el gestor.
4. (2026-10-22) `compilar.yml` compila cada `addins/*/addin.json` para cada año de Revit: con su `addin.json` el
   componente queda compilado en CI para 2026 y 2027 sin tocar ese workflow.
5. (2026-10-22) `LectorEstado.Leer` solo cuenta un `.addin` como instalación del gestor si su DLL está bajo
   `%LOCALAPPDATA%\CQEING\Addins\<id>\<versión>\<año>\`. El componente vive en otra carpeta, así que nunca se
   confunde con un add-in instalado.
6. (2026-10-22) El gestor instalado con Velopack deja `Update.exe` en `%LOCALAPPDATA%\CQEING.Gestor\` (ruta
   estable, fija por el `packId`). Velopack lo documenta como forma de abrir la versión actual
   (`Update.exe --processStart CQEING.Gestor.exe`).

7. (2026-10-22, medido en los journals de Revit 2026 y 2027) **Revit carga los `.addin` en orden alfabético por
   nombre de archivo, y cada add-in agrega su panel al final de la pestaña CQEING cuando carga.** Orden real de
   arranque en Revit 2027: `CQEING.acero-vigas` → `CQEING.armado-columnas` (practicacq) → `CQEING.cuadro-cantidades`
   → `CQEING.gestor-revit` → `CQEING.imagenes`. Por eso el panel «Gestor» quedaba cuarto, entre los de los add-ins
   (usuario: «se confunde con los add-ins y no es la idea»).

## Decisiones

1. **Componente aparte, no un add-in más** (2026-10-22, usuario: «que no se confunda con los add-ins»): carpeta
   `addins/gestor-revit`, panel propio **«Gestor»**, `.addin` propio `CQEING.zz-gestor-revit.addin`, sin tarjeta en el
   gestor y sin tocar los add-ins existentes.
2. **Lo instala el gestor, no el usuario** (2026-10-22, Claude): al abrirse, el gestor copia el componente a
   `%LOCALAPPDATA%\CQEING\Gestor\revit\<versión del gestor>\<año>\` y escribe
   `%APPDATA%\Autodesk\Revit\Addins\<año>\CQEING.zz-gestor-revit.addin` para cada Revit instalado. Una carpeta por
   versión del gestor, como los add-ins: con Revit abierto no se pisa una DLL en uso.
3. **El componente viaja dentro del paquete del gestor** (2026-10-22, Claude): `publicar-gestor.yml` lo compila para
   2026 y 2027 y lo deja en `publish/revit/<año>/` antes de `vpk pack`. Un solo archivo para el cliente.
4. ~~El botón abre el gestor con `Update.exe --processStart`~~ **El botón abre `%LOCALAPPDATA%\CQEING.Gestor\current\CQEING.Gestor.exe`**
   (2026-10-22, Claude, medido): el acceso directo del menú Inicio que crea Velopack apunta exactamente ahí, y la
   ruta es estable aunque el gestor se actualice. Es lo que hace el propio instalador, sin depender de los
   argumentos de `Update.exe`. Si no está, el botón dice dónde descargarlo (no falla en silencio).
7. **Una sola ventana del gestor** (2026-10-22, Claude): con el botón en Revit es fácil abrirlo dos veces. El gestor
   usa un `Mutex` por usuario: una segunda ejecución trae al frente la ventana que ya está abierta y se cierra.
8. **Al desinstalar el gestor se quita el componente** (2026-10-22, Claude): hook de Velopack antes de desinstalar;
   borra `CQEING.zz-gestor-revit.addin` de cada año y la carpeta `Gestor\revit`. Si Revit está abierto y no puede
   borrarla, queda inofensiva (el botón avisa que el gestor no está instalado).
5. **Dos días** (2026-10-22, Claude, usuario: «no sé si lo dividimos en dos días o todo hoy»): el botón (día 1) da
   valor solo y obliga a publicar `gestor-v0.2.0`, que también prueba la autoactualización real; el aviso (día 2)
   depende de que el componente ya esté instalado en Revit. Probar en Revit con 8 GB de RAM es lento: dos
   publicaciones cortas son más seguras que una larga.
6. **Reglas del aviso (día 2)** (2026-10-22, Claude, pendientes de confirmar con el usuario): una consulta por
   apertura de Revit; aviso pequeño, sin bloquear el trabajo, que se cierra solo; solo avisa de add-ins que el
   usuario ya tiene instalados en ese Revit; el botón cambia a «Gestor de add-ins (N)».

9. **El panel «Gestor» va al final, a la derecha de todo** (2026-10-22, usuario): el archivo del componente se llama
   `CQEING.zz-gestor-revit.addin` y su id es `zz-gestor-revit`, para que Revit lo cargue el último (hallazgo 7). Es lo
   soportado: no usa nada interno de Revit (descartado reordenar paneles con `AdWindows`, que no es API pública).
   Lo vigila una prueba que compara con el id de cada `addins/*/addin.json`: si algún add-in nuevo ordenara después
   de «zz-», la prueba falla. `tools/desplegar-local.ps1` ahora escribe `CQEING.<id de addin.json>.addin` (antes usaba el
   nombre de la carpeta; coinciden en los add-ins). Sustituye el nombre `CQEING.gestor-revit.addin` de la primera
   versión del día 1, que nunca se publicó.

## Plan paso a paso (día 1)

1. **Núcleo** — `gestor/CQEING.Gestor.Core/RegistroComplementoRevit.cs`, ajuste en `RutasGestor.cs` e
   `Instalador.cs` (compartir la lectura y activación del `.addin`) — prueba: pruebas nuevas en
   `CQEING.Gestor.Core.Tests` (copia por año, `.addin` con ruta absoluta, versión nueva, limpieza, año sin paquete).
2. **Componente** — `addins/gestor-revit/` (`GestorRevit.csproj`, `AppGestorRevit.cs`, `ComandoAbrirGestor.cs`,
   `CQEING.zz-gestor-revit.addin`, `addin.json`, `Resources/`) — prueba: `dotnet build` para 2026 y 2027.
3. **App del gestor** — `Program.cs`/`VistaGestor.cs`: registrar el componente al abrir, en segundo plano, sin
   avisar si falla (se reintenta al abrir otra vez) — prueba: pruebas de la app + ejecución real.
4. **Publicación** — `publicar-gestor.yml`: compilar el componente para cada año hacia `publish/revit/<año>/` —
   prueba: la misma secuencia en local y revisión del paquete.
5. **Probar en Revit** — `tools/desplegar-local.ps1 -Addin gestor-revit` y luego el camino real
   (`gestor-v0.2.0` desde «Actualizar y reiniciar»).

## Cómo probarlo

```powershell
dotnet build addins/gestor-revit/GestorRevit.csproj -c Release -p:RevitVersion=2026
dotnet build addins/gestor-revit/GestorRevit.csproj -c Release -p:RevitVersion=2027
dotnet test CQEING-Addins.sln
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin gestor-revit -Revit 2026
```

## Estado

- [x] Plan revisado con el usuario (2026-10-22: botón y aviso, que no se confunda con los add-ins; «dividirlo en dos
      días, una parte hoy y otra mañana»).

### Día 1 (2026-10-22): el botón

Hecho y probado en este equipo (sin Revit abierto):

- ✅ `RegistroComplementoRevit` en el núcleo: 11 pruebas nuevas (varios años, solo los años instalados, versión nueva,
  conserva 2 versiones, DLL abierta por Revit, `.addin` borrado, paquete dañado, versión inválida, desinstalar).
  Núcleo 145/145; app 22/22; publicador 8/8; vigas 638/638.
- ✅ Componente `addins/gestor-revit` compila para 2026 y 2027 sin advertencias (dll, deps, 2 iconos y `.addin`).
- ✅ Una sola ventana del gestor (`Mutex`): abrirlo dos veces deja una sola, probado con el ejecutable real.
- ✅ Paquete como lo arma el workflow (`dotnet publish` + componente por año en `publish/revit/<año>`): correcto.
- ✅ Gestor recién compilado abierto en este equipo: registró `CQEING.zz-gestor-revit.addin` en Revit 2026 y 2027
  apuntando a `%LOCALAPPDATA%\CQEING\Gestor\revit\0.2.0\<año>\`; no apareció `registro-revit.log`; los demás
  `.addin` de CQEING quedaron igual.
- ✅ Probado por el usuario en Revit 2027 y 2026: el panel «Gestor» aparece y el botón abre el gestor (PR #160 unido).
- ✅ Panel al final (decisión 9): 146/146 en el núcleo, con la prueba nueva del orden. El gestor recién compilado
  registró `CQEING.zz-gestor-revit.addin` en 2026 y 2027 (borrado antes el registro de prueba con el nombre anterior).
- ⏳ Sin probar todavía: el panel a la derecha de todo en Revit real (usuario), `gestor-v0.2.0`, `gestor-v0.2.0` y la autoactualización 0.1.1 → 0.2.0, y el
  hook de desinstalación de Velopack (su lógica está probada en el núcleo; el gancho solo se ejerce desinstalando).

### Día 2: el aviso

- [ ] Pendiente (mañana). Ver los pasos en la issue #159.
