# Gestor de add-ins CQEING: plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usar superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para implementar este plan tarea por tarea. Los pasos usan casillas (`- [ ]`) para el seguimiento.

**Objetivo:** que el equipo instale, actualice y vuelva a una versión anterior de los add-ins de Revit desde un gestor de escritorio que lee un único repo público, con una sola rama estable y una carpeta por add-in.

**Arquitectura:** el repo `Add-in-Acero-en-columnas-2027` pasa a ser el monorepo `CQEING-Addins`: `main` con `addins/<id>/` (Armado de columnas e Imágenes por actividad) y una base de compilación común que trae la API de Revit por NuGet. Cada etiqueta `<id>-vX.Y.Z` dispara un workflow que compila el add-in para cada año de Revit, arma un ZIP por año y publica la release con un `manifest.json`. El gestor (lógica en `CQEING.Gestor.Core`, ventana WPF provisional y Velopack) lee las releases públicas sin token, descarga y verifica el ZIP, lo deja en una carpeta por versión y apunta el `.addin` de Revit a esa carpeta.

**Tecnologías:** .NET 8 y .NET 10, C#, WPF, xUnit, System.Text.Json, System.IO.Compression, Nice3point.Revit.Api (NuGet), Velopack 1.2.161, GitHub Actions y GitHub CLI (`gh`).

**Spec:** `docs/superpowers/specs/2026-10-15-gestor-addins-cqeing-design.md`

## Restricciones globales

- Repo: `utilidadcqeing-prog/Add-in-Acero-en-columnas-2027`, renombrado a **`utilidadcqeing-prog/CQEING-Addins`** y **público** (tarea 9). El gestor usa siempre el nombre nuevo.
- Una rama estable: `main`. Una carpeta por add-in: `addins/<id>/`. Las ramas son solo trabajo temporal.
- Add-ins: `armado-columnas` (ensamblado `practicacq.dll`, ClientId `2044e120-cc19-42a1-8018-a97f13adf192`, no cambia) e `imagenes` (ensamblado `CQEING.Imagenes.dll`, namespace `practicacq.Imagenes`, ClientId nuevo `5838d75b-878f-41de-a730-839e50e55535`). El Cuadro de cantidades no entra.
- Se conserva el historial de git de los dos add-ins.
- Año de Revit → framework → paquete de la API, definidos solo en `addins/Directory.Build.props`: 2026 → `net8.0-windows` → `Nice3point.Revit.Api.*` `2026.0.4`; 2027 → `net10.0-windows` → `2027.0.0`.
- Etiqueta de release = `<id>-vX.Y.Z`, siempre **anotada** (su mensaje son las notas). Sufijo `-rc.N` = prerelease que el gestor ignora. El gestor se publica con `gestor-vX.Y.Z`.
- Un ZIP por año: `<id>-<versión>-revit<año>.zip`, con la salida de compilación sin `.pdb`, **exactamente un** `.addin` y `Resources/`.
- `manifest.json` con esquema 1, campos y nombres exactos de la sección 4 de la spec.
- El gestor lee `https://api.github.com/repos/utilidadcqeing-prog/CQEING-Addins/releases?per_page=100` **sin token**, con ETag, y descarga los archivos por su `browser_download_url`.
- Rutas del gestor: versiones en `%LOCALAPPDATA%\CQEING\Addins\<id>\<versión>\<año>\`; `.addin` en `%APPDATA%\Autodesk\Revit\Addins\<año>\CQEING.<id>.addin` con `Assembly` en ruta absoluta; caché en `%LOCALAPPDATA%\CQEING\Gestor\cache\`.
- El catálogo muestra las **5** versiones estables más nuevas por add-in; en disco quedan las **3** más nuevas más las que estén activas.
- Sin login en esta fase. No se toca la plataforma CQIngeniería.
- La ventana del gestor de este plan es **provisional** (solo prueba la lógica). La interfaz definitiva va en otro plan.
- `CQEING.Gestor.Core`, `CQEING.Publicador` y sus pruebas: `net8.0`. App del gestor: `net8.0-windows`, `win-x64`, autocontenida. Velopack y `vpk`: `1.2.161`.
- Textos para el usuario y comentarios en español neutro, sin voseo.
- Git: nunca `git add -A` (agregar archivos por nombre). Los pasos de commit solo se ejecutan si el usuario autorizó los commits al iniciar la ejecución del plan. **Cada `git push`, PR, etiqueta subida o cambio en GitHub requiere pedido explícito del usuario en ese momento.**
- Todos los comandos se ejecutan en PowerShell desde la raíz del repo: `C:\Users\LENOVO\Desktop\practicacq`.

## Puntos a revisar

1. **Revit abierto con una versión vieja cargada** mientras se instala otra: la nueva va a su propia carpeta, la vieja bloqueada no se borra ni queda a medias, y se borra en una instalación posterior. Prueba: `Version_vieja_bloqueada_no_rompe_y_se_borra_despues` (tarea 12).
2. **Descarga cortada o archivo alterado**: no se instala nada y no quedan carpetas ni ZIP temporales. Prueba: `Hash_distinto_no_instala_nada_ni_deja_restos` (tarea 12).
3. **GitHub limita (60 consultas por hora por IP; una oficina comparte el cupo) o no hay red**: se usa la última lista guardada con un aviso; sin lista guardada, el error dice qué pasó. Pruebas: `Limite_de_GitHub_usa_la_lista_guardada_con_aviso` y `Sin_conexion_y_sin_lista_guardada_lanza_SinConexion` (tarea 10).
4. **Equipo con la instalación vieja de `Instalar.bat` (`practicacq.addin`) o con un `.addin` de desarrollo**: el heredado se retira al instalar, y un `.addin` que no apunta a las carpetas del gestor no rompe la lectura del estado. Pruebas: `Retira_los_addin_heredados` (tarea 12) y `Addin_ajeno_o_danado_se_ignora` (tarea 13).
5. **Manifiesto con nombres de archivo o heredados con rutas** (`../`, `C:\`): se rechaza antes de descargar o borrar nada. Pruebas: `Archivo_con_ruta_es_invalido` y `Heredado_con_ruta_es_invalido` (tarea 6).

---

## Estructura de archivos

```
CQEING-Addins/
├─ addins/
│  ├─ Directory.Build.props            ← año → framework, API de Revit por NuGet, salida bin/<config>/<año>/, framework.txt, código compartido
│  ├─ compartido/CintaCqeing.cs        ← pestaña CQEING, paneles y botones; se compila dentro de cada DLL
│  ├─ armado-columnas/                 ← practicacq.csproj, practicacq.addin, addin.json, App.cs, Comando*.cs, Logica/, Modelo/, Vistas/, Resources/
│  └─ imagenes/                        ← CQEING.Imagenes.csproj, CQEING.Imagenes.addin, addin.json, AppImagenes.cs, Almacen/, Api/, Configuracion/, Modelo/, Revit/, Vistas/, Resources/
├─ gestor/
│  ├─ CQEING.Gestor.Core/              ← lógica sin interfaz
│  │  ├─ ErrorGestor.cs                ← TipoError, GestorException
│  │  ├─ VersionAddin.cs               ← X.Y.Z[-rc.N], orden
│  │  ├─ EtiquetaRelease.cs            ← <id>-vX.Y.Z
│  │  ├─ Manifiesto.cs                 ← Manifiesto, PaqueteManifiesto, SerializadorManifiesto
│  │  ├─ Sha256Archivo.cs
│  │  ├─ Repositorio.cs                ← nombre del repo y del manifiesto
│  │  ├─ RutasGestor.cs
│  │  ├─ DetectorRevit.cs
│  │  ├─ ClienteHttp.cs
│  │  ├─ FuenteReleases.cs             ← GitHub + ETag + caché
│  │  ├─ ConstructorCatalogo.cs        ← últimas 5 versiones por add-in + manifiestos en caché
│  │  ├─ Instalador.cs                 ← descargar, verificar, extraer, activar, retirar heredados, limpiar
│  │  ├─ LectorEstado.cs               ← qué hay instalado (desde los .addin)
│  │  └─ GestorAddins.cs               ← fachada para la ventana
│  ├─ CQEING.Gestor.Core.Tests/
│  └─ CQEING.Gestor/                   ← WPF provisional + Velopack
├─ tools/
│  ├─ CQEING.Publicador/               ← compila, empaqueta y escribe manifest.json
│  ├─ CQEING.Publicador.Tests/
│  ├─ PreviewUI/                       ← vista previa de la interfaz del armado (ya existía)
│  ├─ imagenes/migrar-imagenes-a-bd.ps1
│  └─ desplegar-local.ps1              ← instala en ESTE equipo la compilación local
├─ .github/workflows/  compilar.yml · publicar.yml · publicar-gestor.yml
└─ CQEING-Addins.sln
```

---

## Fase 1: el repo con una rama y una carpeta por add-in

### Tarea 1: Rama de migración con los dos add-ins y su historial

**Archivos:**
- Commit: `docs/superpowers/specs/2026-10-15-gestor-addins-cqeing-design.md`, `docs/superpowers/plans/2026-10-15-gestor-addins.md` (hoy sin seguimiento en el clon)
- Trae: `Imagenes/**`, `docs/planes/2026-10-13-addin-imagenes-por-actividad.md`, `docs/planes/2026-10-14-actualizar-imagenes.md`, `docs/planes/2026-10-14-imagenes-por-actividad-en-bd.md`, `docs/planes/imagenes/**`, `tools/migrar-imagenes-a-bd.ps1` desde `origin/addin-de-imagenes`

**Interfaces:**
- Produce: rama `migracion/monorepo` cuyo `main` + `equipo/armado-columnas` + `addin-de-imagenes` quedan como ancestros, con `Imagenes/` en la raíz.

- [ ] **Paso 1: Comprobar el punto de partida**

```powershell
git fetch origin
git status --short
```
Esperado: solo `?? docs/` (la spec y este plan). Si aparece otra cosa, detenerse y preguntar al usuario.

- [ ] **Paso 2: Crear la rama desde `main` y traer el armado del equipo**

```powershell
git switch -c migracion/monorepo origin/main
git merge --no-ff origin/equipo/armado-columnas -m "chore: trae a main el armado de columnas de equipo/armado-columnas"
```
Esperado: merge sin conflictos (los 3 commits de `main` que no están en la rama son merges anteriores de esa misma rama).

- [ ] **Paso 3: Traer Imágenes con su historial, sin tocar el armado**

`-s ours` registra la rama como ancestro (así `git log --follow` llega a sus commits) sin aplicar sus cambios al armado; después se copian solo los archivos de Imágenes.

```powershell
git merge --no-commit -s ours origin/addin-de-imagenes
git checkout origin/addin-de-imagenes -- Imagenes docs/planes/2026-10-13-addin-imagenes-por-actividad.md docs/planes/2026-10-14-actualizar-imagenes.md docs/planes/2026-10-14-imagenes-por-actividad-en-bd.md docs/planes/imagenes tools/migrar-imagenes-a-bd.ps1
git commit -m "chore: trae el add-in de imágenes con su historial desde addin-de-imagenes"
```

- [ ] **Paso 4: Verificar el historial y que compila**

```powershell
git log --oneline --follow -- Imagenes/Api/ClienteApi.cs | Select-Object -Last 3
git log --oneline -1 --follow -- Logica
dotnet build practicacq.csproj -c Debug -p:RevitVersion=2026
```
Esperado: el primer comando muestra commits de la rama de imágenes (por ejemplo `1ab9f99 feat(imagenes): registrar las imágenes en la BD del backend (fase B)`); el build termina en `Build succeeded` (Revit 2026 está instalado en este equipo).

- [ ] **Paso 5: Commit de la spec y el plan**

```powershell
git add docs/superpowers/specs/2026-10-15-gestor-addins-cqeing-design.md docs/superpowers/plans/2026-10-15-gestor-addins.md
git commit -m "docs: spec y plan del gestor de add-ins"
```

---

### Tarea 2: Una carpeta por add-in

**Archivos:**
- Mover: `App.cs`, `Comando.cs`, `ComandoDiagnostico*.cs`, `ComandoEstribos.cs`, `Logica/`, `Modelo/`, `Vistas/`, `Resources/`, `practicacq.csproj`, `practicacq.addin` → `addins/armado-columnas/`
- Mover: `Imagenes/` → `addins/imagenes/`
- Mover: `tools/migrar-imagenes-a-bd.ps1` → `tools/imagenes/migrar-imagenes-a-bd.ps1`
- Copiar: `addins/armado-columnas/Resources/iconoCQ16.png`, `iconoCQ32.png` → `addins/imagenes/Resources/`
- Modificar: `tools/PreviewUI/PreviewUI.csproj` (rutas enlazadas)

**Interfaces:**
- Consume: rama `migracion/monorepo` de la tarea 1.
- Produce: `addins/armado-columnas/practicacq.csproj` y `addins/imagenes/` con el código de Imágenes (sin proyecto todavía).

- [ ] **Paso 1: Mover con `git mv`**

```powershell
New-Item -ItemType Directory -Force addins/armado-columnas, tools/imagenes | Out-Null
$armado = @('App.cs','Comando.cs','ComandoEstribos.cs','Logica','Modelo','Vistas','Resources','practicacq.csproj','practicacq.addin') + (Get-ChildItem -Name 'ComandoDiagnostico*.cs')
foreach ($a in $armado) { git mv $a "addins/armado-columnas/$a" }
git mv Imagenes addins/imagenes
git mv tools/migrar-imagenes-a-bd.ps1 tools/imagenes/migrar-imagenes-a-bd.ps1
New-Item -ItemType Directory -Force addins/imagenes/Resources | Out-Null
Copy-Item addins/armado-columnas/Resources/iconoCQ16.png, addins/armado-columnas/Resources/iconoCQ32.png addins/imagenes/Resources/
```

- [ ] **Paso 2: Corregir las rutas de PreviewUI**

En `tools/PreviewUI/PreviewUI.csproj`, reemplazar cada `..\..\Modelo\` por `..\..\addins\armado-columnas\Modelo\` y cada `..\..\Vistas\` por `..\..\addins\armado-columnas\Vistas\` (en `Include`, `Exclude` y en los tres `Page`/`Compile` de las vistas):

```powershell
(Get-Content tools/PreviewUI/PreviewUI.csproj -Raw) -replace '\.\.\\\.\.\\(Modelo|Vistas)\\', '..\..\addins\armado-columnas\$1\' | Set-Content tools/PreviewUI/PreviewUI.csproj -Encoding utf8 -NoNewline
```

- [ ] **Paso 3: Verificar**

```powershell
dotnet build addins/armado-columnas/practicacq.csproj -c Debug -p:RevitVersion=2026
dotnet build tools/PreviewUI/PreviewUI.csproj
git log --oneline --follow -- addins/imagenes/Api/ClienteApi.cs | Select-Object -Last 1
```
Esperado: los dos builds en `Build succeeded`; el log muestra el primer commit de `ClienteApi.cs` en la rama de imágenes.

- [ ] **Paso 4: Commit**

```powershell
git add addins/imagenes/Resources/iconoCQ16.png addins/imagenes/Resources/iconoCQ32.png tools/PreviewUI/PreviewUI.csproj
git commit -m "refactor: una carpeta por add-in en addins/"
```
(Los `git mv` ya quedaron en el índice.)

---

### Tarea 3: Base de compilación común y API de Revit por NuGet

**Archivos:**
- Crear: `addins/Directory.Build.props`
- Crear: `addins/compartido/CintaCqeing.cs`
- Reescribir: `addins/armado-columnas/practicacq.csproj`
- Modificar: `addins/armado-columnas/App.cs`

**Interfaces:**
- Produce: `CQEING.Compartido.CintaCqeing.ObtenerPanel(UIControlledApplication app, string nombre) : RibbonPanel` y `CintaCqeing.CrearBoton(string nombre, string texto, string clase, string ayuda, string iconoBase) : PushButtonData`; propiedad MSBuild `RevitVersion`; archivo `framework.txt` en la carpeta de salida con el `TargetFramework`.

- [ ] **Paso 1: Crear `addins/Directory.Build.props`**

```xml
<Project>
  <!--
    Base común de todos los add-ins de addins/. Único lugar donde se decide, por año de Revit,
    el framework y la versión de la API (por NuGet: compila sin Revit instalado, también en CI).
    Compilar para otro año: dotnet build <proyecto> -p:RevitVersion=2027
  -->
  <PropertyGroup>
    <RevitVersion Condition="'$(RevitVersion)' == ''">2026</RevitVersion>
    <TargetFramework Condition="'$(RevitVersion)' == '2026'">net8.0-windows</TargetFramework>
    <TargetFramework Condition="'$(RevitVersion)' == '2027'">net10.0-windows</TargetFramework>
    <RevitApiVersion Condition="'$(RevitVersion)' == '2026'">2026.0.4</RevitApiVersion>
    <RevitApiVersion Condition="'$(RevitVersion)' == '2027'">2027.0.0</RevitApiVersion>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- Salida predecible por año: la usan tools/desplegar-local.ps1 y el publicador. -->
    <OutputPath>bin\$(Configuration)\$(RevitVersion)\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <!-- Solo para compilar: Revit ya trae estas DLL, no se copian a la salida. -->
    <PackageReference Include="Nice3point.Revit.Api.RevitAPI" Version="$(RevitApiVersion)" ExcludeAssets="runtime" PrivateAssets="all" />
    <PackageReference Include="Nice3point.Revit.Api.RevitAPIUI" Version="$(RevitApiVersion)" ExcludeAssets="runtime" PrivateAssets="all" />
    <Compile Include="$(MSBuildThisFileDirectory)compartido\*.cs" Link="Compartido\%(Filename)%(Extension)" />
  </ItemGroup>

  <Target Name="RevitVersionSoportada" BeforeTargets="Restore;CollectPackageReferences;Build" Condition="'$(RevitApiVersion)' == ''">
    <Error Text="RevitVersion '$(RevitVersion)' no está soportada. Use 2026 o 2027 (addins/Directory.Build.props)." />
  </Target>

  <!-- El publicador lee este archivo para escribir el framework de cada paquete en manifest.json (Empaquetador.ArchivoFramework). -->
  <Target Name="EscribirFramework" AfterTargets="Build">
    <WriteLinesToFile File="$(OutDir)framework.txt" Lines="$(TargetFramework)" Overwrite="true" />
  </Target>
</Project>
```

- [ ] **Paso 2: Crear `addins/compartido/CintaCqeing.cs`**

```csharp
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace CQEING.Compartido;

/// <summary>
/// Pestaña CQEING del ribbon, compartida por todos los add-ins de la empresa. Se compila dentro de cada
/// DLL (addins/Directory.Build.props), así que cada add-in la crea o reutiliza sin depender de otro.
/// </summary>
internal static class CintaCqeing
{
    /// <summary>Pestaña de la empresa en el ribbon. Confirmar la grafía exacta con William.</summary>
    public const string NombrePestana = "CQEING";

    /// <summary>
    /// Devuelve el panel de la pestaña CQEING, creando lo que falte. Si otro add-in de la empresa ya creó
    /// la pestaña o el panel, Revit lanza ArgumentException en vez de devolverlos: no es un error real.
    /// </summary>
    public static RibbonPanel ObtenerPanel(UIControlledApplication app, string nombre)
    {
        try
        {
            app.CreateRibbonTab(NombrePestana);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Ya existe: se usa la que está.
        }

        return app.GetRibbonPanels(NombrePestana).FirstOrDefault(p => p.Name == nombre)
            ?? app.CreateRibbonPanel(NombrePestana, nombre);
    }

    /// <summary>Botón de un comando de la DLL que compila este archivo, con iconos de su carpeta Resources.</summary>
    public static PushButtonData CrearBoton(string nombre, string texto, string clase, string ayuda, string iconoBase)
    {
        string dll = typeof(CintaCqeing).Assembly.Location;
        string carpeta = Path.GetDirectoryName(dll) ?? string.Empty;

        return new PushButtonData(nombre, texto, dll, clase)
        {
            ToolTip = ayuda,
            LargeImage = CargarIcono(Path.Combine(carpeta, "Resources", $"{iconoBase}32.png")),
            Image = CargarIcono(Path.Combine(carpeta, "Resources", $"{iconoBase}16.png")),
        };
    }

    private static BitmapImage CargarIcono(string ruta)
    {
        var imagen = new BitmapImage();
        imagen.BeginInit();
        imagen.UriSource = new Uri(ruta, UriKind.Absolute);
        imagen.CacheOption = BitmapCacheOption.OnLoad;
        imagen.EndInit();
        return imagen;
    }
}
```

- [ ] **Paso 3: Reescribir `addins/armado-columnas/practicacq.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Año de Revit, framework, API por NuGet y salida bin/<config>/<año>/: addins/Directory.Build.props. -->
  <PropertyGroup>
    <!--
      Metadatos del ensamblado: identifican el .dll como de CQEING en las propiedades de Windows y en
      el Add-In Manager de Revit. No es una firma digital (Authenticode).
    -->
    <Company>CQEING</Company>
    <Product>CQEING - Armado de columnas</Product>
    <Copyright>© CQEING</Copyright>
  </PropertyGroup>

  <!--
    Los comandos de diagnóstico (ComandoDiagnostico*.cs) son herramientas de desarrollo: se conservan
    en el repo pero no se publican. El publicador compila en Release, así que ahí quedan afuera;
    tools/desplegar-local.ps1 compila en Debug y los incluye. Sus botones (App.cs) van con #if DEBUG.
  -->
  <ItemGroup Condition="'$(Configuration)' == 'Release'">
    <Compile Remove="ComandoDiagnostico*.cs" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="Resources\*.png" CopyToOutputDirectory="PreserveNewest" />
    <None Include="practicacq.addin" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

- [ ] **Paso 4: Usar `CintaCqeing` en `addins/armado-columnas/App.cs`**

Cambios exactos:
1. Borrar la constante `NombrePestana`, el bloque `try { app.CreateRibbonTab(NombrePestana); } catch (...) { ... }`, las variables `dllPath` y `folder`, y los métodos privados `ObtenerOCrearPanel`, `CrearBoton` y `CargarIcono`.
2. Reemplazar cada `ObtenerOCrearPanel(app, "<panel>")` por `CintaCqeing.ObtenerPanel(app, "<panel>")`.
3. En cada `CrearBoton(...)`, quitar los argumentos `dllPath, folder,` y llamar a `CintaCqeing.CrearBoton(...)`.
4. Usings: quitar `System.Reflection` y `System.Windows.Media.Imaging`; agregar `using CQEING.Compartido;`.

Ejemplo del resultado para el primer botón:

```csharp
RibbonPanel panelEstructural = CintaCqeing.ObtenerPanel(app, "Estructural");

panelEstructural.AddItem(CintaCqeing.CrearBoton(
    "ComandoEstribos",
    "Estribos\nde Columna",
    "practicacq.ComandoEstribos",
    "Genera el acero de estribos con espaciado uniforme en las columnas seleccionadas.",
    "icono"));
```
Los botones de diagnóstico siguen dentro de `#if DEBUG`.

- [ ] **Paso 5: Compilar para los dos años y verificar contra qué API quedó enlazado**

```powershell
foreach ($anio in '2026','2027') {
  dotnet build addins/armado-columnas/practicacq.csproj -c Release -p:RevitVersion=$anio
  $dll = Resolve-Path "addins/armado-columnas/bin/Release/$anio/practicacq.dll"
  [Reflection.Assembly]::LoadFile($dll).GetReferencedAssemblies() | Where-Object Name -like 'RevitAPI*' | ForEach-Object { "$anio -> $($_.Name) $($_.Version)" }
  Get-ChildItem "addins/armado-columnas/bin/Release/$anio" -Name
}
```
Esperado: los dos builds en `Build succeeded` (2027 compila aunque Revit 2027 no esté instalado); 2026 enlaza `RevitAPI 26.*` y 2027 enlaza `RevitAPI 27.*`; la salida trae `practicacq.dll`, `practicacq.addin`, `framework.txt`, `Resources\` y **no** trae `RevitAPI.dll`.

- [ ] **Paso 6: Probar en Revit 2026**

Comprobar que Revit está cerrado (`Get-Process -Name Revit -ErrorAction SilentlyContinue` no devuelve nada), copiar a mano `addins/armado-columnas/bin/Release/2026/*` a una carpeta temporal y apuntar un `.addin` de prueba a ella, o esperar a la tarea 5 (`tools/desplegar-local.ps1`) y probar ahí. En Revit: la pestaña CQEING muestra los paneles «Estructural» y «Herramientas» con sus iconos.

- [ ] **Paso 7: Commit**

```powershell
git add addins/Directory.Build.props addins/compartido/CintaCqeing.cs addins/armado-columnas/practicacq.csproj addins/armado-columnas/App.cs
git commit -m "build: base común de los add-ins con la API de Revit por NuGet"
```

---

### Tarea 4: Proyecto propio de Imágenes

**Archivos:**
- Crear: `addins/imagenes/CQEING.Imagenes.csproj`
- Crear: `addins/imagenes/CQEING.Imagenes.addin`
- Crear: `addins/imagenes/AppImagenes.cs`

**Interfaces:**
- Consume: `CintaCqeing.ObtenerPanel`, `CintaCqeing.CrearBoton` (tarea 3).
- Produce: `CQEING.Imagenes.dll` con `practicacq.Imagenes.AppImagenes : IExternalApplication`.

- [ ] **Paso 1: Crear `addins/imagenes/CQEING.Imagenes.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Año de Revit, framework, API por NuGet y salida bin/<config>/<año>/: addins/Directory.Build.props. -->
  <PropertyGroup>
    <AssemblyName>CQEING.Imagenes</AssemblyName>
    <RootNamespace>practicacq.Imagenes</RootNamespace>
    <Company>CQEING</Company>
    <Product>CQEING - Imágenes por actividad</Product>
    <Copyright>© CQEING</Copyright>
  </PropertyGroup>

  <ItemGroup>
    <Content Include="Resources\*.png" CopyToOutputDirectory="PreserveNewest" />
    <None Include="CQEING.Imagenes.addin" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

- [ ] **Paso 2: Crear `addins/imagenes/CQEING.Imagenes.addin`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>CQEING - Imágenes por actividad</Name>
    <!-- El gestor reescribe esta ruta como absoluta a la carpeta de la versión instalada. -->
    <Assembly>CQEING.Imagenes.dll</Assembly>
    <FullClassName>practicacq.Imagenes.AppImagenes</FullClassName>
    <ClientId>5838d75b-878f-41de-a730-839e50e55535</ClientId>
    <VendorId>CQEING</VendorId>
    <VendorDescription>CQEING</VendorDescription>
  </AddIn>
</RevitAddIns>
```

- [ ] **Paso 3: Crear `addins/imagenes/AppImagenes.cs`**

```csharp
using Autodesk.Revit.UI;
using CQEING.Compartido;

namespace practicacq.Imagenes;

/// <summary>Registra el panel «Imágenes» en la pestaña CQEING. Issue utilidadcqeing-prog/Add-in-Cuadro-de-cantidades-V0#183.</summary>
public class AppImagenes : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        RibbonPanel panel = CintaCqeing.ObtenerPanel(app, "Imágenes");

        panel.AddItem(CintaCqeing.CrearBoton(
            "ComandoImagenesActividad",
            "Imágenes\npor actividad",
            "practicacq.Imagenes.ComandoImagenesActividad",
            "Crea imágenes de los elementos de cada actividad del presupuesto: selecciona en Revit, elige colores y guarda con el código de la actividad.",
            "iconoCQ"));

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
}
```

- [ ] **Paso 4: Actualizar el comentario de ubicación provisional**

En `addins/imagenes/ComandoImagenesActividad.cs`, reemplazar el párrafo «UBICACIÓN PROVISIONAL (decisión del 2026-10-13) … y el botón de `App.cs`.» por:

```csharp
/// Vive en su propio proyecto (addins/imagenes, ensamblado CQEING.Imagenes.dll) dentro del monorepo de
/// add-ins de CQEING; su botón lo registra AppImagenes.cs.
```

- [ ] **Paso 5: Compilar para los dos años**

```powershell
foreach ($anio in '2026','2027') {
  dotnet build addins/imagenes/CQEING.Imagenes.csproj -c Release -p:RevitVersion=$anio
  Get-ChildItem "addins/imagenes/bin/Release/$anio" -Name
}
```
Esperado: `Build succeeded` en ambos; la salida trae `CQEING.Imagenes.dll`, `CQEING.Imagenes.addin`, `framework.txt`, `Resources\` y no trae `RevitAPI.dll`.

- [ ] **Paso 6: Commit**

```powershell
git add addins/imagenes/CQEING.Imagenes.csproj addins/imagenes/CQEING.Imagenes.addin addins/imagenes/AppImagenes.cs addins/imagenes/ComandoImagenesActividad.cs
git commit -m "build: proyecto propio del add-in de imágenes (CQEING.Imagenes.dll)"
```

---

### Tarea 5: `addin.json`, solución, despliegue local, CI de compilación y limpieza

**Archivos:**
- Crear: `addins/armado-columnas/addin.json`, `addins/imagenes/addin.json`
- Crear: `CQEING-Addins.sln`
- Crear: `tools/desplegar-local.ps1`
- Crear: `.github/workflows/compilar.yml`
- Borrar: `practicacq.sln`, `tools/Instalar.bat`, `tools/Instalar.ps1`, `tools/LEEME.txt`, `tools/publish.ps1`, `tools/deploy.ps1`
- Modificar: `.gitignore`, `CLAUDE.md`, `README.md`

**Interfaces:**
- Produce: `addins/<id>/addin.json` con `{ id, nombre, descripcion, proyecto, revit[], heredados[] }` (lo leen el publicador y `desplegar-local.ps1`); `CQEING-Addins.sln`.

- [ ] **Paso 1: Crear los `addin.json`**

`addins/armado-columnas/addin.json`:
```json
{
  "id": "armado-columnas",
  "nombre": "Armado de columnas",
  "descripcion": "Estribos, columnetas y viguetas de confinamiento.",
  "proyecto": "practicacq.csproj",
  "revit": ["2026", "2027"],
  "heredados": ["practicacq.addin"]
}
```

`addins/imagenes/addin.json` (también retira `practicacq.addin`: la versión vieja de la rama de imágenes traía el mismo botón dentro de `practicacq.dll` y chocaría con este):
```json
{
  "id": "imagenes",
  "nombre": "Imágenes por actividad",
  "descripcion": "Imágenes de los elementos de cada actividad del presupuesto.",
  "proyecto": "CQEING.Imagenes.csproj",
  "revit": ["2026", "2027"],
  "heredados": ["practicacq.addin"]
}
```

- [ ] **Paso 2: Crear `tools/desplegar-local.ps1`**

```powershell
<#
.SYNOPSIS
  Instala en ESTE equipo la compilación local de un add-in, para probarlo en Revit mientras se desarrolla.
  Escribe %APPDATA%\Autodesk\Revit\Addins\<año>\CQEING.<id>.addin apuntando a addins\<id>\bin\<config>\<año>\
  (el mismo archivo que escribe el gestor, así que reemplaza la versión instalada desde el gestor).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027
#>
param(
    [Parameter(Mandatory)][string]$Addin,
    [string]$Revit = '2026',
    [ValidateSet('Debug', 'Release')][string]$Configuracion = 'Debug'
)
$ErrorActionPreference = 'Stop'

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) { throw 'Cierre Revit antes de desplegar.' }

$carpetaAddin = Join-Path (Split-Path -Parent $PSScriptRoot) "addins\$Addin"
$rutaConfig = Join-Path $carpetaAddin 'addin.json'
if (-not (Test-Path $rutaConfig)) { throw "No existe $rutaConfig. Add-ins: $((Get-ChildItem (Split-Path $carpetaAddin) -Directory -Exclude compartido).Name -join ', ')" }
$config = Get-Content $rutaConfig -Raw | ConvertFrom-Json

dotnet build (Join-Path $carpetaAddin $config.proyecto) -c $Configuracion -p:RevitVersion=$Revit
if ($LASTEXITCODE) { throw 'La compilación falló.' }

$salida = Join-Path $carpetaAddin "bin\$Configuracion\$Revit"
$origen = Get-ChildItem $salida -Filter *.addin | Select-Object -First 1
[xml]$xml = Get-Content $origen.FullName -Raw
foreach ($assembly in $xml.SelectNodes('//Assembly')) { $assembly.InnerText = Join-Path $salida $assembly.InnerText.Trim() }

$destino = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$Revit"
New-Item -ItemType Directory -Force $destino | Out-Null
foreach ($heredado in $config.heredados) { Remove-Item (Join-Path $destino $heredado) -ErrorAction SilentlyContinue }
$xml.Save((Join-Path $destino "CQEING.$Addin.addin"))
Write-Host "Listo: Revit $Revit cargará $Addin desde $salida"
```

- [ ] **Paso 3: Crear la solución y borrar lo que reemplaza el gestor**

```powershell
dotnet new sln -n CQEING-Addins --format sln
dotnet sln CQEING-Addins.sln add addins/armado-columnas/practicacq.csproj addins/imagenes/CQEING.Imagenes.csproj
git rm practicacq.sln tools/Instalar.bat tools/Instalar.ps1 tools/LEEME.txt tools/publish.ps1 tools/deploy.ps1
```

- [ ] **Paso 4: `.gitignore`**

Agregar al final:
```
# Salidas del publicador y del empaquetado del gestor
dist/
publish/
Releases/
```

- [ ] **Paso 5: Crear `.github/workflows/compilar.yml`**

```yaml
name: Compilar

on:
  pull_request:
  push:
    branches: [main]

jobs:
  compilar:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            10.0.x
      - name: Pruebas
        run: dotnet test CQEING-Addins.sln -c Release
      - name: Compilar cada add-in para cada año de Revit
        shell: pwsh
        run: |
          foreach ($json in Get-ChildItem addins/*/addin.json) {
            $config = Get-Content $json -Raw | ConvertFrom-Json
            foreach ($anio in $config.revit) {
              dotnet build (Join-Path $json.DirectoryName $config.proyecto) -c Release -p:RevitVersion=$anio
              if ($LASTEXITCODE) { exit 1 }
            }
          }
