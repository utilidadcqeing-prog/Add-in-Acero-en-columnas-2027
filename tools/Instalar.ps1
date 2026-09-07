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
    # Este instalador no compila nada -- necesita el .dll ya compilado al
    # lado (carpetas "2026\", "2027\"). Si no estan, lo mas probable es que
    # se esta corriendo el archivo equivocado: el del codigo fuente
    # (tools\Instalar.bat en el repo) en vez del que viene DENTRO del .zip
    # que arma tools\publish.ps1 (que trae esas carpetas al lado). Se
    # detecta ese caso especifico para no mandar a buscar un problema de
    # Revit que no existe.
    $enRepoFuente = Test-Path (Join-Path (Split-Path -Parent $here) 'practicacq.csproj')

    Write-Host "No se encontraron carpetas de version (2026, 2027, ...) al lado de este instalador." -ForegroundColor Red
    Write-Host ""
    if ($enRepoFuente) {
        Write-Host "Este parece ser el Instalar.bat del CODIGO FUENTE del add-in (carpeta tools\ del" -ForegroundColor Yellow
        Write-Host "repositorio), que no trae los .dll compilados. Hace falta el que sale de" -ForegroundColor Yellow
        Write-Host "'.\tools\publish.ps1 -Zip': ese arma un .zip con las carpetas 2026\/2027\ al lado" -ForegroundColor Yellow
        Write-Host "de Instalar.bat. Descomprimi ESE .zip completo y corre el Instalar.bat de ADENTRO." -ForegroundColor Yellow
    } else {
        Write-Host "Asegurate de descomprimir el .zip completo (no solo Instalar.bat) antes de" -ForegroundColor Yellow
        Write-Host "correrlo -- las carpetas 2026\/2027\ tienen que quedar al lado." -ForegroundColor Yellow
    }

    $revitDetectados = Get-ChildItem 'C:\Program Files\Autodesk' -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^Revit \d{4}$' } |
        ForEach-Object { $_.Name -replace '^Revit ', '' }
    Write-Host ""
    if ($revitDetectados) {
        Write-Host "(Esta computadora tiene Revit $($revitDetectados -join ', ') instalado, para referencia --" -ForegroundColor DarkGray
        Write-Host " el problema de arriba es del paquete, no de detectar la version de Revit.)" -ForegroundColor DarkGray
    }
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
