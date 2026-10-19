# Plan: agregar el Cuadro de cantidades al gestor (#105)

## Contexto

- Pedido: sumar el add-in **Cuadro de cantidades** al gestor, como «Armado de columnas» e «Imágenes por actividad».
  Issue #105 (Sprint 5), parte de #70.
- Hoy el add-in vive en el repo privado `utilidadcqeing-prog/Add-in-Cuadro-de-cantidades-V0` (rama `main`,
  última release `v1.8`, solo para Revit 2026) y se instala con `Instalar.bat`.
- Fuera de alcance: cambiar lo que hace el add-in; configurar el `.env` desde la ventana del gestor.
- Entorno: monorepo `utilidadcqeing-prog/Add-in-Acero-en-columnas-2027`, rama `gestor-cuadro-cantidades-105`.
  En el equipo de prueba hay Revit 2026 y Revit 2027.

## Criterios de aceptación

Copiados de la issue #105:

- [ ] Código en `addins/cuadro-cantidades/` (sin historial, sin archivos de prueba ni configuración local) con su
      `addin.json` (`id: cuadro-cantidades`, Revit 2026 y 2027, heredado `CQEING_CuadroCantidades.addin`).
- [ ] Compila para Revit 2026 (.NET 8) y Revit 2027 (.NET 10) con las convenciones del monorepo, y el ZIP lleva
      todas sus dependencias.
- [ ] El `.env`: dónde va con el gestor y cómo pasarlo desde la instalación vieja, sin incluirlo nunca en el paquete.
- [ ] Publicar `cuadro-cantidades-v1.8.0`; el gestor lo muestra, instala y actualiza.
- [ ] Revit 2026 y 2027: el Cuadro abre, se conecta a la API y desaparece el `.addin` viejo sin aviso de duplicado.

## Hallazgos del código

1. (2026-10-19) **El repo de origen tiene una clave real de la API escrita en un archivo de prueba**
   (`test_api.cs`). El monorepo es público: ese archivo no se trae. Hay que pedir que se cambie la clave, porque
   sigue en el historial del repo privado. Se trae el código **sin historial** por la misma razón.
2. (2026-10-19) El proyecto original compila contra `C:\Program Files\Autodesk\Revit 2026` y, después de cada
   compilación, escribe un `.addin` en `%APPDATA%\Autodesk\Revit\Addins\2026` (target `GenerateAddinManifest`).
   En el monorepo eso se reemplaza por la API de NuGet (`addins/Directory.Build.props`) y un `.addin` propio
   con ruta relativa; la compilación nunca toca las carpetas de Revit.
3. (2026-10-19) Los datos del add-in van a `%APPDATA%\CQEING_CuadroCantidades` (bases SQLite, JSON, previews):
   no dependen de la carpeta de la DLL, así que no se pierden al cambiar de versión.
