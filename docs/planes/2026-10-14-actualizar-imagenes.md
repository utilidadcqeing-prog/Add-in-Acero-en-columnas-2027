# Plan: actualizar imágenes de actividad cuando cambia el modelo (#186)

## Contexto

- Issue: utilidadcqeing-prog/Add-in-Cuadro-de-cantidades-V0#186, sub-issue de #183 (add-in de imágenes
  por actividad, plan `docs/planes/2026-10-13-addin-imagenes-por-actividad.md`).
- Qué se pide (josetp, 2026-10-14): cuando el modelo de Revit cambia, las imágenes guardadas quedan
  viejas. Hacen falta dos acciones:
  1. **Actualizar todas:** un botón general que regenera todas las imágenes del presupuesto que salieron
     del modelo abierto.
  2. **Actualizar esta:** un botón dentro de cada imagen para regenerar solo esa.
- Al actualizar se conservan el código (`ACT_006_01`), el nombre, la descripción y los colores por tipo.
  Solo cambian el PNG, la versión y la fecha.
- Fuera de alcance: detectar solo qué imágenes cambiaron (se regeneran todas las del modelo); mostrar las
  imágenes en el cuadro de cantidades o en la web; tocar la API o la interfaz web.
- Entorno: Revit 2026.5 (net10.0-windows). Rama `addin-de-imagenes` (decisión 2: siempre esta rama hasta
  nuevo aviso). PR abierto: Add-in-Acero-en-columnas-2027#68.

## Criterios de aceptación

Copiados de la issue #186:

- [ ] Cada imagen nueva guarda los `UniqueId` de sus elementos y de la vista, y la orientación 3D.
- [ ] El botón **Actualizar esta** regenera una imagen con el mismo código, nombre, descripción y colores.
- [ ] El botón **Actualizar todas** regenera las imágenes del modelo abierto con progreso, opción de
      cancelar y resumen final.
- [ ] Los elementos borrados del modelo se omiten con aviso, sin romper la actualización.
- [ ] La imagen actualizada reemplaza a la anterior en el tablero y en Cloudinary (versión nueva subida,
      versión anterior borrada).
- [ ] Funciona en 3D y en planta.
- [ ] No se modifica la interfaz web ni las tablas de la API.

## Hallazgos del código y de Revit real

Leído en la rama `addin-de-imagenes` (commit `ced5366`). Falta confirmar en Revit lo marcado con *(probar)*.

1. **La imagen guardada no recuerda qué se seleccionó.** `Imagenes/Modelo/ImagenActividad.cs` guarda
   `NombreVistaRevit`, `TipoVista` y `Colores` (solo `NombreTipo` + `ColorHex`). Los ids sí existen en
   `ResultadoSeleccion` (`IdVista`, `TipoSeleccionado.IdsElementos`, en `Imagenes/Modelo/Seleccion.cs`)
   pero se pierden en `RegistrarYSubirAsync` (`Imagenes/Vistas/VentanaImagenesActividad.xaml.cs:479`).
2. **Se guardan `ElementId` (long), no `UniqueId`.** En modelos colaborativos el `ElementId` es estable
   dentro del central, pero `UniqueId` es lo recomendado para guardar referencias fuera de Revit. Se usa
   `UniqueId` y `doc.GetElement(string)` para recuperarlos.
3. **La orientación 3D depende de la vista base.** `CapturaImagen.Exportar` (`Imagenes/Revit/CapturaImagen.cs:59`)
   duplica la vista donde se seleccionó; si después alguien la gira, la imagen actualizada sale distinta.
   Hay que guardar `ViewOrientation3D` (ojo, adelante, arriba) y la caja de sección, y aplicarlos a la
   copia con `View3D.SetOrientation` y `View3D.SetSectionBox` *(probar que `SetOrientation` funciona en
   la copia sin bloquear y que la caja de sección se respeta al exportar)*.
4. **Planta:** la copia exporta con `ZoomFitType.FitToPage`; no hay cámara que guardar. Se guarda la
   región de recorte si está activa (`CropBox` + `CropBoxActive`) *(probar si hace falta en la práctica)*.