```

- [ ] **Paso 6: `CLAUDE.md` y `README.md`**

En `CLAUDE.md`, sección «Flujo de trabajo con el usuario», reemplazar las líneas de build y deploy por:
```markdown
- Antes de desplegar (`tools/desplegar-local.ps1`), verificar que Revit este
  cerrado con `Get-Process -Name Revit -ErrorAction SilentlyContinue`.
- Build de un add-in: `dotnet build addins/armado-columnas/practicacq.csproj -c Debug -p:RevitVersion=2027`.
- Deploy local: `powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027`.
- Publicar una versión: etiqueta anotada `<id>-vX.Y.Z` (ver README).
```
y cambiar la primera línea de la descripción a: «Monorepo de los add-ins de Revit de CQEING (ribbon "CQEING"): un add-in por carpeta en `addins/`, más el gestor de instalación en `gestor/`.»

Reemplazar `README.md` completo por:
```markdown
# CQEING-Addins

Add-ins de Revit de CQEING y su gestor de instalación.

| Carpeta | Qué es |
|---|---|
| `addins/armado-columnas/` | Armado de columnas: estribos, columnetas y viguetas de confinamiento. |
| `addins/imagenes/` | Imágenes por actividad del presupuesto. |
| `gestor/` | App de escritorio que instala, actualiza y revierte los add-ins. |
| `tools/` | Publicador (CI), despliegue local y vista previa de la interfaz del armado. |

## Instalar

Descargar `CQEING.Gestor-win-Setup.exe` de la última release `gestor-v*` y abrirlo. El gestor muestra
los add-ins, instala la versión para el Revit del equipo y avisa cuando hay una nueva.

## Publicar una versión de un add-in

    git tag -a armado-columnas-v1.2.0 -m "Qué cambió en esta versión"
    git push origin armado-columnas-v1.2.0

El workflow `publicar.yml` compila para cada año de `addin.json`, arma un ZIP por año y crea la release
con su `manifest.json`. El sufijo `-rc.N` (por ejemplo `-v1.2.0-rc.1`) publica una prerelease que el
gestor no muestra.

## Desarrollar

    dotnet build addins/armado-columnas/practicacq.csproj -p:RevitVersion=2027
    powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027

No hace falta tener Revit instalado para compilar: la API llega por NuGet (`addins/Directory.Build.props`).
```

- [ ] **Paso 7: Verificar**

```powershell
dotnet build CQEING-Addins.sln
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin imagenes -Revit 2026
```
Esperado: `Build succeeded`; los dos despliegues terminan en «Listo». Abrir Revit 2026: la pestaña CQEING trae «Estructural», «Herramientas» e «Imágenes», y el botón «Imágenes por actividad» abre su ventana.

- [ ] **Paso 8: Commit**

```powershell
git add addins/armado-columnas/addin.json addins/imagenes/addin.json CQEING-Addins.sln tools/desplegar-local.ps1 .github/workflows/compilar.yml .gitignore CLAUDE.md README.md
git commit -m "chore: solución del monorepo, despliegue local y CI de compilación"
```

- [ ] **Paso 9: Subir la rama y abrir el PR a `main` (pedir permiso al usuario antes)**

```powershell
git push -u origin migracion/monorepo
```
Crear el PR `migracion/monorepo` → `main` con el plugin de GitHub (`mcp__plugin_github_github__create_pull_request`). Esperado: `compilar.yml` en verde. El merge a `main` lo decide el usuario y debe ser **merge commit** (no squash ni rebase), para conservar el historial.

---

## Fase 2: publicación

### Tarea 6: Núcleo compartido: versión, etiqueta y manifiesto

**Archivos:**
- Crear: `gestor/CQEING.Gestor.Core/CQEING.Gestor.Core.csproj`, `ErrorGestor.cs`, `VersionAddin.cs`, `EtiquetaRelease.cs`, `Manifiesto.cs`, `Sha256Archivo.cs`, `Repositorio.cs`
- Crear: `gestor/CQEING.Gestor.Core.Tests/` (proyecto xUnit), `VersionAddinTests.cs`, `EtiquetaReleaseTests.cs`, `ManifiestoTests.cs`, `Apoyo/CarpetaTemporal.cs`, `Apoyo/Datos.cs`

**Interfaces:**
- Produce (namespace `CQEING.Gestor.Core`):
  - `enum TipoError { SinConexion, LimiteGitHub, HashNoCoincide, SinPaqueteParaRevit, ManifiestoInvalido, PaqueteInvalido }`
  - `GestorException(TipoError tipo, string mensaje, Exception? causa = null)` con `TipoError Tipo`
  - `record VersionAddin(int Mayor, int Menor, int Parche, int? Rc) : IComparable<VersionAddin>`; `static VersionAddin? Interpretar(string? texto)`; `bool EsEstable`; `ToString()` → `"1.2.0"` / `"1.2.0-rc.1"`
  - `record EtiquetaRelease(string Id, VersionAddin Version)`; `const string IdGestor = "gestor"`; `static EtiquetaRelease? Interpretar(string? etiqueta)`; `ToString()` → `"<id>-v<versión>"`
  - `record PaqueteManifiesto(string Revit, string Framework, string Archivo, string Sha256)`
  - `record Manifiesto(int Esquema, string Id, string Nombre, string Descripcion, string Version, DateTimeOffset PublicadoEn, string Notas, IReadOnlyList<string> Heredados, IReadOnlyList<PaqueteManifiesto> Paquetes)`; `PaqueteManifiesto? PaqueteParaRevit(string anio)`
  - `static class SerializadorManifiesto { const int EsquemaActual = 1; string Escribir(Manifiesto); Manifiesto Leer(string json) }` (lanza `GestorException` con `TipoError.ManifiestoInvalido`)
  - `static class Sha256Archivo { string Calcular(string ruta) }` → 64 hex en minúsculas
  - `static class Repositorio { const string Nombre = "utilidadcqeing-prog/CQEING-Addins"; const string ArchivoManifiesto = "manifest.json"; }`

- [ ] **Paso 1: Crear los proyectos**

```powershell
dotnet new classlib -n CQEING.Gestor.Core -o gestor/CQEING.Gestor.Core -f net8.0
dotnet new xunit -n CQEING.Gestor.Core.Tests -o gestor/CQEING.Gestor.Core.Tests -f net8.0
Remove-Item gestor/CQEING.Gestor.Core/Class1.cs, gestor/CQEING.Gestor.Core.Tests/UnitTest1.cs
dotnet add gestor/CQEING.Gestor.Core.Tests reference gestor/CQEING.Gestor.Core
dotnet sln CQEING-Addins.sln add gestor/CQEING.Gestor.Core gestor/CQEING.Gestor.Core.Tests
```
En `gestor/CQEING.Gestor.Core/CQEING.Gestor.Core.csproj`, dentro del `PropertyGroup`, dejar `<Nullable>enable</Nullable>` y `<ImplicitUsings>enable</ImplicitUsings>` (la plantilla ya los trae) y agregar:
```xml
    <RootNamespace>CQEING.Gestor.Core</RootNamespace>
