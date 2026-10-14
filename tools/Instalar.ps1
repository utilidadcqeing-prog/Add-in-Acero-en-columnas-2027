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

# Con Revit abierto el .dll queda bloqueado y la copia falla a medias: se espera a que lo cierren.
while (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    Write-Host "Revit esta abierto. Cerralo (guarda antes tu trabajo) y presiona Enter para seguir." -ForegroundColor Yellow
    Read-Host | Out-Null
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

# --- Configuracion del add-in de imagenes por actividad (issue #183 / #186) ---
# El add-in lee %APPDATA%\CQEING\imagenes\.env (ver Imagenes/Configuracion/ConfiguracionImagenes.cs).
# Nunca se pisa un valor que ya este puesto: solo se completan los que faltan. Las claves NO viajan
# en el paquete: se toman del add-in de cantidades (API_URL / API_KEY) o se piden aca.
function Read-EnvFile([string]$path) {
    $valores = @{}
    if (Test-Path $path) {
        foreach ($linea in Get-Content $path -Encoding UTF8) {
            if ($linea -match '^\s*([A-Z_]+)\s*=\s*(.*)$') { $valores[$Matches[1]] = $Matches[2].Trim().Trim('"', "'") }
        }
    }
    return $valores
}

function Set-EnvValue([string]$path, [string]$clave, [string]$valor) {
    $lineas = @()
    if (Test-Path $path) { $lineas = @(Get-Content $path -Encoding UTF8) }
    $hecho = $false
    for ($i = 0; $i -lt $lineas.Count; $i++) {
        if ($lineas[$i] -match "^\s*$clave\s*=") { $lineas[$i] = "$clave=$valor"; $hecho = $true }
    }
    if (-not $hecho) { $lineas += "$clave=$valor" }
    # UTF-8 sin BOM: el add-in lo lee con File.ReadAllLines.
    [System.IO.File]::WriteAllLines($path, [string[]]$lineas, (New-Object System.Text.UTF8Encoding($false)))
}

function Configurar-Imagenes {
    $carpeta = Join-Path $env:APPDATA 'CQEING\imagenes'
    $envPath = Join-Path $carpeta '.env'
    New-Item -ItemType Directory -Force -Path $carpeta | Out-Null

    Write-Host ""
    Write-Host "=== Configuracion del add-in de Imagenes por Actividad ===" -ForegroundColor Cyan
    Write-Host "Archivo: $envPath" -ForegroundColor DarkGray

    if (-not (Test-Path $envPath)) {
        $plantilla = @(
            '# Configuracion del add-in de imagenes por actividad (CQEING). NO compartir: lleva claves.',
            '# API de CQEING: la misma URL y X-Api-Key que usa el add-in de cantidades.',
            'API_URL=',
            'API_KEY=',
            '',
            '# Cloudinary de CQEING: con API_KEY + API_SECRET se sube con firma y se borran versiones viejas.',
            'CLOUDINARY_CLOUD_NAME=df6m0s0ri',
            'CLOUDINARY_UPLOAD_PRESET=',
            'CLOUDINARY_API_KEY=',
            'CLOUDINARY_API_SECRET='
        )
        [System.IO.File]::WriteAllLines($envPath, [string[]]$plantilla, (New-Object System.Text.UTF8Encoding($false)))
    }

    $actual = Read-EnvFile $envPath

    # API_URL / API_KEY: del .env del add-in de cantidades, si esta instalado en este equipo.
    if (-not $actual['API_URL'] -or -not $actual['API_KEY']) {
        $deCantidades = Get-ChildItem (Join-Path $env:APPDATA 'Autodesk\Revit\Addins') -Recurse -Filter '.env' -Force -ErrorAction SilentlyContinue |
            ForEach-Object { Read-EnvFile $_.FullName } |
            Where-Object { $_['API_URL'] -and $_['API_KEY'] } |
            Select-Object -First 1
        if ($deCantidades) {
            if (-not $actual['API_URL']) { Set-EnvValue $envPath 'API_URL' $deCantidades['API_URL'] }
            if (-not $actual['API_KEY']) { Set-EnvValue $envPath 'API_KEY' $deCantidades['API_KEY'] }
            Write-Host "[OK] API_URL y API_KEY tomadas del add-in de cantidades." -ForegroundColor Green
        }
    }
    if (-not $actual['CLOUDINARY_CLOUD_NAME']) { Set-EnvValue $envPath 'CLOUDINARY_CLOUD_NAME' 'df6m0s0ri' }

    # Lo que siga faltando se pide. Enter deja el valor vacio (se puede completar despues en el archivo).
    $actual = Read-EnvFile $envPath
    $pedir = [ordered]@{
        'API_URL'               = 'URL de la API de CQEING (ej. https://cqingenieria-production.up.railway.app)'
        'API_KEY'               = 'X-Api-Key de la API de CQEING'
        'CLOUDINARY_API_KEY'    = 'API Key de Cloudinary (consola de Cloudinary > Settings > API Keys)'
        'CLOUDINARY_API_SECRET' = 'API Secret de Cloudinary (misma pantalla)'
    }
    foreach ($clave in $pedir.Keys) {
        if ($actual[$clave]) { continue }
        $valor = Read-Host "$($pedir[$clave]) [Enter para dejarlo vacio]"
        if ($valor) { Set-EnvValue $envPath $clave $valor.Trim() }
    }

    $actual = Read-EnvFile $envPath
    $faltan = @('API_URL', 'API_KEY', 'CLOUDINARY_API_KEY', 'CLOUDINARY_API_SECRET') | Where-Object { -not $actual[$_] }
    if ($faltan) {
        Write-Host "[!] Faltan: $($faltan -join ', '). Sin API el add-in usa datos de ejemplo; sin las claves de" -ForegroundColor Yellow
        Write-Host "    Cloudinary las imagenes quedan solo en esta PC. Se pueden completar despues en el archivo de arriba." -ForegroundColor Yellow
    } else {
        Write-Host "[OK] Configuracion de imagenes completa." -ForegroundColor Green
    }
}

if ($instaladoAlguno) {
    try { Configurar-Imagenes }
    catch { Write-Host "[ERROR] No se pudo configurar el add-in de imagenes -- $($_.Exception.Message)" -ForegroundColor Red }
}

Write-Host ""
if ($instaladoAlguno) {
    Write-Host "Listo. Abri (o reinicia) Revit y busca la pestana CQEING en el ribbon." -ForegroundColor Yellow
    Write-Host "La primera vez Revit pregunta si cargar el add-in: elegi 'Cargar siempre' (Always Load)." -ForegroundColor Yellow
} else {
    Write-Host "No se pudo instalar ninguna version. Revisa los errores de arriba." -ForegroundColor Red
}
Write-Host ""
Read-Host "Presiona Enter para cerrar"
