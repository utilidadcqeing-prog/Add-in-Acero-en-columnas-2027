<#
.SYNOPSIS
  Genera el paquete instalador del add-in (practicacq) para repartir a otros
  equipos. A diferencia de deploy.ps1 (que instala en ESTA maquina), este
  script arma una carpeta "Publicar\" autocontenida con los binarios ya
  compilados en Release para cada version de Revit disponible, mas un
  instalador de doble clic (Instalar.bat) que no requiere consola ni el SDK
  de .NET en la maquina destino.

.EXAMPLE
  .\tools\publish.ps1
  .\tools\publish.ps1 -Versions 2027
  .\tools\publish.ps1 -Zip

  # Compilar para una version que no esta instalada en esta maquina, usando
  # solo RevitAPI.dll y RevitAPIUI.dll de esa version (ver LEEME en README
  # sobre "pedir los DLL de referencia"):
  .\tools\publish.ps1 -Versions 2026 -RevitInstallDirs @{ '2026' = 'C:\RevitRefs\2026\' } -Zip
#>
param(
    [string[]]$Versions = @('2026', '2027'),
    # Si se pasa, ademas arma un .zip listo para mandar (requiere que exista
    # el comando Compress-Archive, incluido en Windows 10/11).
    [switch]$Zip,
    # Override por version de donde buscar RevitAPI.dll / RevitAPIUI.dll,
    # para compilar contra una version que no esta instalada en esta maquina
    # (alcanza con tener los .dll en una carpeta, no hace falta Revit
    # instalado entero). Ej: @{ '2026' = 'C:\RevitRefs\2026\' }
    # OJO: la ruta debe terminar en '\'.
    [hashtable]$RevitInstallDirs = @{},
    # Override por version del TargetFramework, para cuando la deteccion
    # automatica (leer el TargetFrameworkAttribute de RevitAPI.dll) no
    # funciona -- por ejemplo si solo tenes los .dll sueltos sin el resto de
    # la carpeta de Revit al lado. Ej: @{ '2026' = 'net8.0-windows' }
    # (net8.0-windows para Revit 2026 pre-2026.5, net10.0-windows para
    # 2026.5+ y para 2027 -- ver comentario en practicacq.csproj).
    [hashtable]$TargetFrameworks = @{}
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'practicacq.csproj'
$publishDir = Join-Path $root 'Publicar'

# Misma deteccion de TargetFramework que usa deploy.ps1 (issue #14): Revit
# 2026.5+ pasa a net10.0-windows pero instala en la misma carpeta "Revit 2026".
function Get-RevitApiTargetFramework([string]$revitDir) {
    $dllPath = Join-Path $revitDir 'RevitAPI.dll'
    if (-not (Test-Path $dllPath)) { return $null }
    try {
        $asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($dllPath)
        $attr = $asm.GetCustomAttributesData() |
            Where-Object { $_.AttributeType.Name -eq 'TargetFrameworkAttribute' } |
            Select-Object -First 1
        if (-not $attr) { return $null }
        $moniker = [string]$attr.ConstructorArguments[0].Value
        if ($moniker -match 'Version=v(\d+)\.') { return "net$($Matches[1]).0-windows" }
        return $null
    } catch {
        Write-Warning "No se pudo leer el TargetFramework de '$dllPath': $($_.Exception.Message)"
        return $null
    }
}

Write-Host "== Preparando carpeta de publicacion en '$publishDir' ==" -ForegroundColor Cyan
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$builtAny = $false

foreach ($year in $Versions) {

    $revitDir = $RevitInstallDirs[$year]
    $usingOverride = [bool]$revitDir
    if (-not $revitDir) { $revitDir = "C:\Program Files\Autodesk\Revit $year\" }

    if (-not (Test-Path (Join-Path $revitDir 'RevitAPI.dll'))) {
        if ($usingOverride) {
            Write-Warning "No se encontro RevitAPI.dll en '$revitDir' (RevitInstallDirs para $year). Se salta esta version."
        } else {
            Write-Warning "No se encontro Revit $year en '$revitDir' en ESTA maquina. Se salta -- para incluir esta version en el paquete: (a) compilar desde una maquina que tenga Revit $year instalado, o (b) conseguir solo RevitAPI.dll y RevitAPIUI.dll de esa version, ponerlos en una carpeta local, y pasar -RevitInstallDirs @{ '$year' = 'C:\ruta\a\esa\carpeta\' }."
        }
        continue
    }

    $effectiveTargetFramework = $TargetFrameworks[$year]
    if ($effectiveTargetFramework) {
        Write-Host "   TargetFramework para Revit $year forzado a $effectiveTargetFramework (-TargetFrameworks)" -ForegroundColor DarkGray
    } else {
        $effectiveTargetFramework = Get-RevitApiTargetFramework $revitDir
        if ($effectiveTargetFramework) {
            Write-Host "   RevitAPI.dll de Revit $year detectado como $effectiveTargetFramework" -ForegroundColor DarkGray
        } elseif ($usingOverride) {
            Write-Warning "No se pudo detectar el TargetFramework de '$revitDir' -- si la compilacion falla por versiones de System.Runtime, pasa -TargetFrameworks @{ '$year' = 'net8.0-windows' } (o net10.0-windows) a mano."
        }
    }

    Write-Host "== Compilando practicacq (Release) para Revit $year ==" -ForegroundColor Cyan
    $props = @("-p:RevitVersion=$year")
    if ($usingOverride) { $props += "-p:RevitInstallDir=$revitDir" }
    if ($effectiveTargetFramework) { $props += "-p:TargetFramework=$effectiveTargetFramework" }

    # Restore y build por separado: al forzar -p:TargetFramework, un solo
    # `dotnet build` (que hace restore implicito) a veces deja
    # obj\project.assets.json evaluado para el TargetFramework por defecto
    # del .csproj en vez del forzado, y falla con NETSDK1005. Separarlos
    # evita ese desfasaje.
    dotnet restore $proj @props
    if ($LASTEXITCODE -ne 0) { throw "Fallo el restore para Revit $year" }
    dotnet build $proj -c Release --no-restore @props
    if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion para Revit $year" }

    $outDir = Join-Path $root "bin\Release\$year"
    $versionDir = Join-Path $publishDir $year

    New-Item -ItemType Directory -Force -Path $versionDir | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $versionDir 'Resources') | Out-Null

    Copy-Item (Join-Path $outDir 'practicacq.dll')   $versionDir -Force
    Copy-Item (Join-Path $outDir 'practicacq.addin') $versionDir -Force
    Copy-Item (Join-Path $outDir 'Resources\*.png') (Join-Path $versionDir 'Resources') -Force

    $builtAny = $true
    Write-Host "-> Empaquetado Revit $year en '$versionDir'" -ForegroundColor Green
}

if (-not $builtAny) {
    throw "No se pudo compilar ninguna version (no hay Revit instalado en esta maquina para: $($Versions -join ', ')). No se genero el paquete."
}

Copy-Item (Join-Path $PSScriptRoot 'Instalar.bat') $publishDir -Force
Copy-Item (Join-Path $PSScriptRoot 'Instalar.ps1') $publishDir -Force
Copy-Item (Join-Path $PSScriptRoot 'LEEME.txt')    $publishDir -Force

if ($Zip) {
    $zipPath = Join-Path $root 'Publicar-practicacq.zip'
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath
    Write-Host "`nListo. Paquete comprimido en '$zipPath' -- mandalo entero (el .zip) al otro equipo." -ForegroundColor Yellow
} else {
    Write-Host "`nListo. Comprimi la carpeta '$publishDir' entera en un .zip y mandala al otro equipo." -ForegroundColor Yellow
    Write-Host "(O corre este script con -Zip para que lo comprima solo.)" -ForegroundColor Yellow
}
