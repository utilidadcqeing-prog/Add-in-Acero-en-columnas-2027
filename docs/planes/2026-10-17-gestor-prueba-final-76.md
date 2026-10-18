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
   paquete que arma GitHub Actions desde un checkout limpio no puede llevar claves. Comprobado con el
   publicador en local: los ZIP de 2026 traen solo el `.addin`, la DLL, `deps.json` y los iconos.
4. (2026-10-17) Ramas viejas, `git rev-list --count origin/main..origin/<rama>`:
   - `equipo/armado-columnas`: 0 commits fuera de `main` (se puede borrar sin perder nada).
   - `addin-de-imagenes`: 0 commits fuera de `main` (ídem).
   - `migracion/monorepo`: ya no existe en GitHub.
   - `juan/armado-columnas`: 1 commit, `509b390` «Los estribos ya no se borran: se sobreponen (D4)». En `main` el
     borrado quedó como opción («Borrar TODO el armado existente…», `GeneradorArmado.BorrarAnteriores`): el
     cambio quedó superado por otra decisión, pero el commit no está en `main`.
   - `menu-cqeing-wpf`: 1 commit, `2287523` «Agregar menu WPF del add-in CQEING», carpeta `CQEING/` aparte que
     `main` no usa (el menú actual vive en `addins/`).
   El borrado de ramas en GitHub lo hace el usuario (a Claude no se le permitió por ser destructivo); las dos con
   un commit propio, solo si el usuario confirma que se pueden perder.
5. (2026-10-17) La primera corrida de `compilar.yml` en el PR falló en una prueba de la app que comparaba el
   porcentaje final de la barra: sin la ventana, `Progress<T>` avisa desde otro hilo y el último aviso puede
   llegar después de la comparación. Se quitó esa comparación (en la ventana los avisos van en orden por el hilo
   de la interfaz).
6. (2026-10-17) **GitHub no dispara workflows si se suben más de 3 etiquetas en un mismo `git push`.** Se subieron
   las 5 juntas y no arrancó ninguna publicación. Como todavía no tenían release, se quitaron de GitHub
   (`git push origin :refs/tags/<etiqueta>`) y se volvieron a subir de a una (`git push origin <etiqueta>`).
   Para publicar varias versiones a la vez: **una etiqueta por `git push`** (o de a 3 como máximo).

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

- [x] Paso 1: nombre del repo (commit `e379af9`).
- [x] Paso 2: PR #102 en verde y unido a `main` por el usuario con merge commit (`e0bd560`, 2026-10-17).
- [x] Paso 3: etiquetas anotadas sobre `e0bd560`: `armado-columnas-v1.0.0`, `imagenes-v1.0.0`, `gestor-v0.1.0`,
      `armado-columnas-v1.0.1`, `gestor-v0.1.1`.
- [x] Paso 4: prueba automática contra GitHub real (abajo).
- [x] Paso 5: ramas revisadas (Hallazgo 4); el borrado lo hace el usuario.
- [x] Paso 6 (usuario): hecho con Revit 2027 (abajo).

### Publicación (2026-10-17)

Las cinco corridas de `publicar.yml` y `publicar-gestor.yml` terminaron en verde. Archivos de cada release:

- `armado-columnas-v1.0.0`, `armado-columnas-v1.0.1`, `imagenes-v1.0.0`: `manifest.json` + un ZIP por año (2026 y 2027).
- `gestor-v0.1.0`, `gestor-v0.1.1`: `CQEING.Gestor-win-Setup.exe` (78 MB), `CQEING.Gestor-<versión>-full.nupkg`,
  `CQEING.Gestor-win-Portable.zip`, `releases.win.json`, `assets.win.json`, `RELEASES`.

### Prueba automática contra GitHub real (2026-10-17)

Script fuera del repo (`C:\Users\PC\Downloads\trabajo1\Prueba_76\prueba.cs`, `dotnet run prueba.cs`): lógica real del
gestor y releases públicas reales, sobre las carpetas de un equipo limpio temporal con Revit 2026 simulado (solo
la carpeta con `RevitAPI.dll`). Resultado: **23 de 23 comprobaciones bien**:

