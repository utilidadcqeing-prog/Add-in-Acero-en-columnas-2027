# Plan: registrar las imágenes por actividad en la BD del backend (fase B)

## Contexto

- Pedido de josetp (2026-10-14) con el encargo `2026-10-14-encargo-addin-imagenes-bd.md`: cada imagen
  que crea el add-in, además de ir a Cloudinary y al `registro.json` local, se **registra en la BD del
  backend** asociada al `id_item` de su actividad. Esas imágenes van a reemplazar a las que hoy captura
  el add-in del cuadro de cantidades. El endpoint del backend y la lectura desde el cuadro de cantidades
  los hace josetp; aquí va solo el add-in de imágenes.
- El encargo cita un plan `docs/planes/2026-10-14-imagenes-por-actividad-en-bd.md` que no estaba en
  ningún repo ni rama accesible desde esta PC (ver Hallazgo 1). Este archivo es ese plan: si el
  encargo y este plan dicen cosas distintas, manda el plan.
- Base: rama `addin-de-imagenes` con la issue #186 ya hecha (versiones `_vN` en Cloudinary, cuenta
  `df6m0s0ri` con subida firmada). Plan de la #186: `docs/planes/2026-10-14-actualizar-imagenes.md`.
- Fuera de alcance: el endpoint del backend, el cuadro de cantidades, la interfaz web.
- Entorno: Revit 2026.5 (net10.0-windows), rama `addin-de-imagenes`.

## Contrato de la API (propuesto en el encargo, a confirmar con el backend)

Header `X-Api-Key`, campos en snake_case.

```
POST   /api/presupuestos/items/{idItem}/imagenes        201 { id, fecha_creacion, ... } · 409 si ya existe ese codigo
GET    /api/presupuestos/items/{idItem}/imagenes        [ { id, codigo, url, nombre, ... } ]
DELETE /api/presupuestos/items/{idItem}/imagenes/{id}
```

Cuerpo del POST: `codigo`, `url` (https de `res.cloudinary.com`), `nombre`, `descripcion`, `tipo_vista`,
`id_modelo`, `nombre_modelo`, `colores: [{ tipo, color }]`. Un 409 es "ya registrada", no un error.

## Criterios de aceptación