5. **Cloudinary sin firma nunca sobrescribe ni borra.** `SubidaCloudinary.SubirAsync`
   (`Imagenes/Almacen/SubidaCloudinary.cs:33-38`) devuelve `CodigoOcupado` si el `public_id` ya existe.
   Borrar exige la API firmada: `POST https://api.cloudinary.com/v1_1/{cloud}/image/destroy` con
   `public_id`, `timestamp`, `api_key` y `signature = SHA1("public_id=…&timestamp=…" + api_secret)`.
6. **El `public_id` real puede incluir la carpeta.** Se envía `folder` + `public_id` en la subida; según el
   modo de carpetas de la cuenta (fijo o dinámico), Cloudinary devuelve `cuadro_cantidades/imagenes/…/ACT_006_01`
   o solo `ACT_006_01`. Para borrar hay que usar exactamente el `public_id` que devolvió la subida: hoy
   `RespuestaCloudinary` no lo lee *(probar en la cuenta `chhoueyn` qué devuelve)*.
7. **`CodigoImagen.SiguienteNumero`** (`Imagenes/Almacen/CodigoImagen.cs:20`) ignora sufijos que no son
   número (`ACT_006_01_v2` no parsea): versionar el `public_id` no rompe la numeración, siempre que el
   `Codigo` de la imagen se quede como `ACT_006_01`.
8. **Ya existe un botón "Actualizar códigos"** en la barra superior (`VentanaImagenesActividad.xaml:48`)
   que recarga presupuestos. El botón nuevo debe tener otro nombre para no confundirse.
9. **Error encontrado en Revit real (2026-10-14): el resaltado de selección sale en la imagen.** Con
   los elementos todavía seleccionados (como quedan después de pulsar Aceptar en la barra), `ExportImage`
   pinta el resaltado de selección de Revit (cian translúcido) encima de los colores. Visto con
   `PuenteRevit.Capturar` por MCP (`resultados/ACT_PRUEBA_01.png`); con la selección vacía los colores
   salen bien (`ACT_PRUEBA_01_sinSeleccion.png`). Afecta también a la #183.
   **Corrección:** `PuenteRevit.SinSeleccion` vacía la selección durante la exportación y la devuelve después.
10. **Error encontrado en Revit real (2026-10-14): en planta el fondo sale oscuro.** El comentario de
    `CapturaImagen` suponía que en planta el fondo de exportación ya es blanco, pero sale del fondo de
    Revit del usuario (tema oscuro: `Application.BackgroundColor` = 34,41,51). Probado por MCP: con el
    fondo en blanco durante `ExportImage` la planta sale blanca y el fondo del usuario se devuelve igual.
    **Corrección:** `CapturaImagen.Exportar` pone `Application.BackgroundColor` en blanco solo durante la
    exportación.

## Decisiones

1. **El código de la imagen no cambia** (2026-10-14, josetp): `ACT_006_01` sigue siendo el código que ve
   el usuario. La versión va aparte.
2. **Siempre en la rama `addin-de-imagenes`** (2026-10-14, josetp) hasta nuevo aviso.
3. **Cloudinary: `public_id` versionado y borrar la versión anterior** (2026-10-14, josetp). v1 = `ACT_006_01`
   (como hoy), v2 = `ACT_006_01_v2`, etc. Tras subir la nueva con éxito, se borra la anterior.
4. **El `api_secret` va en el `.env` de cada PC** (2026-10-14, josetp): `CLOUDINARY_API_KEY` y
   `CLOUDINARY_API_SECRET` en `%AppData%\CQEING\imagenes\.env`, nunca en el repo ni en `.env.example`
   con valor. No se toca la API del backend.
5. **Mismo tipo al actualizar** (2026-10-14, josetp, confirmada): si la imagen se hizo con "mismo tipo", al actualizar se vuelve a
   ampliar a todos los elementos visibles de esos tipos, así entran las columnas nuevas. Si no, solo los
   elementos guardados.
6. **Alcance de los botones** (2026-10-14, josetp, confirmada): "Actualizar todas" actúa sobre **todo el presupuesto** y va en la
   barra superior con el texto "Actualizar imágenes". "Actualizar esta" va en cada tarjeta del tablero y en
   el visor en grande.
