# Diseño: CQEING-Addins (monorepo) + gestor de escritorio de add-ins

Fecha: 2026-10-15 · Estado: revisado tras la conversación (repo público, sin login en esta fase)
Antecedente: `investigacion-plataforma-addins.md` (repo del Cuadro de cantidades).

## 1. Objetivo

Que el equipo de CQEING instale, actualice y revierta los add-ins de Revit desde una app de
escritorio, sin entrar a GitHub ni descargar ZIP a mano.

**Éxito:** desde un equipo limpio, un usuario abre el gestor, ve los dos add-ins, instala la última
versión para su Revit, ve el aviso cuando se publica una nueva, actualiza y vuelve a la anterior.

## 2. Decisiones tomadas (no se reabren)

| # | Decisión |
|---|---|
| D1 | Un solo repo: `utilidadcqeing-prog/Add-in-Acero-en-columnas-2027`, renombrado a **`CQEING-Addins`** y **público**. |
| D2 | Una rama estable (`main`) con **una carpeta por add-in**. Las ramas son solo trabajo temporal. |
| D3 | Add-ins que entran: **Armado de columnas** (rama `equipo/armado-columnas`) e **Imágenes por actividad** (carpeta `Imagenes/` de la rama `addin-de-imagenes`). El resto de ramas se descarta. **El Cuadro de cantidades no entra.** |
| D4 | Se conserva el historial de git de ambos add-ins. |
| D5 | Gestor = app de escritorio **WPF propia, empaquetada con Velopack**, que lee **directamente** las releases públicas de GitHub, sin token. |
| D6 | **Sin login en esta fase.** No se toca la plataforma CQIngeniería. El login con las cuentas de la plataforma se agrega más adelante. |
| D7 | La interfaz visual del gestor se diseña después. Este diseño cubre la lógica interna. |

**Consecuencia aceptada de D1:** el código y los ZIP quedan al alcance de cualquiera desde GitHub. Por
eso, **antes de hacer público el repo se revisa que el historial no tenga secretos** (Parte B, tarea B5).

## 3. Estructura del repo

```
CQEING-Addins/
├─ addins/
│  ├─ Directory.Build.props        ← común a los add-ins: año de Revit → framework, referencias de la API por NuGet, salida bin/<config>/<año>/, código compartido
│  ├─ compartido/CintaCqeing.cs    ← pestaña CQEING, paneles y botones (se compila DENTRO de cada DLL, no es una DLL aparte)
│  ├─ armado-columnas/             ← practicacq.csproj, practicacq.addin, addin.json, App.cs, Logica/, Modelo/, Vistas/, Resources/
│  └─ imagenes/                    ← CQEING.Imagenes.csproj, CQEING.Imagenes.addin, addin.json, AppImagenes.cs, Api/, Almacen/…
├─ gestor/
│  ├─ CQEING.Gestor.Core/          ← lógica sin interfaz: manifiesto, GitHub, catálogo, instalación
│  ├─ CQEING.Gestor.Core.Tests/
│  └─ CQEING.Gestor/               ← app WPF + Velopack (la interfaz se diseña en el plan siguiente)
├─ tools/
│  ├─ CQEING.Publicador/           ← compila, empaqueta y escribe manifest.json de un add-in
│  ├─ CQEING.Publicador.Tests/
│  └─ desplegar-local.ps1          ← instala en ESTE equipo para desarrollar
├─ .github/workflows/  compilar.yml · publicar.yml · publicar-gestor.yml
└─ CQEING-Addins.sln
```

- La Parte A solo traslada los archivos a `addins/<id>/`. La base de compilación, el proyecto propio de Imágenes y el resto de esta estructura se arman al inicio de la Parte B.
- Cada add-in: su DLL, su `.addin` (ClientId propio), su `addin.json`, sus versiones y sus releases.
- Ambos comparten la pestaña **CQEING** del ribbon (el código ya tolera que exista).
- Imágenes conserva su namespace `practicacq.Imagenes`; su ensamblado pasa a ser `CQEING.Imagenes.dll`.
- Armado conserva el ensamblado `practicacq.dll` y su ClientId; los usuarios que lo tengan instalado por `Instalar.bat` se migran retirando ese `.addin` heredado.

## 4. Publicación