- [ ] Cada actividad del árbol tiene su `IdItem` (≠ 0 con la API real).
- [ ] Al guardar una imagen, después de subirla a Cloudinary se hace el POST; un 409 cuenta como éxito.
- [ ] Si la API falla, la imagen queda pendiente y "Reintentar pendientes" también reintenta el registro.
- [ ] Al actualizar una imagen (#186), la BD queda apuntando a la URL nueva.
- [ ] El tablero muestra las imágenes de la API (también las de otras PCs) más las locales pendientes.
- [ ] El siguiente número `ACT_NNN_NN` se calcula también con los códigos de la API.
- [ ] En modo ejemplo no se registra nada.
- [ ] `tools/migrar-imagenes-a-bd.ps1` con `-Simular`, reporte (registradas / ya existentes / sin actividad)
      y segunda pasada toda "ya existente".

## Hallazgos del código y de la API real

1. **El plan citado no existe en esta PC.** Buscado en `Add-in-Acero-en-columnas-2027`,
   `CQEING_BaseDeDatos` (fork y compartido, todas las ramas) y `Add-in-Cuadro-de-cantidades-V0-J`.
2. **`id_item` sí viene en `/estructura`** (API de producción, 2026-10-14): presupuesto `Proyecto-005`,
   ítem `{ "id_item": 583, "codigo_actividad": "ACT_355", "descripcion", "unidad", "cantidad_obra" }`.
   El add-in lo descarta hoy en `Imagenes/Api/DtosApi.cs` (`ItemDto`).
3. **Las rutas `/api/...` que no existen responden 200 con HTML** (fallback de la web): `GET
   /api/presupuestos/items/583/imagenes` → `200 text/html`. Si el cliente mirara solo el código HTTP,
   daría por registrada una imagen que no lo está. **El cliente exige `application/json`**; si no, es
   "endpoint no disponible" y la imagen queda pendiente.
4. **Cuenta de Cloudinary:** el encargo usa ejemplos de `chhoueyn`, pero desde la #186 el add-in sube a
   `df6m0s0ri` (decisión 10 del plan de la #186). La regla del backend "https de `res.cloudinary.com`"
   sigue sirviendo.
5. **La #186 cambia la URL al actualizar** (`ACT_006_01` → `ACT_006_01_v2`) y el contrato no tiene
   cómo cambiar la URL de un registro: el POST da 409 por el código. Ver decisión 2.
6. **Migración:** el encargo lista con `https://res.cloudinary.com/chhoueyn/image/list/{presupuesto}.json`
   (requiere "Resource list" activo en esa cuenta). Con la cuenta `df6m0s0ri` y su API Key/Secret se
   puede listar por carpeta con la Admin API (`resources/image/upload?prefix=…&context=true`), que trae
   el `context` (código, nombre, descripción, modelo, versión).
7. **Cuenta `chhoueyn` con "Resource list" activo** (2026-10-14, `-Simular -CuentaAnterior chhoueyn`
   contra producción): tiene 3 imágenes de prueba de `PROY_004 / ACT_001` (`id_item` 28) y una de
   `PROY_004_V2 / ACT_006`, que no existe en la API. Un tag sin imágenes responde 404 (se ignora en
   silencio). La cuenta `df6m0s0ri` todavía no tiene imágenes reales.

## Decisiones

1. **Este archivo es el plan de la fase B** (2026-10-14, hallazgo 1). Si aparece el original, se
   comparan y se anotan las diferencias aquí.
2. **Actualizar en la BD = DELETE + POST** (2026-10-14, hallazgo 5) mientras el backend no tenga otra
   forma: la imagen guarda el `id` que devuelve el POST (`IdApi`); al actualizar se borra el registro
   viejo y se registra el nuevo con la URL nueva. **Propuesta para el backend:** `PUT
   /api/presupuestos/items/{idItem}/imagenes/{id}` (o que el POST con un `codigo` existente actualice),
   para que sea una sola operación. Si llega, se cambia solo `ClienteApi.ActualizarImagenAsync`.
3. **Respuestas sin JSON = endpoint no disponible** (hallazgo 3): nunca se toman como éxito.
4. **Migración desde la cuenta configurada con Admin API** (hallazgo 6); la cuenta `chhoueyn` (solo
   imágenes de prueba del 2026-10-13) se puede incluir con `-CuentaAnterior chhoueyn` usando la lista
   pública del encargo, si esa cuenta la tiene activa.
5. **Imágenes de otras PCs en el tablero** se muestran desde la URL y no se pueden actualizar desde esta
   PC (no traen los `UniqueId`), igual que las imágenes de otro modelo.

## Plan paso a paso

1. **Leer `id_item`** — `Imagenes/Api/DtosApi.cs` (`ItemDto.IdItem`), `Imagenes/Modelo/Presupuesto.cs`
   (`ActividadPresupuesto.IdItem`), `Imagenes/Api/ClienteApi.cs` (mapeo), `Imagenes/Api/DatosDeEjemplo.cs`
   (ids inventados). También `ImagenActividad.IdItem`, para registrar sin volver a pedir la estructura.
   Prueba: API real → `IdItem` ≠ 0.
2. **Cliente de la API** — `ClienteApi`: `RegistrarImagenAsync` (POST; 201 → id; 409 → busca el id con el
   GET), `ListarImagenesAsync` (GET), `BorrarImagenAsync` (DELETE), `ActualizarImagenAsync` (decisión 2).
   Exige JSON (decisión 3). Comentario de la clase: ya no es "solo lectura". DTOs nuevos en `DtosApi.cs`.
   Prueba: servidor de prueba local que imita el contrato (201, 409, GET, DELETE, HTML, 500).
3. **Guardar y actualizar** — `ImagenActividad`: `IdItem`, `IdApi`, `RegistradaEnApi`.
   `VentanaImagenesActividad`: después de subir a Cloudinary → POST (o DELETE + POST si es una versión
   nueva). Si falla → pendiente; "Reintentar pendientes" reintenta subida, registro y borrados. Modo
   ejemplo: no registra.
4. **Tablero** — al abrir una actividad se piden sus imágenes a la API y se mezclan con las locales por
   código (gana la local, que tiene más datos). Miniatura desde la URL si no hay archivo local. El
   siguiente número usa también los códigos de la API.
5. **Migración** — `tools/migrar-imagenes-a-bd.ps1` (PowerShell 5.1, sin Revit): `-Simular`,
   `-CuentaAnterior`, reporte en tres grupos.
6. **Documentación** — Hallazgos y Estado de este plan.

## Cómo probarlo

```
dotnet restore practicacq.csproj -p:RevitVersion=2026 -p:TargetFramework=net10.0-windows
dotnet build practicacq.csproj -c Debug --no-restore -p:RevitVersion=2026 -p:TargetFramework=net10.0-windows
```

1. Sin backend: servidor de prueba local (`python`) con el contrato, `API_URL` apuntando a él → guardar,
   409, reintento, actualizar (DELETE + POST), respuesta HTML.
2. Con el backend real cuando exista: guardar → aparece en el GET; red cortada → pendiente → reintentar.
3. Migración: `-Simular`, luego en serio contra pruebas, luego otra vez (todo "ya existente").

## Estado

- [ ] Plan revisado con el usuario (josetp pidió implementarlo directo; falta que confirme la decisión 2).
- [x] Pasos 1-5 implementados (2026-10-14). Compila sin errores; desplegado en Revit 2026 de esta PC.
- [x] Cliente probado contra un servidor de prueba que imita el contrato (scratchpad
      `servidor_prueba.py` + consola .NET con `practicacq.dll`), 11/11: POST 201 con id; 409 → registrada
      (id por GET); actualizar → DELETE + POST con la URL nueva; actualizar sin `IdApi` → lo busca por
      código; GET → imágenes `SoloEnApi` con colores y fecha; URL que no es de Cloudinary → 400; **respuesta
      HTML → no cuenta como registrada**; 500, red cortada y clave incorrecta → pendiente; modo ejemplo →
      no registra ni lista.
- [x] Migración probada: con el servidor de prueba y 3 imágenes reales en `df6m0s0ri` (borradas al
      final): `-Simular` → 1 a registrar + 1 sin actividad; en serio → registra la `_v2` (la más nueva),
      con nombre y modelo del `context` y acentos bien; segunda corrida → "ya existente" (409). Contra
      producción con `-Simular -CuentaAnterior chhoueyn`: ver hallazgo 7.
- [ ] Probado en Revit (guardar → POST; tablero con imágenes de otra PC; "Reintentar pendientes").
- [ ] Probado con el backend real (depende de josetp). Mientras el endpoint no exista, toda imagen queda
      "sin registrar en la API" y "Reintentar pendientes" la cuenta: es lo esperado.
