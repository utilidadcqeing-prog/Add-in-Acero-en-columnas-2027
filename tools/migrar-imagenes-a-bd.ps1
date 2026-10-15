<#
.SYNOPSIS
  Registra en la BD del backend las imagenes por actividad que ya estan en Cloudinary (fase B,
  docs/planes/2026-10-14-imagenes-por-actividad-en-bd.md, paso 5). Corre sin Revit.

  Para cada presupuesto de GET /api/presupuestos:
   1. Lista sus imagenes en Cloudinary: cuenta del .env con la Admin API (carpeta
      cuadro_cantidades/imagenes/<presupuesto>/), y opcionalmente una cuenta anterior con la lista
      publica por tag (-CuentaAnterior). Si una imagen tiene varias versiones (_v2, _v3...), usa la mas
      nueva.
   2. Completa descripcion, colores y modelo con el registro.json local de esta PC, si existe.
   3. Busca el id_item en /estructura por codigo_actividad y hace el POST. Un 409 es "ya existente".
      A las imagenes cuya actividad no esta en la API no se les inventa un id_item.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\migrar-imagenes-a-bd.ps1 -Simular
  powershell -ExecutionPolicy Bypass -File tools\migrar-imagenes-a-bd.ps1
  powershell -ExecutionPolicy Bypass -File tools\migrar-imagenes-a-bd.ps1 -CuentaAnterior chhoueyn -Presupuesto PROY_004
#>
param(
    # Muestra lo que haria sin escribir nada en la API.
    [switch]$Simular,
    # Cuenta de Cloudinary anterior a incluir con la lista publica por tag
    # (https://res.cloudinary.com/<cuenta>/image/list/<presupuesto>.json; requiere "Resource list" activo).
    [string]$CuentaAnterior,
    # Solo este presupuesto (por defecto, todos).
    [string]$Presupuesto,
    # Otro .env (para pruebas). Por defecto %APPDATA%\CQEING\imagenes\.env.
    [string]$Env = (Join-Path $env:APPDATA 'CQEING\imagenes\.env')
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Read-EnvFile([string]$path) {
    $valores = @{}
    foreach ($linea in Get-Content $path -Encoding UTF8) {
        if ($linea -match '^\s*([A-Z_]+)\s*=\s*(.*)$') { $valores[$Matches[1]] = $Matches[2].Trim().Trim('"', "'") }
    }
    return $valores
}

if (-not (Test-Path $Env)) { throw "No existe $Env" }
$cfg = Read-EnvFile $Env
$apiUrl = ([string]$cfg['API_URL']).TrimEnd('/')
$apiKey = [string]$cfg['API_KEY']
if (-not $apiUrl -or -not $apiKey) { throw "Faltan API_URL o API_KEY en $Env" }
$cloud = [string]$cfg['CLOUDINARY_CLOUD_NAME']
$cloudKey = [string]$cfg['CLOUDINARY_API_KEY']
$cloudSecret = [string]$cfg['CLOUDINARY_API_SECRET']
$carpetaLocal = Split-Path -Parent $Env

# Llamada a la API de CQEING. Devuelve @{ Estado; Json; Texto }. Exige JSON: las rutas que no existen
# responden 200 con el HTML de la web (hallazgo 3 del plan), y eso no puede contar como exito.
function Invoke-Api([string]$metodo, [string]$ruta, $cuerpo = $null) {
    $p = @{ Uri = $apiUrl + $ruta; Method = $metodo; Headers = @{ 'X-Api-Key' = $apiKey }; UseBasicParsing = $true }
    if ($null -ne $cuerpo) {
        $p['Body'] = [Text.Encoding]::UTF8.GetBytes(($cuerpo | ConvertTo-Json -Depth 5 -Compress))
        $p['ContentType'] = 'application/json; charset=utf-8'
    }
    try {
        $r = Invoke-WebRequest @p
        $estado = [int]$r.StatusCode; $tipo = [string]$r.Headers['Content-Type']
        $texto = [Text.Encoding]::UTF8.GetString($r.RawContentStream.ToArray())
    } catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        if (-not $resp) { throw }
        $estado = [int]$resp.StatusCode; $tipo = [string]$resp.ContentType
        $texto = (New-Object IO.StreamReader($resp.GetResponseStream(), [Text.Encoding]::UTF8)).ReadToEnd()
    }
    if ($texto -and $tipo -notmatch 'json') { throw "La API no tiene $ruta (respondio $tipo, no JSON)." }
    $json = $null
    if ($texto) { $json = $texto | ConvertFrom-Json }
    return @{ Estado = $estado; Json = $json; Texto = $texto }
}