```

- [ ] **Paso 2: Apoyo de pruebas**

`gestor/CQEING.Gestor.Core.Tests/Apoyo/CarpetaTemporal.cs`:
```csharp
namespace CQEING.Gestor.Core.Tests.Apoyo;

/// <summary>Carpeta de prueba que se borra al terminar.</summary>
internal sealed class CarpetaTemporal : IDisposable
{
    public string Ruta { get; } = Directory.CreateTempSubdirectory("cqeing-pruebas-").FullName;

    public string Combinar(params string[] partes) => Path.Combine([Ruta, .. partes]);

    public void Dispose()
    {
        try { Directory.Delete(Ruta, recursive: true); }
        catch (IOException) { /* algún archivo sigue abierto; queda en %TEMP% */ }
    }
}
```

`gestor/CQEING.Gestor.Core.Tests/Apoyo/Datos.cs` (en esta tarea solo con lo del manifiesto; las tareas 10 y 12 le agregan métodos):
```csharp
namespace CQEING.Gestor.Core.Tests.Apoyo;

internal static partial class Datos
{
    public static readonly DateTimeOffset Fecha = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);

    public static string NombreDe(string id) => id == "imagenes" ? "Imágenes por actividad" : "Armado de columnas";

    public static PaqueteManifiesto Paquete(string anio, string sha256, string id = "armado-columnas", string version = "1.0.0") =>
        new(anio, anio == "2026" ? "net8.0-windows" : "net10.0-windows", $"{id}-{version}-revit{anio}.zip", sha256);

    public static Manifiesto Manifiesto(
        string id = "armado-columnas",
        string version = "1.0.0",
        IReadOnlyList<PaqueteManifiesto>? paquetes = null,
        IReadOnlyList<string>? heredados = null) =>
        new(1, id, NombreDe(id), "Descripción de prueba.", version, Fecha, "Notas de prueba",
            heredados ?? ["practicacq.addin"],
            paquetes ?? [Paquete("2026", new string('a', 64), id, version)]);
}
```

- [ ] **Paso 3: Escribir las pruebas que fallan**

`gestor/CQEING.Gestor.Core.Tests/VersionAddinTests.cs`:
```csharp
namespace CQEING.Gestor.Core.Tests;

public class VersionAddinTests
{
    [Fact]
    public void Interpreta_version_estable()
    {
        var v = VersionAddin.Interpretar("1.2.3");
        Assert.Equal(new VersionAddin(1, 2, 3, null), v);
        Assert.True(v!.EsEstable);
        Assert.Equal("1.2.3", v.ToString());
    }

    [Fact]
    public void Interpreta_prerelease()
    {
        var v = VersionAddin.Interpretar("1.2.0-rc.4");
        Assert.Equal(new VersionAddin(1, 2, 0, 4), v);
        Assert.False(v!.EsEstable);
        Assert.Equal("1.2.0-rc.4", v.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.2")]
    [InlineData("v1.2.0")]
    [InlineData("1.2.0-beta")]
    [InlineData("1.2.0 ")]
    [InlineData("12345678901.0.0")]
    public void Rechaza_textos_que_no_son_version(string? texto) => Assert.Null(VersionAddin.Interpretar(texto));

    [Theory]
    [InlineData("1.10.0", "1.9.0")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("1.2.0", "1.2.0-rc.5")]
    [InlineData("1.2.0-rc.2", "1.2.0-rc.1")]
    public void Ordena_por_numero_no_por_texto(string mayor, string menor) =>
        Assert.True(VersionAddin.Interpretar(mayor)!.CompareTo(VersionAddin.Interpretar(menor)) > 0);
}
```

`gestor/CQEING.Gestor.Core.Tests/EtiquetaReleaseTests.cs`:
```csharp
namespace CQEING.Gestor.Core.Tests;

public class EtiquetaReleaseTests
{
    [Theory]
    [InlineData("armado-columnas-v1.2.0", "armado-columnas", "1.2.0")]
    [InlineData("imagenes-v1.0.0-rc.1", "imagenes", "1.0.0-rc.1")]
    [InlineData("gestor-v0.1.0", "gestor", "0.1.0")]
    public void Separa_id_y_version(string texto, string id, string version)
    {
        var e = EtiquetaRelease.Interpretar(texto);
        Assert.Equal(id, e!.Id);
        Assert.Equal(version, e.Version.ToString());
        Assert.Equal(texto, e.ToString());
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("armado-columnas-1.2.0")]
    [InlineData("Armado-v1.0.0")]
    [InlineData("armado_columnas-v1.0.0")]
    [InlineData("armado-columnas-v1.0")]
    public void Rechaza_etiquetas_mal_formadas(string texto) => Assert.Null(EtiquetaRelease.Interpretar(texto));
}
```

`gestor/CQEING.Gestor.Core.Tests/ManifiestoTests.cs`:
```csharp
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class ManifiestoTests
{
    [Fact]
    public void Escribe_los_nombres_de_campo_de_la_spec_y_lee_lo_mismo()
    {
        Manifiesto original = Datos.Manifiesto(paquetes: [Datos.Paquete("2026", new string('a', 64)), Datos.Paquete("2027", new string('b', 64))]);

        string json = SerializadorManifiesto.Escribir(original);
        Manifiesto leido = SerializadorManifiesto.Leer(json);

        foreach (string campo in new[] { "esquema", "id", "nombre", "descripcion", "version", "publicadoEn", "notas", "heredados", "paquetes", "revit", "framework", "archivo", "sha256" })
            Assert.Contains($"\"{campo}\"", json);
        Assert.Contains("Imágenes", SerializadorManifiesto.Escribir(Datos.Manifiesto(id: "imagenes")));
        Assert.Equal(original.Id, leido.Id);
        Assert.Equal(original.Version, leido.Version);
        Assert.Equal(original.PublicadoEn, leido.PublicadoEn);
        Assert.Equal(original.Heredados, leido.Heredados);
        Assert.Equal(original.Paquetes, leido.Paquetes);
        Assert.Equal("2027", leido.PaqueteParaRevit("2027")!.Revit);
        Assert.Null(leido.PaqueteParaRevit("2025"));
    }

    [Fact]
    public void Esquema_mas_nuevo_pide_actualizar_el_gestor()
    {
        string json = SerializadorManifiesto.Escribir(Datos.Manifiesto() with { Esquema = 2 });
        var error = Assert.Throws<GestorException>(() => SerializadorManifiesto.Leer(json));
        Assert.Equal(TipoError.ManifiestoInvalido, error.Tipo);
        Assert.Contains("Actualice el gestor", error.Message);
    }

    [Fact]
    public void Sin_heredados_queda_lista_vacia()
    {
        string json = SerializadorManifiesto.Escribir(Datos.Manifiesto()).Replace("\"heredados\"", "\"otroCampo\"");
        Assert.Empty(SerializadorManifiesto.Leer(json).Heredados);
    }

    [Theory]
    [InlineData("{ no es json")]
    [InlineData("null")]
    public void Json_invalido_es_ManifiestoInvalido(string json) =>
        Assert.Equal(TipoError.ManifiestoInvalido, Assert.Throws<GestorException>(() => SerializadorManifiesto.Leer(json)).Tipo);

    [Theory]
    [InlineData("../fuera.zip")]
    [InlineData("C:\\Windows\\x.zip")]
    [InlineData("sin-extension")]
    public void Archivo_con_ruta_es_invalido(string archivo)
    {
        var m = Datos.Manifiesto(paquetes: [Datos.Paquete("2026", new string('a', 64)) with { Archivo = archivo }]);
        Assert.Throws<GestorException>(() => SerializadorManifiesto.Leer(SerializadorManifiesto.Escribir(m)));
    }

    [Fact]
    public void Sha256_mal_formado_es_invalido()
    {
        var m = Datos.Manifiesto(paquetes: [Datos.Paquete("2026", "ABC")]);
        Assert.Throws<GestorException>(() => SerializadorManifiesto.Leer(SerializadorManifiesto.Escribir(m)));
    }

    [Theory]
    [InlineData("..\\otro.addin")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("practicacq.dll")]
    public void Heredado_con_ruta_es_invalido(string heredado)
    {
        var m = Datos.Manifiesto(heredados: [heredado]);
        Assert.Throws<GestorException>(() => SerializadorManifiesto.Leer(SerializadorManifiesto.Escribir(m)));
    }

    [Fact]
    public void Sha256Archivo_da_hex_en_minusculas()
    {
        using var tmp = new CarpetaTemporal();
        string ruta = tmp.Combinar("a.txt");
        File.WriteAllText(ruta, "abc");
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Sha256Archivo.Calcular(ruta));
    }
}
```

- [ ] **Paso 4: Ejecutar y ver que falla**

Run: `dotnet test gestor/CQEING.Gestor.Core.Tests`
Esperado: no compila (`VersionAddin`, `EtiquetaRelease`, `Manifiesto`… no existen).

- [ ] **Paso 5: Implementar**

`gestor/CQEING.Gestor.Core/ErrorGestor.cs`:
```csharp
namespace CQEING.Gestor.Core;

/// <summary>Errores que el usuario del gestor tiene que entender; el mensaje va tal cual a la ventana.</summary>
public enum TipoError
{
    SinConexion,
    LimiteGitHub,
    HashNoCoincide,
    SinPaqueteParaRevit,
    ManifiestoInvalido,
    PaqueteInvalido,
}

public sealed class GestorException(TipoError tipo, string mensaje, Exception? causa = null) : Exception(mensaje, causa)
{
    public TipoError Tipo { get; } = tipo;
}
```

`gestor/CQEING.Gestor.Core/VersionAddin.cs`:
```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace CQEING.Gestor.Core;

/// <summary>Versión X.Y.Z de un add-in, con sufijo opcional -rc.N (prerelease, anterior a X.Y.Z).</summary>
public sealed record VersionAddin(int Mayor, int Menor, int Parche, int? Rc) : IComparable<VersionAddin>
{
    private static readonly Regex Patron = new(@"^([0-9]{1,9})\.([0-9]{1,9})\.([0-9]{1,9})(?:-rc\.([0-9]{1,9}))?$", RegexOptions.CultureInvariant);

    public bool EsEstable => Rc is null;

    public static VersionAddin? Interpretar(string? texto)
    {
        Match m = Patron.Match(texto ?? string.Empty);
        if (!m.Success) return null;

        static int Numero(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);
        return new VersionAddin(Numero(m.Groups[1]), Numero(m.Groups[2]), Numero(m.Groups[3]), m.Groups[4].Success ? Numero(m.Groups[4]) : null);
    }

    public int CompareTo(VersionAddin? otra)
    {
        if (otra is null) return 1;
        int c = Mayor.CompareTo(otra.Mayor);
        if (c == 0) c = Menor.CompareTo(otra.Menor);
        if (c == 0) c = Parche.CompareTo(otra.Parche);
        if (c != 0) return c;
        return (Rc, otra.Rc) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            var (a, b) => a.Value.CompareTo(b.Value),
        };
    }

    public override string ToString() => Rc is null ? $"{Mayor}.{Menor}.{Parche}" : $"{Mayor}.{Menor}.{Parche}-rc.{Rc}";
}
```

`gestor/CQEING.Gestor.Core/EtiquetaRelease.cs`:
```csharp
using System.Text.RegularExpressions;

namespace CQEING.Gestor.Core;

/// <summary>Etiqueta de release del monorepo: &lt;id del add-in&gt;-vX.Y.Z[-rc.N]. El gestor se publica con el id «gestor».</summary>
public sealed record EtiquetaRelease(string Id, VersionAddin Version)
{
    public const string IdGestor = "gestor";

    private static readonly Regex Patron = new(@"^(?<id>[a-z0-9]+(?:-[a-z0-9]+)*)-v(?<version>[0-9.]+(?:-rc\.[0-9]+)?)$", RegexOptions.CultureInvariant);

    public static EtiquetaRelease? Interpretar(string? etiqueta)
    {
        Match m = Patron.Match(etiqueta ?? string.Empty);
        if (!m.Success) return null;
        VersionAddin? version = VersionAddin.Interpretar(m.Groups["version"].Value);
        return version is null ? null : new EtiquetaRelease(m.Groups["id"].Value, version);
    }

    public override string ToString() => $"{Id}-v{Version}";
}
```

`gestor/CQEING.Gestor.Core/Manifiesto.cs`:
```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CQEING.Gestor.Core;

public sealed record PaqueteManifiesto(string Revit, string Framework, string Archivo, string Sha256);

/// <summary>manifest.json de una release (esquema 1, sección 4 de la spec).</summary>
public sealed record Manifiesto(
    int Esquema,
    string Id,
    string Nombre,
    string Descripcion,
    string Version,
    DateTimeOffset PublicadoEn,
    string Notas,
    IReadOnlyList<string> Heredados,
    IReadOnlyList<PaqueteManifiesto> Paquetes)
{
    public PaqueteManifiesto? PaqueteParaRevit(string anio) => Paquetes.FirstOrDefault(p => p.Revit == anio);
}

public static class SerializadorManifiesto
{
    public const int EsquemaActual = 1;

    private static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // tildes legibles en el archivo
    };

    private static readonly Regex Sha256Valido = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex NombreAddinValido = new(@"^[A-Za-z0-9._-]+\.addin$", RegexOptions.CultureInvariant);

    public static string Escribir(Manifiesto manifiesto) => JsonSerializer.Serialize(manifiesto, Opciones);

    /// <summary>
    /// Lee y valida. Los nombres de archivo y de .addin heredados se usan para descargar y para BORRAR
    /// archivos del usuario, así que solo se aceptan nombres sueltos, sin carpetas.
    /// </summary>
    public static Manifiesto Leer(string json)
    {
        Manifiesto? m;
        try
        {
            m = JsonSerializer.Deserialize<Manifiesto>(json, Opciones);
        }
        catch (JsonException e)
        {
            throw Invalido("no es un JSON válido.", e);
        }

        if (m is null) throw Invalido("está vacío.");
        if (m.Esquema != EsquemaActual)
            throw Invalido($"usa el esquema {m.Esquema} y este gestor entiende el {EsquemaActual}. Actualice el gestor.");
        if (string.IsNullOrWhiteSpace(m.Id) || string.IsNullOrWhiteSpace(m.Nombre) || VersionAddin.Interpretar(m.Version) is null)
            throw Invalido("no trae id, nombre o una versión válida.");
        if (m.Paquetes is not { Count: > 0 } || !m.Paquetes.All(PaqueteValido))
            throw Invalido("no trae paquetes válidos.");

        IReadOnlyList<string> heredados = m.Heredados ?? [];
        if (!heredados.All(h => h is not null && NombreAddinValido.IsMatch(h)))
            throw Invalido("trae un .addin heredado con un nombre no permitido.");

        return m with { Descripcion = m.Descripcion ?? string.Empty, Notas = m.Notas ?? string.Empty, Heredados = heredados };
    }

    private static bool PaqueteValido(PaqueteManifiesto? p) =>
        p is not null
        && !string.IsNullOrWhiteSpace(p.Revit)
        && p.Archivo is not null
        && p.Archivo == Path.GetFileName(p.Archivo)
        && p.Archivo.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        && p.Sha256 is not null
        && Sha256Valido.IsMatch(p.Sha256);

    private static GestorException Invalido(string detalle, Exception? causa = null) =>
        new(TipoError.ManifiestoInvalido, $"El manifiesto {detalle}", causa);
}
```

`gestor/CQEING.Gestor.Core/Sha256Archivo.cs`:
```csharp
using System.Security.Cryptography;

namespace CQEING.Gestor.Core;

public static class Sha256Archivo
{
    public static string Calcular(string ruta)
    {
        using FileStream archivo = File.OpenRead(ruta);
        return Convert.ToHexString(SHA256.HashData(archivo)).ToLowerInvariant();
    }
}
```

`gestor/CQEING.Gestor.Core/Repositorio.cs`:
```csharp
namespace CQEING.Gestor.Core;

public static class Repositorio
{
    /// <summary>Repo público de los add-ins (antes Add-in-Acero-en-columnas-2027; GitHub redirige el nombre viejo).</summary>
    public const string Nombre = "utilidadcqeing-prog/CQEING-Addins";

