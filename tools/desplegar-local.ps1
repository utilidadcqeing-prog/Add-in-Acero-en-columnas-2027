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
$config = Get-Content $rutaConfig -Raw -Encoding UTF8 | ConvertFrom-Json

dotnet build (Join-Path $carpetaAddin $config.proyecto) -c $Configuracion -p:RevitVersion=$Revit
if ($LASTEXITCODE) { throw 'La compilación falló.' }

$salida = Join-Path $carpetaAddin "bin\$Configuracion\$Revit"
$origen = Get-ChildItem $salida -Filter *.addin | Select-Object -First 1
$xml = New-Object System.Xml.XmlDocument
$xml.Load($origen.FullName)
foreach ($assembly in $xml.SelectNodes('//Assembly')) { $assembly.InnerText = Join-Path $salida $assembly.InnerText.Trim() }

$destino = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$Revit"
New-Item -ItemType Directory -Force $destino | Out-Null
foreach ($heredado in $config.heredados) { Remove-Item (Join-Path $destino $heredado) -ErrorAction SilentlyContinue }
# El nombre sale del id de addin.json (no de la carpeta): coincide en los add-ins y deja que «zz-gestor-revit» cargue el último.
$xml.Save((Join-Path $destino "CQEING.$($config.id).addin"))
Write-Host "Listo: Revit $Revit cargará $Addin desde $salida"
