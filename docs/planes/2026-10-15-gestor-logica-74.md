# Plan: lógica del gestor de add-ins (#74)

## Contexto

- Issue: [#74](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/74). Corresponde a las tareas 10 a 13 del plan `docs/superpowers/plans/2026-10-15-gestor-addins.md`; la spec es `docs/superpowers/specs/2026-10-15-gestor-addins-cqeing-design.md`.
- Esta entrega cubre la **tarea 10**: rutas, años de Revit instalados, cliente HTTP y lista de releases de GitHub con caché.
- Fuera de alcance de esta entrega: catálogo, instalador, estado instalado y fachada (tareas 11 a 13), y la app de escritorio.
- Entorno: .NET SDK 8.0.425 y 10.0.401, Revit 2027 instalado en el equipo de desarrollo, rama `gestor-logica-74`.

## Criterios de aceptación

- [x] `RutasGestor`, `DetectorRevit` (años con `RevitAPI.dll`) y `ClienteHttp` (User-Agent).
- [x] `FuenteReleases`: lista sin token, con ETag y 304, y uso de la lista guardada si GitHub limita o no hay red, con lista guardada y sin ella.
- [ ] `ConstructorCatalogo` (tarea 11).
- [ ] `Instalador` (tarea 12).
- [ ] `LectorEstado` y fachada `GestorAddins` (tarea 13).
- [ ] `dotnet test` en verde con todas las pruebas del plan (por ahora: las de la tarea 10 en verde).

## Hallazgos

1. **(2026-10-15) Sin token, un 304 de GitHub también descuenta del cupo.** Se midió con `curl` contra `api.github.com/repos/velopack/velopack/releases`: tres pedidos con `If-None-Match` respondieron 304 y `X-RateLimit-Remaining` bajó 48 → 47 → 46. El ETag sigue evitando volver a descargar la lista, pero no ahorra cupo. Ver la Decisión 4.
2. **(2026-10-15) La API responde 404 para `utilidadcqeing-prog/CQEING-Addins`** porque el repo todavía no es público con ese nombre. Mientras tanto, la prueba real se hizo contra un repo público (`velopack/velopack`): primero 200 con ETag y luego 304.
3. **(2026-10-15) En el equipo de desarrollo existen `Revit 2027` y `Revit Assistant 2027`** dentro de `C:\Program Files\Autodesk`. `DetectorRevit` devuelve solo 2027; una prueba cubre el caso «Revit Assistant».

## Decisiones

1. **`RutasGestor` valida el id, la versión y el año** (2026-10-15). Esas rutas terminan en carpetas que el gestor crea y borra, así que un valor con `..` o con barras lanza `ArgumentException`. Consecuencia para la tarea 13: `LectorEstado` debe descartar los `.addin` cuyo id no cumpla el formato.
2. **`FuenteReleases` es más robusta que el código del plan** (2026-10-15):
   - Un 200 con cuerpo ilegible usa la lista guardada y avisa.
   - Se descartan las releases sin etiqueta y los archivos sin nombre o sin URL `https` absoluta.
   - El tiempo agotado se distingue de la cancelación del usuario.
   - Un 304 sin lista guardada no entra en bucle.
   - La caché se escribe de forma atómica.
   - Se agrega `ResultadoReleases.DesdeCache` para que la ventana muestre el aviso de «lista guardada».
3. **`TipoError` no cambia** (2026-10-15): una respuesta ilegible se informa como `SinConexion`, con su propio mensaje.
4. **Por decidir:** cómo cuidar el cupo de 60 consultas por hora (Hallazgo 1). Propuesta: no consultar la API si la lista guardada tiene menos de N minutos, salvo cuando el usuario pulse «Buscar novedades».

## Plan paso a paso

1. **Tarea 10.** Archivos: `gestor/CQEING.Gestor.Core/{RutasGestor,DetectorRevit,ClienteHttp,FuenteReleases}.cs`, `gestor/CQEING.Gestor.Core.Tests/{RutasYRevitTests,FuenteReleasesTests}.cs` y `Apoyo/{HandlerFalso,Respuestas}.cs`. Se prueba con `dotnet test gestor/CQEING.Gestor.Core.Tests`.
2. **Tareas 11 a 13:** catálogo, instalador, lector de estado y fachada, con las pruebas del plan.

## Cómo probarlo

```powershell
dotnet test gestor/CQEING.Gestor.Core.Tests
dotnet build CQEING-Addins.sln
```

## Estado

- 2026-10-15: tarea 10 implementada.
  - Núcleo: 69/69 pruebas en verde (35 nuevas).
  - Publicador: 8/8 pruebas en verde.
  - La solución compila con 0 advertencias.
  - Verificado contra la API real de GitHub: 200 con ETag y luego 304.
