<#
.SYNOPSIS
  Compila practicacq (add-in de armado de columnas, CQEING) y lo copia a la
  carpeta de Add-ins de cada version de Revit que este instalada
  (%APPDATA%\Autodesk\Revit\Addins\<year>).

.EXAMPLE
  .\tools\deploy.ps1
  .\tools\deploy.ps1 -Versions 2027
  .\tools\deploy.ps1 -Configuration Release
  .\tools\deploy.ps1 -Versions 2026 -TargetFramework net10.0-windows   # Revit 2026.5+ (ver #14)
#>
param(
    [string[]]$Versions = @('2026', '2027'),
    [string]$Configuration = 'Debug',
    # Revit 2026.0-2026.4 usa net8.0-windows; 2026.5+ pasa a net10.0-windows,
    # y las dos instalan en la misma carpeta "Revit 2026" -- si tu 2026 es
    # 2026.5 o mas nuevo, pasa esto explicito (ver el comentario del .csproj).
    [string]$TargetFramework
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'practicacq.csproj'
$addinsRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'

foreach ($year in $Versions) {

    $revitDir = "C:\Program Files\Autodesk\Revit $year"
    if (-not (Test-Path $revitDir)) {
        Write-Warning "No se encontro Revit $year en '$revitDir'. Se salta esta version."
        continue
    }

    Write-Host "== Compilando practicacq para Revit $year ==" -ForegroundColor Cyan
    if ($TargetFramework) {
        dotnet build $proj -c $Configuration -p:RevitVersion=$year -p:TargetFramework=$TargetFramework
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