7. **Imágenes viejas** (2026-10-14, josetp, confirmada): las imágenes guardadas antes de este cambio (sin `UniqueId`) no se pueden actualizar
   solas. Su botón queda deshabilitado con el aviso "Imagen anterior a la actualización automática:
   rehazla con Nueva imagen". En "Actualizar todas" cuentan como omitidas.
8. **Claves de Cloudinary** (2026-10-14): no estaban en `%AppData%\CQEING\imagenes\.env` de esta PC. Se
   agregaron las líneas `CLOUDINARY_API_KEY=` y `CLOUDINARY_API_SECRET=` vacías (respaldo
   `.env.bak-20261014`); josetp las llena. El código funciona sin ellas (decisión 4).
9. **Pruebas en Revit al final** (2026-10-14): los pasos 1-6 se implementan y compilan en orden, y la
   lista de "Cómo probarlo" se corre completa en Revit al terminar, porque cada prueba necesita al usuario
   en Revit con el add-in desplegado.
10. **Cuenta de Cloudinary de Juan y subida firmada** (2026-10-14, josetp + hallazgo): josetp pidió usar
    la cuenta `df6m0s0ri` (de Juan) con su API Key/Secret, en vez de `chhoueyn`. Su único preset sin firma
    (`b2npho5c`) es el de materiales de la web: carpeta fija `materiales/` y `public_id` sin carpeta, así
    que `ACT_006_01` de dos presupuestos chocarían. Por eso, con `CLOUDINARY_API_KEY` + `CLOUDINARY_API_SECRET`
    el add-in **sube con firma** (sin preset, `overwrite=false`) y el `public_id` lleva la ruta completa
    `cuadro_cantidades/imagenes/{presupuesto}/{actividad}/{codigo}[_vN]`, igual que `asset_folder`. Sin
    secret se sigue usando el preset sin firma (`CLOUDINARY_UPLOAD_PRESET`), como antes. No se crea ni
    cambia nada en la cuenta de Juan. Las imágenes de prueba que quedaron en `chhoueyn` no se migran.

## Plan paso a paso

1. **Guardar lo necesario para rehacer la imagen**
   - Archivos: `Imagenes/Modelo/Seleccion.cs`, `Imagenes/Modelo/ImagenActividad.cs`,
     `Imagenes/Revit/PuenteRevit.cs` (`LeerSeleccion`), `Imagenes/Vistas/VentanaImagenesActividad.xaml.cs`
     (`RegistrarYSubirAsync`).
   - `TipoSeleccionado` + `UniqueIdsElementos`; `ResultadoSeleccion` + `UniqueIdVista`, `MismoTipo`,
     `Camara` (nueva clase `CamaraVista`: ojo, adelante, arriba como `double[3]`, caja de sección
     min/max/transform o null, recorte de planta o null).
   - `ColorDeTipo` + `IdTipo`, `UniqueIdsElementos`. `ImagenActividad` + `UniqueIdVista`, `MismoTipo`,
     `Camara`, `Version` (int, 1 por defecto), `PublicIdCloudinary`, `PublicIdsPorBorrar` (lista).
   - Todo campo nuevo con valor por defecto: los `registro.json` viejos se siguen leyendo igual.
   - Cómo se prueba: crear una imagen y revisar `registro.json`.
2. **Capturar desde una imagen guardada** (Revit)
   - Archivos: `Imagenes/Revit/CapturaImagen.cs`, `Imagenes/Revit/PuenteRevit.cs`, `Imagenes/Modelo/IPuenteRevit.cs`.
   - `IPuenteRevit.Recapturar(ImagenActividad, rutaSinExtension, Action<ResultadoRecaptura?, string?>)`:
     resuelve vista y elementos por `UniqueId`, amplía al mismo tipo si corresponde, arma el
     `ResultadoSeleccion` con los colores guardados y llama a `CapturaImagen.Exportar` aplicando la cámara.
   - `ResultadoRecaptura`: ruta del PNG, elementos encontrados, elementos faltantes.
   - Vista borrada: se usa la vista 3D por defecto o la planta del nivel (si se puede resolver) con la
     cámara guardada; si no hay cómo, error claro.
   - Cómo se prueba: borrar un elemento y una vista en el modelo de prueba y recapturar.
