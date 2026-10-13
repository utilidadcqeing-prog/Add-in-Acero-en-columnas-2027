<#
.SYNOPSIS
  Compila practicacq (add-in de armado de columnas, CQEING) y lo copia a la
  carpeta de Add-ins de cada version de Revit que este instalada
  (%APPDATA%\Autodesk\Revit\Addins\<year>).

.EXAMPLE
  .\tools\deploy.ps1
  .\tools\deploy.ps1 -Versions 2027
  .\tools\deploy.ps1 -Configuration Release
  .\tools\deploy.ps1 -Versions 2026 -TargetFramework net10.0-windows   # forzar a mano, salta la deteccion
#>
param(
    [string[]]$Versions = @('2026', '2027'),
    [string]$Configuration = 'Debug',
    # Por defecto se detecta solo leyendo el RevitAPI.dll instalado (ver
    # Get-RevitApiTargetFramework mas abajo, issue #14). Pasar esto fuerza el
    # valor y salta la deteccion -- util si la deteccion no corre en tu maquina.
    [string]$TargetFramework
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'practicacq.csproj'
$addinsRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'

# Revit 2026.0-2026.4 trae un RevitAPI.dll compilado contra .NETCoreApp v8;
# 2026.5+ pasa a v10 (mismo runtime que 2027) pero instala en la MISMA carpeta
# "Revit 2026" -- el numero de version por si solo no alcanza para distinguirlos
# (issue #14). Leemos el TargetFrameworkAttribute real del ensamblado con
# ReflectionOnlyLoadFrom (no lo ejecuta ni lo carga contra el runtime actual,
# asi que no importa si es mas nuevo que el de esta maquina).
function Get-RevitApiTargetFramework([string]$revitDir) {
    $dllPath = Join-Path $revitDir 'RevitAPI.dll'
    if (-not (Test-Path $dllPath)) { return $null }

    try {
        $asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($dllPath)
        $attr = $asm.GetCustomAttributesData() |
            Where-Object { $_.AttributeType.Name -eq 'TargetFrameworkAttribute' } |
            Select-Object -First 1
        if (-not $attr) { return $null }

        $moniker = [string]$attr.ConstructorArguments[0].Value  # p.ej. ".NETCoreApp,Version=v10.0"
        if ($moniker -match 'Version=v(\d+)\.') {
            return "net$($Matches[1]).0-windows"
        }
        return $null
    } catch {
        Write-Warning "No se pudo leer el TargetFramework de '$dllPath': $($_.Exception.Message)"
        return $null
    }
}

foreach ($year in $Versions) {

    $revitDir = "C:\Program Files\Autodesk\Revit $year"
    if (-not (Test-Path $revitDir)) {
        Write-Warning "No se encontro Revit $year en '$revitDir'. Se salta esta version."
        continue
    }

    $effectiveTargetFramework = $TargetFramework
    if (-not $effectiveTargetFramework) {
        $detected = Get-RevitApiTargetFramework $revitDir
        if ($detected) {
            $effectiveTargetFramework = $detected
            Write-Host "   RevitAPI.dll de Revit $year detectado como $detected" -ForegroundColor DarkGray
        } else {
            Write-Warning "No se pudo detectar el TargetFramework de RevitAPI.dll para Revit $year -- se usa el default del .csproj (net8.0-windows para 2026). Si tu Revit 2026 es 2026.5+, pasa -TargetFramework net10.0-windows a mano."
        }
    }

    Write-Host "== Compilando practicacq para Revit $year ==" -ForegroundColor Cyan
    # Restore explicito y despues build con --no-restore: el restore implicito de `dotnet build`
    # descarta el -p:TargetFramework que se le pasa, restaura con el default del .csproj
    # (net8.0-windows para 2026) y el build falla con NETSDK1005 en Revit 2026.5+ (net10).
    # Visto el 2026-10-13 al desplegar el add-in de imagenes en Revit 2026.5.
    if ($effectiveTargetFramework) {
        dotnet restore $proj -p:RevitVersion=$year -p:TargetFramework=$effectiveTargetFramework
        if ($LASTEXITCODE -ne 0) { throw "Fallo el restore para Revit $year" }
        dotnet build $proj --no-restore -c $Configuration -p:RevitVersion=$year -p:TargetFramework=$effectiveTargetFramework
    } else {
        dotnet build $proj -c $Configuration -p:RevitVersion=$year
    }
    if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion para Revit $year" }

    $outDir = Join-Path $root "bin\$Configuration\$year"
    $targetDir = Join-Path $addinsRoot $year

    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $targetDir 'Resources') | Out-Null

    Copy-Item (Join-Path $outDir 'practicacq.dll')   $targetDir -Force
    Copy-Item (Join-Path $outDir 'practicacq.pdb')   $targetDir -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $outDir 'practicacq.addin') $targetDir -Force
    Copy-Item (Join-Path $outDir 'Resources\*.png') (Join-Path $targetDir 'Resources') -Force

    Write-Host "-> Instalado en $targetDir" -ForegroundColor Green
}

Write-Host "`nListo. Abri (o reinicia) Revit y busca la pestana CQEING en el ribbon." -ForegroundColor Yellow
