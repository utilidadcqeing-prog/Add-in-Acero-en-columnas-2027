# Plan: lógica del gestor de add-ins (#74)

## Contexto

- Issue: [#74](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/74). Corresponde a las tareas 10 a 13 del plan `docs/superpowers/plans/2026-10-15-gestor-addins.md`; la spec es `docs/superpowers/specs/2026-10-15-gestor-addins-cqeing-design.md`.
- Entrega 1 (2026-10-15): tarea 10, rutas, años de Revit, cliente HTTP y lista de releases con caché.
- Entrega 2 (2026-10-16): tareas 11 a 13, catálogo, instalador, estado instalado y fachada `GestorAddins`.
- Fuera de alcance: la app de escritorio (WPF, Velopack y workflow de publicación).
- Entorno: .NET SDK 8.0.425 y 10.0.401, Revit 2027 instalado en el equipo de desarrollo, rama `gestor-logica-74`.

## Criterios de aceptación

- [x] `RutasGestor`, `DetectorRevit` (años con `RevitAPI.dll`) y `ClienteHttp` (User-Agent).
- [x] `FuenteReleases`: lista sin token, con ETag y 304, y uso de la lista guardada si GitHub limita o no hay red, con lista guardada y sin ella.
- [x] `ConstructorCatalogo`: 5 versiones estables más nuevas por add-in; ignora `-rc`, borradores y `gestor-v*`; cada `manifest.json` se descarga una sola vez.
- [x] `Instalador`: descarga, SHA-256, extracción en carpeta temporal, `.addin` con ruta absoluta, retiro de heredados, retención de 3 versiones más las activas, y volver a una versión en disco sin descargar.
- [x] `LectorEstado` y fachada `GestorAddins` (`ConsultarAsync`, `InstalarAsync`, `HayActualizacion`).
- [x] Casos de #74 con prueba: Revit con versión vieja cargada, ZIP alterado, descarga cortada, límite de GitHub o sin red (con y sin lista guardada), `.addin` de desarrollo o dañado.
- [x] `dotnet test` en verde.
- [ ] Prueba contra releases públicas reales (depende de que el repo sea público con el nombre `CQEING-Addins`).

## Hallazgos

1. **(2026-10-15) Sin token, un 304 de GitHub también descuenta del cupo.** Se midió con `curl` contra `api.github.com/repos/velopack/velopack/releases`: tres pedidos con `If-None-Match` respondieron 304 y `X-RateLimit-Remaining` bajó 48 → 47 → 46. El ETag evita volver a descargar la lista, pero no ahorra cupo. Ver la Decisión 4.
2. **(2026-10-15) La API responde 404 para `utilidadcqeing-prog/CQEING-Addins`** porque el repo todavía no es público con ese nombre. La prueba real se hizo contra un repo público (`velopack/velopack`): primero 200 con ETag y luego 304.
3. **(2026-10-15) En el equipo de desarrollo existen `Revit 2027` y `Revit Assistant 2027`** dentro de `C:\Program Files\Autodesk`. `DetectorRevit` devuelve solo 2027.
4. **(2026-10-16) El plan grande dejaba la zona de descarga dentro de la carpeta de versiones** (`<carpeta final>.descarga-<guid>`). Al fallar, quedaba vacía la carpeta padre `<versión>`, y `LimpiarVersionesViejas` la contaba como una versión: podía hacer que una versión real quedara fuera de las 3 conservadas. Se corrige con `RutasGestor.CarpetaTemporal` (Decisión 5).
5. **(2026-10-16) `RutasGestor` rechaza ids con mayúsculas o guion bajo.** Un `CQEING.Mal_Nombre.addin` en la carpeta de Revit hacía lanzar `ArgumentException` a `LectorEstado` y rompía la lectura de todo. Se corrige filtrando antes (Decisión 6).
6. **(2026-10-16) Un archivo abierto con `FileShare.None` hace fallar el `Directory.Move` de su carpeta en Windows.** Eso es lo que permite renombrar antes de borrar: si Revit tiene la DLL cargada, la versión vieja queda entera y se borra en la próxima instalación.

## Decisiones

1. **`RutasGestor` valida el id, la versión y el año** (2026-10-15). Esas rutas terminan en carpetas que el gestor crea y borra; un valor con `..` o con barras lanza `ArgumentException`.
2. **`FuenteReleases` es más robusta que el código del plan** (2026-10-15): respuesta ilegible con lista guardada, descarte de releases y archivos incompletos, distinción entre tiempo agotado y cancelación, 304 sin lista guardada sin bucle, caché atómica y la propiedad `DesdeCache`.
3. **`TipoError` no cambia** (2026-10-15): una respuesta ilegible se informa como `SinConexion` con su propio mensaje.
4. **Por decidir:** cómo cuidar el cupo de 60 consultas por hora (Hallazgo 1). Propuesta: no consultar la API si la lista guardada tiene menos de N minutos, salvo cuando el usuario pulse «Buscar novedades».
5. **Zona de descarga fuera de las versiones** (2026-10-16): `RutasGestor.CarpetaTemporal` = `%LOCALAPPDATA%\CQEING\Gestor\temp`. Cada descarga usa una subcarpeta propia que se borra siempre (éxito, error o cancelación); las de más de un día (cierre brusco) se borran al empezar la siguiente instalación. La carpeta `<versión>` solo se crea en el momento de mover el resultado ya verificado.
6. **`LectorEstado` solo acepta lo que el gestor pudo haber escrito** (2026-10-16): carpeta de año de 4 dígitos, id con el formato válido, y un `Assembly` que apunta a `...\<id>\<versión>\<mismo año>\`. Lo demás se ignora. «Instalada» significa «activa en el `.addin`»; el gestor no afirma que Revit la tenga cargada.
7. **El instalador valida más que el plan** (2026-10-16): el `.addin` del paquete debe estar bien formado y nombrar DLL que el paquete trae; un ZIP con entradas que se salen de la carpeta, un archivo que no es ZIP o un id de manifiesto inválido terminan en `PaqueteInvalido` o `ManifiestoInvalido` sin dejar nada. Una carpeta de versión vacía o a medias se aparta y se vuelve a descargar.
8. **Progreso, cancelación y descarga detenida** (2026-10-16): `InstalarAsync(version, anio, ct, progreso)` reporta de 0 a 1; la cancelación limpia todo; si el servidor deja de mandar datos 60 s (configurable en el constructor), se corta como `SinConexion`.
9. **`EstadoAddin` y `ResumenGestor` traen lo que necesita la ventana** (2026-10-16): `EstadoAddin.EstaEnDisco(año, versión)` y `ResumenGestor.DesdeCache`, para el menú «⋯» y la franja de «sin conexión». Los parámetros nuevos son opcionales: el contrato del plan no cambia.

## Plan paso a paso

1. **Tarea 10** (hecha). Archivos: `RutasGestor`, `DetectorRevit`, `ClienteHttp`, `FuenteReleases`, sus pruebas y `Apoyo/{HandlerFalso,Respuestas}.cs`.
2. **Tarea 11** (hecha). `gestor/CQEING.Gestor.Core/ConstructorCatalogo.cs` y `ConstructorCatalogoTests.cs`.
3. **Tarea 12** (hecha). `Instalador.cs`, `LectorEstado.cs`, `InstaladorTests.cs`, `Apoyo/{Datos.Paquetes,FlujosFalsos}.cs`.
4. **Tarea 13** (hecha). `GestorAddins.cs`, `LectorEstadoTests.cs`, `GestorAddinsTests.cs`.

## Cómo probarlo

```powershell
dotnet test gestor/CQEING.Gestor.Core.Tests
dotnet build CQEING-Addins.sln
```

## Estado

- 2026-10-15: tarea 10 implementada y verificada contra la API real de GitHub (200 con ETag y luego 304) sobre un repo público.
- 2026-10-16: tareas 11 a 13 implementadas.
  - Núcleo: 133/133 pruebas en verde, repetidas 5 veces sin fallos intermitentes.
  - Publicador: 8/8 pruebas en verde. La solución compila con 0 advertencias.
  - Pendiente: la prueba contra releases públicas reales de `CQEING-Addins`, y la prueba de instalación dentro de Revit.
