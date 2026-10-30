# CQEING-Addins

Add-ins de Revit de CQEING y su gestor de instalación. Todo vive en la rama `main`, con una carpeta
por add-in.

| Carpeta | Qué es |
|---|---|
| `addins/armado-columnas/` | Armado de columnas: estribos, columnetas y viguetas de confinamiento. |
| `addins/imagenes/` | Imágenes por actividad del presupuesto. |
| `addins/acero-vigas/` | Acero en vigas: barras con traslapos a múltiplos de corte, bastones y estribos, por pórtico. |
| `addins/despiece/` | Despiece: vistas de despiece de vigas y columnas, listas para llevar al plano. |
| `addins/gestor-revit/` | **No es un add-in:** el botón «Gestor de add-ins» (panel «Gestor») que el gestor instala solo dentro de Revit. Viaja en el paquete del gestor y no se publica con etiqueta propia. |
| `addins/compartido/` | Código común: la pestaña CQEING del ribbon (se compila dentro de cada add-in). |
| `gestor/` | App de escritorio que instala, actualiza y revierte los add-ins (en construcción). |
| `tools/` | Despliegue local, vista previa de la interfaz del armado y utilidades. |

Los tres add-ins se compilan para **Revit 2026** (.NET 8) y **Revit 2027** (.NET 10). Todos aparecen
en la pestaña **CQEING** del ribbon.

## Instalar

Con el gestor: descargar `CQEING.Gestor-win-Setup.exe` de la última release `gestor-v*` y abrirlo.
El gestor muestra los add-ins, instala la versión para el Revit del equipo y avisa cuando hay una
nueva. Mientras el gestor no esté publicado, se instala desde el código con el despliegue local
(ver «Desarrollar»).

## Publicar una versión de un add-in

```powershell
git tag -a armado-columnas-v1.2.0 -m "Qué cambió en esta versión"
git push origin armado-columnas-v1.2.0
```

La etiqueta es `<id del add-in>-vX.Y.Z` y debe ser **anotada**: su mensaje son las notas de la
versión. Al subirla, GitHub compila el add-in para cada año de su `addin.json`, arma un ZIP por año y
crea la release con su `manifest.json`. El sufijo `-rc.N` (por ejemplo `-v1.2.0-rc.1`) publica una
prueba que el gestor no muestra.

## Desarrollar

```powershell
dotnet build addins/armado-columnas/practicacq.csproj -p:RevitVersion=2027
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027
```

- No hace falta tener Revit instalado para compilar: la API llega por NuGet. El año de Revit decide el
  framework y la versión de la API en un solo lugar, `addins/Directory.Build.props`.
- `desplegar-local.ps1` compila y deja el add-in cargado en ese Revit de este equipo. Revit tiene que
  estar cerrado.
- Cada add-in describe su nombre, proyecto y años de Revit en su `addin.json`.
- Agregar un add-in: crear `addins/<id>/` con su `.csproj`, su `.addin` (ClientId propio) y su
  `addin.json`, y sumarlo a `CQEING-Addins.sln`.

**Cómo deben ser los add-ins** (licencia, actualización obligatoria, publicación y versiones):
[`docs/guia-addins-cqeing.md`](docs/guia-addins-cqeing.md). Es lo que hay que leer antes de tocar o publicar un add-in.

Diseño y plan: `docs/superpowers/specs/2026-10-15-gestor-addins-cqeing-design.md` y
`docs/superpowers/plans/2026-10-15-gestor-addins.md`.