3. **Subida versionada y borrado de la anterior**
   - Archivos: `Imagenes/Almacen/SubidaCloudinary.cs`, `Imagenes/Configuracion/ConfiguracionImagenes.cs`,
     `Imagenes/.env.example` (claves vacías).
   - `SubirAsync` acepta el `public_id` a usar (`Codigo` o `Codigo_vN`) y devuelve el `public_id` que
     respondió Cloudinary.
   - `BorrarAsync(publicId)`: `image/destroy` firmado (SHA-1). Respuesta `ok` o `not found` = éxito.
   - Sin `CLOUDINARY_API_SECRET`: la versión vieja queda en `PublicIdsPorBorrar` y se avisa una vez.
   - Cómo se prueba: actualizar una imagen y comprobar en la consola de Cloudinary que existe `_v2` y ya no
     la v1.
4. **Botón "Actualizar esta"**
   - Archivos: `Imagenes/Vistas/VentanaImagenesActividad.xaml(.cs)`, `Imagenes/Vistas/VisorImagen.xaml(.cs)`.
   - Flujo: recapturar → PNG local nuevo (`ACT_006_01_v2.png`) → registro (Version+1, Fecha, Url vacía) →
     subir → si sube, borrar la versión anterior en Cloudinary y el PNG local viejo.
   - Deshabilitado si la imagen es de otro modelo (`EsDelModelo`) o no tiene `UniqueId` (decisión 7).
   - Cómo se prueba: mover una columna, actualizar, ver la miniatura nueva en el tablero.
5. **Botón "Actualizar imágenes" (todas)**
   - Archivo: `Imagenes/Vistas/VentanaImagenesActividad.xaml(.cs)`.
   - Recorre las imágenes del presupuesto del modelo abierto una por una (Revit es de un solo hilo),
     muestra `Actualizando 3 de 12 · ACT_006_01`, tiene botón Cancelar (termina la imagen en curso y
     para) y al final muestra el resumen: actualizadas, con elementos faltantes, omitidas (otro modelo o
     sin `UniqueId`), con error.
   - Cómo se prueba: presupuesto con 3+ imágenes, una de otro modelo y una vieja.
6. **Reintentos**
   - El botón actual "Reintentar subidas pendientes" también reintenta los borrados en `PublicIdsPorBorrar`.
7. **Instalador rápido para Juan** (pedido de josetp, 2026-10-14)
   - Archivos: `tools/Instalar.ps1`, `tools/LEEME.txt` (el `.bat` ya existía y llama al `.ps1`).
   - Espera a que Revit esté cerrado antes de copiar.
   - Crea o completa `%APPDATA%\CQEING\imagenes\.env` sin pisar valores: toma `API_URL`/`API_KEY`
     del `.env` del add-in de cantidades (`Addins\<año>\…\.env`) si existe, y pide lo que falte
     (incluidas las claves de Cloudinary). Las claves nunca viajan en el paquete.
   - Paquete (2026-10-14): en esta PC la detección automática de .NET de `publish.ps1` falla y compila
     con net8 (error MSB3277 System.Runtime 8 vs 10). Hay que forzarlo:
     `powershell -ExecutionPolicy Bypass -Command "& .\tools\publish.ps1 -Versions 2026 -TargetFrameworks @{ '2026' = 'net10.0-windows' } -Zip"`
     → `Publicar-practicacq.zip` (138 KB, solo Revit 2026.5+; sin claves adentro). Si Juan tiene Revit
     2027 o un 2026 anterior a 2026.5, hay que compilar para esa versión.
   - Probado 2026-10-14 en un AppData de prueba: (a) con `.env` de cantidades → completa API y deja
     las claves existentes; (b) equipo nuevo → crea el archivo y guarda lo que se escribe.

## Cómo probarlo

```
dotnet restore practicacq.csproj -p:RevitVersion=2026 -p:TargetFramework=net10.0-windows
dotnet build practicacq.csproj -c Debug --no-restore -p:RevitVersion=2026 -p:TargetFramework=net10.0-windows
# Cerrar Revit antes del deploy
powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Versions 2026 -TargetFramework net10.0-windows
```

Al abrir Revit, aceptar "Cargar siempre" (la .dll cambió).

