# Add-in-Acero-en-columnas-2027

Add-in de Revit (CQEING) para armado de columnas: genera el refuerzo
longitudinal, los estribos y las cajas/grapas a partir de la geometria de
la seccion.

## Para desarrollo (compilar y probar en tu propia maquina)

Requiere el SDK de .NET y Revit instalado. Compila e instala el add-in en
esta misma maquina, en `%APPDATA%\Autodesk\Revit\Addins\<version>\`:

```bash
.\tools\deploy.ps1
```

Por defecto compila para Revit 2026 y 2027 (salta la version que no tengas
instalada) en configuracion Debug. Ver los comentarios de
[tools/deploy.ps1](tools/deploy.ps1) para variantes (una sola version,
Release, forzar el `TargetFramework`, etc.).

## Para repartir el add-in a otro equipo (sin consola, sin SDK de .NET)

Esto es lo que hay que usar cuando alguien que **no** tiene el entorno de
desarrollo instalado necesita el add-in en su computadora.

### 1. Vos (quien compila) generás el paquete

Desde una maquina con el SDK de .NET y Revit instalado, corré:

```bash
.\tools\publish.ps1 -Zip
```

Esto compila el add-in en modo Release para cada version de Revit que
tengas instalada en tu maquina (2026, 2027) y arma
`Publicar-practicacq.zip` en la raiz del repo, listo para mandar.

> Nota: el paquete solo incluye las versiones de Revit que esten instaladas
> en la maquina donde corres `publish.ps1`. Si necesitas el paquete con
> ambas versiones (2026 y 2027) pero vos solo tenes una instalada, corré
> el script una vez en cada maquina y copiá manualmente la carpeta de la
> version que falte (p. ej. `Publicar\2027\`) adentro del `Publicar\` de la
> otra corrida antes de comprimir.

#### Si no tenés Revit 2026 instalado en tu maquina

No hace falta instalar Revit 2026 entero para poder compilar contra esa
version: alcanza con conseguir dos archivos de alguien que sí lo tenga
instalado (por ejemplo, William) y pedirle que te los copie asi, tal cual:

1. En su computadora, abrir el explorador de archivos y navegar a:
   `C:\Program Files\Autodesk\Revit 2026\`
2. Copiar estos dos archivos (nada mas) y mandartelos (mail, Drive, etc.):
   - `RevitAPI.dll`
   - `RevitAPIUI.dll`
3. En tu maquina, poner esos dos archivos juntos en una carpeta cualquiera,
   por ejemplo `C:\RevitRefs\2026\`.
4. Compilar apuntando a esa carpeta:

   ```bash
   .\tools\publish.ps1 -Versions 2026 -RevitInstallDirs @{ '2026' = 'C:\RevitRefs\2026\' } -Zip
   ```

   (La ruta que le pasas a `RevitInstallDirs` tiene que terminar en `\`.)

   Si la deteccion automatica del framework falla (falla al compilar
   mencionando "System.Runtime" o "NETSDK1005"), forzalo a mano agregando
   `-TargetFrameworks @{ '2026' = 'net8.0-windows' }` (o `net10.0-windows`
   si es Revit 2026.5+ -- pedile la version exacta a quien te mando los
   `.dll`, o probá con `net8.0-windows` primero ya que es lo mas comun).

Esto arma `Publicar\2026\` igual que si lo hubieras compilado en una
maquina con Revit 2026 instalado. Si despues necesitas el paquete completo
con 2026 y 2027 juntos, corré primero `.\tools\publish.ps1 -Versions 2027`
y despues el comando de arriba sin `-Zip` en cada uno, para juntar ambas
carpetas dentro de `Publicar\` antes de comprimir (o simplemente copiá
`Publicar\2026\` a mano dentro del zip que ya generaste para 2027).

### 2. Mandás el .zip al otro equipo

Por mail, Drive, WhatsApp, como sea. No requiere que la otra persona tenga
Visual Studio, el SDK de .NET, ni sepa usar la consola.

### 3. La otra persona instala con doble clic

En su computadora:

1. Descomprimir el `.zip` en cualquier carpeta.
2. Cerrar Revit si esta abierto.
3. Doble clic en **`Instalar.bat`**.
4. Esperar el mensaje **"Listo."** y presionar Enter para cerrar la ventana.
5. Abrir Revit. El add-in aparece en el ribbon, en la pestana **CQEING**.

El instalador (`tools/Instalar.ps1`, ejecutado por `Instalar.bat`) detecta
solo que version de Revit corresponde a cada carpeta que viene en el
paquete (2026, 2027) y copia los archivos a
`%APPDATA%\Autodesk\Revit\Addins\<version>\` -- funciona igual sin importar
que version de Revit tenga esa computadora, o si tiene ambas.

Estas instrucciones tambien quedan en `LEEME.txt` dentro del paquete, para
que la persona que instala no tenga que venir a este repo.