    public const string ArchivoManifiesto = "manifest.json";
}
```

- [ ] **Paso 6: Ejecutar y ver que pasa**

Run: `dotnet test gestor/CQEING.Gestor.Core.Tests`
Esperado: todas las pruebas en PASS.

- [ ] **Paso 7: Commit**

```powershell
git add CQEING-Addins.sln gestor/CQEING.Gestor.Core gestor/CQEING.Gestor.Core.Tests
git commit -m "feat(gestor): versión, etiqueta y manifiesto de las releases"
```

---

### Tarea 7: Publicador (compila, empaqueta y escribe `manifest.json`)

**Archivos:**
- Crear: `tools/CQEING.Publicador/CQEING.Publicador.csproj`, `ConfiguracionAddin.cs`, `Empaquetador.cs`, `Publicacion.cs`, `CompiladorDotnet.cs`, `Program.cs`
- Crear: `tools/CQEING.Publicador.Tests/` (xUnit), `PublicacionTests.cs`, `EmpaquetadorTests.cs`, `CarpetaTemporal.cs`

**Interfaces:**
- Consume: `EtiquetaRelease`, `Manifiesto`, `PaqueteManifiesto`, `SerializadorManifiesto`, `Sha256Archivo`, `Repositorio.ArchivoManifiesto` (tarea 6); `addin.json` (tarea 5); `framework.txt` (tarea 3).
- Produce (namespace `CQEING.Publicador`):
  - `record ConfiguracionAddin(string Id, string Nombre, string Descripcion, string Proyecto, IReadOnlyList<string> Revit, IReadOnlyList<string> Heredados)`; `static ConfiguracionAddin Leer(string ruta)`
  - `static class Empaquetador { const string ArchivoFramework = "framework.txt"; void CrearZip(string carpetaCompilada, string zipDestino) }`
  - `interface ICompilador { string Compilar(string rutaProyecto, string anio, string version); }` (devuelve la carpeta de salida)
  - `class Publicacion(ICompilador compilador) { Manifiesto Ejecutar(string raizRepo, string etiqueta, string notas, DateTimeOffset publicadoEn, string carpetaSalida) }`
  - CLI: `dotnet run --project tools/CQEING.Publicador -- --repo <raíz> --etiqueta <tag> --notas-archivo <ruta> --salida <carpeta>`

- [ ] **Paso 1: Crear los proyectos**

```powershell
dotnet new console -n CQEING.Publicador -o tools/CQEING.Publicador -f net8.0
dotnet new xunit -n CQEING.Publicador.Tests -o tools/CQEING.Publicador.Tests -f net8.0
Remove-Item tools/CQEING.Publicador.Tests/UnitTest1.cs
dotnet add tools/CQEING.Publicador reference gestor/CQEING.Gestor.Core
dotnet add tools/CQEING.Publicador.Tests reference tools/CQEING.Publicador gestor/CQEING.Gestor.Core
dotnet sln CQEING-Addins.sln add tools/CQEING.Publicador tools/CQEING.Publicador.Tests
```

- [ ] **Paso 2: Escribir las pruebas que fallan**

`tools/CQEING.Publicador.Tests/CarpetaTemporal.cs` (misma clase de apoyo que en las pruebas del gestor; cada proyecto de pruebas tiene la suya):
```csharp
namespace CQEING.Publicador.Tests;

internal sealed class CarpetaTemporal : IDisposable
{
    public string Ruta { get; } = Directory.CreateTempSubdirectory("cqeing-publicador-").FullName;

    public string Combinar(params string[] partes) => Path.Combine([Ruta, .. partes]);

    public void Dispose()
    {
        try { Directory.Delete(Ruta, recursive: true); }
        catch (IOException) { }
    }
}
```

`tools/CQEING.Publicador.Tests/EmpaquetadorTests.cs`:
```csharp
using System.IO.Compression;

namespace CQEING.Publicador.Tests;

public class EmpaquetadorTests : IDisposable
{
    private readonly CarpetaTemporal _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string Compilada(int addins)
    {
        string carpeta = _tmp.Combinar("salida");
        Directory.CreateDirectory(Path.Combine(carpeta, "Resources"));
        File.WriteAllText(Path.Combine(carpeta, "practicacq.dll"), "dll");
        File.WriteAllText(Path.Combine(carpeta, "practicacq.pdb"), "símbolos");
        File.WriteAllText(Path.Combine(carpeta, Empaquetador.ArchivoFramework), "net8.0-windows");
        File.WriteAllText(Path.Combine(carpeta, "Resources", "icono16.png"), "png");
        for (int i = 0; i < addins; i++) File.WriteAllText(Path.Combine(carpeta, $"a{i}.addin"), "<RevitAddIns/>");
        return carpeta;
    }

    [Fact]
    public void Zip_sin_pdb_ni_framework_y_con_resources()
    {
        string zip = _tmp.Combinar("p.zip");
        Empaquetador.CrearZip(Compilada(addins: 1), zip);

        using ZipArchive archivo = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "Resources/icono16.png", "a0.addin", "practicacq.dll" }, archivo.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Exige_exactamente_un_addin(int addins) =>
        Assert.Throws<InvalidOperationException>(() => Empaquetador.CrearZip(Compilada(addins), _tmp.Combinar("p.zip")));
}
```

`tools/CQEING.Publicador.Tests/PublicacionTests.cs`:
```csharp
using System.IO.Compression;
using CQEING.Gestor.Core;

namespace CQEING.Publicador.Tests;

public class PublicacionTests : IDisposable
{
    private static readonly DateTimeOffset Fecha = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);
    private readonly CarpetaTemporal _tmp = new();
    private readonly CompiladorFalso _compilador = new();

    public PublicacionTests()
    {
        string carpeta = _tmp.Combinar("repo", "addins", "armado-columnas");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "addin.json"), """
            {
              "id": "armado-columnas",
              "nombre": "Armado de columnas",
              "descripcion": "Estribos, columnetas y viguetas de confinamiento.",
              "proyecto": "practicacq.csproj",
              "revit": ["2026", "2027"],
              "heredados": ["practicacq.addin"]
            }
            """);
    }

    public void Dispose() => _tmp.Dispose();

    private Manifiesto Publicar(string etiqueta) =>
        new Publicacion(_compilador).Ejecutar(_tmp.Combinar("repo"), etiqueta, "Notas de la versión\n", Fecha, _tmp.Combinar("dist"));

    [Fact]
    public void Crea_un_zip_por_anio_y_el_manifiesto()
    {
        Manifiesto devuelto = Publicar("armado-columnas-v1.2.0");

        Manifiesto m = SerializadorManifiesto.Leer(File.ReadAllText(_tmp.Combinar("dist", "manifest.json")));
        Assert.Equal(devuelto.Paquetes, m.Paquetes);
        Assert.Equal(new[] { "2026", "2027" }, _compilador.Anios);
        Assert.Equal(("armado-columnas", "Armado de columnas", "1.2.0", "Notas de la versión", Fecha),
            (m.Id, m.Nombre, m.Version, m.Notas, m.PublicadoEn));
        Assert.Equal(new[] { "practicacq.addin" }, m.Heredados);
        Assert.Equal(
            new[] { ("2026", "net8.0-windows", "armado-columnas-1.2.0-revit2026.zip"), ("2027", "net10.0-windows", "armado-columnas-1.2.0-revit2027.zip") },
            m.Paquetes.Select(p => (p.Revit, p.Framework, p.Archivo)));
        foreach (PaqueteManifiesto p in m.Paquetes)
            Assert.Equal(Sha256Archivo.Calcular(_tmp.Combinar("dist", p.Archivo)), p.Sha256);
    }

    [Fact]
    public void Cada_zip_trae_la_compilacion_de_su_anio()
    {
        Publicar("armado-columnas-v1.2.0");

        using ZipArchive zip = ZipFile.OpenRead(_tmp.Combinar("dist", "armado-columnas-1.2.0-revit2027.zip"));
        using var lector = new StreamReader(zip.GetEntry("practicacq.dll")!.Open());
        Assert.Equal("dll 2027 1.2.0", lector.ReadToEnd());
    }

    [Fact]
    public void Prerelease_conserva_el_sufijo_en_la_version()
    {
        Assert.Equal("1.2.0-rc.1", Publicar("armado-columnas-v1.2.0-rc.1").Version);
        Assert.Equal("1.2.0-rc.1", _compilador.Versiones[0]);
    }

    [Theory]
    [InlineData("v1.2.0")]
    [InlineData("inexistente-v1.0.0")]
    public void Etiqueta_mal_formada_o_de_otro_addin_falla(string etiqueta) =>
        Assert.Throws<InvalidOperationException>(() => Publicar(etiqueta));

    private sealed class CompiladorFalso : ICompilador
    {
        public List<string> Anios { get; } = [];
        public List<string> Versiones { get; } = [];

        public string Compilar(string rutaProyecto, string anio, string version)
        {
            string salida = Path.Combine(Path.GetDirectoryName(rutaProyecto)!, "bin", "Release", anio);
            Directory.CreateDirectory(Path.Combine(salida, "Resources"));
            File.WriteAllText(Path.Combine(salida, "practicacq.dll"), $"dll {anio} {version}");
            File.WriteAllText(Path.Combine(salida, "practicacq.pdb"), "símbolos");
            File.WriteAllText(Path.Combine(salida, "practicacq.addin"), "<RevitAddIns/>");
            File.WriteAllText(Path.Combine(salida, "Resources", "icono16.png"), "png");
            File.WriteAllText(Path.Combine(salida, Empaquetador.ArchivoFramework), anio == "2026" ? "net8.0-windows\r\n" : "net10.0-windows\r\n");
            Anios.Add(anio);
            Versiones.Add(version);
            return salida;
        }
    }
}
```

- [ ] **Paso 3: Ejecutar y ver que falla**

Run: `dotnet test tools/CQEING.Publicador.Tests`
Esperado: no compila (`Empaquetador`, `Publicacion`, `ICompilador` no existen).

- [ ] **Paso 4: Implementar**

`tools/CQEING.Publicador/ConfiguracionAddin.cs`:
```csharp
using System.Text.Json;

namespace CQEING.Publicador;

/// <summary>addins/&lt;id&gt;/addin.json: qué es el add-in, qué proyecto compilar y para qué años de Revit.</summary>
public sealed record ConfiguracionAddin(
    string Id,
    string Nombre,
    string Descripcion,
    string Proyecto,
    IReadOnlyList<string> Revit,
    IReadOnlyList<string> Heredados)
{
    private static readonly JsonSerializerOptions Opciones = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static ConfiguracionAddin Leer(string ruta)
    {
        ConfiguracionAddin c = JsonSerializer.Deserialize<ConfiguracionAddin>(File.ReadAllText(ruta), Opciones)
            ?? throw new InvalidOperationException($"{ruta} está vacío.");
        if (string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Nombre) || string.IsNullOrWhiteSpace(c.Proyecto) || c.Revit is not { Count: > 0 })
            throw new InvalidOperationException($"{ruta}: faltan id, nombre, proyecto o revit.");
        return c with { Descripcion = c.Descripcion ?? string.Empty, Heredados = c.Heredados ?? [] };
    }
}
```

`tools/CQEING.Publicador/Empaquetador.cs`:
```csharp
using System.IO.Compression;

namespace CQEING.Publicador;

public static class Empaquetador
{
    /// <summary>Lo escribe addins/Directory.Build.props (target EscribirFramework); no va dentro del ZIP.</summary>
    public const string ArchivoFramework = "framework.txt";

    /// <summary>ZIP con la salida de compilación sin .pdb. Debe traer exactamente un .addin: el gestor lo activa.</summary>
    public static void CrearZip(string carpetaCompilada, string zipDestino)
    {
        int addins = Directory.GetFiles(carpetaCompilada, "*.addin").Length;
        if (addins != 1)
            throw new InvalidOperationException($"{carpetaCompilada} debe tener exactamente un .addin y tiene {addins}.");

        File.Delete(zipDestino);
        using ZipArchive zip = ZipFile.Open(zipDestino, ZipArchiveMode.Create);
        foreach (string archivo in Directory.EnumerateFiles(carpetaCompilada, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relativa = Path.GetRelativePath(carpetaCompilada, archivo);
            if (relativa == ArchivoFramework || Path.GetExtension(archivo).Equals(".pdb", StringComparison.OrdinalIgnoreCase))
                continue;
            zip.CreateEntryFromFile(archivo, relativa.Replace('\\', '/'), CompressionLevel.Optimal);
        }
    }
}
```

`tools/CQEING.Publicador/Publicacion.cs`:
```csharp
using CQEING.Gestor.Core;

namespace CQEING.Publicador;

public interface ICompilador
{
    /// <summary>Compila el proyecto en Release para ese año de Revit y devuelve la carpeta de salida.</summary>
    string Compilar(string rutaProyecto, string anio, string version);
}

/// <summary>De una etiqueta &lt;id&gt;-vX.Y.Z a los archivos de la release: un ZIP por año de Revit y manifest.json.</summary>
public sealed class Publicacion(ICompilador compilador)
{
    public Manifiesto Ejecutar(string raizRepo, string etiqueta, string notas, DateTimeOffset publicadoEn, string carpetaSalida)
    {
        EtiquetaRelease tag = EtiquetaRelease.Interpretar(etiqueta)
            ?? throw new InvalidOperationException($"La etiqueta '{etiqueta}' no tiene la forma <add-in>-vX.Y.Z.");
        string carpetaAddin = Path.Combine(raizRepo, "addins", tag.Id);
        string rutaConfig = Path.Combine(carpetaAddin, "addin.json");
        if (!File.Exists(rutaConfig))
            throw new InvalidOperationException($"No existe {rutaConfig}: la etiqueta no corresponde a ningún add-in.");
        ConfiguracionAddin config = ConfiguracionAddin.Leer(rutaConfig);
        if (config.Id != tag.Id)
            throw new InvalidOperationException($"{rutaConfig} dice id '{config.Id}' y la etiqueta es de '{tag.Id}'.");

        string version = tag.Version.ToString();
        Directory.CreateDirectory(carpetaSalida);
        var paquetes = new List<PaqueteManifiesto>();
        foreach (string anio in config.Revit)
        {
            string compilada = compilador.Compilar(Path.Combine(carpetaAddin, config.Proyecto), anio, version);
            string framework = File.ReadAllText(Path.Combine(compilada, Empaquetador.ArchivoFramework)).Trim();
            string archivo = $"{tag.Id}-{version}-revit{anio}.zip";
            string zip = Path.Combine(carpetaSalida, archivo);
            Empaquetador.CrearZip(compilada, zip);
            paquetes.Add(new PaqueteManifiesto(anio, framework, archivo, Sha256Archivo.Calcular(zip)));
        }

        var manifiesto = new Manifiesto(SerializadorManifiesto.EsquemaActual, tag.Id, config.Nombre, config.Descripcion,
            version, publicadoEn, notas.Trim(), config.Heredados, paquetes);
        string json = SerializadorManifiesto.Escribir(manifiesto);
        SerializadorManifiesto.Leer(json); // lo que el gestor rechazaría no se publica
        File.WriteAllText(Path.Combine(carpetaSalida, Repositorio.ArchivoManifiesto), json);
        return manifiesto;
    }
}
```

`tools/CQEING.Publicador/CompiladorDotnet.cs`:
```csharp
using System.Diagnostics;

namespace CQEING.Publicador;

public sealed class CompiladorDotnet : ICompilador
{
    public string Compilar(string rutaProyecto, string anio, string version)
    {
        // Misma carpeta que fija addins/Directory.Build.props (OutputPath). Se vacía antes para que
        // nada de una compilación anterior se cuele en el ZIP.
        string salida = Path.Combine(Path.GetDirectoryName(rutaProyecto)!, "bin", "Release", anio);
        if (Directory.Exists(salida)) Directory.Delete(salida, recursive: true);

        var inicio = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (string argumento in new[] { "build", rutaProyecto, "-c", "Release", $"-p:RevitVersion={anio}", $"-p:Version={version}" })
            inicio.ArgumentList.Add(argumento);

        using Process proceso = Process.Start(inicio) ?? throw new InvalidOperationException("No se pudo ejecutar dotnet.");
        proceso.WaitForExit();
        if (proceso.ExitCode != 0)
            throw new InvalidOperationException($"Falló la compilación de {Path.GetFileName(rutaProyecto)} para Revit {anio}.");
        return salida;
    }
}
```

`tools/CQEING.Publicador/Program.cs`:
```csharp
using CQEING.Gestor.Core;
using CQEING.Publicador;

try
{
    Dictionary<string, string> argumentos = LeerArgumentos(args);
    Manifiesto m = new Publicacion(new CompiladorDotnet()).Ejecutar(
        Requerido(argumentos, "--repo"),
        Requerido(argumentos, "--etiqueta"),
        File.ReadAllText(Requerido(argumentos, "--notas-archivo")),
        DateTimeOffset.UtcNow,
        Requerido(argumentos, "--salida"));
    Console.WriteLine($"Listo {m.Id} {m.Version}: {string.Join(", ", m.Paquetes.Select(p => p.Archivo))}");
    return 0;
}
catch (Exception e) when (e is InvalidOperationException or GestorException or IOException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

static Dictionary<string, string> LeerArgumentos(string[] args)
{
    if (args.Length % 2 != 0)
        throw new InvalidOperationException("Uso: --repo <raíz> --etiqueta <id-vX.Y.Z> --notas-archivo <ruta> --salida <carpeta>");
    return args.Chunk(2).ToDictionary(par => par[0], par => par[1]);
}

static string Requerido(Dictionary<string, string> argumentos, string clave) =>
    argumentos.TryGetValue(clave, out string? valor) ? valor : throw new InvalidOperationException($"Falta el argumento {clave}.");
```
Borrar el `Program.cs` de la plantilla antes de crear este (el comando `dotnet new console` lo genera con «Hello, World!»).

- [ ] **Paso 5: Ejecutar y ver que pasa**

Run: `dotnet test tools/CQEING.Publicador.Tests`
Esperado: todas en PASS.

- [ ] **Paso 6: Probar el publicador de verdad**

```powershell
"Prueba local" | Set-Content $env:TEMP\notas.md
dotnet run --project tools/CQEING.Publicador -- --repo . --etiqueta armado-columnas-v0.0.1-rc.1 --notas-archivo $env:TEMP\notas.md --salida dist
Get-ChildItem dist -Name; Get-Content dist/manifest.json
Remove-Item dist -Recurse
```
Esperado: `armado-columnas-0.0.1-rc.1-revit2026.zip`, `...-revit2027.zip`, `manifest.json` con `framework` `net8.0-windows` y `net10.0-windows`.

- [ ] **Paso 7: Commit**

```powershell
git add CQEING-Addins.sln tools/CQEING.Publicador tools/CQEING.Publicador.Tests
git commit -m "feat(publicador): ZIP por año de Revit y manifest.json"
```

---

### Tarea 8: Workflow de publicación

**Archivos:**
- Crear: `.github/workflows/publicar.yml`

**Interfaces:**
- Consume: CLI del publicador (tarea 7).
- Produce: release `<id>-vX.Y.Z` con `<id>-<versión>-revit<año>.zip` por año y `manifest.json`; prerelease si la etiqueta termina en `-rc.N`.

- [ ] **Paso 1: Crear `.github/workflows/publicar.yml`**

```yaml
name: Publicar add-in

on:
  push:
    tags: ['*-v*']

permissions:
  contents: write

jobs:
  publicar:
    # Las etiquetas gestor-v* las publica publicar-gestor.yml.
    if: ${{ !startsWith(github.ref_name, 'gestor-v') }}
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      # checkout deja la etiqueta como ligera; se trae de nuevo para leer su mensaje (las notas).
      - run: git fetch --force --tags origin
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            10.0.x
      - name: Notas (mensaje de la etiqueta anotada)
        shell: pwsh
        run: |
          $tag = '${{ github.ref_name }}'
          if ((git cat-file -t "refs/tags/$tag") -ne 'tag') { throw "La etiqueta $tag no es anotada. Créela con: git tag -a $tag -m 'notas'" }
          git tag -l --format='%(contents)' $tag | Set-Content notas.md -Encoding utf8
      - name: Compilar, empaquetar y escribir manifest.json
        run: dotnet run --project tools/CQEING.Publicador -c Release -- --repo . --etiqueta "${{ github.ref_name }}" --notas-archivo notas.md --salida dist
      - name: Crear la release
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          $tag = '${{ github.ref_name }}'
          $extra = @()
          if ($tag -match '-rc\.[0-9]+$') { $extra += '--prerelease' }
          gh release create $tag (Get-ChildItem dist -File).FullName --title $tag --notes-file notas.md @extra
```

- [ ] **Paso 2: Commit**

```powershell
git add .github/workflows/publicar.yml
git commit -m "ci: publicar un add-in al subir su etiqueta"
```

- [ ] **Paso 3: Probar con una prerelease (pedir permiso al usuario para el push y la etiqueta)**

Con la rama `migracion/monorepo` ya fusionada en `main` (tarea 5, paso 9) y este commit subido:
```powershell
git switch main; git pull
git tag -a armado-columnas-v0.9.0-rc.1 -m "Prueba del flujo de publicación"
git push origin armado-columnas-v0.9.0-rc.1
```
Esperado: el workflow «Publicar add-in» en verde y una release marcada como prerelease con los dos ZIP y `manifest.json`. Descargar `manifest.json` y comprobar que el SHA-256 de un ZIP coincide: `(Get-FileHash <zip> -Algorithm SHA256).Hash.ToLower()`.

---

### Tarea 9: Revisar secretos, renombrar y hacer público el repo

Tarea manual del usuario en GitHub, guiada. Nada se hace público sin su confirmación.

- [ ] **Paso 1: Buscar secretos en todo el historial**

```powershell
winget install --id Gitleaks.Gitleaks -e
gitleaks git . --report-path $env:TEMP\gitleaks.json
git log --all --diff-filter=A --name-only --format= | Select-String -Pattern '(^|/)\.env$|\.pfx$|\.pem$|appsettings\..*\.json$'
```
Esperado: gitleaks sin hallazgos (`no leaks found`) y el segundo comando sin resultados (solo existe `.env.example`). Si aparece un secreto real: **detenerse**, avisar al usuario, rotar esa clave en su servicio (API de la plataforma, Cloudinary) y decidir con el usuario si se limpia el historial antes de seguir.

- [ ] **Paso 2: El usuario renombra el repo**

GitHub → Settings → General → Repository name: `CQEING-Addins`. Luego, en el clon:
```powershell
git remote set-url origin https://github.com/utilidadcqeing-prog/CQEING-Addins.git
git fetch origin
```

- [ ] **Paso 3: El usuario lo hace público**

GitHub → Settings → Danger Zone → Change visibility → Public. Activar Settings → Code security → Secret scanning y Push protection.

- [ ] **Paso 4: Comprobar el acceso sin token**

```powershell
(Invoke-WebRequest https://api.github.com/repos/utilidadcqeing-prog/CQEING-Addins/releases -UseBasicParsing).StatusCode
```
Esperado: `200`, y la prerelease de la tarea 8 en la respuesta.

---

## Fase 3: el gestor

### Tarea 10: Rutas, Revit instalados y releases de GitHub con caché

**Archivos:**
- Crear: `gestor/CQEING.Gestor.Core/RutasGestor.cs`, `DetectorRevit.cs`, `ClienteHttp.cs`, `FuenteReleases.cs`
- Crear: `gestor/CQEING.Gestor.Core.Tests/RutasYRevitTests.cs`, `FuenteReleasesTests.cs`, `Apoyo/HandlerFalso.cs`, `Apoyo/Respuestas.cs`

**Interfaces:**
- Consume: `GestorException`, `TipoError`, `Repositorio.Nombre` (tarea 6).
- Produce:
  - `class RutasGestor(string localAppData, string appData)`: `static DelUsuario()`, `CarpetaAddins`, `CarpetaCache`, `CarpetaVersiones(string id)`, `CarpetaVersion(string id, string version, string anio)`, `CarpetaAddinsRevitRaiz`, `CarpetaAddinsRevit(string anio)`, `ArchivoAddin(string id, string anio)`
  - `static class DetectorRevit { IReadOnlyList<string> AniosInstalados(string programFiles) }`
  - `static class ClienteHttp { HttpClient Crear(HttpMessageHandler? handler = null) }`
  - `record ArchivoRelease(string Nombre, Uri Url)`; `record ReleaseGitHub(string Etiqueta, bool Borrador, bool Prerelease, IReadOnlyList<ArchivoRelease> Archivos)`; `record ResultadoReleases(IReadOnlyList<ReleaseGitHub> Releases, string? Aviso)`
  - `class FuenteReleases(HttpClient http, string carpetaCache, string repositorio = Repositorio.Nombre) { Task<ResultadoReleases> ObtenerAsync(CancellationToken ct = default) }`
  - Pruebas: `HandlerFalso(Func<HttpRequestMessage, HttpResponseMessage>)` con `Pedidos` y `Cliente()`; `Respuestas.Texto/Bytes/Estado`

- [ ] **Paso 1: Apoyo de pruebas HTTP**

`gestor/CQEING.Gestor.Core.Tests/Apoyo/HandlerFalso.cs`:
```csharp
namespace CQEING.Gestor.Core.Tests.Apoyo;

/// <summary>Servidor HTTP falso: responde con la función dada y guarda cada pedido.</summary>
internal sealed class HandlerFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<HttpRequestMessage> Pedidos { get; } = [];

    public HttpClient Cliente() => ClienteHttp.Crear(this);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
    {
        Pedidos.Add(pedido);
        return Task.FromResult(responder(pedido));
    }
}
```

`gestor/CQEING.Gestor.Core.Tests/Apoyo/Respuestas.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;

namespace CQEING.Gestor.Core.Tests.Apoyo;

internal static class Respuestas
{
    public static HttpResponseMessage Texto(string contenido, string? etag = null)
    {
        var respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(contenido) };
        if (etag is not null) respuesta.Headers.ETag = new EntityTagHeaderValue(etag);
        return respuesta;
    }

    public static HttpResponseMessage Bytes(byte[] datos) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(datos) };

