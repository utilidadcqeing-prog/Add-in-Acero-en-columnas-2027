<#
.SYNOPSIS
  Instalador del add-in de armado de columnas (CQEING) para quien lo recibe
  ya compilado -- no requiere consola, no requiere el SDK de .NET, no
  requiere saber que version de Revit tiene instalada.

  Se ejecuta con doble clic en Instalar.bat (que llama a este script). Copia
  cada carpeta de version (2026\, 2027\, etc.) que venga al lado de este
  script hacia %APPDATA%\Autodesk\Revit\Addins\<version>\. Revit solo lee la
  carpeta que corresponde a la version que este instalada, asi que copiar
  todas las que vengan en el paquete es inofensivo y funciona sin importar
  que version tenga el equipo.
#>

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$addinsRoot = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'

Write-Host ""
Write-Host "=== Instalador del Add-in de Armado de Columnas (CQEING) ===" -ForegroundColor Cyan
Write-Host ""

$versionFolders = Get-ChildItem -Path $here -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d{4}$' -and (Test-Path (Join-Path $_.FullName 'practicacq.addin')) }

if (-not $versionFolders) {
    Write-Host "No se encontraron carpetas de version (2026, 2027, ...) al lado de este instalador." -ForegroundColor Red
    Write-Host "Asegurate de descomprimir el .zip completo antes de correr Instalar.bat." -ForegroundColor Red
    Write-Host ""
    Read-Host "Presiona Enter para cerrar"
    exit 1
}

$instaladoAlguno = $false

foreach ($versionFolder in $versionFolders) {
    $year = $versionFolder.Name
    $targetDir = Join-Path $addinsRoot $year

    try {
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $targetDir 'Resources') | Out-Null

        Copy-Item (Join-Path $versionFolder.FullName 'practicacq.dll')   $targetDir -Force
        Copy-Item (Join-Path $versionFolder.FullName 'practicacq.addin') $targetDir -Force
        Copy-Item (Join-Path $versionFolder.FullName 'Resources\*.png') (Join-Path $targetDir 'Resources') -Force

        Write-Host "[OK] Instalado para Revit $year en:" -ForegroundColor Green
        Write-Host "     $targetDir"
        $instaladoAlguno = $true
    } catch {
        Write-Host "[ERROR] No se pudo instalar para Revit $year -- $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "        Si Revit $year esta abierto, cerralo y volve a correr este instalador." -ForegroundColor Red
    }
}

Write-Host ""
if ($instaladoAlguno) {
    Write-Host "Listo. Abri (o reinicia) Revit y busca la pestana CQEING en el ribbon." -ForegroundColor Yellow
} else {
    Write-Host "No se pudo instalar ninguna version. Revisa los errores de arriba." -ForegroundColor Red
}
Write-Host ""
Read-Host "Presiona Enter para cerrar"
