# Plan: add-in de imágenes por actividad (issue #183)

## Contexto

Issue: [utilidadcqeing-prog/Add-in-Cuadro-de-cantidades-V0#183](https://github.com/utilidadcqeing-prog/Add-in-Cuadro-de-cantidades-V0/issues/183),
asignada a luffyjpl. Pedido de Carlos en la reunión del 09/10/2026: hoy las imágenes de cada
actividad del presupuesto se arman a mano (seleccionar, poner colores, transparencia, recortar y
subir) y eso toma mucho tiempo.

En esta etapa lo único que importa es **crear y guardar** las imágenes. Mostrarlas en el cuadro de
cantidades o en la web queda para después.

- Maqueta interactiva: https://claude.ai/artifact/FaGSVM936JnA7GZjzvW8Ao
- Imagen de referencia (hecha a mano, la que el add-in debe lograr solo):
  `docs/planes/imagenes/2026-10-13-referencia-imagen-actividad.jpeg`. Isométrica 3D con fondo
  blanco: los elementos de la actividad (aquí, columnas de un piso) opacos y con su sombreado; todo
  el resto del modelo como "fantasma" (gris claro, translúcido, aristas finas). Hoy hacer esto a
  mano en Revit son varios pasos (duplicar vista, aislar/sobrescribir gráficos, transparencia,
  recortar, exportar); el add-in tiene que hacerlos todos de forma automática.
- Modelo de prueba: `Prueba 080720sfsd26.rvt` (local del usuario, no se sube al repo).
- Rama de trabajo: `addin-de-imagenes` (al 2026-10-13 es idéntica a `main`; se arranca de cero).
- Entorno de prueba: Revit 2026.5 (RevitAPI 26.5, `net10.0-windows`).

## Flujo que pide la issue (y muestra la maqueta)

1. **Presupuesto** *(el bloqueo por vínculo de este paso se reemplazó: ver decisión 5)*: selector
   con buscador (código, nombre) y estado del vínculo:
   "Vinculado a este modelo", "Sin vínculo", "Vinculado a otro modelo · solo lectura".
   Si está vinculado a otro modelo, se ven las imágenes pero no se crean.
2. **Estructura**: capítulos → subcapítulos → actividades, cada una con su código y cuántas
   imágenes tiene. Buscador "por código o descripción".
3. **Tablero de la actividad**: solo las imágenes de esa actividad (código, nombre, descripción,
   vista 3D/planta, leyenda de colores por tipo).
4. **Nueva imagen → "Ir a Revit"**: el add-in cede el control; el usuario acomoda la vista y
   selecciona. Opción "Seleccionar todos los del mismo tipo" (un clic en una columna 25x30 toma
   todas las 25x30). Contador "N seleccionados", botones **Cancelar** / **Aceptar**.
5. **Colores**: un color sólido por tipo seleccionado. Paleta de la maqueta:
   `#E0453A #F08C1A #E3BD12 #2BA36B #1F8FD6 #4B5BD6 #9B4BD6 #D6457F` + color libre.
   El resto del modelo queda **70 % translúcido, automático**, sin control para el usuario.
6. **Nombre y guardar**: código automático `ACT_006_01` (código de actividad + número de imagen
   de dos cifras; no se usa el número de ítem porque cambia al reordenar), nombre y descripción.

## Datos (sin cambios en la web ni en la API)

- `GET /api/presupuestos` y `GET /api/presupuestos/{codigo}/estructura` (→ `codigo_actividad`,
  `descripcion`, `unidad`), con el mismo `X-Api-Key` del cuadro de cantidades.
- `GET /api/presupuestos/by-revit-model/{revitModelId}` para saber a qué presupuesto está
  vinculado el modelo abierto.
- **No se usan**: `/api/presupuestos/{codigo}/galeria` (galería del portal del cliente) ni
  `POST /api/presupuestos/items/{id}/elementos` (pisa elementos y cantidades).
- **Guardado** (propuesta de la issue): Cloudinary con el preset sin firma del add-in de
  cantidades.
  - Carpeta `cuadro_cantidades/imagenes/{presupuesto}/{actividad}/`, `public_id` = código.
  - `tags`: `{presupuesto}` y `{presupuesto}_{actividad}`.
  - `context`: código, nombre y descripción.
  - Copia local con el mismo registro, para el tablero y para trabajar sin conexión.

## Código que se reutiliza del add-in de cantidades

Revisado en `upstream/Actualizaciones` del repo de cantidades (rama más reciente, 2026-10-09).
Ese proyecto es otro `.csproj` (`CQEING_CuadroCantidades`, net8, con DI y EF Core), así que **se
copia y adapta**, no se referencia:

| Qué | Origen | Qué cambia |
|---|---|---|
| Resaltado con relleno sólido (superficie y corte) y resto al 70 % | `Services/ElementHighlightService.cs` (113 líneas) | Hoy el color es azul fijo y se aplica a una lista de ids. Pasa a recibir un **color por tipo** (`Dictionary<ElementId tipo, Color>`). |
| Vista temporal, encuadre y exportación a PNG | `Services/RevitImageCaptureService.cs` (444 líneas) | Hoy encuadra **todo el modelo** en una isométrica nueva. Aquí se **duplica la vista que acomodó el usuario** (3D o planta) para respetar su encuadre. Se conserva el renombrado seguro de la vista (`{3D - usuario}` con `\` rompe la ruta). |
| Subida a Cloudinary con reintentos | `Services/CloudinaryUploadService.cs` (165 líneas) | Hoy solo manda `upload_preset` y `folder` fijo. Se agregan `folder` por actividad, `public_id`, `tags` y `context`. |
| Cliente de la API, `.env`, vínculo con el modelo | `Services/ApiClient.cs`, `EnvLoader.cs`, `RevitModelIdentifier.cs` | Solo los tres GET de arriba. Nada de escritura. |
| Llamadas a Revit desde ventana no modal | `Core/RevitEventExecutor.cs` (126 líneas) | Se copia casi igual. |

Este repo nombra en español (`Logica/`, `Modelo/`, `Vistas/`, `GeneradorArmado`…); lo copiado se
adapta a ese estilo.

## Estructura propuesta

Lo nuevo nace en su propia carpeta, sin tocar el armado de columnas. Lo único que se toca fuera
de ella es `App.cs` (un panel "Imágenes" con un botón en la pestaña CQEING).

Así quedó implementado (2026-10-13):

```
Imagenes/
  ComandoImagenesActividad.cs  — botón del ribbon: abre la ventana no modal (una sola instancia)
  .env.example                 — plantilla; el real va en %AppData%\CQEING\imagenes\.env
  Configuracion/ ConfiguracionImagenes.cs (lee/crea el .env)
  Api/           ClienteApi.cs, DtosApi.cs, DatosDeEjemplo.cs (sin API configurada)
  Revit/         EjecutorRevit.cs (ExternalEvent), PuenteRevit.cs (selección y captura),
                 CapturaImagen.cs (la receta de la fase 0), IdentificadorModelo.cs
  Almacen/       SubidaCloudinary.cs, RegistroLocal.cs (JSON), CodigoImagen.cs
  Modelo/        Presupuesto.cs, ImagenActividad.cs, Seleccion.cs, PaletaColores.cs, IPuenteRevit.cs
  Vistas/        VentanaImagenesActividad.xaml(.cs) (tablero + editor), BarraSeleccion.xaml(.cs),
                 FilasDeVista.cs
```

Las ventanas no referencian `Autodesk.Revit` (misma regla que el resto del repo): hablan con Revit
solo por `IPuenteRevit`.

`CodigoImagen` (siguiente `ACT_NN`) y el armado del árbol con búsqueda son lógica pura, sin
Revit, para poder probarlos sin abrir Revit.

## Fase 0 — verificar contra Revit real antes de programar

Regla del `CLAUDE.md`: no dar por buena ninguna conducta de la API sin probarla en un modelo real.
Ahora además hay dos MCP conectados a Revit (`revit-mcp` y `revit-claude-connector`) que permiten
leer vistas, tipos, selección y overrides sin compilar nada. Lo que hay que confirmar:

1. **Selección desde ventana no modal.** ¿`Selection.PickObjects` funciona llamado desde un
   `ExternalEvent`? Alternativa de la issue: leer `uidoc.Selection.GetElementIds()` al pulsar
   Aceptar (más simple y no bloquea; el usuario selecciona con las herramientas normales de
   Revit). "Mismo tipo" = todos los elementos con el mismo `GetTypeId()` **en la vista**.
2. **Color por elemento en 3D y en planta.** `SetElementOverrides` con relleno sólido en
   superficie **y en corte** (en planta las columnas se ven cortadas). Confirmar que la
   transparencia de superficie al 70 % del resto se nota en planta, o si ahí solo sirve halftone.
3. **No dejar rastro en el modelo.** Probar: `TransactionGroup` → transacción que duplica la vista
   y aplica overrides → `ExportImage` (fuera de la transacción) → `RollBack` del grupo. Si funciona,
   no queda la vista temporal ni una entrada en "Deshacer". Si no, se borra la vista como hoy.
4. ~~Convivencia de DLLs con SQLite~~: descartado, la copia local va en JSON (ver Decisiones).
5. **Que la imagen salga como la de referencia.** Sobre el modelo de prueba, reproducir con los
   MCP el aspecto de la imagen de referencia (elementos opacos, resto fantasma, fondo blanco) y
   anotar qué combinación de overrides lo logra: transparencia, halftone, color de línea, estilo
   visual.

Lo que se encuentre se anota en este archivo antes de pasar a la fase 1.

### Resultados de la fase 0 (2026-10-13, punto 5)

Probado con `revit-mcp` (`send_code_to_revit`) sobre `Prueba 080720sfsd26.rvt`, vista `{3D}`
(sin worksharing; 40 columnas en 3 tipos, 54 vigas, 12 zapatas, un Toposolid de 20 × 30 m).
Se pintaron las 12 columnas `(0.30 x 0.30)` del Piso 1. Resultado final:
`docs/planes/imagenes/2026-10-13-fase0-resultado.png`. La vista de prueba se borró al terminar.

Lo que hay que hacer (en este orden) sobre la **copia** de la vista del usuario para lograr la
imagen de referencia:

1. `DisplayStyle = ShadingWithEdges` y `DetailLevel = Fine`. La vista del usuario estaba en
   `FlatColors`; se fuerza para que todas las imágenes salgan iguales.
2. **Fondo blanco**: `view.SetBackground(ViewDisplayBackground.CreateGradient(blanco, blanco, blanco))`.
   Sin esto la imagen sale con el fondo del tema de Revit del usuario (oscuro en este PC).
3. **Ocultar anotaciones**: `view.AreAnnotationCategoriesHidden = true` (sin esto salen las líneas
   y etiquetas de nivel).
4. **Ocultar terreno y entorno**: categorías `OST_Toposolid`, `OST_Topography`, `OST_Site`,
   `OST_Planting`, `OST_Entourage`, `OST_Cameras`. El Toposolid tapaba y achicaba la estructura.
5. **Fantasma elemento por elemento, NO por categoría** (hallazgo importante): a cada elemento de
   modelo visible que **no** esté seleccionado, `SetElementOverrides` con halftone, transparencia
   70, relleno sólido gris `(200,200,200)` y líneas `(160,160,160)` de grosor 1.
   - Primero se probó como lo hace el add-in de cantidades (fantasma por categoría + color por
     elemento) y **las columnas seleccionadas salían translúcidas** (rosa pálido). Motivo: en
     `OverrideGraphicSettings`, un valor igual al de fábrica (transparencia 0, halftone apagado)
     significa "no sobrescribir", así que el elemento hereda el 70 % de su categoría. No se puede
     "anular" la transparencia de la categoría desde el elemento.
6. **Color de los seleccionados**: relleno sólido de superficie y de corte con el color del tipo,
   líneas de proyección más oscuras del mismo tono y grosor 3. Nada más (no tocar transparencia ni
   halftone).
7. `ExportImage` con `ZoomFitType.FitToPage`, PNG.

Otros hallazgos:
- **`ExportImage` funciona con una transacción abierta** (el MCP corre todo dentro de una). Falta
  probar el `TransactionGroup` + `RollBack` (punto 3), que no se puede desde el MCP; se hará con un
  comando de diagnóstico temporal en la fase 1.
- **Revit 2026 quitó `ElementId.IntegerValue`**: hay que usar `ElementId.Value` (`long`) y
  `new ElementId(long)`. El código de cantidades que use `IntegerValue` no compila tal cual.
- **Encuadre**: `FitToPage` encuadra todo lo visible. En este modelo hay un cerramiento perimetral
  de vigas delgadas (perfiles `TEST Tubo 100x100x3` y `0.10 x 0.35`) que agranda el cuadro y deja
  la estructura chica. Como la issue dice que el usuario "acomoda la vista" antes de seleccionar, se
  respeta su caja de sección/recorte al duplicar; si no tiene, se propone una caja de sección
  automática alrededor de los elementos visibles sin terreno (a definir en la fase 3).
- Puertas, ventanas y escaleras salen algo más oscuras que el resto del fantasma (tienen
  subcomponentes con sus propios materiales). Revisar en la fase 3 si molesta.
- Pendientes de la fase 0: punto 1 (selección desde ventana no modal) y punto 2 en **planta**.

## Fases de implementación

1. **Esqueleto y datos.** Botón en el ribbon, ventana no modal, `.env` (`API_URL`, `API_KEY`),
   lista de presupuestos con buscador y estado del vínculo, árbol de la estructura con buscador.
   *Criterio 1 de la issue.*
2. **Selección.** "Ir a Revit" minimiza/oculta la ventana, barra de Aceptar/Cancelar con contador y
   "mismo tipo", vuelta al add-in con los elementos agrupados por tipo. *Criterio 2.*
3. **Colores y captura.** Un color por tipo (paleta + libre), resto translúcido automático, vista
   previa, exportación PNG desde la vista del usuario (3D o planta). *Criterios 3 y 5.*
4. **Guardar y tablero.** Código `ACT_NN`, nombre, descripción, subida a Cloudinary, copia local,
   tablero que muestra solo las imágenes de la actividad, modo solo lectura. *Criterios 4 y 6.*

Cada fase se compila, se despliega (`tools/deploy.ps1`, con Revit cerrado) y se prueba en Revit
antes de seguir.

## Riesgos y decisiones

- **Número de imagen repetido entre PCs.** Si el siguiente `NN` se calcula solo con la copia local,
  dos personas en PCs distintas pueden generar el mismo `ACT_006_02`. Mitigación: consultar
  Cloudinary por tag (requiere "Resource list" activo) antes de numerar; si la subida dice que el
  `public_id` ya existe, pasar al siguiente número. Las subidas sin firma no sobrescriben, así que
  no se pierde nada, pero hay que detectarlo.
- **La vista del usuario no se modifica.** Toda la pintura va sobre una copia temporal.
- **Modelos colaborativos.** Duplicar una vista no requiere tomar elementos prestados, pero hay
  que probarlo en un modelo con worksharing.

## Decisiones (2026-10-13, con luffyjpl)

1. **Cloudinary: cuenta propia nueva**, no el preset `addin_cuadro_cantidades`. Falta crear en
   esa cuenta un *upload preset* **sin firma** y confirmar que permite `public_id`, `tags` y
   `context`, y activar "Resource list" (Settings → Security). El *cloud name* y el nombre del
   preset no son secretos (cualquier subida sin firma los expone), pero igual van en `.env`.
2. **Copia local: JSON, no SQLite.** Un archivo por presupuesto en
   `%AppData%\CQEING\imagenes\{presupuesto}.json`. Por qué (va también como comentario en
   `Almacen/RegistroLocal.cs`):
   - El add-in de cantidades carga `Microsoft.EntityFrameworkCore.Sqlite 8.0.10` (con su
     `e_sqlite3` nativo) en el mismo proceso de Revit. Si este add-in trae otra versión, la que
     cargue primero gana y la otra puede fallar al arrancar: un error difícil de diagnosticar que
     depende del orden en que Revit carga los add-ins.
   - El volumen es chico (decenas de imágenes por presupuesto) y no hace falta consultar con SQL;
     un JSON se lee entero en milisegundos y se puede abrir a mano para revisar.
   - Si más adelante hace falta SQLite, el registro está detrás de una sola clase y se cambia ahí.
3. **Ubicación: este repo, rama `addin-de-imagenes`**, como un botón más de la pestaña CQEING.
   Queda anotado como provisional (comentario en `App.cs` y en `ComandoImagenesActividad.cs`): la
   issue vive en el repo de cantidades y el equipo puede decidir moverlo a su propio proyecto o al
   de cantidades. Por eso todo va en `Imagenes/` sin depender del armado de columnas: moverlo es
   copiar la carpeta.
4. **Configuración: `.env` propio en `%AppData%\CQEING\imagenes\.env`**, con
   `Imagenes/.env.example` en el repo y `.env` en `.gitignore`. Primero se pensó junto al `.dll`,
   pero `practicacq.dll` se instala directo en `Addins\{año}\`, carpeta compartida por todos los
   add-ins de Revit, y un `.env` suelto ahí puede pisarse con el de otro. En AppData además sobrevive
   a reinstalar. Si no existe, el add-in lo crea con los datos de Cloudinary ya puestos y la API
   vacía:
   ```
   API_URL=https://...            # la misma del add-in de cantidades
   API_KEY=...                    # la misma X-Api-Key del add-in de cantidades
   CLOUDINARY_CLOUD_NAME=...      # de la cuenta nueva
   CLOUDINARY_UPLOAD_PRESET=...   # preset sin firma de la cuenta nueva
   ```
   Por qué propio y no compartir el del add-in de cantidades: la cuenta de Cloudinary ya es otra,
   los dos add-ins se instalan en carpetas distintas y pueden actualizarse por separado, y así
   ninguno se rompe si el otro cambia su `.env`. Si falta alguna variable, el add-in lo dice al
   abrir en vez de fallar al guardar.
5. **El modelo de Revit se vincula a cada imagen de una actividad, NO al presupuesto** (pedido de
   luffyjpl, 2026-10-13). Reemplaza el paso 1 de la issue ("si el presupuesto está vinculado a otro
   modelo, solo lectura"):
   - Ya no hay modo solo lectura ni se mira el `revitModelId` del presupuesto (ese vínculo es del
     add-in de cantidades). Desde cualquier modelo se pueden crear imágenes en cualquier
     presupuesto.
   - Cada imagen guarda `IdModelo` (mismo cálculo que `RevitModelIdentifier` del add-in de
     cantidades) y `NombreModelo` (título del documento), en el `registro.json` y en el `context`
     de Cloudinary (`modelo`, `nombre_modelo`).
   - El tablero de la actividad muestra todas sus imágenes, primero las de este modelo, y cada
     tarjeta dice de qué modelo es ("este modelo" en verde, "otro modelo" en naranja).
   - "Mismo modelo" compara solo la parte del id antes de `:`, para que en modelos colaborativos
     cuente igual una imagen hecha desde otra copia local del mismo modelo central.
   - Las imágenes guardadas antes de este cambio (`ACT_006_01`) muestran "Modelo: sin registrar".

## Estado

- [x] Análisis de la issue, la maqueta y el código reutilizable.
- [x] Decisiones sobre Cloudinary, copia local, ubicación y configuración.
- [x] Datos de la cuenta nueva de Cloudinary: cloud name `chhoueyn`, preset sin firma
  `addin_imagenes_actividad`.
- [~] Fase 0: verificación en Revit real. Hecho: aspecto de la imagen en 3D (punto 5) y exportación.
  Falta: selección desde ventana no modal, planta y `TransactionGroup` + `RollBack` (los tres se
  prueban con la primera versión del add-in, ver abajo).
- [~] Fases 1 a 4: primera versión completa implementada el 2026-10-13 (compila para Revit 2026.5 /
  net10). Pendiente de prueba en Revit con el modelo de prueba.
  - Sin `API_URL`/`API_KEY` trabaja con **datos de ejemplo** (los de la maqueta) y lo avisa en la
    barra de estado; así se puede probar todo el flujo visual antes de tener la API. Las imágenes se
    generan, se guardan y se suben de verdad.
  - Selección: con las herramientas normales de Revit + barra flotante (Aceptar/Cancelar, contador,
    "mismo tipo" que amplía la selección en vivo con `UIApplication.SelectionChanged`). No se usa
    `PickObjects` porque bloquea Revit y no deja acomodar la vista.
  - "Vista previa" y "Guardar" usan la misma captura; si la vista previa está al día, Guardar la
    reutiliza sin volver a Revit.
  - Subida: si el código ya existe en Cloudinary (otra PC), pasa al siguiente número y renombra la
    copia local. Si falla la conexión, la imagen queda en la PC y aparece "Reintentar subidas
    pendientes".

### Evaluación de los criterios de aceptación (2026-10-13, primera prueba en Revit)

Prueba del usuario sobre `Prueba 080720sfsd26.rvt`, vista `{3D}`, con datos de ejemplo. Evidencia:
`%AppData%\CQEING\imagenes\PROY_004_V2\` (`ACT_006_01.png` + `registro.json`) y la URL
`https://res.cloudinary.com/chhoueyn/image/upload/v1791882820/cuadro_cantidades/imagenes/PROY_004_V2/ACT_006/ACT_006_01.png`.

| # | Criterio | Estado | Evidencia / qué falta |
|---|---|---|---|
| 1 | Lista presupuestos y estructura con los códigos **reales** de la API | ✅ Cumple (sin Revit) | Verificado con un programa de consola que usa la misma `practicacq.dll` contra la API de producción (`https://cqingenieria-production.up.railway.app`): 9 presupuestos con su estado de vínculo y la estructura de PROY_001 (3 capítulos, 13 actividades, `ACT_001`…). Falta verlo en la ventana dentro de Revit. |
| 2 | Ir a Revit, seleccionar (también todos los del mismo tipo) y volver con Aceptar | ✅ Cumple | Flujo probado por el usuario. Se seleccionaron las losas `Floor : Losa macisa e = 0.12`. |
| 3 | Cada tipo con su color; el resto translúcido sin configurar nada | ✅ Cumple (1 tipo) | La imagen muestra la losa en `#E0453A` y el resto como fantasma al 70 %. Falta probar **varios tipos con colores distintos**. |
| 4 | Se guarda como `ACTIVIDAD_NN` con nombre y descripción, y aparece solo en su actividad | ✅ Cumple | `ACT_006_01`, nombre "prueba", en el registro de PROY_004_V2 / ACT_006 y en Cloudinary bajo `.../PROY_004_V2/ACT_006/`. |
| 5 | Funciona en 3D y en planta | ⚠️ Parcial | 3D probado. **Planta sin probar.** |
| 6 | No se modifica la interfaz web ni las tablas de la API | ✅ Cumple | Por diseño: el add-in solo hace `GET /api/presupuestos` y `GET .../estructura`; no llama a `galeria` ni a `items/{id}/elementos`. No se tocó el repo `CQEING_BaseDeDatos`. |

Otros puntos a confirmar antes del PR:
- **Sin rastro en el modelo**: que en "Deshacer" no quede ninguna entrada y que no queden vistas
  `CQ_Imagen_*` (prueba del `TransactionGroup` + `RollBack`).
- ~~**Solo lectura**~~: ya no aplica (decisión 5). En su lugar: una imagen nueva debe mostrar
  "Modelo: <nombre> (este modelo)" en el tablero y llevar `modelo`/`nombre_modelo` en Cloudinary.
- ~~**Cloudinary**: tags y contexto~~ ✅ Verificado: `https://res.cloudinary.com/chhoueyn/image/list/PROY_004_V2_ACT_006.json`
  responde (Resource list activo) con `ACT_006_01`, sus dos tags y `context.custom` (codigo,
  nombre; la descripción vacía no se guarda). El cuadro de cantidades podrá listar las imágenes de
  una actividad con esa URL, sin clave secreta.
- ~~**Código repetido en Cloudinary**~~ ✅ Verificado con una imagen de 1 px en `pruebas_addin/`:
  la segunda subida con el mismo `public_id` no sobrescribe y responde `existing: true` con la misma
  versión, que es lo que usa `SubirAsync` para pasar al siguiente número.

Hallazgos al conectar la API real (2026-10-13):
- `GET /api/presupuestos` responde en **camelCase y paginado** (`codigoPresupuesto`, `page`,
  `pages`), no en snake_case como asumía el código copiado del add-in de cantidades; la estructura sí
  es snake_case. Corregido en `DtosApi.cs` (acepta los dos estilos) y `ClienteApi.cs` (recorre las
  páginas). Sin la prueba de consola, la lista de presupuestos habría salido vacía.
- ~~El modelo de prueba no está vinculado a ningún presupuesto, así que 7 salían en solo lectura~~:
  resuelto por la decisión 5 (el vínculo con el modelo va en cada imagen, no en el presupuesto).
- **Encuadre**: el cerramiento perimetral del modelo de prueba deja la estructura chica en la imagen.
  Funciona como se diseñó (respeta lo que muestra la vista del usuario); si se quiere un encuadre
  automático más ajustado, es una mejora aparte.
- Corrección menor ya hecha (sin desplegar todavía): el `registro.json` guardaba dos campos
  calculados (`SubidaPendiente`, `TextoEstadoSubida`); ahora llevan `[JsonIgnore]`.

### Arreglo en `tools/deploy.ps1` (afecta a todo el repo)

Con Revit 2026.5 (net10) el deploy fallaba con `NETSDK1005`: el restore implícito de
`dotnet build` descarta el `-p:TargetFramework` y restaura con el default del `.csproj`
(`net8.0-windows` para 2026). Ahora el script hace `dotnet restore` explícito con los mismos
parámetros y después `dotnet build --no-restore`. Sin `-TargetFramework` (caso 2027) no cambia nada.

### Por probar en Revit (primera versión)

1. Abre la ventana, lista los presupuestos de ejemplo con su estado de vínculo y la estructura con
   buscador.
2. Ir a Revit → seleccionar una columna con "mismo tipo" → se seleccionan todas las del tipo en vivo.
3. Aceptar → tipos con colores de la paleta → vista previa como la de la fase 0.
4. Cambiar un color → Actualizar vista previa.
5. Guardar → aparece en el tablero con código `ACT_006_01` y sube a Cloudinary.
6. En "Deshacer" de Revit no debe quedar nada (prueba del `TransactionGroup` + `RollBack`).
7. Lo mismo desde una vista de **planta**.
8. ~~Presupuesto vinculado a otro modelo: sin botón Nueva imagen~~ → reemplazado por la decisión 5:
   la imagen nueva muestra su modelo en el tablero.