function Get-Presupuestos {
    $codigos = @()
    $paginas = 1
    for ($pagina = 1; $pagina -le $paginas -and $pagina -le 50; $pagina++) {
        $r = Invoke-Api GET "/api/presupuestos?page=$pagina&pageSize=50"
        if ($r.Estado -ne 200) { throw "GET /api/presupuestos respondio $($r.Estado)" }
        $lista = if ($r.Json -is [array]) { $r.Json } else { $r.Json.data }
        foreach ($p in $lista) {
            $c = if ($p.codigoPresupuesto) { $p.codigoPresupuesto } else { $p.codigo_presupuesto }
            if ($c) { $codigos += [string]$c }
        }
        if ($r.Json -is [array]) { break }
        if ($r.Json.pages) { $paginas = [int]$r.Json.pages }
    }
    return $codigos | Sort-Object -Unique
}

function Get-IdItems([string]$codigoPresupuesto) {
    $mapa = @{}
    $r = Invoke-Api GET "/api/presupuestos/$([Uri]::EscapeDataString($codigoPresupuesto))/estructura"
    if ($r.Estado -ne 200) { return $mapa }
    foreach ($c in $r.Json.capitulos) { foreach ($s in $c.subcapitulos) { foreach ($i in $s.items) {
        if ($i.codigo_actividad -and $i.id_item) { $mapa[[string]$i.codigo_actividad] = [int]$i.id_item }
    } } }
    return $mapa
}

# Codigo y version a partir del public_id: ".../ACT_006_01_v2" -> ACT_006_01, 2.
function Split-Codigo([string]$publicId) {
    $nombre = ($publicId -split '/')[-1]
    if ($nombre -match '^(.*)_v(\d+)$') { return @($Matches[1], [int]$Matches[2]) }
    return @($nombre, 1)
}

# Imagenes de un presupuesto en la cuenta del .env (Admin API, firmada con la API Key/Secret).
function Get-ImagenesCuenta([string]$codigoPresupuesto) {
    $res = @()
    if (-not $cloud -or -not $cloudKey -or -not $cloudSecret) {
        Write-Warning "Sin CLOUDINARY_API_KEY/SECRET en $Env : no se lista la cuenta $cloud."
        return $res
    }
    $auth = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${cloudKey}:${cloudSecret}"))
    $prefijo = "cuadro_cantidades/imagenes/$codigoPresupuesto/"
    $cursor = $null
    do {
        $u = "https://api.cloudinary.com/v1_1/$cloud/resources/image/upload?prefix=$([Uri]::EscapeDataString($prefijo))&context=true&max_results=500"
        if ($cursor) { $u += "&next_cursor=$cursor" }
        $r = Invoke-RestMethod -Uri $u -Headers @{ Authorization = $auth } -UseBasicParsing
        foreach ($x in $r.resources) {
            $partes = $x.public_id -split '/'
            $cv = Split-Codigo $x.public_id
            $ctx = $x.context.custom
            $res += [pscustomobject]@{
                Codigo = if ($ctx.codigo) { [string]$ctx.codigo } else { $cv[0] }
                Version = if ($ctx.version) { [int]$ctx.version } else { $cv[1] }
                Actividad = $partes[-2]
                Url = [string]$x.secure_url
                Nombre = [string]$ctx.nombre; Descripcion = [string]$ctx.descripcion
                IdModelo = [string]$ctx.modelo; NombreModelo = [string]$ctx.nombre_modelo
            }
        }
        $cursor = $r.next_cursor
    } while ($cursor)
    return $res
}

# Imagenes de un presupuesto en una cuenta anterior, con la lista publica por tag.
function Get-ImagenesCuentaAnterior([string]$cuenta, [string]$codigoPresupuesto) {
    $res = @()
    try {
        $r = Invoke-RestMethod -Uri "https://res.cloudinary.com/$cuenta/image/list/$([Uri]::EscapeDataString($codigoPresupuesto)).json" -UseBasicParsing
    } catch {
        # 404 = ese tag no tiene imagenes en la cuenta: es lo normal, no se avisa.
        $resp = $_.Exception.Response
        if (-not ($resp -and [int]$resp.StatusCode -eq 404)) {
            Write-Warning "No se pudo listar $codigoPresupuesto en la cuenta $cuenta (revisa que tenga 'Resource list' activo): $($_.Exception.Message)"
        }
        return $res
    }
    foreach ($x in $r.resources) {
        $cv = Split-Codigo $x.public_id
        $ctx = $x.context.custom
        $res += [pscustomobject]@{
            Codigo = if ($ctx.codigo) { [string]$ctx.codigo } else { $cv[0] }
            Version = $cv[1]
            Actividad = ''
            Url = "https://res.cloudinary.com/$cuenta/image/upload/v$($x.version)/$($x.public_id).$($x.format)"
            Nombre = [string]$ctx.nombre; Descripcion = [string]$ctx.descripcion
            IdModelo = [string]$ctx.modelo; NombreModelo = [string]$ctx.nombre_modelo
        }
    }
    return $res
}

