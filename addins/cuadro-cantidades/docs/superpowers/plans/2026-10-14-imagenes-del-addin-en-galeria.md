# Plan: las imágenes del add-in de imágenes llegan a la "Galería de Capturas" (fase C)

## Contexto

- Plan general (fases A backend, B add-in de imágenes, C este add-in): repo
  `utilidadcqeing-prog/Add-in-Acero-en-columnas-2027`, rama `addin-de-imagenes`,
  `docs/planes/2026-10-14-imagenes-por-actividad-plan-general.md`.
- Hecho: **fase B** (PR #69 del repo del add-in de imágenes: cada imagen se registra en la BD) y **fase A**
  (backend, rama `feat/imagenes-por-actividad` del fork `juatnegreteg-crypto/CQEING_BaseDeDatos2`, commit
  `98286622`, plan `docs/plan-imagenes-por-actividad.md` del backend). Probado de punta a punta el
  2026-10-14 contra la API local: 6 imágenes de `PROY_004_COPIA_V2` registradas sin duplicados.
- **Pedido del usuario (2026-10-14):** en la ventana "Detalles de Cuantificación", pestaña
  **"Galería de Capturas"**, tienen que aparecer las imágenes del add-in de imágenes **de esa actividad**, para
  elegir cuáles se muestran. **El flujo es el mismo de hoy**: se marcan (máximo 3) y al "Guardar Modelo" se
  publican como ahora. Lo único nuevo es que esas imágenes lleguen a la galería.
- Rama: nueva desde `origin/Actualizaciones` (la más reciente del compartido, ya trae el PR #185), por ejemplo
  `feature/imagenes-del-addin-en-galeria`; push a `personal` (fork `juatnegreteg-crypto`).
- Fuera de alcance: cambiar cómo se publica (Cloudinary, `urls_imagenes`), el Excel, la colección de la tabla
  principal; quitar las capturas propias ("Actualizar Preview" se queda).

## Cómo funciona hoy (revisado en el código, 2026-10-14)

1. **Galería** (`UI/Views/DynamicTableWindow.xaml:165-262`, `UI/ViewModels/DynamicTableViewModel.cs`):
   `Snapshots` (`ObservableCollection<ActivityImageSnapshot>`) sale de `BudgetRow.PreviewImagesJson`
   (`LoadSnapshots`, línea 244), que se guarda en la SQLite local. Cada captura tiene un check
   `IsSelectedForPublication`; `ImageSelection_Click` (`DynamicTableWindow.xaml.cs:158`) limita a 3 y llama a
   `UpdatePreviewImagesJson` (`DynamicTableViewModel.cs:1227`), que guarda el JSON, publica `PreviewUpdatedEvent`
   y pide auto-guardado.
2. **Marcar para enviar:** `PreviewUpdatedEvent` → `MainWindowViewModel.OnPreviewUpdatedEventReceived`
   (línea 3748) → `MarkActivityModified`: la actividad se manda en el próximo "Guardar Modelo".
3. **Publicar** (`MainWindowViewModel.cs:1719-1770` + `Services/ActivityImagePublisher.cs`): toma las marcadas
   (máx. 3, más nuevas primero). Si la captura ya tiene `PublishedUrl` o su ruta **es una URL**, la **reutiliza
   sin volver a subirla** (`PublishSnapshotAsync` → `Reuse`). Las URLs van en `urls_imagenes` de
   `POST /api/presupuestos/items/{id}/elementos` (línea 1904).
4. **Ver las imágenes:** la miniatura (`UI/ImagePathToBitmapConverter.cs:70-78`), la imagen grande
   (`Services/ImageCacheBypassHelper.cs:19`) y el Excel (`Services/ExcelStructureWriter.cs:180`) **ya aceptan
   URLs http(s)**. La colección de la tabla principal (`UI/Models/BudgetRow.cs:140`) usa las marcadas.
5. Las capturas nuevas llegan **sin marcar** (`ActivityImageSnapshot.IsSelectedForPublication = false`).

Conclusión: si las imágenes del add-in entran a `Snapshots` como capturas con su URL de Cloudinary, todo el
resto del flujo (marcar, límite de 3, publicar, Excel, colección) funciona **sin cambios** y no se vuelven a
subir a Cloudinary.

## Hallazgos

1. **Conflicto con la fase A (backend):** su paso 5 hizo que la web (Cuadro de Cantidades) muestre **todas** las
   imágenes del add-in de imágenes de la actividad, ignorando lo que se marque aquí (decisión 6 del plan del
   backend). Con el flujo de hoy, la web tiene que mostrar **lo que se publica desde este add-in** (las marcadas,
   vía `urls_imagenes`). Hay que **revertir ese paso 5** en el backend (ver paso 0).
2. **Identificar la actividad:** el `BudgetItem.Id` local es `IdMapper.IntToGuid(id_item)` solo cuando el
   presupuesto se cargó desde la API. Para no depender de eso, se piden las imágenes **por presupuesto**
   (`GET /api/presupuestos/{codigo}/imagenes-actividad`, una llamada) y se filtran por `codigo_actividad`.
3. **Actualizar en el add-in de imágenes cambia la URL** (`ACT_006_01.png` → `ACT_006_01_v2.png`, mismo código).
   La galería tiene que reconocer la imagen por su **código**, no por la URL, para no perder la marca.
4. **No hay proyecto de tests** en este repo (y por decisión 6 no se crea).
5. `MainWindowViewModel.cs` tiene 3985 líneas y `DynamicTableViewModel.cs` 1252: la lógica nueva va en clases
   propias y en los ViewModels solo se cablea.

## Criterios de aceptación

- [ ] Al abrir "Detalles" de una actividad, la galería muestra sus imágenes del add-in de imágenes (con su
      código y nombre) junto con las capturas propias, sin marcar si son nuevas.
- [ ] Se marcan y desmarcan igual que hoy (máx. 3) y la marca se conserva al cerrar y volver a abrir.
- [ ] Al "Guardar Modelo", las marcadas se publican con su URL de Cloudinary **sin volver a subirlas**.
- [ ] La web muestra lo que se marcó aquí (no todas las del add-in de imágenes).
- [ ] Si la imagen se actualiza en el add-in de imágenes (`_v2`), la galería muestra la nueva y conserva la marca;
      si estaba marcada, al guardar se publica la URL nueva.
- [ ] Si se borra en el add-in de imágenes, desaparece de la galería.
- [ ] Sin conexión a la API, la galería abre igual con lo que ya tenía (sin borrar nada).
- [ ] Las capturas propias y "Actualizar Preview" siguen funcionando como hoy.

## Decisiones (2026-10-14, con el usuario)

1. **Las capturas propias se quedan** (usuario: "el flujo será el mismo, lo único que importa es que lleguen las
   imágenes para elegir las que quiera, igual que ahora"). Cambia la decisión 1 del plan general ("el cuadro de
   cantidades deja de capturar"); se anota allí.
2. **Las imágenes del add-in llegan sin marcar**, igual que una captura nueva: el usuario elige.
3. **"Eliminar" no borra imágenes del add-in de imágenes** (confirmado): son de la BD compartida y las usan otras
   PCs. Para esas el botón queda deshabilitado con un aviso; se borran desde el add-in de imágenes.
4. **Se piden al abrir "Detalles"** (siempre frescas), no al cargar el presupuesto.
5. **La web muestra solo las marcadas** (confirmado): se revierte el paso 5 de la fase A (hallazgo 1).
6. **Sin proyecto de tests** (decisión del usuario): la lógica de combinación va en su propia clase, pero se
   prueba a mano en Revit con los pasos de "Cómo probarlo".

## Plan paso a paso

0. **Backend (rama `feat/imagenes-por-actividad`)** — revertir el cambio de `CuadroCantidadesReadService` (las
   imágenes de la actividad vuelven a salir de sus elementos) y cambiar sus 3 tests por uno que fije que la web
   **no** mezcla las del add-in de imágenes. Actualizar la decisión 6 y el Estado del plan del backend.
   Prueba: `dotnet test` completo en verde.
1. **Rama** — `git switch -c feature/imagenes-del-addin-en-galeria origin/Actualizaciones`.
2. **Cliente de la API** — `Core/Application/Interfaces/IApiClient.cs` + `Services/ApiClient.cs`:
   `GetImagenesActividadAsync(string codigoPresupuesto)` → `GET /api/presupuestos/{codigo}/imagenes-actividad`,
   DTOs `ApiImagenesDeActividadResponse` / `ApiImagenActividadResponse` (snake_case). `null` si la API no
   responde o devuelve algo que no es JSON; en modo mock, lista vacía.
3. **Combinación (lógica pura)** — `ActivityImageSnapshot` suma `AddinImageCode`, `AddinImageName` (null en las
   capturas propias). Clase nueva `Services/AddinImagesGalleryMerger.cs`:
   - Imagen del API con código ya en la galería → actualiza URL (`ImagePath` y `PublishedUrl`), nombre y fecha;
     conserva la marca. Devuelve si cambió la URL de una marcada.
   - Imagen nueva → se agrega sin marcar.
   - Imagen del add-in que ya no está en la API → se quita.
   - Capturas propias → no se tocan. Respuesta `null` (API caída) → no se toca nada.
4. **Galería** — `DynamicTableViewModel`: después de `LoadSnapshots`, pedir las imágenes del presupuesto, filtrar
   la actividad, combinar y, si algo cambió, `UpdatePreviewImagesJson()` (eso guarda y marca la actividad para el
   próximo guardado si cambió una URL publicada). `DeleteSnapshotAsync`: no borrar las del add-in (decisión 3).
5. **Vista** — `DynamicTableWindow.xaml`: en la lista, para las del add-in mostrar `código · nombre` y
   "Add-in de imágenes" en vez de "Captura de Modelo 3D".
6. **Plan general** — anotar la decisión 1 (las capturas propias se quedan) y el estado de la fase C.

## Cómo probarlo

Compilar y desplegar el add-in con el comando del repo (con Revit cerrado), sin proyecto de tests (decisión 6).

Con la API local (`http://localhost:5187`, backend con el paso 0) y Revit con el add-in desplegado:

1. Abrir `PROY_004_COPIA_V2` → actividad `ACT-000015` → "Detalles" → "Galería de Capturas": aparecen
   `ACT-000015_01…_04` sin marcar, junto a las capturas propias.
2. Marcar 2 → cerrar y abrir: siguen marcadas. Marcar una cuarta → aviso de máximo 3.
3. "Guardar Modelo" → la web muestra esas 2 (y no las otras). El reporte de publicación dice "reutilizada", no
   "subida".
4. En el add-in de imágenes, "Actualizar" una marcada → volver a abrir "Detalles": URL `_v2`, sigue marcada;
   "Guardar Modelo" → la web muestra la `_v2`.
5. Apagar la API → abrir "Detalles": la galería abre con lo que tenía.

## Estado

- [x] Revisión del flujo actual y plan (2026-10-14).
- [x] Decisiones confirmadas por el usuario (2026-10-14): web solo las marcadas, "Eliminar" no borra las del
  add-in de imágenes, sin proyecto de tests.
- [x] Paso 0 (backend): hecho según el usuario (2026-10-14), en la rama `feat/imagenes-por-actividad`.
- [x] Paso 1 (2026-10-14): rama `feature/imagenes-del-addin-en-galeria` creada desde `origin/Actualizaciones`
  (`132f0bd`, merge del PR #185). Se le quitó el upstream (`git branch --unset-upstream`) para que un `git push`
  no vaya al compartido; el primer push será `git push -u personal feature/imagenes-del-addin-en-galeria`.
  - Cambio (2026-10-14, pedido del usuario): se trajo `feature/caras-exteriores` (6 commits sin subir,
    `e78d6e7..665c6e7`) a esta rama con `git merge --ff-only` (las dos salían de `132f0bd`), para que el PR
    lleve las caras exteriores y las imágenes juntas.
- [x] Paso 2 (2026-10-14): `IApiClient.GetImagenesActividadAsync(codigoPresupuesto)` + DTOs
  `ApiImagenesDeActividadResponse` (`id_item`, `codigo_actividad`, `imagenes`) y `ApiImagenActividadResponse`
  (`id`, `codigo`, `url`, `nombre`, `descripcion`, `tipo_vista`, `orden`, `fecha_creacion`; se omiten `colores`,
  `id_modelo` y `nombre_modelo`, que la galería no usa). Nombres tomados de
  `Services/Presupuesto/ImagenesActividad/ImagenActividadDtos.cs` del backend. Solo acepta respuesta 2xx que
  empiece con `[`; cualquier otra cosa → `null`.
  - Build: `dotnet build CQEING_CuadroCantidades.csproj -c Debug` → 0 errores, ninguna advertencia en los
    archivos tocados.
  - Prueba con un programa de consola descartable que referencia el DLL compilado, contra la API local
    (`http://localhost:5187`, clave del `.env`): `PROY_004_COPIA_V2` → 2 actividades (`ACT_002` id_item 134 con
    `ACT_002_01`/`_02`; `ACT-000015` id_item 179 con `_01`…`_04`), todos los campos leídos. Clave inválida (401)
    → `null`; API apagada → `null`; presupuesto inexistente → `null`; modo mock → lista vacía.
  - Hallazgo: un presupuesto que la API no conoce también da `null` (= "no tocar nada"), no lista vacía. Es lo
    seguro: nunca se borran imágenes de la galería por un error.
- [x] Paso 3 (2026-10-14):
  - `Core/Domain/Entities/ActivityImageSnapshot.cs`: `AddinImageCode`, `AddinImageName` y, además de lo previsto,
    `IsFromImagesAddin` (calculada, `[JsonIgnore]`, no se guarda), que usarán los pasos 4 y 5.
  - `Services/AddinImagesGalleryMerger.cs`: `Merge(galeríaActual, imágenesApi?)` →
    `AddinImagesMergeResult(Snapshots, Changed, SelectedUrlChanged)`. No modifica la lista que recibe; las
    capturas propias pasan tal cual y en su lugar; las nuevas del add-in van al final, por `orden`.
  - Una imagen del add-in queda con la URL en `ImagePath` y `PublishedUrl`, y `DebugImagePath` vacío: la galería
    (`ImagePathToBitmapConverter`, `DynamicTableViewModel.cs:544`) dibuja `DebugImagePath` antes que `ImagePath`,
    así nunca muestra una URL vieja tras un `_v2`. El publicador reutiliza `PublishedUrl` sin subir.
  - La fecha de la API (UTC) se pasa a hora local, como la de las capturas propias: el publicador ordena las
    marcadas por fecha y no debe mezclar UTC con local.
  - El código se compara sin distinguir mayúsculas (el backend lo guarda en mayúsculas).
  - Build: 0 errores, sin advertencias nuevas. Verificado con el programa de consola descartable (19 casos, todos
    OK): API caída no toca nada; nuevas sin marcar y ordenadas; la propia intacta; guardar en JSON y reabrir sin
    cambios → `Changed = false` (no se guarda en cada apertura); `_v2` en una marcada → misma imagen, URL nueva,
    sigue marcada, `SelectedUrlChanged`; `_v2` en una no marcada → solo `Changed`; borrada en el add-in → se quita;
    API con lista vacía → quedan solo las propias; imagen sin URL → se ignora; JSON viejo sin los campos nuevos → se
    lee como captura propia. (Dos casos fallaron la primera vez por un error del propio programa de prueba: comparaba
    por referencia una captura que ya venía del JSON; se corrigió la comprobación, no el código.)
- [x] Paso 4 (2026-10-14), en `UI/ViewModels/DynamicTableViewModel.cs`:
  - El constructor recibe además `IApiClient` y el código del presupuesto en la API. Ese código es
    `_currentBudget.VersionName`, el mismo que usa `GetBudgetStructureAsync`; se pasa desde
    `MainWindowViewModel` al abrir "Detalles".
  - Después de `LoadSnapshots()`, `LoadAddinImagesAsync()` (sin bloquear la apertura): pide las imágenes del
    presupuesto, filtra por `codigo_actividad` == `_budgetRow.Code` (sin distinguir mayúsculas), combina con
    `AddinImagesGalleryMerger` en el hilo de UI y, si algo cambió, rearma `Snapshots` conservando la imagen elegida
    (por código si es del add-in).
  - Guardado: si cambió la URL de una marcada → `UpdatePreviewImagesJson()` (guarda y marca la actividad para el
    próximo "Guardar Modelo"). Si no → solo se escribe el JSON en la fila y se pide el auto-guardado, **sin** marcar
    la actividad: `OnPreviewUpdatedEventReceived` marca siempre, y no tiene sentido reenviar una actividad porque
    llegó una imagen sin marcar. El auto-guardado copia el JSON de la fila a la SQLite
    (`ProjectStateService.cs:195`). En modo lectura se muestra la galería al día pero no se guarda nada.
  - "Eliminar": `CanExecute` falso para las del add-in (botón deshabilitado) y el método además las ignora. El aviso
    (tooltip en el botón deshabilitado) va en el paso 5, que es el de la vista.
  - Build: 0 errores, 30 advertencias (las mismas de antes). La prueba en Revit se hace junto con el paso 5
    (sin él, las imágenes del add-in se verían con el texto "Captura de Modelo 3D").
- [x] Paso 5 (2026-10-14), en `UI/Views/DynamicTableWindow.xaml`:
  - Lista de la galería: las del add-in muestran `código · nombre` (con el nombre completo en tooltip) y debajo
    `Add-in de imágenes · dd/MM/yyyy HH:mm`; las capturas propias siguen con la fecha y "Captura de Modelo 3D".
    Se hace con `DataTrigger` sobre `IsFromImagesAddin`.
  - "Eliminar": `ToolTipService.ShowOnDisabled` y un tooltip que explica por qué está deshabilitado
    (decisión 3). El estilo `ModernButtonStyle` ya atenúa el botón deshabilitado (opacidad 0.45).
  - Build: 0 errores, 30 advertencias (las mismas). Despliegue: el propio build escribe
    `%APPDATA%\Autodesk\Revit\Addins\2026\CQEING_CuadroCantidades.addin` apuntando a
    `bin\Debug\net8.0-windows\CQEING_CuadroCantidades.dll` (verificado; Revit estaba cerrado). El `.env` de la
    raíz del repo apunta a `http://localhost:5187/`.
  - Pendiente: la prueba en Revit de los pasos 4 y 5 ("Cómo probarlo", puntos 1 a 5).
- [x] Paso 6 (2026-10-14): plan general (repo `Add-in-Acero-en-columnas-2027`, rama `addin-de-imagenes`,
  `docs/planes/2026-10-14-imagenes-por-actividad-plan-general.md`): decisión 1 tachada y reemplazada (las capturas
  propias se quedan), decisión 6 resuelta (la web muestra solo lo publicado desde aquí), la sección "Fase C" apunta
  a este plan (pasos C1, C4 y C5 descartados) y el Estado registra las fases A y B hechas y la C en curso.
- [x] Prueba en Revit de los pasos 4 y 5 (2026-10-14): el usuario confirmó que está probado ("ya está probado"),
  sin detalle de cada punto de "Cómo probarlo".
- [x] Commits `15f7174..adb9fe5` y push a `personal` (2026-10-14).
- [ ] Release v1.8 en el repo compartido (versión 1.8.0 en el `.csproj`, PR hacia `Actualizaciones` sin fusionar,
  ZIP como el de v1.7: compilación Release + `Installer/` + `.env.example`, sin `.env`).