1. Crear una imagen nueva en 3D y otra en planta; revisar `registro.json`.
2. Mover una columna y borrar otra; "Actualizar esta" en la imagen 3D → miniatura nueva, aviso de 1
   elemento faltante, `_v2` en Cloudinary y v1 borrada.
3. Girar la vista 3D base y actualizar → la imagen sale con la orientación guardada.
4. Agregar una columna del mismo tipo y actualizar una imagen hecha con "mismo tipo" → aparece.
5. "Actualizar imágenes" con imágenes de otro modelo y viejas → resumen correcto; probar Cancelar.
6. Quitar `CLOUDINARY_API_SECRET` del `.env` y actualizar → sube la nueva, avisa, v1 queda pendiente de
   borrar; volver a ponerlo y "Reintentar" → v1 borrada.

## Estado

- [x] Plan revisado con el usuario (2026-10-14; decisiones 5, 6 y 7 confirmadas).
- [x] Pasos 1-7 implementados (2026-10-14). Compila sin errores con Revit 2026.5 / net10.0-windows.
- [x] Desplegado en esta PC (Revit 2026) el 2026-10-14.
- [ ] Prueba remota en Revit (2026-10-14, josetp fuera de casa): se abrió Revit con una copia del modelo
      (`scratchpad
evit\PruebaImagenes.rvt`), pero alguien respondió el aviso de seguridad y cerró
      Revit (journal.0010: `ID_APP_EXIT` 08:16:18, 8 s después de abrir el modelo). No se reintentó
      sin confirmación de josetp.
- [x] Cloudinary probado con el código real (2026-10-14, consola .NET que usa `practicacq.dll`, sin
      Revit), cuenta `df6m0s0ri`: subida firmada de `PRUEBA_186_01` → `public_id`
      `cuadro_cantidades/imagenes/PRUEBA_CLAUDE/PRUEBA_186/PRUEBA_186_01`; resubir la misma versión →
      `CodigoOcupado`; versión 2 → `…_v2`; `PublicIdDesdeUrl(url)` = `public_id`; borrar v1 → ok;
      borrar otra vez → ok ("not found"); limpieza de v2 → ok. Listado posterior de la carpeta: vacío.
      Resuelve el *(probar)* del hallazgo 6: con subida firmada el `public_id` incluye la carpeta.
- [x] Probado en Revit 2026.5 por MCP (2026-10-14), sobre una copia de `Prueba 080720sfsd26.rvt`, con
      `PuenteRevit` real (seleccionar → capturar → recapturar) a través de `EjecutorRevit`:
      - 3D con "mismo tipo": 1 columna 0.30 + 1 de 0.40 → 36 elementos (24 + 12); se guardan
        `UniqueId`, vista y cámara 3D.
      - Tras mover una 0.40, borrar una 0.30, copiar una 0.30 nueva y girar la vista base: `Recapturar` →
        36 encontrados, 1 faltante; la imagen conserva el ángulo guardado (el control sin cámara sale
        girado), entra la columna nueva y no sale la borrada. ✅ criterios 1, 4 y "mismo tipo".
      - Planta con recorte, 2 columnas sin "mismo tipo": se guarda el recorte; tras quitar el recorte de la
        vista base, la v2 sale con el mismo recorte (2048×5953 las dos). ✅ planta.
      - Capturas: `docs/planes/imagenes/2026-10-14-186-3d-v1.png`, `…-3d-v2-actualizada.png`,
        `…-3d-control-sin-camara.png`, `…-error-seleccion.png`, `…-error-fondo-planta.png`,
        `…-planta-fondo-blanco.png`.
      - Encontrados los errores de los hallazgos 9 y 10 (ya corregidos, falta repetir la prueba con la
        versión corregida).
- [ ] Probar en Revit los botones de la ventana ("Actualizar", "Actualizar imágenes", Cancelar) y repetir
      la prueba con las correcciones 9 y 10. No se pudo seguir a distancia: la ventana de Revit se
      minimiza sola (sesión de Windows sin pantalla) y la cinta no se dibuja, así que no se puede encender
      el "Revit MCP Switch".
- [ ] Probar en Revit la lista de "Cómo probarlo" (incluye confirmar los *(probar)* de Hallazgos).
- [ ] Probado en Revit.
