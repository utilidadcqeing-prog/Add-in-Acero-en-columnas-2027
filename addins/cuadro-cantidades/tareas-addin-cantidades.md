# Tareas – Add-in Cuadro de Cantidades

Origen: reunión con Carlos Quintero (08/10/2026). Solo tareas del add-in de Revit; las de la interfaz web quedan fuera.

Seguimiento en GitHub: épica [#163](https://github.com/utilidadcqeing-prog/Add-in-Cuadro-de-cantidades-V0/issues/163). Cada tarea lleva el número de su sub-issue.

> **Estado al 10/10/2026**
> - Terminadas: 2, 11 y 12.
> - Con avance: 1 (falta probar en Revit), 3, 6, 7 y 9.
> - Sin empezar: 5, 8, 10, 13, 14 y 15. La 4 está en espera porque no se ha podido reproducir.
> - El trabajo del 09/10 (`feature/tabla-ventanas`, que incluye `fix/nivel-json-api`) se fusionó en `Actualizaciones` con el PR #184; todavía no está en `main`.
> - En GitHub están cerradas #164, #165, #167, #172, #174 y #175. De ellas, #167 (tarea 4) y #172 (tarea 9) tienen criterios de aceptación sin cumplir. #164 (tarea 1) está cerrada sin la prueba en Revit.

> **Regla para las tablas:** añadir lo que falta, nunca quitar ni mover lo que ya existe. Las columnas actuales de cada tabla (vigas y columnas, muros, zapatas, losas, escaleras, varillas) se conservan en el mismo orden; lo nuevo se agrega al final. Así no cambia lo que ya ven los usuarios ni el JSON que lee la web.

## Fase 1 – Correcciones (sin dependencias)

- [ ] **1. Peso de perfiles metálicos** (#164)
  - Leer el parámetro compartido "Peso lineal metálico" por tipo de dato (`SpecTypeId.MassPerUnitLength`), no por nombre, igual que `RebarQuantityExtractor.GetBarMassPerMeter`.
  - Corregir la conversión de unidades: hoy se multiplica por `1.0` y Revit guarda kg/pie (sale un 30 % del valor real).
  - Calcular `kg = longitud × peso lineal` para armazones y pilares estructurales.
  - Corrige la cantidad en cero del acero estructural y del acero sin nivel (`QuantificationEngine` busca `Peso` para KG).
  - Ejemplo de referencia: tubo cuadrado 100×100×3 = 9,32 kg/m.

  > **Hallazgos en el código**
  > - `MainWindowViewModel.cs:1179` (y su copia en `:3345`) busca el peso por nombres fijos: `"kg/m"`, `"Mass per unit length"`, `"Peso"`, `"Weight"`. **"Peso lineal metálico" no coincide con ninguno**, así que hoy no se lee.
  > - Esos valores se guardan con factor `1.0` (`ExtractDimension(wp, 1.0)`), sin `UnitUtils.ConvertFromInternalUnits`. Aunque se encontrara el parámetro, el kg/m saldría en unidades internas de Revit (kg/pie).
  > - `LookupParameter` por nombre puede devolver un parámetro de texto o de otro tipo; `AsDouble()` daría 0 sin avisar.
  > - El cálculo `Peso = Length × kg/m` solo existe en `RebarQuantityExtractor.AddQuantities` (`:55-72`). Para perfiles no hay nada equivalente.
  > - **Por qué sale cantidad 0:** con unidad KG, `MainWindowViewModel.cs:820` toma la cantidad de la columna `PESO` de la fila TOTAL, pero esa columna solo existe en la tabla de acero de refuerzo (`DynamicTableBuilder.cs:74-79`). En vigas y columnas cae a `QuantificationEngine`, que busca la llave `"Peso"` en el JSON; como nunca se escribe para perfiles, suma 0.
  > - **Error extra con TON:** `QuantificationEngine.cs:121` trata `TON` igual que `KG` y devuelve kilos sin dividir entre 1000. Lo mismo pasa en `MainWindowViewModel.cs:820`.
  > - Para saber si un elemento es metálico se puede usar `FamilyInstance.StructuralMaterialType == StructuralMaterialType.Steel` en lugar del nombre de la categoría.
  > - Propuesta: crear un `SteelProfileQuantityExtractor` análogo a `RebarQuantityExtractor`, que busque en el tipo el parámetro con `SpecTypeId.MassPerUnitLength`, lo convierta con `UnitTypeId.KilogramsPerMeter` y escriba `kg/m` y `Peso`.

  > **Estado: implementado, falta probar en Revit**
  > - `Services/MassPerLengthReader.cs`: lectura de kg/m por tipo de dato, compartida con las varillas.
  > - `Services/SteelProfileQuantityExtractor.cs`: para armazón y pilares estructurales con peso lineal (instancia o tipo) escribe `kg/m` y `Peso = longitud × kg/m`. Sin peso lineal no toca el elemento.
  > - Llamado en las dos copias de la extracción (`MainWindowViewModel`).
  > - TON ahora divide entre 1000 (`QuantificationEngine.ConvertFromKilograms`).
  > - Los elementos ya vinculados guardan su JSON viejo: hay que volver a extraerlos para que tomen el peso.
  > - La tabla de detalle de vigas y columnas metálicas se resolvió en la tarea 2.
  > - #164 está cerrada en GitHub, pero sus criterios no están marcados. Falta probar: viga de 29,80 m con 9,32 kg/m da 277,74 en una actividad KG y 0,28 en una TON, y el concreto no cambia.
- [x] **2. Tablas de perfiles metálicos** (#165)
  - **Regla: añadir lo que falta, no quitar ni mover lo que ya existe.** La tabla de vigas y columnas se conserva tal cual.
  - Agregar las columnas KG/M y PESO cuando la actividad tiene perfiles metálicos (I/W y tubulares: cuadrado, rectangular, circular) (`DynamicTableBuilder`).

  > **Hallazgos en el código**
  > - `DynamicTableBuilder.cs:21` decide el tipo de tabla por texto en el nombre de la categoría (`"column"`, `"pilar"`, `"viga"`, `"framing"`, `"armazón"`). No distingue concreto de acero: todo cae en `isColumnBeam`.
  > - Esa tabla (`:46-52`) tiene BASE B, ALTURA H, LONGITUD y VOLUMEN; **no tiene KG/M ni PESO**.
  > - BASE B y ALTURA H salen de `GetParamValue(..., "Base", "b", "Width")` y `("Altura", "h", "Height", "Depth")` (`:167-168`). En familias de acero `b` y `h`/`d` son el ancho del ala y el peralte, así que aparecen como si fuera una sección rectangular de concreto. En pilares, `"Height"` puede ser la altura del elemento, no de la sección.
  > - La tabla se decide por la categoría del **primer** elemento (`elements.FirstOrDefault()`); si una actividad mezcla concreto y acero, todos usan la misma tabla.
  > - Propuesta inicial (descartada): una tabla de acero aparte (TIPO, CANTIDAD, NIVEL, LONGITUD, KG/M, PESO). Se descartó porque quitaba BASE B, ALTURA H y VOLUMEN.

  > **Estado: terminada (revisada en Revit el 08/10/2026)**
  > - Misma tabla para todos los perfiles (I/W y tubulares): las columnas de peso son las mismas. Si hace falta, más adelante se **añade** una columna SECCIÓN.
  > - `LinkedElement` no guarda `BuiltInCategory` ni material, y los elementos que vienen de la API tampoco: el acero se detecta por los datos. Una viga o columna es perfil metálico si su JSON trae `kg/m > 0` (lo escribe `SteelProfileQuantityExtractor`, o vuelve de la API como `KG/M`).
  > - `DynamicTableBuilder`: la tabla de vigas y columnas conserva No., TIPO, CANTIDAD, NIVEL, BASE B, ALTURA H, LONGITUD y VOLUMEN. Si algún elemento tiene kg/m se **agregan** KG/M y PESO al final. TOTAL suma PESO; KG/M queda vacío (sumar pesos lineales no tiene sentido).
  > - Sin elementos con kg/m (p. ej. concreto) la tabla queda idéntica a la de `main`.
  > - Si una actividad mezcla concreto y acero, los elementos de concreto quedan con KG/M y PESO en 0.
  > - Como no se quita ninguna columna, cambiar la unidad de la actividad sigue funcionando: M3 toma VOLUMEN, ML toma LONGITUD y KG/TON toman PESO del TOTAL (`MainWindowViewModel.cs:820`, `DynamicTableViewModel.cs:672`).
  > - El `DatosJson` enviado por elemento mantiene sus columnas y suma KG/M y PESO (mismos nombres que las varillas). La web solo lee ese JSON.
  > - Pendiente aparte: BASE B y ALTURA H en perfiles de acero muestran el ancho del ala y el peralte (o 0), no una sección rectangular. No se tocan por la regla de no quitar.
- [ ] **3. Error intermitente de imágenes** (~10 % de las capturas, "base ocupada") (#166)
  - Registrar en log los fallos de exportación (`RevitImageCaptureService`) y de subida (`CloudinaryUploadService`).
  - No borrar `PreviewImagePath` cuando todas las subidas fallan (`MainWindowViewModel.cs:1663`).
  - Revisar bloqueo de SQLite ("database is locked") y la cola de regeneración de previsualizaciones.
  - Reintentos con espera creciente en la subida.

  > **Hallazgos en el código** (hipótesis a confirmar reproduciendo el fallo)
  > - **"Base ocupada" → mismo `DbContext` usado desde varios hilos.** `ModelessWindowService.cs:34` crea un único scope para toda la ventana, así que `MainWindowViewModel`, `ActivityPreviewService` y `AutoSaveService` comparten el mismo `IUnitOfWork`/`DbContext`. `ActivityPreviewService.GeneratePreviewAsync` corre dentro de `Task.Run` (`:43`) y guarda con `SaveChangesAsync` (`:112`, `:215`) mientras el auto-guardado (temporizador) o "Guardar modelo" pueden estar usando el mismo contexto. EF Core no es thread-safe: lanza *"A second operation was started on this context instance…"*, que encaja con "base ocupada".
  > - **"Cola ocupada" → la captura puede quedarse esperando para siempre.** `ActivityPreviewService.cs:117-138` pide la captura con `RevitEventExecutor.Execute` y hace `await tcs.Task` **sin timeout**. Si Revit no procesa el `ExternalEvent` (ver tarea 4), la tarea no termina y `PreviewRegenerationQueue` mantiene tomado su `SemaphoreSlim(1,1)` (`:12`, `:41`): todas las capturas siguientes quedan en cola.
  > - Todos los errores se tragan con `Debug.WriteLine` (`ActivityPreviewService.cs:221-226`, `PreviewRegenerationQueue.cs:53-56`, `RevitImageCaptureService.cs:288-293`). En `sync_error.log` no hay ni un registro de imágenes; por eso nadie sabe la causa.
  > - Si la exportación no encuentra el PNG, `RevitImageCaptureService.cs:322-335` devuelve `string.Empty` sin error, y `ActivityPreviewService.cs:140-146` sale en silencio.
  > - **Pérdida de la imagen al subir:** si todas las subidas a Cloudinary fallan, `imageUrl` queda `null` y `MainWindowViewModel.cs:1663-1666` lo guarda en `PreviewImagePath`, borrando la ruta local. En el siguiente guardado ya no hay imagen que subir.
  > - `CloudinaryUploadService` tiene timeout de 30 s y no reintenta.
  > - Propuesta: un `DbContext` por operación (`IServiceScopeFactory`/`IDbContextFactory`) en el servicio de previsualización; timeout en `await tcs.Task`; registrar fallos en el log; no sobrescribir `PreviewImagePath` con `null`; reintentos en la subida.

  > **Corrección tras revisar el código**
  > - `ActivityPreviewService` no se usa (nadie llama a `GeneratePreviewAsync`) y la captura automática de `LivePreviewUpdater` está apagada. El flujo real es manual: `DynamicTableViewModel.GeneratePreview` → `PreviewRefreshService` → `ManualPreviewService` → `ActivityPreviewUpdater` (con su propio scope).
  > - El `DbContext` compartido sí se usa en paralelo, pero entre el auto-guardado (`AutoSaveService`, en hilo aparte) y la ventana: tras cada captura `DynamicTableViewModel.cs:373` lee con el contexto compartido y pide guardado inmediato. Falta el mensaje exacto del error para confirmarlo.
  > - La captura espera a Revit sin timeout (`ManualPreviewService.cs:59`); si se cuelga, `_isGeneratingPreview` queda en `true` y el botón deja de responder sin aviso.

  > **Estado de la subida: implementado, falta probar**
  > - La web reemplaza todos los elementos e imágenes de la actividad en cada envío (`PresupuestoItemElementosController.GuardarElementos`): enviar `urls_imagenes` vacío borra la imagen publicada.
  > - `ActivityImagePublisher` (nuevo): sube las imágenes marcadas (máx. 3). La URL se guarda en la captura (`ActivityImageSnapshot.PublishedUrl`) y no se vuelve a subir. Si el archivo local no existe, sube la copia Base64 de la captura.
  > - Si alguna imagen marcada falla, no se toca `PreviewImagePath` ni `UrlImagen`: se reenvían las URLs ya publicadas y la actividad queda pendiente para el próximo guardado. El reporte "Diagnóstico de Imágenes" lista esas actividades.
  > - `CloudinaryUploadService`: hasta 4 intentos (esperas de 1, 2 y 4 s) en errores de red, tiempo agotado, 5xx y 429.
  > - `ErrorLog` (nuevo): registra fallos en `%AppData%\CQEING_CuadroCantidades\logs\errors.log`.
  > - Las imágenes que vienen de la web quedan marcadas para publicar al abrir el detalle, para que una captura nueva no las borre.
  > - Pendiente de la tarea: timeout de la captura, uso concurrente del `DbContext` y borrar `ActivityPreviewService`.
  > - Avance relacionado (09/10, commit `742680f`): quitar elementos en Detalles ya no consulta la base con el contexto compartido, que chocaba con el auto-guardado. El evento lleva los elementos que quedan.

  > **Revisión del 10/10/2026:** siguen pendientes el timeout de la captura (`ManualPreviewService.cs:59`) y el `DbContext` compartido con el auto-guardado. `Services/ActivityPreviewService.cs` todavía existe.
- [ ] **4. Bloqueo con "Seleccionar similares"** (#167)
  - Tras usar el comando hay que mover la rueda del ratón sobre la ventana principal para que el add-in no se bloquee.

  > **Hallazgos en el código** (hipótesis a confirmar reproduciendo el fallo)
  > - La selección de Revit se detecta **sondeando en `Idling`** (`DocumentLifecycleService.cs:113-143`). Revit solo dispara `Idling` cuando su bucle de mensajes recibe actividad; tras un comando nativo como "Seleccionar todas las instancias", puede no dispararse hasta que se mueve el ratón.
  > - Las acciones del add-in sobre Revit van por `ExternalEvent.Raise()` (`RevitEventExecutor.cs:50`), que tiene el mismo problema conocido desde ventanas no modales: queda pendiente hasta que Revit recibe un mensaje (mover el ratón o la rueda lo "despierta").
  > - **Bucle de selección Revit ↔ add-in:** `OnSelectionChanged` (`MainWindowViewModel.cs:1893`) marca `IsSelected` en la lista y lanza `ElementSelectedForScroll`; `MainWindow.xaml.cs:21-28` hace `ScrollIntoView` y **`Focus()`**, quitándole el foco a Revit. Al materializarse filas virtualizadas, `ListView_SelectionChanged` (`MainWindow.xaml.cs:43-52`) llama a `SyncSelectionToRevit`, que vuelve a mandar la selección a Revit por `ExternalEvent`. Como el bloqueo `_isSyncingSelection` se libera antes de que corra el scroll asíncrono, el ciclo no queda protegido.
  > - Con selecciones grandes, `AreCollectionsEqual` (`DocumentLifecycleService.cs:151-163`) usa `list1.All(list2.Contains)`, que es O(n²) en cada `Idling`.
  > - Propuesta: usar el evento nativo `UIApplication.SelectionChanged` (Revit 2023+) en lugar de sondear en `Idling`; quitar `Focus()` del scroll; no reenviar a Revit una selección que vino de Revit; comparar con `HashSet`.

  > **Revisión del 08/10/2026: hallazgos confirmados en el código, bloqueo no reproducido**
  > - Hallazgo nuevo: `SyncSelectionToRevit` (`MainWindowViewModel.cs:1899`) solo envía a Revit los elementos marcados en la lista de no asignados. Cuando el scroll realiza filas nuevas, reenvía esa selección y **deselecciona en Revit los elementos que no están en la lista** (p. ej. muros ya asignados u otras categorías). El usuario queda con una selección distinta a la que hizo.
  > - Prueba en Revit con 4 vigas: la selección se sincronizó bien y no hubo bloqueo.
  > - **Pendiente:**
  >   - Pedir a Carlos el comando exacto, el modelo y si usa la ventana del add-in en otro monitor.
  >   - Reproducir con muchos elementos: clic derecho → Seleccionar todas las instancias → En todo el proyecto (p. ej. varillas `#4`) y no mover el ratón 5–10 s.
  >   - Justo después del comando, sin pasar por la vista de Revit, pulsar en el add-in algo que use Revit (Asignar a Actividad, Detalles, Actualizar Preview) y ver si responde o espera hasta mover la rueda.
  >   - Comparar el número de elementos seleccionados en Revit antes y después de que reaccione el add-in, con un tipo que tenga elementos asignados y no asignados.
  >   - Tras el comando, pulsar una tecla de Revit (`Supr` o un atajo) para ver si el `Focus()` le quitó el foco.
  > - Arreglos listos para aplicar cuando se confirme: evento nativo `SelectionChanged`, quitar `Focus()`, no reenviar la selección que vino de Revit. Revisar el resultado de `Raise()` y "despertar" a Revit solo si después de eso sigue el bloqueo.

  > **Estado al 10/10/2026: en espera.** #167 está cerrada en GitHub como completada, pero no se aplicó ningún arreglo y el bloqueo no se ha reproducido. Sigue sin arreglar el reenvío de selección que deselecciona en Revit lo que no está en la lista.
- [ ] **5. No sobrescribir cantidades manuales al sincronizar** (#168)
  - Una cantidad sin vínculo con Revit ("huérfana") debe conservarse; hoy al actualizar/guardar se pone en cero o cambia (10 → 20).

  > **Hallazgos en el código**
  > - En el add-in **no se pueden editar cantidades**: la grilla del presupuesto es `IsReadOnly="True"` (`MainWindow.xaml:467`) y `BudgetItem` **no tiene campo de cantidad** (`Core/Domain/Entities/BudgetItem.cs`). La edición manual ocurre en la web.
  > - Al cargar, el add-in **recalcula siempre** la cantidad desde los elementos vinculados locales (`MainWindowViewModel.cs:777-783`); sin vínculos da 0. La `cantidad_obra` que devuelve la API (`IApiClient.cs:164-165`) se recibe pero **no se usa** en ningún sitio.
  > - Al guardar, el add-in no envía una cantidad de la actividad: envía la lista de elementos (`UpdateLinkedElementsAsync`) con `Cantidad` y `Totales` dentro del `DatosJson` de cada elemento (`MainWindowViewModel.cs:1688-1799`). Si la actividad quedó sin elementos, envía una lista vacía para "vaciarla" en el servidor (`:1801-1807`).
  > - Solo se sincronizan las actividades marcadas en `_modifiedActivityCodes` (`:1570`). "Limpiar estructura" marca **todas** (`:2756-2758`), así que vacía también las actividades con cantidad manual.
  > - Conclusión: la sobrescritura (0 o 10 → 20) ocurre en la API al recalcular con lo que manda el add-in. La parte del add-in es: no enviar lista vacía a actividades sin vínculos que tengan cantidad manual, y mostrar `cantidad_obra` cuando no hay elementos vinculados. Hay que acordar con la API una marca de "cantidad manual" para que el servidor no la recalcule.

  > **Estado al 10/10/2026: sin empezar.** `cantidad_obra` se recibe (`IApiClient.cs:164`) pero sigue sin usarse.

## Fase 2 – Base técnica

- [ ] **6. Unificar la extracción de datos de Revit** (#169)
  - La lógica está duplicada en `MainWindowViewModel.cs:905` y `:3077`; extraerla a un servicio propio antes de añadir categorías.
  - Se corrigen aquí los errores de nivel y del tramo de escalera encontrados en la tarea 9.

  > **Avance (09/10/2026)**
  > - Hecho: "Sin Nivel" al recargar desde la API. `ElementDataJsonReader` lee las claves sin distinguir mayúsculas (`Nivel`/`NIVEL`, `Anfitrión`/`ANFITRIÓN`); si están las dos, gana la que escribe el add-in. Lo usan `DynamicTableBuilder` y la búsqueda de niveles faltantes de Detalles. No se tocó el guardado ni los datos ya subidos (commit `6468778`).
  > - Guardar Modelo espera a que Revit termine la sincronización (`RevitEventExecutor.ExecuteAsync`) antes de armar lo que envía. Antes los datos podían llegar tarde y perderse, también el anfitrión de las varillas.
  > - Los datos de puertas y ventanas salen de un servicio propio (`DoorWindowDataExtractor`), que las dos copias de la extracción llaman en una línea.
  > - Las actividades pendientes de enviar se guardan en disco (`PendingActivitySyncStore`): si Revit se cierra antes de Guardar Modelo, no se pierden (commit `742680f`).
  > - **Falta:** unificar la extracción duplicada en un servicio, el nivel de las escaleras (`STAIRS_BASE_LEVEL_PARAM`, "Nivel base") y no listar el tramo de escalera como elemento aparte.
- [ ] **7. Ampliar el JSON que envía el add-in** (#170)
  - Campos nuevos: código, descripción corta, hojas, ubicación, apertura, base × altura, material ~~, observaciones~~.
  - Guardar el tipo como "Familia : Tipo" para no confundir tipos con el mismo nombre.

  > **Avance (09/10/2026)**
  > - Puertas y ventanas ya envían en `DatosJson`: `Código` ("Marca de tipo"), `Descripción` ("Descripción" del tipo, o el nombre del tipo si está vacía), `Lugar` (tarea 11), `Medidas` (anchura × altura en m, p. ej. `1,20 × 1,80`), `Material` (parámetros de material del tipo, unidos con ` + `), y `Hojas` y `Apertura` (se llenan a mano en Detalles).
  > - Las claves que ya existían no cambian de nombre ni de valor. Probado con 2 puertas y 3 ventanas.
  > - Cambio de alcance: **observaciones** y **descripción detallada** se llenan en la interfaz web y no pasan por el add-in.
  > - **Falta:** la clave nueva `Familia : Tipo`, y los campos nuevos para las demás categorías (barandas, etc.) cuando se definan sus tablas.
- [ ] **8. Aviso de datos faltantes** (#171)
  - Antes de enviar: "Falta esta información, ¿enviar igual?".
  - Estado al 10/10/2026: sin empezar.

## Fase 3 – Investigación en Revit

- [ ] **9. Ejemplos en Revit y guía para el modelador** *(prioridad de Carlos, sesión con él el 09/10)* (#172)
  - Modelar cada caso problemático (p. ej. escalera con baranda), ver qué extrae el add-in y documentar qué funciona, qué no y por qué.
  - Lista de objetos a modelar: `modelo-ejemplo-tarea-9.md`.

  > **Caso 1: escalera con baranda (probado el 08/10/2026)**
  > - Modelo: escalera "Escalera moldeada in situ : Escalera monolítica v0" de N+0.00 Piso 1 a N+3.50 Piso 2, 20 contrahuellas de 0,175 m, huella 0,28 m, ancho 1,20 m. Revit creó 2 barandillas tipo "900mm" de 6,27 m cada una.
  > - **Volumen: bien.** La escalera da 1,38 m³ (estimado a mano ≈ 1,4 m³: escalones ≈ 0,59 + losa inclinada ≈ 0,8). El tramo da 0, así que no hay doble conteo. Pendiente comparar con una tabla de cómputo de materiales de Revit.
  > - **Error: la escalera sale "Sin Nivel".** `GetElementLevelName` (`MainWindowViewModel.cs:3584`) no consulta `STAIRS_BASE_LEVEL_PARAM`, y la búsqueda por nombre (`:3637`) prueba "Nivel de base" pero en español el parámetro es "Nivel base". El tramo no tiene nivel propio: habría que tomarlo de su escalera.
  > - **Error: el tramo aparece como elemento aparte.** Sale "Mamperlán 25 mm" (el tipo de tramo) en la lista, con volumen 0 pero suma 1 en CANTIDAD (TOTAL 2 con una sola escalera). En actividades en UN/GL daría una cantidad equivocada. Pasa porque la lista incluye toda categoría con cantidades de material (`MainWindowViewModel.cs:880-887`). Arreglo propuesto: no listar sub-elementos de escalera (tramos, descansillos, soportes) y cuantificar solo la escalera.
  > - **Las barandillas no aparecen en la lista.** Causa probable (sin verificar): la categoría Barandillas no tiene cantidades de material y el filtro de `:886` la descarta. Para la tabla de barandas en metros lineales (tarea 13) hay que incluir la categoría explícitamente, como ya se hace con varillas y escaleras, y leer su longitud.

  > **Caso 2: muros en M2 (probado el 09/10/2026)**
  > - Modelo: actividad ACT-000015 (M2) con 5 muros de N+0.00 Piso 1, altura 3,50 m: 4 "Muro primer piso e = 0.12" (7,12 / 7,12 / 12,12 / 12,12 m) y 1 "Interior - bloques 100 mm" (7,12 m). Cuatro tienen un hueco (puerta o ventana).
  > - **Área y longitud: bien.** Coinciden con Revit muro por muro (`HOST_AREA_COMPUTED`, ya descuenta los huecos: el muro sin hueco da 7,12 × 3,50 = 24,92 m²). TOTAL 149,82 m² y 45,6 m.
  > - **Error: los muros salen "Sin Nivel"** en la tabla desplegada de la actividad, aunque en Revit los 5 están en N+0.00 Piso 1. Causa probable (falta confirmarla con los datos guardados): al asignar se guarda la clave `"Nivel"` (`MainWindowViewModel.cs:1258`), pero al guardar en el servidor el `DatosJson` se arma con las columnas de la tabla (`:1683-1690`) y la clave pasa a ser `"NIVEL"`. Al recargar desde la API (`:666`), `DynamicTableBuilder.cs:133-139` solo busca `"Nivel"` o `"Level"` (distingue mayúsculas) y pone "Sin Nivel". Al abrir **Detalles** el nivel se vuelve a pedir a Revit (`DynamicTableViewModel.cs:760-850`), así que ahí debería verse bien. Lo mismo pasa con `"ANFITRIÓN"` / `"Anfitrión"`.
  > - Arreglo propuesto: que `DynamicTableBuilder` lea también `"NIVEL"` y `"ANFITRIÓN"`. No hace falta tocar el guardado ni los datos ya subidos. **Hecho el 09/10 en la tarea 6** (`ElementDataJsonReader`).

  > **Caso 3: puertas y ventanas (probado el 09/10/2026)**
  > - Modelo de prueba con 2 puertas y 3 ventanas: código, descripción, nivel, lugar, medidas y material salen bien. Detalle en `investigacion-puertas-ventanas.md`.

  > **Caso 4: variantes de escalera (modeladas el 10/10/2026, falta probarlas en el add-in)**
  > - Modeladas por API en x = 20–29 m, fuera del edificio. Vista **3D TAREA 9 ESCALERAS**. Todas tienen barandillas automáticas "900mm" y una Marca para reconocerlas:
  >
  > | Id | Marca | Tipo | Niveles | Sub-elementos | Volumen en Revit |
  > |---|---|---|---|---|---|
  > | 811395 | T9-U-DESCANSO | Moldeada in situ : Escalera monolítica v0, en U | Piso 1 → Piso 2 (20 × 0,175) | 2 tramos + 1 descansillo, barandas de 6,25 y 10,89 m | 2,147 m³ |
  > | 811479 | T9-ACERO | Ensamblada : Industrial y montaje, recta | Piso 1 → Piso 2 (20 × 0,175) | 1 tramo + 2 zancas (Soportes), barandas de 6,27 m | 0 (el tipo no tiene materiales con volumen) |
  > | 811520 | T9-SIN-NIVEL-SUP | Moldeada in situ : Escalera monolítica v0, recta | Piso 1, sin nivel superior, altura 1,40 m (8 × 0,175) | 1 tramo, barandas de 2,31 m | 0,586 m³ |
  >
  > - Confirmado en Revit: todas las escaleras y sus tramos tienen `LevelId = -1`. El nivel solo está en `STAIRS_BASE_LEVEL_PARAM`, que en español se llama **"Nivel base"**.
  > - **Doble conteo posible:** el volumen de la escalera ya incluye el de sus partes. En la escalera en U, tramos 0,769 + 0,794 m³ y descansillo 0,584 m³ suman los 2,147 m³ de la escalera. Por eso hay que cuantificar solo la escalera y no listar tramos, descansillos ni soportes (`OST_StairsRuns`, `OST_StairsLandings`, `OST_StairsStringerCarriage`). Estas categorías tienen `HasMaterialQuantities = true` y pasan el filtro de `MainWindowViewModel.cs:915`.
  > - Escalera de acero: con su tipo de fábrica no da volumen ni en la escalera ni en sus partes. Para cuantificarla en kg habría que leer el peso de las zancas y peldaños (ver tarea 1).
  > - Falta la **escalera in situ** (familia de modelo in situ, el caso del PR #160). La API no permite crearla; hay que modelarla a mano: Arquitectura → Componente → Modelar in situ → categoría Escaleras → una extrusión de escalones, por ejemplo en x = 32 m.
  > - **Por probar en el add-in:** qué aparece en la lista (escalera, tramos, descansillo, zancas, barandas), el nivel de cada una y el volumen que muestra la tabla.

  > **Estado al 10/10/2026:** #172 está cerrada en GitHub, pero faltan sus dos criterios: un ejemplo de cada categoría problemática (barandas, celosías, genéricos, techos, cielorrasos, rampas…) y la guía para el modelador con el resultado de cada prueba.
- [ ] **10. Datos de barandas, celosías y elementos genéricos** (#173)
  - Revisar qué entrega la API de Revit para cada uno.
  - Estado al 10/10/2026: sin empezar. Punto de partida: las barandillas de la escalera de la tarea 9 no aparecen en la lista.
- [x] **11. Ubicación dentro del piso** *(con Joseph)* (#174)
  - Ligar cada elemento a su zona (alcoba, sala…) con Rooms o Áreas de Revit (`FamilyInstance.Room`, `FromRoom`/`ToRoom`).
  - Evaluar si la apertura de puertas se puede automatizar con `FacingFlipped`/`HandFlipped`.

  > **Estado: terminada (09/10/2026)**
  > - El lugar sale de `FamilyInstance.FromRoom` / `ToRoom`, la habitación a cada lado de la puerta o ventana:
  >   - Habitación a los dos lados (puerta interior): `Sala 1 / Alcoba 1 2`.
  >   - Un solo lado (fachada): `Sala 1 (fachada)`.
  >   - Ningún lado: `Sin habitación`, que avisa que falta modelarla.
  > - Se usaron Habitaciones y no Áreas: lo que pidió Carlos ("esta ventana hace parte de la alcoba 1") es el dato directo de las Habitaciones. Con Áreas habría que calcular la posición con geometría.
  > - Apertura: `FacingFlipped` / `HandFlipped` dependen de cómo se dibujó la familia, y las puertas de doble sentido o corredizas no tienen parámetro estándar. Queda manual (lista desplegable en Detalles).
  > - Qué debe modelar el arquitecto: Habitaciones con nombre en cada espacio, muros con "Delimitación de habitación" activada y en la misma fase que la puerta o ventana.
  > - Código: `DoorWindowDataExtractor` (columna LUGAR), commits `4313c94` y `742680f`.
  > - Solo cubre puertas y ventanas. Para barandas y otras categorías (tarea 13) habrá que buscar otra forma, porque no tienen `FromRoom`/`ToRoom`.

## Fase 4 – Tablas arquitectónicas (dependen de la arquitecta)

- [x] **12. Puertas y ventanas, por tipo** (#175)
  - Código (P1, V1…), descripción corta, cantidad, hojas, ubicación (piso + lugar), apertura (manual), base × altura, material, ~~observaciones~~ (van en la web).
  - Agrupación: carpintería metálica / carpintería en madera.

  > **Estado: terminada (09/10/2026)**
  > - Puertas y ventanas usan la misma tabla: No., CÓDIGO, DESCRIPCIÓN, CANTIDAD, NIVEL, LUGAR, MEDIDAS, MATERIAL, HOJAS y APERTURA, con fila TOTAL. Anchura y altura se leen de la puerta o de la ventana según la categoría.
  > - HOJAS (texto) y APERTURA (lista) se editan en Detalles y se copian a los elementos del mismo código, porque son datos del tipo.
  > - Guardar Modelo agrega estos datos a las puertas y ventanas ya asignadas.
  > - Código: `DoorWindowDataExtractor`, `DoorWindowManualFields`, `DynamicTableBuilder` (commits `4313c94` y `742680f`). Fusionado en `Actualizaciones` (PR #184).
  > - Criterios de #175 cumplidos:
  >   - Total por tipo: cada actividad es un tipo (p. ej. "Ventana batiente 2 hojas 1,20 × 1,80"), con una fila por elemento y su CÓDIGO, más la fila TOTAL. El cuadro agrupado por piso y con una fila por tipo (formato del Excel de la arquitecta) lo arma la interfaz web (`investigacion-puertas-ventanas.md`).
  >   - Medidas sin depender del idioma: se leen con `DOOR_WIDTH`/`DOOR_HEIGHT` y `WINDOW_WIDTH`/`WINDOW_HEIGHT` (`BuiltInParameter`), y el código con `ALL_MODEL_TYPE_MARK`.
  > - Por confirmar con Carlos: la agrupación carpintería metálica / carpintería en madera no está en el add-in, y el Excel de la arquitecta agrupa por piso y por tipo. Si hace falta, sería en la interfaz web.
- [ ] **13. Barandas y pasamanos (ml) y divisiones (m²)** (#176)
  - Código, metros lineales o m², ubicación (piso + lugar), material.
- [ ] **14. Resto de categorías** (#177)
  - Techos y cubiertas, cielorrasos, vidrio, rampas, celosías y genéricos.

## Fase 5 – Más adelante

- [ ] **15. Perfiles preconfigurados** (#178)
  - Perfiles como IPE 300 o HEA 300 creados en Revit con su peso lineal ya cargado.

## Bloqueos (a cargo de Carlos)

- Definir con la arquitecta qué lleva cada tabla (bloquea 13 y 14).
- ~~Qué significa "Hojas"~~ y ~~enviar el Excel del listado de puertas y ventanas~~: resueltos. Hojas son las partes que se mueven; las que no se mueven son "fijos" (p. ej. "3 + fijo"). Ver `investigacion-puertas-ventanas.md`.
- Confirmar si puertas y ventanas se agrupan por carpintería metálica / madera en la interfaz web (tarea 12).
- Asegurar que las familias de perfiles tengan cargado "Peso lineal metálico" (sin eso, la tarea 1 lee 0).
