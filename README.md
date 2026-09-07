# Add-in-Acero-en-columnas-2027

Add-in de Revit (CQEING) para armado de columnas: genera el refuerzo
longitudinal, los estribos y las cajas/grapas a partir de la geometria de
la seccion.

## Como instalar el add-in

> **Importante:** este repositorio tiene el *codigo fuente*, no el
> instalador. El `Instalar.bat` que esta en `tools/` de este repo **no
> sirve por si solo** -- no tiene los `.dll` compilados al lado, asi que si
> le haces doble clic ahi va a fallar diciendo que no encuentra las
> carpetas de version. Lo que hay que usar es el **paquete ya compilado**
> (un `.zip` separado, que no se sube a este repo -- ver mas abajo por
> que), que trae su propia copia de `Instalar.bat` junto con los binarios.

No hace falta saber nada de consola ni de programacion para instalar.

1. Descargar `Publicar-practicacq.zip` de la
   [ultima Release](../../releases/latest) de este repositorio (seccion
   "Assets" de la pagina de la release).
2. Descomprimir el `.zip` **completo** en cualquier carpeta.
3. Cerrar Revit si esta abierto.
4. Doble clic en **`Instalar.bat`** (el que esta DENTRO de la carpeta que
   acabas de descomprimir, con las carpetas `2026\`/`2027\` al lado -- no
   el de este repositorio).
5. Esperar el mensaje **"Listo."** y presionar Enter para cerrar la ventana.
6. Abrir Revit. El add-in aparece en el ribbon, en la pestana **CQEING**.

El instalador detecta solo que version de Revit corresponde a cada carpeta
que viene en el paquete (2026, 2027) y copia los archivos donde va --
funciona igual sin importar que version de Revit tenga esa computadora, o
si tiene ambas.

Si algo falla durante la instalacion (por ejemplo, Revit estaba abierto),
cerrar Revit y volver a correr `Instalar.bat`. No hace falta ejecutarlo
como administrador (solo escribe en una carpeta del usuario); si Windows no
lo deja abrir con doble clic normal, es porque viene de un `.zip`
descargado -- antes de descomprimir, clic derecho en el `.zip` >
Propiedades > "Desbloquear" > Aceptar.

Estas mismas instrucciones quedan tambien en `LEEME.txt`, adentro del
paquete que se entrega -- para que quien instala no tenga que venir a este
repositorio.

### Por que el paquete no esta en el arbol del repositorio

`Publicar-practicacq.zip` (y la carpeta `Publicar\` que lo arma) son
binarios generados a partir del codigo fuente, no codigo fuente en si --
por eso estan en `.gitignore`, igual que `bin\`/`obj\`. Un binario que
cambia entero cada vez infla el historial de git para siempre (no se puede
"actualizar" sin dejar todas las versiones viejas completas guardadas
adentro). Por eso se sube como **asset de una
[GitHub Release](../../releases)** en vez de comitearlo: queda versionado,
descargable, y no pesa en el repo.

### Como publicar una nueva version (para quien compila)

Cada vez que se cambia el codigo y hace falta un paquete nuevo:

```bash
.\tools\publish.ps1 -Zip
git tag -a vX.Y.Z -m "que cambio"
git push origin vX.Y.Z
gh release create vX.Y.Z Publicar-practicacq.zip --title "Add-in de Armado de Columnas vX.Y.Z" --notes "que cambio"
```

(`gh` es la [CLI de GitHub](https://cli.github.com/); necesita estar
autenticada con `gh auth login` una sola vez por maquina.)