    public static HttpResponseMessage Estado(HttpStatusCode estado) => new(estado);
}
```

- [ ] **Paso 2: Escribir las pruebas que fallan**

`gestor/CQEING.Gestor.Core.Tests/RutasYRevitTests.cs`:
```csharp
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class RutasYRevitTests
{
    [Fact]
    public void Rutas_de_la_spec()
    {
        var rutas = new RutasGestor(@"C:\U\Local", @"C:\U\Roaming");
        Assert.Equal(@"C:\U\Local\CQEING\Addins\imagenes\1.2.0\2026", rutas.CarpetaVersion("imagenes", "1.2.0", "2026"));
        Assert.Equal(@"C:\U\Roaming\Autodesk\Revit\Addins\2027\CQEING.imagenes.addin", rutas.ArchivoAddin("imagenes", "2027"));
        Assert.Equal(@"C:\U\Local\CQEING\Gestor\cache", rutas.CarpetaCache);
    }

    [Fact]
    public void Detecta_solo_carpetas_de_Revit_con_su_API()
    {
        using var tmp = new CarpetaTemporal();
        foreach (string carpeta in new[] { "Revit 2027", "Revit 2026", "Revit 2025", "Revit Viewer", "AutoCAD 2026" })
            Directory.CreateDirectory(tmp.Combinar("Autodesk", carpeta));
        File.WriteAllText(tmp.Combinar("Autodesk", "Revit 2026", "RevitAPI.dll"), "");
        File.WriteAllText(tmp.Combinar("Autodesk", "Revit 2027", "RevitAPI.dll"), "");

        Assert.Equal(new[] { "2026", "2027" }, DetectorRevit.AniosInstalados(tmp.Ruta));
    }

    [Fact]
    public void Sin_Autodesk_no_hay_Revit()
    {
        using var tmp = new CarpetaTemporal();
        Assert.Empty(DetectorRevit.AniosInstalados(tmp.Ruta));
    }
}
```

`gestor/CQEING.Gestor.Core.Tests/FuenteReleasesTests.cs`:
```csharp
using System.Net;
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class FuenteReleasesTests : IDisposable
{
    private const string Json = """
        [{ "tag_name": "armado-columnas-v1.0.0", "draft": false, "prerelease": false,
           "assets": [{ "name": "manifest.json", "browser_download_url": "https://github.test/a/manifest.json" }] }]
        """;

    private readonly CarpetaTemporal _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private FuenteReleases Fuente(HandlerFalso handler) => new(handler.Cliente(), _tmp.Ruta, "dueno/repo");

    [Fact]
    public async Task Lee_las_releases_y_las_guarda_con_su_ETag()
    {
        var handler = new HandlerFalso(_ => Respuestas.Texto(Json, "\"v1\""));

        ResultadoReleases r = await Fuente(handler).ObtenerAsync();

        Assert.Null(r.Aviso);
        ReleaseGitHub release = Assert.Single(r.Releases);
        Assert.Equal("armado-columnas-v1.0.0", release.Etiqueta);
        Assert.Equal(new Uri("https://github.test/a/manifest.json"), release.Archivos.Single().Url);
        Assert.Equal("https://api.github.com/repos/dueno/repo/releases?per_page=100", handler.Pedidos[0].RequestUri!.ToString());
        Assert.Contains("CQEING-Gestor", handler.Pedidos[0].Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Sin_cambios_GitHub_responde_304_y_se_usa_la_lista_guardada()
    {
        int llamada = 0;
        var handler = new HandlerFalso(_ => ++llamada == 1 ? Respuestas.Texto(Json, "\"v1\"") : Respuestas.Estado(HttpStatusCode.NotModified));
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();

        ResultadoReleases r = await fuente.ObtenerAsync();

        Assert.Equal("\"v1\"", handler.Pedidos[1].Headers.IfNoneMatch.Single().ToString());
        Assert.Null(r.Aviso);
        Assert.Single(r.Releases);
    }

    [Fact]
    public async Task Limite_de_GitHub_usa_la_lista_guardada_con_aviso()
    {
        int llamada = 0;
        var handler = new HandlerFalso(_ =>
        {
            if (++llamada == 1) return Respuestas.Texto(Json, "\"v1\"");
            var limitada = Respuestas.Estado(HttpStatusCode.Forbidden);
            limitada.Headers.Add("x-ratelimit-remaining", "0");
            return limitada;
        });
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();

        ResultadoReleases r = await fuente.ObtenerAsync();

        Assert.Single(r.Releases);
        Assert.Contains("limitó", r.Aviso);
    }

    [Fact]
    public async Task Limite_de_GitHub_sin_lista_guardada_lanza_LimiteGitHub()
    {
        var handler = new HandlerFalso(_ => Respuestas.Estado(HttpStatusCode.TooManyRequests));
        var error = await Assert.ThrowsAsync<GestorException>(() => Fuente(handler).ObtenerAsync());
        Assert.Equal(TipoError.LimiteGitHub, error.Tipo);
    }

    [Fact]
    public async Task Sin_conexion_y_sin_lista_guardada_lanza_SinConexion()
    {
        var handler = new HandlerFalso(_ => throw new HttpRequestException("sin red"));
        var error = await Assert.ThrowsAsync<GestorException>(() => Fuente(handler).ObtenerAsync());
        Assert.Equal(TipoError.SinConexion, error.Tipo);
    }

    [Fact]
    public async Task Lista_guardada_danada_con_304_se_vuelve_a_pedir_sin_ETag()
    {
        int llamada = 0;
        var handler = new HandlerFalso(p => ++llamada == 2 ? Respuestas.Estado(HttpStatusCode.NotModified) : Respuestas.Texto(Json, "\"v1\""));
        FuenteReleases fuente = Fuente(handler);
        await fuente.ObtenerAsync();
        File.WriteAllText(Path.Combine(_tmp.Ruta, "releases.json"), "{ dañado");

        ResultadoReleases r = await fuente.ObtenerAsync();

        Assert.Single(r.Releases);
        Assert.Empty(handler.Pedidos[2].Headers.IfNoneMatch);
    }
}
```

- [ ] **Paso 3: Ejecutar y ver que falla**

Run: `dotnet test gestor/CQEING.Gestor.Core.Tests`
Esperado: no compila (`RutasGestor`, `DetectorRevit`, `ClienteHttp`, `FuenteReleases` no existen).

- [ ] **Paso 4: Implementar**

`gestor/CQEING.Gestor.Core/RutasGestor.cs`:
```csharp
namespace CQEING.Gestor.Core;

/// <summary>Dónde vive cada cosa del gestor. Recibe las carpetas base para poder probarlo sin tocar las del usuario.</summary>
public sealed class RutasGestor(string localAppData, string appData)
{
    public static RutasGestor DelUsuario() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public string CarpetaAddins => Path.Combine(localAppData, "CQEING", "Addins");

    public string CarpetaCache => Path.Combine(localAppData, "CQEING", "Gestor", "cache");

    public string CarpetaVersiones(string id) => Path.Combine(CarpetaAddins, id);

    /// <summary>Una carpeta por versión y año: instalar con Revit abierto no choca con la DLL que Revit tiene cargada.</summary>
    public string CarpetaVersion(string id, string version, string anio) => Path.Combine(CarpetaVersiones(id), version, anio);

    public string CarpetaAddinsRevitRaiz => Path.Combine(appData, "Autodesk", "Revit", "Addins");

    public string CarpetaAddinsRevit(string anio) => Path.Combine(CarpetaAddinsRevitRaiz, anio);

    public string ArchivoAddin(string id, string anio) => Path.Combine(CarpetaAddinsRevit(anio), $"CQEING.{id}.addin");
}
```

`gestor/CQEING.Gestor.Core/DetectorRevit.cs`:
```csharp
namespace CQEING.Gestor.Core;

public static class DetectorRevit
{
    /// <summary>Años de Revit instalados: carpetas «Revit AAAA» de Autodesk que traen RevitAPI.dll.</summary>
    public static IReadOnlyList<string> AniosInstalados(string programFiles)
    {
        string autodesk = Path.Combine(programFiles, "Autodesk");
        if (!Directory.Exists(autodesk)) return [];

        return Directory.GetDirectories(autodesk, "Revit *")
            .Select(carpeta => Path.GetFileName(carpeta)["Revit ".Length..])
            .Where(anio => anio.Length == 4 && anio.All(char.IsAsciiDigit)
                && File.Exists(Path.Combine(autodesk, $"Revit {anio}", "RevitAPI.dll")))
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
```

`gestor/CQEING.Gestor.Core/ClienteHttp.cs`:
```csharp
namespace CQEING.Gestor.Core;

public static class ClienteHttp
{
    /// <summary>GitHub rechaza pedidos sin User-Agent. El handler solo se pasa en las pruebas.</summary>
    public static HttpClient Crear(HttpMessageHandler? handler = null)
    {
        HttpClient http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromMinutes(5);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CQEING-Gestor/1.0");
        return http;
    }
}
```

`gestor/CQEING.Gestor.Core/FuenteReleases.cs`:
```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CQEING.Gestor.Core;

public sealed record ArchivoRelease(
    [property: JsonPropertyName("name")] string Nombre,
    [property: JsonPropertyName("browser_download_url")] Uri Url);

public sealed record ReleaseGitHub(
    [property: JsonPropertyName("tag_name")] string Etiqueta,
    [property: JsonPropertyName("draft")] bool Borrador,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] IReadOnlyList<ArchivoRelease> Archivos);

/// <param name="Aviso">Texto para el usuario cuando se usó la lista guardada en vez de la de GitHub.</param>
public sealed record ResultadoReleases(IReadOnlyList<ReleaseGitHub> Releases, string? Aviso);

/// <summary>
/// Releases del repo público, sin token. GitHub da 60 consultas por hora por IP pública (una oficina
/// comparte el cupo), así que la respuesta se guarda con su ETag: si no cambió, GitHub responde 304 y esa
/// consulta no gasta cupo. Si GitHub limita o no hay red, se usa la última lista guardada.
/// </summary>
public sealed class FuenteReleases(HttpClient http, string carpetaCache, string repositorio = Repositorio.Nombre)
{
    private string RutaJson => Path.Combine(carpetaCache, "releases.json");
    private string RutaEtag => Path.Combine(carpetaCache, "releases.etag");

    public Task<ResultadoReleases> ObtenerAsync(CancellationToken ct = default) => ObtenerAsync(usarEtag: true, ct);

    private async Task<ResultadoReleases> ObtenerAsync(bool usarEtag, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repositorio}/releases?per_page=100");
        pedido.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (usarEtag && File.Exists(RutaJson) && File.Exists(RutaEtag))
            pedido.Headers.TryAddWithoutValidation("If-None-Match", File.ReadAllText(RutaEtag));

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.SendAsync(pedido, ct);
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return DesdeCache(TipoError.SinConexion, "No hay conexión con GitHub.", e);
        }

        using (respuesta)
        {
            if (respuesta.StatusCode == HttpStatusCode.NotModified)
            {
                IReadOnlyList<ReleaseGitHub>? guardadas = LeerCache();
                return guardadas is not null ? new ResultadoReleases(guardadas, null) : await ObtenerAsync(usarEtag: false, ct);
            }
            if (EsLimite(respuesta))
                return DesdeCache(TipoError.LimiteGitHub, "GitHub limitó las consultas de esta red; se libera en menos de una hora.", null);
            if (!respuesta.IsSuccessStatusCode)
                return DesdeCache(TipoError.SinConexion, $"GitHub respondió con el error {(int)respuesta.StatusCode}.", null);

            string json = await respuesta.Content.ReadAsStringAsync(ct);
            IReadOnlyList<ReleaseGitHub> releases = Interpretar(json)
                ?? throw new GestorException(TipoError.SinConexion, "GitHub devolvió una lista de releases que no se pudo leer.");
            Directory.CreateDirectory(carpetaCache);
            File.WriteAllText(RutaJson, json);
            if (respuesta.Headers.ETag is { } etag) File.WriteAllText(RutaEtag, etag.ToString());
            else File.Delete(RutaEtag);
            return new ResultadoReleases(releases, null);
        }
    }

    private static bool EsLimite(HttpResponseMessage respuesta) =>
        respuesta.StatusCode == HttpStatusCode.TooManyRequests
        || (respuesta.StatusCode == HttpStatusCode.Forbidden
            && respuesta.Headers.TryGetValues("x-ratelimit-remaining", out IEnumerable<string>? restantes)
            && restantes.FirstOrDefault() == "0");

    private ResultadoReleases DesdeCache(TipoError tipo, string mensaje, Exception? causa)
    {
        IReadOnlyList<ReleaseGitHub> guardadas = LeerCache() ?? throw new GestorException(tipo, mensaje, causa);
        return new ResultadoReleases(guardadas, $"{mensaje} Se muestra la última lista guardada.");
    }

    private IReadOnlyList<ReleaseGitHub>? LeerCache() => File.Exists(RutaJson) ? Interpretar(File.ReadAllText(RutaJson)) : null;

    private static IReadOnlyList<ReleaseGitHub>? Interpretar(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ReleaseGitHub>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Paso 5: Ejecutar y ver que pasa**

Run: `dotnet test gestor/CQEING.Gestor.Core.Tests`
Esperado: todas en PASS.

- [ ] **Paso 6: Commit**

```powershell
git add gestor/CQEING.Gestor.Core gestor/CQEING.Gestor.Core.Tests
git commit -m "feat(gestor): releases de GitHub con ETag y lista guardada"
```

---

### Tarea 11: Catálogo (últimas 5 versiones por add-in)

**Archivos:**
- Crear: `gestor/CQEING.Gestor.Core/ConstructorCatalogo.cs`
- Crear: `gestor/CQEING.Gestor.Core.Tests/ConstructorCatalogoTests.cs`, `Apoyo/Datos.Releases.cs`

**Interfaces:**
- Consume: `ReleaseGitHub`, `ArchivoRelease` (tarea 10); `EtiquetaRelease`, `SerializadorManifiesto`, `Manifiesto`, `Repositorio` (tarea 6).
- Produce:
  - `record VersionPublicada(Manifiesto Manifiesto, VersionAddin Version, IReadOnlyDictionary<string, Uri> Archivos)`; `Uri UrlDe(PaqueteManifiesto paquete)`
  - `record EntradaCatalogo(string Id, string Nombre, string Descripcion, IReadOnlyList<VersionPublicada> Versiones)`; `VersionPublicada Ultima` (versiones de la más nueva a la más vieja)
  - `record ResultadoCatalogo(IReadOnlyList<EntradaCatalogo> Addins, IReadOnlyList<string> Avisos)`
  - `class ConstructorCatalogo(HttpClient http, string carpetaCache)`: `const int VersionesPorAddin = 5`; `Task<ResultadoCatalogo> ConstruirAsync(IReadOnlyList<ReleaseGitHub> releases, CancellationToken ct = default)`; `static EtiquetaRelease? UltimoGestor(IReadOnlyList<ReleaseGitHub> releases)`

- [ ] **Paso 1: Datos de prueba de releases**

`gestor/CQEING.Gestor.Core.Tests/Apoyo/Datos.Releases.cs`:
```csharp
namespace CQEING.Gestor.Core.Tests.Apoyo;

internal static partial class Datos
{
    public static ReleaseGitHub Release(string etiqueta, bool prerelease = false, bool borrador = false, bool conManifiesto = true) =>
        new(etiqueta, borrador, prerelease,
            conManifiesto ? [new ArchivoRelease("manifest.json", new Uri($"https://github.test/{etiqueta}/manifest.json"))] : []);

    /// <summary>Sirve el manifest.json de cada etiqueta, con el id y la versión que dice la etiqueta.</summary>
    public static HttpResponseMessage ManifiestoDe(HttpRequestMessage pedido)
    {
        string etiqueta = pedido.RequestUri!.Segments[1].TrimEnd('/');
        EtiquetaRelease e = EtiquetaRelease.Interpretar(etiqueta)!;
        return Respuestas.Texto(SerializadorManifiesto.Escribir(Manifiesto(e.Id, e.Version.ToString())));
    }
}
```

- [ ] **Paso 2: Escribir las pruebas que fallan**

`gestor/CQEING.Gestor.Core.Tests/ConstructorCatalogoTests.cs`:
```csharp
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class ConstructorCatalogoTests : IDisposable
{
    private readonly CarpetaTemporal _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public async Task Solo_las_5_estables_mas_nuevas_por_addin_y_sin_el_gestor()
    {
        var releases = new List<ReleaseGitHub>
        {
            Datos.Release("armado-columnas-v1.7.0-rc.1", prerelease: true),
            Datos.Release("armado-columnas-v2.0.0", borrador: true),
            Datos.Release("imagenes-v1.0.0"),
            Datos.Release("gestor-v0.2.0"),
            Datos.Release("otra-cosa"),
        };
        releases.AddRange(Enumerable.Range(0, 7).Select(i => Datos.Release($"armado-columnas-v1.{i}.0")));
        var handler = new HandlerFalso(Datos.ManifiestoDe);

        ResultadoCatalogo r = await new ConstructorCatalogo(handler.Cliente(), _tmp.Ruta).ConstruirAsync(releases);

        Assert.Equal(new[] { "armado-columnas", "imagenes" }, r.Addins.Select(a => a.Id));
        EntradaCatalogo armado = r.Addins[0];
        Assert.Equal("Armado de columnas", armado.Nombre);
        Assert.Equal(new[] { "1.6.0", "1.5.0", "1.4.0", "1.3.0", "1.2.0" }, armado.Versiones.Select(v => v.Version.ToString()));
        Assert.Equal("1.6.0", armado.Ultima.Manifiesto.Version);
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public async Task Los_manifiestos_se_descargan_una_sola_vez()
    {
        var releases = new[] { Datos.Release("imagenes-v1.0.0") };
        var handler = new HandlerFalso(Datos.ManifiestoDe);
        var constructor = new ConstructorCatalogo(handler.Cliente(), _tmp.Ruta);

        await constructor.ConstruirAsync(releases);
        ResultadoCatalogo r = await constructor.ConstruirAsync(releases);

        Assert.Single(handler.Pedidos);
        Assert.Equal("1.0.0", r.Addins.Single().Ultima.Manifiesto.Version);
    }

    [Fact]
    public async Task Release_sin_manifiesto_o_que_no_coincide_se_omite_con_aviso()
    {
        var releases = new[]
        {
            Datos.Release("imagenes-v1.0.0", conManifiesto: false),
            Datos.Release("imagenes-v1.1.0"),
        };
        var handler = new HandlerFalso(_ => Respuestas.Texto(SerializadorManifiesto.Escribir(Datos.Manifiesto("imagenes", "9.9.9"))));

        ResultadoCatalogo r = await new ConstructorCatalogo(handler.Cliente(), _tmp.Ruta).ConstruirAsync(releases);

        Assert.Empty(r.Addins);
        Assert.Equal(2, r.Avisos.Count);
        Assert.Contains(r.Avisos, a => a.StartsWith("imagenes-v1.1.0") && a.Contains("9.9.9"));
    }

    [Fact]
    public async Task Manifiesto_de_esquema_nuevo_avisa_que_hay_que_actualizar_el_gestor()
    {
        var handler = new HandlerFalso(_ => Respuestas.Texto(SerializadorManifiesto.Escribir(Datos.Manifiesto("imagenes") with { Esquema = 2 })));

        ResultadoCatalogo r = await new ConstructorCatalogo(handler.Cliente(), _tmp.Ruta).ConstruirAsync([Datos.Release("imagenes-v1.0.0")]);

        Assert.Contains("Actualice el gestor", Assert.Single(r.Avisos));
    }

    [Fact]
    public void Ultimo_gestor_es_la_version_estable_mas_alta()
    {
        var releases = new[] { Datos.Release("gestor-v0.9.0"), Datos.Release("gestor-v0.10.0"), Datos.Release("gestor-v1.0.0-rc.1", prerelease: true), Datos.Release("imagenes-v5.0.0") };
        Assert.Equal("gestor-v0.10.0", ConstructorCatalogo.UltimoGestor(releases)!.ToString());
        Assert.Null(ConstructorCatalogo.UltimoGestor([Datos.Release("imagenes-v1.0.0")]));
    }

    [Fact]
    public void UrlDe_un_paquete_que_la_release_no_trae_es_PaqueteInvalido()
    {
        var version = new VersionPublicada(Datos.Manifiesto(), VersionAddin.Interpretar("1.0.0")!, new Dictionary<string, Uri>());
        var error = Assert.Throws<GestorException>(() => version.UrlDe(version.Manifiesto.Paquetes[0]));
        Assert.Equal(TipoError.PaqueteInvalido, error.Tipo);
    }
}
```

- [ ] **Paso 3: Ejecutar y ver que falla**

Run: `dotnet test gestor/CQEING.Gestor.Core.Tests --filter FullyQualifiedName~ConstructorCatalogoTests`
Esperado: no compila (`ConstructorCatalogo`, `VersionPublicada` no existen).

- [ ] **Paso 4: Implementar `gestor/CQEING.Gestor.Core/ConstructorCatalogo.cs`**

```csharp
namespace CQEING.Gestor.Core;

public sealed record VersionPublicada(Manifiesto Manifiesto, VersionAddin Version, IReadOnlyDictionary<string, Uri> Archivos)
{
    public Uri UrlDe(PaqueteManifiesto paquete) =>
        Archivos.TryGetValue(paquete.Archivo, out Uri? url)
            ? url
            : throw new GestorException(TipoError.PaqueteInvalido, $"La versión {Version} de {Manifiesto.Nombre} no trae el archivo {paquete.Archivo}.");
}

/// <param name="Versiones">De la más nueva a la más vieja; nunca está vacía.</param>
public sealed record EntradaCatalogo(string Id, string Nombre, string Descripcion, IReadOnlyList<VersionPublicada> Versiones)
{
    public VersionPublicada Ultima => Versiones[0];
}

/// <param name="Avisos">Releases que no se pudieron usar, explicadas para el usuario.</param>
public sealed record ResultadoCatalogo(IReadOnlyList<EntradaCatalogo> Addins, IReadOnlyList<string> Avisos);

/// <summary>
/// Agrupa las releases estables por add-in y lee el manifest.json de las más nuevas. Una release publicada no
/// cambia, así que cada manifiesto se descarga una vez y queda en la caché.
/// </summary>
public sealed class ConstructorCatalogo(HttpClient http, string carpetaCache)
{
    public const int VersionesPorAddin = 5;

    private string CarpetaManifiestos => Path.Combine(carpetaCache, "manifiestos");

    public async Task<ResultadoCatalogo> ConstruirAsync(IReadOnlyList<ReleaseGitHub> releases, CancellationToken ct = default)
    {
        var avisos = new List<string>();
        var addins = new List<EntradaCatalogo>();
        foreach (var grupo in Estables(releases).Where(x => x.Etiqueta.Id != EtiquetaRelease.IdGestor).GroupBy(x => x.Etiqueta.Id))
        {
            var versiones = new List<VersionPublicada>();
            foreach (var (release, etiqueta) in grupo.OrderByDescending(x => x.Etiqueta.Version).Take(VersionesPorAddin))
            {
                VersionPublicada? version = await LeerVersionAsync(release, etiqueta, avisos, ct);
                if (version is not null) versiones.Add(version);
            }
            if (versiones.Count > 0)
                addins.Add(new EntradaCatalogo(grupo.Key, versiones[0].Manifiesto.Nombre, versiones[0].Manifiesto.Descripcion, versiones));
        }
        return new ResultadoCatalogo(addins.OrderBy(a => a.Nombre, StringComparer.CurrentCulture).ToList(), avisos);
    }

    /// <summary>Última versión estable del gestor (etiquetas gestor-vX.Y.Z), para su autoactualización.</summary>
    public static EtiquetaRelease? UltimoGestor(IReadOnlyList<ReleaseGitHub> releases) =>
        Estables(releases).Where(x => x.Etiqueta.Id == EtiquetaRelease.IdGestor).Select(x => x.Etiqueta).MaxBy(e => e.Version);

    private static IEnumerable<(ReleaseGitHub Release, EtiquetaRelease Etiqueta)> Estables(IReadOnlyList<ReleaseGitHub> releases)
    {
        foreach (ReleaseGitHub release in releases)
        {
            if (release.Borrador || release.Prerelease) continue;
            if (EtiquetaRelease.Interpretar(release.Etiqueta) is { Version.EsEstable: true } etiqueta)
                yield return (release, etiqueta);
        }
    }

    private async Task<VersionPublicada?> LeerVersionAsync(ReleaseGitHub release, EtiquetaRelease etiqueta, List<string> avisos, CancellationToken ct)
    {
        Dictionary<string, Uri> archivos = (release.Archivos ?? []).ToDictionary(a => a.Nombre, a => a.Url, StringComparer.Ordinal);
        string guardado = Path.Combine(CarpetaManifiestos, $"{release.Etiqueta}.json");

        string? json = File.Exists(guardado) ? File.ReadAllText(guardado) : await DescargarManifiestoAsync(release.Etiqueta, archivos, avisos, ct);
        if (json is null) return null;

        Manifiesto manifiesto;
        try
        {
            manifiesto = SerializadorManifiesto.Leer(json);
        }
        catch (GestorException e)
        {
            avisos.Add($"{release.Etiqueta}: {e.Message}");
            return null;
        }
        if (manifiesto.Id != etiqueta.Id || manifiesto.Version != etiqueta.Version.ToString())
        {
            avisos.Add($"{release.Etiqueta}: su manifest.json dice {manifiesto.Id} {manifiesto.Version}; se omite.");
            return null;
        }

        Directory.CreateDirectory(CarpetaManifiestos);
        File.WriteAllText(guardado, json); // solo se guarda lo que pasó la validación
        return new VersionPublicada(manifiesto, etiqueta.Version, archivos);
    }

    private async Task<string?> DescargarManifiestoAsync(string etiqueta, Dictionary<string, Uri> archivos, List<string> avisos, CancellationToken ct)
    {
        if (!archivos.TryGetValue(Repositorio.ArchivoManifiesto, out Uri? url))
        {
            avisos.Add($"{etiqueta}: la release no trae {Repositorio.ArchivoManifiesto}; se omite.");
            return null;
        }
        try
        {
            return await http.GetStringAsync(url, ct);
        }
        catch (HttpRequestException)
        {
            avisos.Add($"{etiqueta}: no se pudo descargar su manifiesto; se omite por ahora.");
            return null;
        }
    }
}
```

- [ ] **Paso 5: Ejecutar y ver que pasa**

Run: `dotnet test gestor/CQEING.Gestor.Core.Tests`
Esperado: todas en PASS.

- [ ] **Paso 6: Commit**

```powershell
git add gestor/CQEING.Gestor.Core/ConstructorCatalogo.cs gestor/CQEING.Gestor.Core.Tests/ConstructorCatalogoTests.cs gestor/CQEING.Gestor.Core.Tests/Apoyo/Datos.Releases.cs
git commit -m "feat(gestor): catálogo con las últimas 5 versiones de cada add-in"
```

---

### Tarea 12: Instalar, actualizar y volver a una versión anterior

**Archivos:**
- Crear: `gestor/CQEING.Gestor.Core/Instalador.cs`, `gestor/CQEING.Gestor.Core/LectorEstado.cs`
- Crear: `gestor/CQEING.Gestor.Core.Tests/InstaladorTests.cs`, `Apoyo/Datos.Paquetes.cs`

**Interfaces:**
- Consume: `RutasGestor` (tarea 10); `VersionPublicada` (tarea 11); `Manifiesto`, `Sha256Archivo`, `VersionAddin` (tarea 6).
- Produce:
  - `class Instalador(HttpClient http, RutasGestor rutas)`: `const int VersionesConservadas = 3`; `Task InstalarAsync(VersionPublicada version, string anio, CancellationToken ct = default)` (instalar, actualizar y revertir son la misma operación)
  - `record AddinInstalado(string Id, string Version, string Anio)`; `static class LectorEstado { IReadOnlyList<AddinInstalado> Leer(RutasGestor rutas) }` (aquí solo lo que necesita la limpieza; sus pruebas propias van en la tarea 13)

- [ ] **Paso 1: Datos de prueba de paquetes**

`gestor/CQEING.Gestor.Core.Tests/Apoyo/Datos.Paquetes.cs`:
```csharp
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace CQEING.Gestor.Core.Tests.Apoyo;

internal static partial class Datos
{
    /// <summary>ZIP como el que arma el publicador: la DLL, Resources/ y .addin con la DLL sin carpeta.</summary>
    public static byte[] ZipAddin(string dll = "practicacq.dll", int addins = 1, string? assembly = null)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            Escribir(zip, dll, "contenido de la dll");
            Escribir(zip, "Resources/icono16.png", "png");
            for (int i = 0; i < addins; i++)
                Escribir(zip, i == 0 ? "practicacq.addin" : $"otro{i}.addin", XmlAddin(assembly ?? dll));
        }
        return memoria.ToArray();
    }

    public static string XmlAddin(string assembly) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <RevitAddIns>
          <AddIn Type="Application">
            <Name>practicacq</Name>
            <Assembly>{assembly}</Assembly>
            <FullClassName>practicacq.App</FullClassName>
            <ClientId>2044e120-cc19-42a1-8018-a97f13adf192</ClientId>
            <VendorId>ADSK</VendorId>
          </AddIn>
        </RevitAddIns>
        """;

    public static string Sha256(byte[] datos) => Convert.ToHexString(SHA256.HashData(datos)).ToLowerInvariant();

    /// <summary>Versión publicada de armado-columnas con un paquete para <paramref name="anio"/> y la URL de su ZIP.</summary>
    public static (VersionPublicada Version, string Url, byte[] Zip) VersionConZip(
        string version, string anio = "2026", byte[]? zip = null, IReadOnlyList<string>? heredados = null)
    {
        zip ??= ZipAddin();
        PaqueteManifiesto paquete = Paquete(anio, Sha256(zip), version: version);
        string url = $"https://github.test/descargas/{paquete.Archivo}";
        var publicada = new VersionPublicada(
            Manifiesto(version: version, paquetes: [paquete], heredados: heredados),
            VersionAddin.Interpretar(version)!,
            new Dictionary<string, Uri> { [paquete.Archivo] = new Uri(url) });
        return (publicada, url, zip);
    }

    private static void Escribir(ZipArchive zip, string nombre, string contenido)
    {
        using Stream destino = zip.CreateEntry(nombre).Open();
        destino.Write(Encoding.UTF8.GetBytes(contenido));
    }
}
```

- [ ] **Paso 2: Escribir las pruebas que fallan**

`gestor/CQEING.Gestor.Core.Tests/InstaladorTests.cs`:
```csharp
using System.Net;
using System.Xml.Linq;
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class InstaladorTests : IDisposable
{
    private const string Id = "armado-columnas";
    private readonly CarpetaTemporal _tmp = new();
    private readonly Dictionary<string, byte[]> _servidor = [];
    private readonly HandlerFalso _handler;
    private readonly RutasGestor _rutas;
    private readonly Instalador _instalador;

    public InstaladorTests()
    {
        _rutas = new RutasGestor(_tmp.Combinar("local"), _tmp.Combinar("roaming"));
        _handler = new HandlerFalso(p => _servidor.TryGetValue(p.RequestUri!.ToString(), out byte[]? zip)
            ? Respuestas.Bytes(zip)
            : Respuestas.Estado(HttpStatusCode.NotFound));
        _instalador = new Instalador(_handler.Cliente(), _rutas);
    }

    public void Dispose() => _tmp.Dispose();

    private VersionPublicada Publicar(string version, string anio = "2026", byte[]? zip = null, IReadOnlyList<string>? heredados = null)
    {
        var (publicada, url, bytes) = Datos.VersionConZip(version, anio, zip, heredados);
        _servidor[url] = bytes;
        return publicada;
    }

    private string AssemblyActivo(string anio = "2026") =>
        XDocument.Load(_rutas.ArchivoAddin(Id, anio)).Descendants("Assembly").Single().Value;

    private IEnumerable<string> VersionesEnDisco() =>
        Directory.GetDirectories(_rutas.CarpetaVersiones(Id)).Select(Path.GetFileName).Order(StringComparer.Ordinal)!;

    [Fact]
    public async Task Instala_en_su_carpeta_y_apunta_el_addin_con_ruta_absoluta()
    {
        await _instalador.InstalarAsync(Publicar("1.0.0"), "2026");

        string carpeta = _rutas.CarpetaVersion(Id, "1.0.0", "2026");
        Assert.True(File.Exists(Path.Combine(carpeta, "practicacq.dll")));
        Assert.True(File.Exists(Path.Combine(carpeta, "Resources", "icono16.png")));
        Assert.Equal(Path.Combine(carpeta, "practicacq.dll"), AssemblyActivo());
        Assert.Equal("2044e120-cc19-42a1-8018-a97f13adf192", XDocument.Load(_rutas.ArchivoAddin(Id, "2026")).Descendants("ClientId").Single().Value);
    }

    [Fact]
    public async Task Hash_distinto_no_instala_nada_ni_deja_restos()
    {
        var (publicada, url, _) = Datos.VersionConZip("1.0.0");
        _servidor[url] = Datos.ZipAddin(dll: "alterada.dll");

        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(publicada, "2026"));

        Assert.Equal(TipoError.HashNoCoincide, error.Tipo);
        Assert.False(File.Exists(_rutas.ArchivoAddin(Id, "2026")));
        Assert.Empty(Directory.EnumerateFiles(_rutas.CarpetaVersiones(Id), "*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(_rutas.CarpetaVersion(Id, "1.0.0", "2026")));
    }

    [Fact]
    public async Task Sin_paquete_para_ese_Revit_no_descarga_nada()
    {
        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(Publicar("1.0.0", anio: "2026"), "2027"));

        Assert.Equal(TipoError.SinPaqueteParaRevit, error.Tipo);
        Assert.Contains("Revit 2027", error.Message);
        Assert.Empty(_handler.Pedidos);
    }

    [Fact]
    public async Task Volver_a_una_version_en_disco_no_descarga_nada()
    {
        VersionPublicada v1 = Publicar("1.0.0");
        await _instalador.InstalarAsync(v1, "2026");
        await _instalador.InstalarAsync(Publicar("1.1.0"), "2026");
        _handler.Pedidos.Clear();
        _servidor.Clear(); // sin red

        await _instalador.InstalarAsync(v1, "2026");

        Assert.Empty(_handler.Pedidos);
        Assert.Contains(@"\1.0.0\2026\", AssemblyActivo());
    }

    [Fact]
    public async Task Retira_los_addin_heredados()
    {
        string carpetaRevit = _rutas.CarpetaAddinsRevit("2026");
        Directory.CreateDirectory(carpetaRevit);
        File.WriteAllText(Path.Combine(carpetaRevit, "practicacq.addin"), Datos.XmlAddin("practicacq.dll"));
        File.WriteAllText(Path.Combine(carpetaRevit, "practicacq.dll"), "dll vieja");
        File.WriteAllText(Path.Combine(carpetaRevit, "OtroFabricante.addin"), "<RevitAddIns/>");

        await _instalador.InstalarAsync(Publicar("1.0.0", heredados: ["practicacq.addin"]), "2026");

        Assert.False(File.Exists(Path.Combine(carpetaRevit, "practicacq.addin")));
        Assert.True(File.Exists(Path.Combine(carpetaRevit, "OtroFabricante.addin")));
        Assert.True(File.Exists(Path.Combine(carpetaRevit, "practicacq.dll"))); // sin su .addin, Revit no la carga
    }

    [Fact]
    public async Task Conserva_las_3_versiones_mas_nuevas_y_las_activas()
    {
        await _instalador.InstalarAsync(Publicar("1.0.0", anio: "2027"), "2027");
        foreach (string version in new[] { "1.1.0", "1.2.0", "1.3.0", "1.4.0" })
            await _instalador.InstalarAsync(Publicar(version), "2026");

        // 1.0.0 sigue activa en Revit 2027; 1.1.0 es la cuarta más nueva y no está activa.
        Assert.Equal(new[] { "1.0.0", "1.2.0", "1.3.0", "1.4.0" }, VersionesEnDisco());
    }

    [Fact]
    public async Task Version_vieja_bloqueada_no_rompe_y_se_borra_despues()
    {
        foreach (string version in new[] { "1.0.0", "1.1.0", "1.2.0" })
            await _instalador.InstalarAsync(Publicar(version), "2026");
        string dllVieja = Path.Combine(_rutas.CarpetaVersion(Id, "1.0.0", "2026"), "practicacq.dll");

        using (File.Open(dllVieja, FileMode.Open, FileAccess.Read, FileShare.None)) // Revit la tiene cargada
        {
            await _instalador.InstalarAsync(Publicar("1.3.0"), "2026");
            Assert.True(File.Exists(Path.Combine(_rutas.CarpetaVersion(Id, "1.0.0", "2026"), "practicacq.addin"))); // intacta, no a medias
        }

        await _instalador.InstalarAsync(Publicar("1.3.0"), "2026");
        Assert.Equal(new[] { "1.1.0", "1.2.0", "1.3.0" }, VersionesEnDisco());
    }

    [Theory]
    [InlineData(2, null)]
    [InlineData(1, @"C:\otra\carpeta\practicacq.dll")]
    public async Task Paquete_mal_armado_es_PaqueteInvalido_y_no_queda_instalado(int addins, string? assembly)
    {
        VersionPublicada publicada = Publicar("1.0.0", zip: Datos.ZipAddin(addins: addins, assembly: assembly));

        var error = await Assert.ThrowsAsync<GestorException>(() => _instalador.InstalarAsync(publicada, "2026"));

        Assert.Equal(TipoError.PaqueteInvalido, error.Tipo);
        Assert.False(Directory.Exists(_rutas.CarpetaVersion(Id, "1.0.0", "2026")));
        Assert.False(File.Exists(_rutas.ArchivoAddin(Id, "2026")));
    }
}
```

- [ ] **Paso 3: Ejecutar y ver que falla**

Run: `dotnet test gestor/CQEING.Gestor.Core.Tests --filter FullyQualifiedName~InstaladorTests`
Esperado: no compila (`Instalador` no existe).

- [ ] **Paso 4: Implementar**

`gestor/CQEING.Gestor.Core/LectorEstado.cs`:
```csharp
using System.Xml;
using System.Xml.Linq;

namespace CQEING.Gestor.Core;

public sealed record AddinInstalado(string Id, string Version, string Anio);

/// <summary>
/// Qué hay instalado, leído del disco: cada %APPDATA%\Autodesk\Revit\Addins\&lt;año&gt;\CQEING.&lt;id&gt;.addin cuyo
/// Assembly apunta a ...\CQEING\Addins\&lt;id&gt;\&lt;versión&gt;\&lt;año&gt;\. Lo demás (por ejemplo, un .addin de
/// desarrollo que apunta al repo) no es una instalación del gestor y se ignora.
/// </summary>
public static class LectorEstado
{
    public static IReadOnlyList<AddinInstalado> Leer(RutasGestor rutas)
    {
        if (!Directory.Exists(rutas.CarpetaAddinsRevitRaiz)) return [];

        var instalados = new List<AddinInstalado>();
        foreach (string carpetaAnio in Directory.GetDirectories(rutas.CarpetaAddinsRevitRaiz))
        {
            foreach (string archivo in Directory.GetFiles(carpetaAnio, "CQEING.*.addin"))
            {
                string id = Path.GetFileNameWithoutExtension(archivo)["CQEING.".Length..];
                string? version = VersionApuntada(archivo, rutas.CarpetaVersiones(id));
                if (version is not null)
                    instalados.Add(new AddinInstalado(id, version, Path.GetFileName(carpetaAnio)));
            }
        }
        return instalados;
    }

    private static string? VersionApuntada(string archivoAddin, string carpetaVersiones)
    {
        string? assembly;
        try
        {
            assembly = XDocument.Load(archivoAddin).Descendants("Assembly").FirstOrDefault()?.Value.Trim();
        }
        catch (Exception e) when (e is XmlException or IOException)
        {
            return null;
        }
        if (string.IsNullOrEmpty(assembly) || !Path.IsPathFullyQualified(assembly)) return null;

        // ...\Addins\<id>\<versión>\<año>\<dll>
        string? carpetaVersion = Path.GetDirectoryName(Path.GetDirectoryName(assembly));
        string? carpetaId = Path.GetDirectoryName(carpetaVersion);
        if (carpetaVersion is null || carpetaId is null
            || !string.Equals(Path.GetFullPath(carpetaId), Path.GetFullPath(carpetaVersiones), StringComparison.OrdinalIgnoreCase))
            return null;

        string version = Path.GetFileName(carpetaVersion);
        return VersionAddin.Interpretar(version) is null ? null : version;
    }
}
```

`gestor/CQEING.Gestor.Core/Instalador.cs`:
```csharp
using System.IO.Compression;
using System.Xml.Linq;

namespace CQEING.Gestor.Core;

public sealed class Instalador(HttpClient http, RutasGestor rutas)
{
    public const int VersionesConservadas = 3;

    /// <summary>
    /// Deja <paramref name="version"/> activa en Revit <paramref name="anio"/>. Sirve igual para instalar, actualizar
    /// y volver a una versión anterior: si la versión ya está en disco, no se descarga nada. Con Revit abierto es
    /// seguro (cada versión tiene su carpeta); el cambio se ve al reiniciar Revit.
    /// </summary>
    public async Task InstalarAsync(VersionPublicada version, string anio, CancellationToken ct = default)
    {
        Manifiesto m = version.Manifiesto;
        PaqueteManifiesto paquete = m.PaqueteParaRevit(anio)
            ?? throw new GestorException(TipoError.SinPaqueteParaRevit, $"{m.Nombre} {m.Version} no tiene paquete para Revit {anio}.");

        string carpeta = rutas.CarpetaVersion(m.Id, m.Version, anio);
        if (!Directory.Exists(carpeta))
            await DescargarAsync(version.UrlDe(paquete), paquete.Sha256, carpeta, ct);

        ActivarAddin(carpeta, rutas.ArchivoAddin(m.Id, anio));
        RetirarHeredados(m.Heredados, anio);
        LimpiarVersionesViejas(m.Id);
    }

    /// <summary>Descarga, verifica y extrae en una carpeta temporal; solo si todo salió bien la mueve a su lugar.</summary>
    private async Task DescargarAsync(Uri url, string sha256Esperado, string carpetaFinal, CancellationToken ct)
    {
        string temporal = $"{carpetaFinal}.descarga-{Guid.NewGuid():N}";
        string zip = temporal + ".zip";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(carpetaFinal)!);
            try
            {
                using HttpResponseMessage respuesta = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                respuesta.EnsureSuccessStatusCode();
                await using FileStream destino = File.Create(zip);
                await respuesta.Content.CopyToAsync(destino, ct);
            }
            catch (HttpRequestException e)
            {
                throw new GestorException(TipoError.SinConexion, "No se pudo descargar el paquete. Revise la conexión e intente de nuevo.", e);
            }

            if (Sha256Archivo.Calcular(zip) != sha256Esperado)
                throw new GestorException(TipoError.HashNoCoincide, "El archivo descargado no coincide con el publicado (SHA-256). No se instaló nada; intente de nuevo.");

            ZipFile.ExtractToDirectory(zip, temporal);
            LeerAddinDelPaquete(temporal);
            Directory.Move(temporal, carpetaFinal);
        }
        finally
        {
            if (File.Exists(zip)) File.Delete(zip);
            if (Directory.Exists(temporal)) Directory.Delete(temporal, recursive: true);
        }
    }

    private static XDocument LeerAddinDelPaquete(string carpeta)
    {
        string[] addins = Directory.GetFiles(carpeta, "*.addin");
        if (addins.Length != 1)
            throw new GestorException(TipoError.PaqueteInvalido, $"El paquete debe traer exactamente un .addin y trae {addins.Length}.");

        XDocument doc = XDocument.Load(addins[0]);
        List<string> assemblies = doc.Descendants("Assembly").Select(a => a.Value.Trim()).ToList();
        if (assemblies.Count == 0 || assemblies.Any(a => a.Length == 0 || Path.GetFileName(a) != a))
            throw new GestorException(TipoError.PaqueteInvalido, "El .addin del paquete debe nombrar su DLL sin carpeta.");
        return doc;
    }

    private static void ActivarAddin(string carpetaVersion, string destino)
    {
        XDocument doc = LeerAddinDelPaquete(carpetaVersion);
        foreach (XElement assembly in doc.Descendants("Assembly"))
            assembly.Value = Path.Combine(carpetaVersion, assembly.Value.Trim());

        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        string temporal = destino + ".tmp";
        doc.Save(temporal);
        File.Move(temporal, destino, overwrite: true); // Revit nunca ve un .addin a medio escribir
    }

    private void RetirarHeredados(IReadOnlyList<string> heredados, string anio)
    {
        foreach (string nombre in heredados)
        {
            string ruta = Path.Combine(rutas.CarpetaAddinsRevit(anio), nombre);
            if (File.Exists(ruta)) File.Delete(ruta);
        }
    }

    /// <summary>
    /// Deja las 3 versiones más nuevas y las activas en algún año. Antes de borrar, renombra la carpeta: si Revit
    /// tiene una DLL abierta, Windows no deja renombrarla y la versión queda entera para la próxima vez, en vez de
    /// quedar a medio borrar.
    /// </summary>
    private void LimpiarVersionesViejas(string id)
    {
        string raiz = rutas.CarpetaVersiones(id);
        HashSet<string> activas = LectorEstado.Leer(rutas).Where(a => a.Id == id).Select(a => a.Version).ToHashSet();

        IEnumerable<string> sobrantes = Directory.GetDirectories(raiz)
            .Select(carpeta => (Carpeta: carpeta, Version: VersionAddin.Interpretar(Path.GetFileName(carpeta))))
            .Where(x => x.Version is not null)
            .OrderByDescending(x => x.Version)
            .Skip(VersionesConservadas)
            .Where(x => !activas.Contains(x.Version!.ToString()))
            .Select(x => x.Carpeta);

        foreach (string carpeta in sobrantes)
        {
            string aBorrar = $"{carpeta}.borrar-{Guid.NewGuid():N}";
            try
            {
                Directory.Move(carpeta, aBorrar);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue; // en uso: se reintenta en la próxima instalación
            }
            BorrarSinFallar(aBorrar);
        }

        foreach (string resto in Directory.GetDirectories(raiz, "*.borrar-*"))
            BorrarSinFallar(resto);
    }

    private static void BorrarSinFallar(string carpeta)
    {
        try
        {
            Directory.Delete(carpeta, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Se reintenta en la próxima instalación.
        }
    }
}
```

- [ ] **Paso 5: Ejecutar y ver que pasa**

Run: `dotnet test gestor/CQEING.Gestor.Core.Tests`
Esperado: todas en PASS.

- [ ] **Paso 6: Commit**

```powershell
git add gestor/CQEING.Gestor.Core/Instalador.cs gestor/CQEING.Gestor.Core/LectorEstado.cs gestor/CQEING.Gestor.Core.Tests/InstaladorTests.cs gestor/CQEING.Gestor.Core.Tests/Apoyo/Datos.Paquetes.cs
git commit -m "feat(gestor): instalar, actualizar y revertir en una carpeta por versión"
```

---

### Tarea 13: Estado instalado y fachada `GestorAddins`

**Archivos:**
- Crear: `gestor/CQEING.Gestor.Core/GestorAddins.cs`
- Crear: `gestor/CQEING.Gestor.Core.Tests/LectorEstadoTests.cs`, `GestorAddinsTests.cs`

**Interfaces:**
- Consume: `FuenteReleases` (tarea 10), `ConstructorCatalogo`, `EntradaCatalogo` (tarea 11), `Instalador`, `LectorEstado` (tarea 12), `DetectorRevit`, `RutasGestor`, `ClienteHttp` (tarea 10).
- Produce:
  - `record EstadoAddin(EntradaCatalogo Addin, IReadOnlyDictionary<string, string> InstaladaPorAnio)`: `string? Instalada(string anio)`, `bool HayActualizacion(string anio)`
  - `record ResumenGestor(IReadOnlyList<string> AniosRevit, IReadOnlyList<EstadoAddin> Addins, IReadOnlyList<string> Avisos, EtiquetaRelease? UltimoGestor)`
  - `class GestorAddins(FuenteReleases fuente, ConstructorCatalogo catalogo, Instalador instalador, RutasGestor rutas, string programFiles)`: `static GestorAddins Crear()`, `Task<ResumenGestor> ConsultarAsync(CancellationToken ct = default)`, `Task InstalarAsync(VersionPublicada version, string anio, CancellationToken ct = default)`

- [ ] **Paso 1: Escribir las pruebas que fallan**

`gestor/CQEING.Gestor.Core.Tests/LectorEstadoTests.cs`:
```csharp
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class LectorEstadoTests : IDisposable
{
    private readonly CarpetaTemporal _tmp = new();
    private readonly RutasGestor _rutas;

    public LectorEstadoTests() => _rutas = new RutasGestor(_tmp.Combinar("local"), _tmp.Combinar("roaming"));

    public void Dispose() => _tmp.Dispose();

    private void EscribirAddin(string id, string anio, string contenido)
    {
        Directory.CreateDirectory(_rutas.CarpetaAddinsRevit(anio));
        File.WriteAllText(_rutas.ArchivoAddin(id, anio), contenido);
    }

    [Fact]
    public void Lee_id_version_y_anio_de_los_addin_del_gestor()
    {
        EscribirAddin("imagenes", "2027", Datos.XmlAddin(Path.Combine(_rutas.CarpetaVersion("imagenes", "1.2.0", "2027"), "CQEING.Imagenes.dll")));

        Assert.Equal(new[] { new AddinInstalado("imagenes", "1.2.0", "2027") }, LectorEstado.Leer(_rutas));
    }

    [Fact]
    public void Addin_ajeno_o_danado_se_ignora()
    {
        EscribirAddin("armado-columnas", "2026", Datos.XmlAddin(@"C:\repo\addins\armado-columnas\bin\Debug\2026\practicacq.dll"));
        EscribirAddin("imagenes", "2026", "<<< no es xml");
        EscribirAddin("vacio", "2026", Datos.XmlAddin("practicacq.dll"));

        Assert.Empty(LectorEstado.Leer(_rutas));
    }

    [Fact]
    public void Sin_carpeta_de_Revit_no_hay_nada_instalado() => Assert.Empty(LectorEstado.Leer(_rutas));
}
```

`gestor/CQEING.Gestor.Core.Tests/GestorAddinsTests.cs`:
```csharp
using System.Net;
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class GestorAddinsTests : IDisposable
{
    private readonly CarpetaTemporal _tmp = new();
    private readonly RutasGestor _rutas;
    // Una respuesta HTTP se consume al leerla: el servidor falso crea una nueva en cada pedido.
    private readonly Dictionary<string, Func<HttpResponseMessage>> _servidor = [];
    private readonly GestorAddins _gestor;

    public GestorAddinsTests()
    {
        _rutas = new RutasGestor(_tmp.Combinar("local"), _tmp.Combinar("roaming"));
        Directory.CreateDirectory(_tmp.Combinar("pf", "Autodesk", "Revit 2026"));
        File.WriteAllText(_tmp.Combinar("pf", "Autodesk", "Revit 2026", "RevitAPI.dll"), "");

        var handler = new HandlerFalso(p => _servidor.TryGetValue(p.RequestUri!.ToString(), out Func<HttpResponseMessage>? r) ? r() : Respuestas.Estado(HttpStatusCode.NotFound));
        HttpClient http = handler.Cliente();
        _gestor = new GestorAddins(
            new FuenteReleases(http, _rutas.CarpetaCache, "dueno/repo"),
            new ConstructorCatalogo(http, _rutas.CarpetaCache),
            new Instalador(http, _rutas),
            _rutas,
            _tmp.Combinar("pf"));
    }

    public void Dispose() => _tmp.Dispose();

    /// <summary>Publica en el servidor falso las versiones dadas de armado-columnas, más un gestor-v0.2.0.</summary>
    private void Publicar(params string[] versiones)
    {
        var releases = new List<object> { new { tag_name = "gestor-v0.2.0", draft = false, prerelease = false, assets = Array.Empty<object>() } };
        foreach (string version in versiones)
        {
            var (publicada, urlZip, zip) = Datos.VersionConZip(version);
            string urlManifiesto = $"https://github.test/armado-columnas-v{version}/manifest.json";
            _servidor[urlZip] = () => Respuestas.Bytes(zip);
            _servidor[urlManifiesto] = () => Respuestas.Texto(SerializadorManifiesto.Escribir(publicada.Manifiesto));
            releases.Add(new
            {
                tag_name = $"armado-columnas-v{version}",
                draft = false,
                prerelease = false,
                assets = new[]
                {
                    new { name = "manifest.json", browser_download_url = urlManifiesto },
                    new { name = publicada.Manifiesto.Paquetes[0].Archivo, browser_download_url = urlZip },
                },
            });
        }
        string json = System.Text.Json.JsonSerializer.Serialize(releases);
        _servidor["https://api.github.com/repos/dueno/repo/releases?per_page=100"] = () => Respuestas.Texto(json);
    }

    [Fact]
    public async Task Instalar_y_ver_que_hay_una_version_nueva()
    {
        Publicar("1.0.0");
        ResumenGestor antes = await _gestor.ConsultarAsync();
        Assert.Equal(new[] { "2026" }, antes.AniosRevit);
        Assert.Equal("gestor-v0.2.0", antes.UltimoGestor!.ToString());
        EstadoAddin armado = Assert.Single(antes.Addins);
        Assert.Null(armado.Instalada("2026"));
        Assert.False(armado.HayActualizacion("2026"));

        await _gestor.InstalarAsync(armado.Addin.Ultima, "2026");
        Publicar("1.0.0", "1.1.0");
        ResumenGestor despues = await _gestor.ConsultarAsync();

        EstadoAddin actualizado = despues.Addins.Single();
        Assert.Equal("1.0.0", actualizado.Instalada("2026"));
        Assert.True(actualizado.HayActualizacion("2026"));
        Assert.False(actualizado.HayActualizacion("2027"));
        Assert.Empty(despues.Avisos);
    }

    [Fact]
    public async Task Sin_paquete_para_su_Revit_la_version_nueva_no_cuenta_como_actualizacion()
    {
        Publicar("1.0.0");
        EstadoAddin armado = (await _gestor.ConsultarAsync()).Addins.Single();
        await _gestor.InstalarAsync(armado.Addin.Ultima, "2026");

        var sinPaquete = armado.Addin.Ultima with
        {
            Version = VersionAddin.Interpretar("2.0.0")!,
            Manifiesto = Datos.Manifiesto(version: "2.0.0", paquetes: [Datos.Paquete("2027", new string('a', 64), version: "2.0.0")]),
        };
        var estado = new EstadoAddin(armado.Addin with { Versiones = [sinPaquete, armado.Addin.Ultima] }, new Dictionary<string, string> { ["2026"] = "1.0.0" });

        Assert.False(estado.HayActualizacion("2026"));
    }
}
```

- [ ] **Paso 2: Ejecutar y ver que falla**

Run: `dotnet test gestor/CQEING.Gestor.Core.Tests`
Esperado: no compila (`GestorAddins`, `EstadoAddin`, `ResumenGestor` no existen).

- [ ] **Paso 3: Implementar `gestor/CQEING.Gestor.Core/GestorAddins.cs`**

```csharp
namespace CQEING.Gestor.Core;

/// <param name="InstaladaPorAnio">Año de Revit → versión instalada por el gestor.</param>
public sealed record EstadoAddin(EntradaCatalogo Addin, IReadOnlyDictionary<string, string> InstaladaPorAnio)
{
    public string? Instalada(string anio) => InstaladaPorAnio.GetValueOrDefault(anio);

    /// <summary>Hay una versión más nueva que la instalada en ese año y trae paquete para ese Revit.</summary>
    public bool HayActualizacion(string anio) =>
        VersionAddin.Interpretar(Instalada(anio)) is { } actual
        && Addin.Ultima.Version.CompareTo(actual) > 0
        && Addin.Ultima.Manifiesto.PaqueteParaRevit(anio) is not null;
}

/// <param name="AniosRevit">Años de Revit instalados en este equipo.</param>
/// <param name="UltimoGestor">Última versión publicada del gestor, para su autoactualización.</param>
public sealed record ResumenGestor(
    IReadOnlyList<string> AniosRevit,
    IReadOnlyList<EstadoAddin> Addins,
    IReadOnlyList<string> Avisos,
    EtiquetaRelease? UltimoGestor);

/// <summary>Punto de entrada de la ventana: qué hay publicado, qué hay instalado y cómo instalar.</summary>
public sealed class GestorAddins(FuenteReleases fuente, ConstructorCatalogo catalogo, Instalador instalador, RutasGestor rutas, string programFiles)
{
    public static GestorAddins Crear()
    {
        HttpClient http = ClienteHttp.Crear();
        RutasGestor rutas = RutasGestor.DelUsuario();
        return new GestorAddins(
            new FuenteReleases(http, rutas.CarpetaCache),
            new ConstructorCatalogo(http, rutas.CarpetaCache),
            new Instalador(http, rutas),
            rutas,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
    }

    public async Task<ResumenGestor> ConsultarAsync(CancellationToken ct = default)
    {
        ResultadoReleases releases = await fuente.ObtenerAsync(ct);
        ResultadoCatalogo resultado = await catalogo.ConstruirAsync(releases.Releases, ct);
        IReadOnlyList<AddinInstalado> instalados = LectorEstado.Leer(rutas);

        List<EstadoAddin> addins = resultado.Addins
            .Select(addin => new EstadoAddin(addin, instalados.Where(i => i.Id == addin.Id).ToDictionary(i => i.Anio, i => i.Version)))
            .ToList();
        var avisos = new List<string>();
        if (releases.Aviso is not null) avisos.Add(releases.Aviso);
        avisos.AddRange(resultado.Avisos);

        return new ResumenGestor(DetectorRevit.AniosInstalados(programFiles), addins, avisos, ConstructorCatalogo.UltimoGestor(releases.Releases));
    }

    /// <summary>Instala, actualiza o vuelve a <paramref name="version"/> en ese año de Revit.</summary>
    public Task InstalarAsync(VersionPublicada version, string anio, CancellationToken ct = default) =>
        instalador.InstalarAsync(version, anio, ct);
}
```

- [ ] **Paso 4: Ejecutar y ver que pasa**

Run: `dotnet test gestor/CQEING.Gestor.Core.Tests`
Esperado: todas en PASS.

- [ ] **Paso 5: Commit**

```powershell
git add gestor/CQEING.Gestor.Core/GestorAddins.cs gestor/CQEING.Gestor.Core.Tests/LectorEstadoTests.cs gestor/CQEING.Gestor.Core.Tests/GestorAddinsTests.cs
git commit -m "feat(gestor): estado instalado y aviso de versión nueva"
```

---

### Tarea 14: App del gestor (ventana provisional) con autoactualización

**Archivos:**
- Crear: `gestor/CQEING.Gestor/CQEING.Gestor.csproj`, `Program.cs`, `VentanaPrincipal.xaml`, `VentanaPrincipal.xaml.cs`, `FilaAddin.cs`
- Crear: `.github/workflows/publicar-gestor.yml`

**Interfaces:**
- Consume: `GestorAddins`, `ResumenGestor`, `EstadoAddin`, `VersionPublicada`, `GestorException`, `EtiquetaRelease`, `Repositorio` (tareas 6–13).
- Produce: `CQEING.Gestor.exe`; releases `gestor-vX.Y.Z` con el paquete de Velopack (`releases.win.json`, `*-full.nupkg`, `CQEING.Gestor-win-Setup.exe`, `CQEING.Gestor-win-Portable.zip`).

- [ ] **Paso 1: Crear el proyecto**

`gestor/CQEING.Gestor/CQEING.Gestor.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>CQEING.Gestor</AssemblyName>
    <RootNamespace>CQEING.Gestor</RootNamespace>
    <Version>0.1.0</Version>
    <Company>CQEING</Company>
    <Product>Gestor de add-ins CQEING</Product>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Velopack" Version="1.2.161" />
    <ProjectReference Include="..\CQEING.Gestor.Core\CQEING.Gestor.Core.csproj" />
  </ItemGroup>

</Project>
```
```powershell
dotnet sln CQEING-Addins.sln add gestor/CQEING.Gestor/CQEING.Gestor.csproj
```

- [ ] **Paso 2: `gestor/CQEING.Gestor/Program.cs`**

```csharp
using System.Windows;
using Velopack;

namespace CQEING.Gestor;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Debe ir primero: atiende los pasos de instalación y actualización que lanza Velopack.
        VelopackApp.Build().Run();
        new Application().Run(new VentanaPrincipal());
    }
}
```

- [ ] **Paso 3: `gestor/CQEING.Gestor/FilaAddin.cs`**

```csharp
using System.ComponentModel;
using CQEING.Gestor.Core;

namespace CQEING.Gestor;

/// <summary>Una fila de la tabla: un add-in en un año de Revit.</summary>
public sealed class FilaAddin(EstadoAddin estado, string anio) : INotifyPropertyChanged
{
    private VersionPublicada _elegida = estado.Addin.Ultima;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Nombre => estado.Addin.Nombre;
    public string Anio => anio;
    public string Instalada => estado.Instalada(anio) ?? "—";
    public bool HayActualizacion => estado.HayActualizacion(anio);
    public IReadOnlyList<VersionPublicada> Versiones => estado.Addin.Versiones;

    public VersionPublicada Elegida
    {
        get => _elegida;
        set
        {
            _elegida = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Elegida)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextoAccion)));
        }
    }

    public string TextoAccion => VersionAddin.Interpretar(estado.Instalada(anio)) switch
    {
        null => "Instalar",
        var actual when Elegida.Version.CompareTo(actual) > 0 => "Actualizar",
        var actual when Elegida.Version.CompareTo(actual) < 0 => "Volver a esta",
        _ => "Reinstalar",
    };
}
```

- [ ] **Paso 4: `gestor/CQEING.Gestor/VentanaPrincipal.xaml`**

```xml
<Window x:Class="CQEING.Gestor.VentanaPrincipal"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Gestor de add-ins CQEING" Width="820" Height="440" WindowStartupLocation="CenterScreen">
    <!-- Ventana PROVISIONAL: solo prueba la lógica. La interfaz definitiva se diseña en su propio plan. -->
    <DockPanel Margin="12">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <Button Content="Buscar novedades" Padding="10,4" Click="Refrescar_Click"/>
            <Button x:Name="BotonActualizarGestor" Content="Actualizar el gestor" Padding="10,4" Margin="8,0,0,0"
                    Visibility="Collapsed" Click="ActualizarGestor_Click"/>
        </StackPanel>
        <TextBlock x:Name="TextoEstado" DockPanel.Dock="Bottom" TextWrapping="Wrap" Margin="0,8,0,0"/>
        <DataGrid x:Name="Tabla" AutoGenerateColumns="False" CanUserAddRows="False" HeadersVisibility="Column">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Add-in" Binding="{Binding Nombre}" IsReadOnly="True" Width="*"/>
                <DataGridTextColumn Header="Revit" Binding="{Binding Anio}" IsReadOnly="True"/>
                <DataGridTextColumn Header="Instalada" Binding="{Binding Instalada}" IsReadOnly="True"/>
                <DataGridCheckBoxColumn Header="Hay nueva" Binding="{Binding HayActualizacion, Mode=OneWay}" IsReadOnly="True"/>
                <DataGridTemplateColumn Header="Versión">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <ComboBox ItemsSource="{Binding Versiones}" DisplayMemberPath="Version"
                                      SelectedItem="{Binding Elegida, UpdateSourceTrigger=PropertyChanged}"/>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
                <DataGridTemplateColumn>
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <Button Content="{Binding TextoAccion}" Padding="8,2" Click="Instalar_Click"/>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
        </DataGrid>
    </DockPanel>
</Window>
```

- [ ] **Paso 5: `gestor/CQEING.Gestor/VentanaPrincipal.xaml.cs`**

```csharp
using System.Net.Http;
using System.Windows;
using CQEING.Gestor.Core;
using Velopack;
using Velopack.Sources;

namespace CQEING.Gestor;

public partial class VentanaPrincipal : Window
{
    private readonly GestorAddins _gestor = GestorAddins.Crear();
    private UpdateManager? _velopack;
    private UpdateInfo? _gestorNuevo;

    public VentanaPrincipal()
    {
        InitializeComponent();
        Loaded += async (_, _) => await EjecutarAsync("Buscando novedades…", CargarAsync);
    }

    private async Task CargarAsync()
    {
        ResumenGestor resumen = await _gestor.ConsultarAsync();
        Tabla.ItemsSource = resumen.Addins.SelectMany(a => resumen.AniosRevit.Select(anio => new FilaAddin(a, anio))).ToList();

        var mensajes = new List<string>(resumen.Avisos);
        if (resumen.AniosRevit.Count == 0) mensajes.Insert(0, "No se encontró Revit instalado en este equipo.");
        TextoEstado.Text = mensajes.Count == 0 ? "Al día." : string.Join(Environment.NewLine, mensajes);

        await RevisarGestorAsync(resumen.UltimoGestor);
    }

    /// <summary>
    /// Velopack lee el releases.win.json de la última release gestor-vX.Y.Z. Se le da esa carpeta directamente
    /// porque en el repo también hay releases de los add-ins, que no son del gestor.
    /// </summary>
    private async Task RevisarGestorAsync(EtiquetaRelease? ultimo)
    {
        if (ultimo is null) return;
        var velopack = new UpdateManager(new SimpleWebSource($"https://github.com/{Repositorio.Nombre}/releases/download/{ultimo}/"));
        if (!velopack.IsInstalled) return; // ejecutado desde el código fuente, sin instalar

        try
        {
            _gestorNuevo = await velopack.CheckForUpdatesAsync();
            _velopack = velopack;
        }
        catch (HttpRequestException)
        {
            _gestorNuevo = null; // sin red: se revisa la próxima vez
        }
        BotonActualizarGestor.Visibility = _gestorNuevo is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task EjecutarAsync(string mensaje, Func<Task> accion)
    {
        IsEnabled = false;
        TextoEstado.Text = mensaje;
        try
        {
            await accion();
        }
        catch (GestorException e)
        {
            TextoEstado.Text = e.Message;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TextoEstado.Text = $"No se pudo escribir en el disco: {e.Message}";
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void Refrescar_Click(object sender, RoutedEventArgs e) => await EjecutarAsync("Buscando novedades…", CargarAsync);

    private async void Instalar_Click(object sender, RoutedEventArgs e)
    {
        var fila = (FilaAddin)((FrameworkElement)sender).DataContext;
        await EjecutarAsync($"Instalando {fila.Nombre} {fila.Elegida.Version} para Revit {fila.Anio}…", async () =>
        {
            await _gestor.InstalarAsync(fila.Elegida, fila.Anio);
            await CargarAsync();
            TextoEstado.Text = $"Listo: {fila.Nombre} {fila.Elegida.Version}. Reinicie Revit {fila.Anio} para usarla.";
        });
    }

    private async void ActualizarGestor_Click(object sender, RoutedEventArgs e)
    {
        if (_velopack is null || _gestorNuevo is null) return;
        await EjecutarAsync("Descargando el gestor nuevo…", async () =>
        {
            await _velopack.DownloadUpdatesAsync(_gestorNuevo);
            _velopack.ApplyUpdatesAndRestart(_gestorNuevo.TargetFullRelease);
        });
    }
}
```

- [ ] **Paso 6: Compilar y probar contra el repo público**

```powershell
dotnet build CQEING-Addins.sln
dotnet run --project gestor/CQEING.Gestor
```
Esperado: se abre la ventana; como hasta ahora solo hay una prerelease (tarea 8), la tabla queda vacía y el estado dice «Al día.». Con la red desconectada, el estado dice «No hay conexión con GitHub. Se muestra la última lista guardada.».

- [ ] **Paso 7: Crear `.github/workflows/publicar-gestor.yml`**

```yaml
name: Publicar gestor

on:
  push:
    tags: ['gestor-v*']

permissions:
  contents: write

jobs:
  publicar:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            10.0.x
      - name: Compilar y empaquetar con Velopack
        shell: pwsh
        run: |
          $version = '${{ github.ref_name }}'.Substring('gestor-v'.Length)
          dotnet publish gestor/CQEING.Gestor/CQEING.Gestor.csproj -c Release -r win-x64 --self-contained -o publish -p:Version=$version
          if ($LASTEXITCODE) { exit 1 }
          dotnet tool install -g vpk --version 1.2.161
          vpk pack --packId CQEING.Gestor --packVersion $version --packDir publish --mainExe CQEING.Gestor.exe --packTitle "Gestor de add-ins CQEING" --outputDir Releases
          if ($LASTEXITCODE) { exit 1 }
      - name: Crear la release
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
        run: gh release create '${{ github.ref_name }}' (Get-ChildItem Releases -File).FullName --title '${{ github.ref_name }}' --notes "Gestor de add-ins ${{ github.ref_name }}"
```

- [ ] **Paso 8: Commit**

```powershell
git add CQEING-Addins.sln gestor/CQEING.Gestor .github/workflows/publicar-gestor.yml
git commit -m "feat(gestor): ventana provisional y autoactualización con Velopack"
```

---

### Tarea 15: Prueba completa en un equipo limpio y cierre

Todo lo que sube algo a GitHub se hace con permiso del usuario en ese momento.

- [ ] **Paso 1: Subir lo de la fase 3 a `main` por PR**

PR de la rama de trabajo a `main` con el plugin de GitHub; esperar `compilar.yml` en verde; el usuario hace el merge (merge commit).

- [ ] **Paso 2: Publicar las primeras versiones estables y el gestor**

```powershell
git switch main; git pull
git tag -a armado-columnas-v1.0.0 -m "Primera versión publicada desde el monorepo."
git tag -a imagenes-v1.0.0 -m "Primera versión publicada desde el monorepo."
git tag -a gestor-v0.1.0 -m "Primera versión del gestor."
git push origin armado-columnas-v1.0.0 imagenes-v1.0.0 gestor-v0.1.0
```
Esperado: tres workflows en verde y tres releases (las de los add-ins con dos ZIP y `manifest.json`; la del gestor con `CQEING.Gestor-win-Setup.exe` y `releases.win.json`).

- [ ] **Paso 3: Prueba en un equipo limpio (criterio de éxito de la spec)**

En un equipo sin el gestor ni los add-ins (o con un usuario de Windows nuevo) y con Revit 2026:
1. Descargar y abrir `CQEING.Gestor-win-Setup.exe` → el gestor se abre y muestra «Armado de columnas» e «Imágenes por actividad» para Revit 2026.
2. Instalar los dos → abrir Revit 2026 → la pestaña CQEING trae «Estructural», «Herramientas» e «Imágenes»; los botones funcionan.
3. Desde el clon: `git tag -a armado-columnas-v1.0.1 -m "Prueba de actualización"; git push origin armado-columnas-v1.0.1`. En el gestor, «Buscar novedades» → la fila de Armado marca «Hay nueva» y el botón dice «Actualizar». Actualizar **con Revit abierto** → «Listo… Reinicie Revit»; al reiniciar, Revit carga la 1.0.1 (Add-In Manager o propiedades de la DLL: versión 1.0.1).
4. Elegir 1.0.0 en el desplegable → «Volver a esta» → sin descarga (la carpeta está en disco) → al reiniciar Revit carga la 1.0.0.
5. Publicar `gestor-v0.1.1` (etiqueta anotada en el mismo commit) → al reabrir el gestor aparece «Actualizar el gestor» → se actualiza y se reabre.
6. En un equipo que tenía el add-in instalado con el `Instalar.bat` viejo: instalar Armado desde el gestor → `%APPDATA%\Autodesk\Revit\Addins\2026\practicacq.addin` desaparece y Revit no avisa de ClientId duplicado.

- [ ] **Paso 4: Borrar las ramas viejas (confirmación explícita del usuario, rama por rama)**

Ramas a descartar según la spec (D3): `equipo/armado-columnas`, `juan/armado-columnas`, `addin-de-imagenes`, `menu-cqeing-wpf`, `migracion/monorepo`. Antes de borrar cada una, mostrar al usuario `git log --oneline origin/main..origin/<rama>` (commits que se perderían) y esperar su sí:
```powershell
git push origin --delete <rama>
```

- [ ] **Paso 5: Registrar el estado**

Agregar al final de este plan una sección «Estado» con la fecha, qué se probó en el paso 3 y lo pendiente (interfaz definitiva del gestor, login con la plataforma, configuración del `.env` de Imágenes desde el gestor).
