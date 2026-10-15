# Guía: armar el paquete y publicar una release

Paso a paso, desde cero, para generar `Publicar-practicacq.zip` con el
add-in compilado para **Revit 2026 y 2027** y subirlo como release a GitHub
para que el resto del equipo lo descargue e instale.

Escrita el 2026-10-06. Los comandos de compilación de esta guía salen de
leer `tools/publish.ps1` y `practicacq.csproj`; el paso 4 no se volvió a
ejecutar al escribirla, así que si algo cambió en esos archivos, mandan
ellos.

## Idea general (qué es cada cosa)

| Cosa | Para qué sirve |
|---|---|
| `tools/deploy.ps1` | Compila e instala el add-in **en esta máquina**, para probar. No genera nada para repartir. |
| `tools/publish.ps1` | Compila en Release y arma la carpeta `Publicar\` y el `.zip` **para repartir**. No instala nada. |
| `Rev2026\` | Carpeta en la raíz del repo con `RevitAPI.dll` y `RevitAPIUI.dll` de Revit 2026. Permite compilar para 2026 sin tener Revit 2026 instalado. |
| Tag (`v1.2.0`) | Etiqueta de git que marca qué commit corresponde a esa versión. |
| Release de GitHub | Página del repo donde queda el `.zip` colgado para descargar. |

El `.zip` y la carpeta `Publicar\` **no se comitean** (están en
`.gitignore`): el zip viaja solo como archivo adjunto de la release.

## Requisitos (se revisan una vez por máquina)

1. **SDK de .NET 10** instalado. Comprobar:

   ```powershell
   dotnet --version
   ```

   Debe mostrar `10.x`.

2. **Revit 2027 instalado** en `C:\Program Files\Autodesk\Revit 2027\`
   (de ahí se toman los DLL para compilar la versión 2027).

3. **Carpeta `Rev2026\`** en la raíz del repo, con estos dos archivos:

   ```
   C:\Proyecto\addins\Rev2026\RevitAPI.dll
   C:\Proyecto\addins\Rev2026\RevitAPIUI.dll
   ```

   Esta carpeta está en `.gitignore`, así que **no viene al clonar el
   repo**. Si falta (máquina nueva), hay que copiar esos dos archivos
   desde una computadora que tenga Revit 2026 instalado
   (`C:\Program Files\Autodesk\Revit 2026\`) y ponerlos en `Rev2026\`.

4. **CLI de GitHub (`gh`) autenticada**. Comprobar:

   ```powershell
   gh auth status
   ```

   Si dice que no hay sesión, correr `gh auth login` y seguir los pasos.

## Paso 1 — Abrir PowerShell en la carpeta del repo

Abrir PowerShell y ubicarse en el repo:

```powershell
cd C:\Proyecto\addins
```

Después, habilitar la ejecución de scripts **para esa ventana** (hay que
repetirlo cada vez que se abre una ventana nueva de PowerShell; no cambia
nada del sistema de forma permanente):

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

Sin esto, el paso 4 falla con "No se puede cargar el archivo ...
publish.ps1 porque la ejecución de scripts está deshabilitada en este
sistema".

## Paso 2 — Dejar el código listo

El paquete se compila con **lo que hay en la carpeta en ese momento**, y el
tag marca **el commit actual**. Para que las dos cosas coincidan:

```powershell
git status
```

- No debería haber cambios sin comitear que se quieran publicar (ni
  cambios de prueba que NO se quieran publicar, como un comando de
  diagnóstico temporal).
- Lo que se va a publicar tiene que estar comiteado y pusheado.

## Paso 3 — Elegir el número de versión

Ver cuál fue la última:

```powershell
gh release list --limit 5
```

Elegir el siguiente número `vX.Y.Z`:

- Arreglos chicos: sube el último número (`v1.1.0` -> `v1.1.1`).
- Funciones nuevas: sube el del medio (`v1.1.0` -> `v1.2.0`).
- Cambio grande o incompatible: sube el primero (`v1.1.0` -> `v2.0.0`).

## Paso 4 — Armar el paquete (2026 + 2027)

Un solo comando, en una sola línea:

```powershell
.\tools\publish.ps1 -Zip -RevitInstallDirs @{ '2026' = 'C:\Proyecto\addins\Rev2026\' } -TargetFrameworks @{ '2026' = 'net8.0-windows'; '2027' = 'net10.0-windows' }
```

Qué hace cada parte:

- `-Zip`: además de la carpeta `Publicar\`, genera el `.zip`.
- `-RevitInstallDirs @{ '2026' = '...\Rev2026\' }`: le dice dónde están
  los DLL de Revit 2026. **La ruta tiene que terminar en `\`.** Para 2027
  no hace falta: usa el Revit 2027 instalado.
- `-TargetFrameworks`: fija la versión de .NET de cada Revit. Revit 2026
  (el de `Rev2026\`, versión 26.3) usa .NET 8; Revit 2027 usa .NET 10. Se
  pasa a mano porque la detección automática del script no es confiable
  en esta máquina.

El script borra la carpeta `Publicar\` anterior, compila las dos versiones
y al final muestra:

```
Listo. Paquete comprimido en 'C:\Proyecto\addins\Publicar-practicacq.zip'
```

No hace falta cerrar Revit para este paso (no toca la carpeta de Add-ins).

## Paso 5 — Revisar que el paquete salió completo

En la salida del script deben aparecer **dos** líneas verdes:

```
-> Empaquetado Revit 2026 en '...\Publicar\2026'
-> Empaquetado Revit 2027 en '...\Publicar\2027'
```

Si aparece "Se salta esta versión", el paquete quedó incompleto: no
publicarlo y revisar la sección de problemas más abajo.

La carpeta `Publicar\` debe quedar así:

```
Publicar\
  2026\  practicacq.dll, practicacq.addin, Resources\*.png
  2027\  practicacq.dll, practicacq.addin, Resources\*.png
  Instalar.bat
  Instalar.ps1
  LEEME.txt
```

Recomendado antes de publicar: probar el paquete de verdad. Cerrar Revit,
hacer doble clic en `Publicar\Instalar.bat`, abrir Revit 2027 y comprobar
que la pestaña CQEING aparece y los comandos funcionan.

## Paso 6 — Crear y subir el tag

Reemplazar `vX.Y.Z` por el número elegido y el texto por un resumen corto:

```powershell
git tag -a vX.Y.Z -m "que cambio"
```

```powershell
git push origin vX.Y.Z
```

## Paso 7 — Crear la release con el zip

```powershell
gh release create vX.Y.Z Publicar-practicacq.zip --title "Add-in de Armado de Columnas vX.Y.Z" --notes "que cambio"
```

Al terminar muestra la dirección de la release. Abrirla y comprobar que en
"Assets" figura `Publicar-practicacq.zip`.

## Paso 8 — Avisar al equipo

Instrucciones para quien instala (son las mismas de `LEEME.txt`, que va
dentro del zip):

1. Descargar `Publicar-practicacq.zip` de la última release (sección
   "Assets").
2. Descomprimirlo **completo** en cualquier carpeta.
3. Cerrar Revit.
4. Doble clic en `Instalar.bat`.
5. Esperar "Listo.", presionar Enter y abrir Revit: pestaña CQEING.

## Si algo sale mal

| Síntoma | Qué hacer |
|---|---|
| "No se encontró RevitAPI.dll en '...Rev2026\\'" | Falta la carpeta `Rev2026\` o sus dos DLL (ver Requisitos, punto 3), o la ruta no termina en `\`. |
| "No se encontró Revit 2027 ... Se salta" | Revit 2027 no está instalado en esta máquina. Compilar desde una que lo tenga, o conseguir sus dos DLL y pasarlos también por `-RevitInstallDirs`. |
| Errores de compilación que mencionan `System.Runtime` o versiones de .NET | El framework no coincide con los DLL. Revisar `-TargetFrameworks`: `net8.0-windows` para Revit 2026.0-2026.4, `net10.0-windows` para 2026.5+ y 2027. |
| Error `NETSDK1005` | Borrar la carpeta `obj\` de la raíz del repo y volver a correr el paso 4. |
| `git tag` dice que el tag ya existe | Ese número ya se usó. Elegir el siguiente. |
| Se subió un zip equivocado a la release | Rearmar el paquete y reemplazar el archivo: `gh release upload vX.Y.Z Publicar-practicacq.zip --clobber`. |
| A quien instala le dice que no encuentra las carpetas de versión | Corrió el `Instalar.bat` sin descomprimir todo el zip, o el de `tools\` del repo en vez del que viene dentro del zip. |

## Aparte: instalar en esta máquina para probar (deploy)

No forma parte de la release; es para probar cambios mientras se
desarrolla.

1. Cerrar Revit y comprobar que realmente cerró (no debe mostrar nada):

   ```powershell
   Get-Process -Name Revit -ErrorAction SilentlyContinue
   ```

2. Compilar e instalar:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Versions 2027 -TargetFramework net10.0-windows
   ```

3. Abrir Revit y buscar la pestaña CQEING.
