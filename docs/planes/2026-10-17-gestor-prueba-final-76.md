# Plan: prueba completa del gestor en un equipo limpio y cierre (#76)

## Contexto

- Es la tarea 15 de `docs/superpowers/plans/2026-10-15-gestor-addins.md`: publicar las primeras versiones
  estables y el gestor, probar todo el ciclo en un equipo limpio con Revit 2026 y borrar las ramas viejas.
- Parte de lo ya hecho: lógica (`docs/planes/2026-10-15-gestor-logica-74.md`) y app
  (`docs/planes/2026-10-17-gestor-app-75.md`).
- Fuera de alcance (queda anotado como pendiente): iniciar sesión con la plataforma y configurar el `.env` de
  Imágenes desde el gestor.
- Entorno: repo `utilidadcqeing-prog/Add-in-Acero-en-columnas-2027` (público desde el 2026-10-17, todavía con el
  nombre viejo), rama `gestor-logica-74`. En este equipo solo hay Revit 2027; el equipo limpio con Revit 2026
  lo prepara el usuario.

## Criterios de aceptación

Copiados de la issue #76:

- [ ] Publicar `armado-columnas-v1.0.0`, `imagenes-v1.0.0` y `gestor-v0.1.0` (etiquetas anotadas).
- [ ] En un equipo sin el gestor ni los add-ins, con Revit 2026:
  - [ ] Instalar el gestor con `CQEING.Gestor-win-Setup.exe`; aparecen los dos add-ins.
  - [ ] Instalar los dos; en Revit, la pestaña CQEING trae «Estructural», «Herramientas» e «Imágenes» y los botones funcionan.
  - [ ] Publicar `armado-columnas-v1.0.1`: el gestor avisa, se actualiza con Revit abierto y la versión nueva se ve al reiniciar Revit.
  - [ ] Volver a la 1.0.0 desde «⋯»: no descarga nada.
  - [ ] Publicar `gestor-v0.1.1`: el gestor se actualiza solo.
  - [ ] En un equipo con la instalación vieja (`Instalar.bat`): al instalar desde el gestor desaparece
        `practicacq.addin` y Revit no avisa de ClientId duplicado.
- [ ] Borrar las ramas viejas (`equipo/armado-columnas`, `juan/armado-columnas`, `addin-de-imagenes`,
      `menu-cqeing-wpf`, `migracion/monorepo`), revisando una por una qué commits se perderían.

## Hallazgos

1. (2026-10-17) El repo ya es público, pero con el nombre `Add-in-Acero-en-columnas-2027`: su API de releases
   responde 200 y la de `CQEING-Addins` responde 404. Con el nombre que tenía el gestor, la app no veía nada.
2. (2026-10-17) Las releases viejas `v1.0.0` y `v1.1.0` no siguen el formato `<id>-vX.Y.Z`: el gestor las
   ignora (`EtiquetaRelease.Interpretar`), no hace falta borrarlas.
3. (2026-10-17) El `.env` de Imágenes vive en `%AppData%\CQEING\imagenes\.env` y `.gitignore` lo excluye: el
   paquete que arma GitHub Actions desde un checkout limpio no puede llevar claves.

## Decisiones

1. **El gestor usa el nombre actual del repo** (2026-10-17, Claude, con el visto bueno del usuario para hacer
   toda la #76): `Repositorio.Nombre = "utilidadcqeing-prog/Add-in-Acero-en-columnas-2027"`. Funciona hoy y
   seguirá funcionando si el repo se renombra, porque GitHub redirige el nombre viejo (API y descargas). Así la
   prueba no depende del renombre.
2. **Las cinco etiquetas se publican al principio** (2026-10-17, Claude): `armado-columnas-v1.0.1` y
   `gestor-v0.1.1` no cambian código, solo sirven para probar la actualización. En el equipo limpio se instala
   primero la 1.0.0 desde «⋯» y después aparece «Actualizar» hacia la 1.0.1; el `Setup.exe` de
   `gestor-v0.1.0` instala la 0.1.0 y el gestor avisa de la 0.1.1. Así el usuario no depende de que alguien
   publique a mitad de su prueba.
3. **Las extras de la app se quedan** (2026-10-17, usuario): «Actualizar todas», F5 y abrir en el año con novedades.

## Plan paso a paso

1. **Nombre del repo** — `gestor/CQEING.Gestor.Core/Repositorio.cs` — prueba: `dotnet test CQEING-Addins.sln`.
2. **Subir a `main`** — commit del Día 3 y de este plan en `gestor-logica-74`, merge de `origin/main` en la
   rama, compilar y probar, push, PR a `main`, esperar `compilar.yml` en verde y merge (merge commit).
3. **Publicar** — desde `main` actualizado, etiquetas anotadas:
   `armado-columnas-v1.0.0`, `imagenes-v1.0.0`, `gestor-v0.1.0`, `armado-columnas-v1.0.1`, `gestor-v0.1.1`.
   Prueba: cada workflow en verde y cada release con sus archivos (`manifest.json` + ZIP por año; o
   `CQEING.Gestor-win-Setup.exe` + `releases.win.json`).
4. **Prueba automática contra GitHub real** (fuera del repo, en una carpeta temporal que simula un equipo
   limpio): consultar, instalar 1.0.0, ver la actualización a 1.0.1, actualizar, volver a 1.0.0 sin descargar,
   retiro de `practicacq.addin` y aviso del gestor 0.1.1 con Velopack.
5. **Ramas viejas** — por cada una, `git log --oneline origin/main..origin/<rama>`; si no tiene commits fuera
   de `main`, se borra; si tiene, se anotan aquí y se consulta al usuario antes de borrar.
6. **Equipo limpio con Revit 2026** — lo hace el usuario con la guía
   `C:\Users\PC\Downloads\trabajo1\Guia_Prueba_Equipo_Limpio_76.md`; los resultados se anotan en Estado.

## Estado

- [ ] Pasos 1 a 5.
- [ ] Paso 6 (usuario).