- **Etiqueta = add-in + versión:** `armado-columnas-v1.2.0`, `imagenes-v1.0.0`. Sufijo `-rc.N` = prerelease (el gestor la ignora; sirve para probar el flujo).
- La etiqueta es **anotada**; su mensaje son las notas de la versión.
- `publicar.yml` (Windows, sin Revit instalado: la API de Revit llega por NuGet) compila el add-in para cada año declarado en su `addin.json`, arma **un ZIP por año de Revit**, calcula su SHA-256, escribe `manifest.json` y crea la release con todo adjunto.
- El año → framework sale de un único lugar (`addins/Directory.Build.props`): 2026 → `net8.0-windows`, 2027 → `net10.0-windows`.

`manifest.json` (esquema 1):

```json
{
  "esquema": 1,
  "id": "armado-columnas",
  "nombre": "Armado de columnas",
  "descripcion": "Estribos, columnetas y viguetas de confinamiento.",
  "version": "1.2.0",
  "publicadoEn": "2026-10-20T15:00:00+00:00",
  "notas": "Texto del mensaje de la etiqueta",
  "heredados": ["practicacq.addin"],
  "paquetes": [
    { "revit": "2026", "framework": "net8.0-windows",  "archivo": "armado-columnas-1.2.0-revit2026.zip", "sha256": "…64 hex…" },
    { "revit": "2027", "framework": "net10.0-windows", "archivo": "armado-columnas-1.2.0-revit2027.zip", "sha256": "…" }
  ]
}
```

Cada ZIP contiene la salida de compilación (sin `.pdb`): la DLL, **exactamente un** `.addin` y `Resources/`.

## 5. Gestor: lógica interna

```
GitHub público (sin token):
  GET api.github.com/repos/utilidadcqeing-prog/CQEING-Addins/releases?per_page=100   (con ETag: si no cambió, responde 304 y no gasta cupo)
  manifest.json y ZIP por su browser_download_url (github.com; no cuenta para el límite de la API)
        │
        ▼
catálogo: últimas 5 versiones estables por add-in, con su manifest
Revit instalados (C:\Program Files\Autodesk\Revit AAAA\RevitAPI.dll)
        │
Instalar(versión, año):  descargar ZIP → verificar SHA-256 → extraer en
   %LOCALAPPDATA%\CQEING\Addins\<id>\<versión>\<año>\  → escribir
   %APPDATA%\Autodesk\Revit\Addins\<año>\CQEING.<id>.addin  (Assembly = ruta absoluta a esa carpeta)
   → retirar .addin heredados → conservar solo las 3 versiones más nuevas
```

- **Límite de GitHub sin token:** 60 consultas por hora **por IP pública** (una oficina detrás del mismo router comparte el cupo). Por eso la lista de releases se guarda con su ETag (las respuestas 304 no gastan cupo) y, si GitHub limita, se usa la última lista guardada. Las descargas de archivos no usan la API.
- **Carpeta por versión:** instalar con Revit abierto es seguro (la DLL vieja sigue bloqueada en su carpeta; la nueva va en otra). El cambio se ve al reiniciar Revit. Borrar versiones viejas bloqueadas se reintenta la próxima vez.
- **Revertir** = volver a escribir el `.addin` apuntando a otra carpeta. Si la versión está en disco, no se descarga nada.
- **Estado instalado** se lee del disco (los `.addin` `CQEING.*.addin`), no de un archivo propio.
- **Configuración de Imágenes** (`%APPDATA%\CQEING\imagenes\.env`) vive fuera de las carpetas de versión: actualizar no la toca.
- **Autoactualización del gestor:** Velopack, releases `gestor-vX.Y.Z` del mismo repo público.

Errores que el usuario debe entender: sin conexión, GitHub limitó las consultas, hash que no coincide, versión sin paquete para su año de Revit.

## 6. Fuera de alcance (fases posteriores)

- **Login con las cuentas de la plataforma CQIngeniería** (sin rol Cliente). Lo que se sabe para cuando se retome: el login deja el JWT en la cookie `sesion`; `GET /api/auth/me` ya devuelve `esAccesoExterno`; hoy `ApiKeyMiddleware` exige `X-Api-Key` a toda llamada que no venga del navegador, así que habrá que liberar `/api/auth` para la app de escritorio.
- Interfaz visual del gestor (plan siguiente).
- Configurar desde el gestor el `.env` de Imágenes (`API_URL`, `API_KEY`, claves de Cloudinary). Hoy lo hace `Instalar.ps1` preguntando por consola; con el gestor, el add-in crea su `.env` vacío y trabaja con datos de ejemplo hasta que alguien lo complete.
- Add-in «avisador» dentro de Revit, formato `.bundle`, firma de código, métricas de descargas.
- Paquete distinto para Revit 2026.5+ (.NET 10): el esquema del manifiesto ya admite el campo `framework`; se agrega si las pruebas lo exigen.