function Get-RegistroLocal([string]$codigoPresupuesto) {
    $mapa = @{}
    $ruta = Join-Path (Join-Path $carpetaLocal $codigoPresupuesto) 'registro.json'
    if (Test-Path $ruta) {
        foreach ($i in (Get-Content $ruta -Raw -Encoding UTF8 | ConvertFrom-Json)) { $mapa[[string]$i.Codigo] = $i }
    }
    return $mapa
}

$registradas = @(); $existentes = @(); $sinActividad = @(); $errores = @()
$presupuestos = if ($Presupuesto) { @($Presupuesto) } else { Get-Presupuestos }
Write-Host "Presupuestos: $($presupuestos.Count)$(if ($Simular) { '  (SIMULACION: no se escribe nada)' })" -ForegroundColor Cyan

foreach ($p in $presupuestos) {
    $imagenes = @(Get-ImagenesCuenta $p)
    if ($CuentaAnterior) { $imagenes += @(Get-ImagenesCuentaAnterior $CuentaAnterior $p) }
    if ($imagenes.Count -eq 0) { continue }

    $idItems = Get-IdItems $p
    $locales = Get-RegistroLocal $p
    # Una entrada por codigo: la version mas nueva (las de la cuenta del .env ganan a la anterior).
    $porCodigo = $imagenes | Group-Object Codigo | ForEach-Object { $_.Group | Sort-Object Version -Descending | Select-Object -First 1 }

    foreach ($img in $porCodigo) {
        $local = $locales[$img.Codigo]
        $actividad = if ($img.Actividad) { $img.Actividad } elseif ($local) { [string]$local.CodigoActividad } else { ($img.Codigo -replace '_\d+$', '') }
        $etiqueta = "$p / $actividad / $($img.Codigo)"
        if (-not $idItems.ContainsKey($actividad)) { $sinActividad += $etiqueta; continue }

        $cuerpo = [ordered]@{
            codigo = $img.Codigo
            url = $img.Url
            nombre = if ($img.Nombre) { $img.Nombre } elseif ($local) { [string]$local.Nombre } else { $img.Codigo }
            descripcion = if ($local -and $local.Descripcion) { [string]$local.Descripcion } else { $img.Descripcion }
            tipo_vista = if ($local) { [string]$local.TipoVista } else { '' }
            id_modelo = if ($img.IdModelo) { $img.IdModelo } elseif ($local) { [string]$local.IdModelo } else { '' }
            nombre_modelo = if ($img.NombreModelo) { $img.NombreModelo } elseif ($local) { [string]$local.NombreModelo } else { '' }
            colores = @(if ($local) { $local.Colores | ForEach-Object { @{ tipo = [string]$_.NombreTipo; color = [string]$_.ColorHex } } })
        }
        $idItem = $idItems[$actividad]
        if ($Simular) { $registradas += "$etiqueta -> item $idItem (simulado)"; continue }

        try {
            $r = Invoke-Api POST "/api/presupuestos/items/$idItem/imagenes" $cuerpo
            if ($r.Estado -in 200, 201) { $registradas += "$etiqueta -> item $idItem" }
            elseif ($r.Estado -eq 409) { $existentes += $etiqueta }
            else { $errores += "$etiqueta : $($r.Estado) $($r.Texto)" }
        } catch {
            $errores += "$etiqueta : $($_.Exception.Message)"
        }
    }
}

Write-Host ""
Write-Host "Registradas$(if ($Simular) { ' (simulado)' }): $($registradas.Count)" -ForegroundColor Green
$registradas | ForEach-Object { "   $_" }
Write-Host "Ya existentes (409): $($existentes.Count)" -ForegroundColor Yellow
$existentes | ForEach-Object { "   $_" }
Write-Host "Sin actividad en la API (no se registran): $($sinActividad.Count)" -ForegroundColor DarkYellow
$sinActividad | ForEach-Object { "   $_" }
if ($errores.Count) {
    Write-Host "Con error: $($errores.Count)" -ForegroundColor Red
    $errores | ForEach-Object { "   $_" }
    exit 1
}