4. (2026-10-19) El `.env` (API_URL, API_KEY y Cloudinary) se busca junto a la DLL y hasta 4 carpetas arriba
   (`Services/EnvLoader.cs`). Con el gestor la DLL queda en
   `%LOCALAPPDATA%\CQEING\Addins\cuadro-cantidades\<versión>\<año>\`, que cambia en cada versión.
   La instalación vieja (`Instalar.ps1`) lo deja en `C:\CQEING\CuadroCantidades\.env`.
5. (2026-10-19) El proyecto copiaba el `.env` a la salida si existía (`<None Update=".env">`): en una compilación
   local, el publicador lo habría metido en el ZIP. El empaquetador del monorepo empaqueta toda la salida.
6. (2026-10-19) En el equipo de prueba, Revit 2027 carga una compilación de desarrollo `net10.0-windows` del
   repo de origen: el add-in ya funciona en .NET 10.
7. (2026-10-19) El add-in usa su propia pestaña de la cinta (`AppConstants.RibbonTabName`); se deja igual.
8. (2026-10-19) **La rama `main` del repo de origen está atrasada**: la versión publicada `v1.8` (galería con las
   imágenes del add-in de imágenes, caras exteriores, área útil) está en la etiqueta `v1.8` / rama
   `Actualizaciones`. Se copió desde la etiqueta `v1.8` (`1885db2`).
9. (2026-10-19) **SQLite vulnerable**: EF Core Sqlite 8.0.10 trae `SQLitePCLRaw.lib.e_sqlite3` 2.1.6, con la
   vulnerabilidad alta GHSA-2m69-gcr7-jv3q (afecta hasta 2.1.11; NU1903 al compilar). Se fijó
   `SQLitePCLRaw.bundle_e_sqlite3` 2.1.14: compila sin el aviso en 2026 y 2027 y la salida trae la 2.1.14.
10. (2026-10-19) Compila para 2026 y 2027 sin errores; quedan 56 avisos de nulos (CS86xx) que ya tenía el código
    original. Paquete local: 101 archivos (2026) y 99 (2027), 13 MB, con `e_sqlite3.dll`, el `.addin` y
    `Resources/rubik.png`, sin `.env`, `.pdb` ni `.md`.

## Decisiones

1. **Traer el código al monorepo** (2026-10-19, usuario: «haz lo que corresponda»): copia sin historial a
   `addins/cuadro-cantidades/`, como los otros add-ins. Desde aquí, el desarrollo del Cuadro sigue en el monorepo
   y el repo de origen queda como histórico (hay que avisarle a quien lo programa).
2. **Revit 2026 y 2027** (2026-10-19, usuario): hoy se usa en los dos.
3. **El `.env` se busca también en dos lugares fijos** (2026-10-19, Claude, por el hallazgo 4): después de los
   lugares de siempre, `%APPDATA%\CQEING_CuadroCantidades\.env` (junto a sus datos; sobrevive a las
   actualizaciones) y `C:\CQEING\CuadroCantidades\.env` (el de la instalación vieja: así un usuario que venía de
   `Instalar.bat` no tiene que mover nada). Es el único cambio al código del add-in.
4. **El empaquetador nunca mete un `.env` en el ZIP** (2026-10-19, Claude, por el hallazgo 5): se excluye en
   `Empaquetador`, con una prueba; y el proyecto ya no lo copia a la salida.
5. **Primera versión `1.8.0`** (2026-10-19, Claude): sigue la numeración del repo de origen (última `v1.8`).
6. **No se traen**: `test_api.cs`, `sync_error.log`, `bip.html`, `pr-body*.md`, `pr_body.md`, `task_plan.md`,
   `*.lscache`, `CQEING_CuadroCantidades.sln`, `AddinManifest/` (lo reemplaza el `.addin`), `GET apipresupuestos.txt`.
   Se traen el código, `Resources/` y los documentos de diseño (`README.md`, `endpoints-addin.md`,
   `vinculo-presupuesto-revit.md`, `prisma/`), revisados sin claves.

## Plan paso a paso

1. **Copiar** el código de `main` del repo de origen a `addins/cuadro-cantidades/` (lista de la decisión 6) —
   prueba: buscar claves en la carpeta (`API_KEY=`, `X-Api-Key`, cadenas largas) y que no haya ninguna.
2. **Proyecto** `CQEING_CuadroCantidades.csproj` con las convenciones del monorepo, `CQEING_CuadroCantidades.addin`
   y `addin.json` — prueba: `dotnet build ... -p:RevitVersion=2026` y `2027` sin errores.
3. **`.env`** en `Services/EnvLoader.cs` (decisión 3).
4. **Empaquetador** sin `.env` + prueba en `tools/CQEING.Publicador.Tests` — prueba: `dotnet test CQEING-Addins.sln`.
5. **Paquete local** con el publicador (`--etiqueta cuadro-cantidades-v1.8.0`) — prueba: el ZIP de cada año trae
   la DLL, sus dependencias (incluido `e_sqlite3.dll`) y el `.addin`, y no trae `.env`.
6. **Revit real** con `tools/desplegar-local.ps1 -Addin cuadro-cantidades -Revit 2026` y `-Revit 2027` — prueba:
   la pestaña abre, carga un presupuesto de la API y no hay aviso de duplicado.
7. **PR**, CI en verde, merge (usuario), etiqueta `cuadro-cantidades-v1.8.0` y prueba desde el gestor en los dos Revit.

## Cómo probarlo

```powershell
dotnet build addins/cuadro-cantidades/CQEING_CuadroCantidades.csproj -c Release -p:RevitVersion=2026
dotnet build addins/cuadro-cantidades/CQEING_CuadroCantidades.csproj -c Release -p:RevitVersion=2027
dotnet test CQEING-Addins.sln
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin cuadro-cantidades -Revit 2026
```

## Estado

- [x] Plan revisado con el usuario (2026-10-19: traerlo al monorepo, Revit 2026 y 2027).
- [ ] Pasos 1 a 7.