- La lista viene de GitHub; aparecen «Armado de columnas» e «Imágenes por actividad»; Armado ofrece 1.0.1 y 1.0.0
  (la `-rc` no aparece); el último gestor es `gestor-v0.1.1`.
- Con un `practicacq.addin` viejo en `Addins\2026` (como deja `Instalar.bat`): al instalar los dos add-ins 1.0.0,
  `practicacq.addin` desaparece, no hay `ClientId` repetidos y cada `.addin` apunta a una DLL que existe.
- El gestor avisa la 1.0.1, la instala y la 1.0.0 queda en disco; volver a la 1.0.0 hace **0 pedidos a internet**.
- `releases.win.json` de `gestor-v0.1.0` y `gestor-v0.1.1` anuncian su versión y los dos `Setup.exe` se descargan.

Captura de la ventana real leyendo las releases reales: `imagenes/2026-10-17-gestor-real-github.png` (la franja azul
del gestor nuevo en esa captura viene de un actualizador simulado, porque la ventana no estaba instalada con
`Setup.exe`; la versión 0.1.1 que muestra sí es la publicada).

### Prueba manual del usuario (2026-10-17)

Hecha con la guía `Guia_Prueba_Equipo_Limpio_76.md` en el equipo del usuario, que **solo tiene Revit 2027**
(no había un equipo con Revit 2026; Revit es de licencia y lo instala quien maneja las licencias). Se usaron los
mismos pasos con 2027 en lugar de 2026:

| Paso | Resultado |
|---|---|
| 1. Instalar el gestor con `Setup.exe` de `gestor-v0.1.0`; aparecen los dos add-ins | ✔ |
| 2. Instalar Armado 1.0.0 (desde «⋯») e Imágenes 1.0.0 | ✔ (`.addin` de 2027 apuntan a `%LOCALAPPDATA%\CQEING\Addins\<id>\1.0.0\2027`) |
| 3. Revit 2027: pestaña CQEING con «Estructural», «Herramientas» e «Imágenes»; botones abren | ✔ |
| 4. Actualizar Armado a 1.0.1 con Revit abierto; funciona al reiniciar Revit | ✔ |
| 5. Volver a la 1.0.0 sin internet | ✔ |
| 6. «Actualizar y reiniciar»: el gestor queda en 0.1.1 | ✔ (Windows lo registra como 0.1.1; `current\CQEING.Gestor.exe` = `0.1.1+e0bd560`) |
| 7. Equipo con la instalación vieja en un Revit real | No aplica: no había equipo con Revit 2026 y `Instalar.bat` (el retiro de `practicacq.addin` y la ausencia de ClientId repetidos sí se comprobaron contra GitHub real) |

### Pendiente

- Repetir los pasos 0 a 7 en un equipo con **Revit 2026** cuando haya uno (paquete `net8.0-windows`).
- Borrar en GitHub las ramas viejas (lo hace el usuario; ver Hallazgo 4).
- Fuera de la #76: iniciar sesión con la plataforma y configurar el `.env` de Imágenes desde el gestor; firmar el instalador.

### Criterios de aceptación

- ✅ Publicar `armado-columnas-v1.0.0`, `imagenes-v1.0.0` y `gestor-v0.1.0`.
- ✅ Instalar el gestor con `Setup.exe`; aparecen los dos add-ins (Revit 2027).
- ✅ Instalar los dos; pestaña CQEING con sus tres paneles y botones (Revit 2027).
- ✅ `armado-columnas-v1.0.1`: avisa, se actualiza con Revit abierto y se ve al reiniciar (Revit 2027).
- ✅ Volver a la 1.0.0 desde «⋯» sin descargar (manual sin internet + automático: 0 pedidos).
- ✅ `gestor-v0.1.1`: el gestor se actualiza solo.
- ⚠️ Instalación vieja: retiro de `practicacq.addin` y sin ClientId repetidos comprobados de forma automática; falta verlo en un Revit real.
- ⚠️ Ramas viejas: revisadas; falta borrarlas en GitHub.
- ⚠️ Todo lo anterior falta repetirlo con Revit 2026.
