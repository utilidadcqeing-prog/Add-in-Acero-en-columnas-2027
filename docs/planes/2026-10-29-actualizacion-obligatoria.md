# Actualización obligatoria de los add-ins

Pedido de Juan (2026-10-29), junto con la licencia (#189): al abrir Revit, todos deben tener la última versión de cada
add-in; si no, el add-in no se deja usar hasta actualizar desde el gestor. Solo aplica a las versiones que se publiquen
desde ahora en `CQEING-releases`: las copias viejas no traen este código.

## Decisiones

1. **No se cierra Revit** (Juan, 2026-10-29). Un add-in desactualizado no crea sus botones; Revit abre normal y queda el
   botón del gestor con su aviso. Así no se pierde trabajo ni se afecta a quien usa Revit sin los add-ins. Es lo mismo
   que pasa sin licencia.
2. **Sin red se deja usar, salvo que ya se supiera** (Juan, 2026-10-29). Si nunca se pudo saber que había versión nueva,
   el add-in funciona (la licencia sigue limitando a 72 h sin red). Si una consulta anterior ya la vio, sigue bloqueado
   aunque ahora no haya red: si no, desconectarse bastaría para esquivarlo.
3. **Las etiquetas esperan a esto** (Juan, 2026-10-29). La primera versión con licencia trae también la actualización
   obligatoria; si saliera antes, quien la instale nunca quedaría obligado.
4. **El gestor anota, los add-ins leen** (Claude). Cada vez que el gestor conoce la lista de releases (la ventana o el
   aviso dentro de Revit, también con la lista guardada), escribe `%LOCALAPPDATA%\CQEING\ultimas-versiones-<año>.json`
   con la última versión de cada add-in que trae paquete para ese año. Escribe aparte y reemplaza de una vez.
5. **El add-in sabe su versión por su carpeta** (Claude). El gestor instala en
   `...\CQEING\Addins\<id>\<versión>\<año>\`. Una compilación de desarrollo (`tools/desplegar-local.ps1`) apunta al repo y
   nunca se bloquea. Tras actualizar, la versión nueva ya es la última y carga sola, aunque la lista no se haya refrescado.
6. **Si la versión nueva aparece con Revit abierto** (Claude). Los add-ins cargan antes que el componente del gestor
   (`zz-`), que consulta en segundo plano. Cada add-in vigila el archivo y, si queda desactualizado, desactiva sus botones
   (los encuentra por la DLL de su comando) con un texto que dice que actualice y reinicie.
7. **Nunca tumba Revit** (Claude). Cualquier error leyendo el archivo, la carpeta o la cinta deja usar el add-in.
8. **Volver a una versión anterior** desde el gestor deja ese add-in bloqueado mientras exista una más nueva publicada:
   es la consecuencia de que actualizar sea obligatorio. Para retirar una versión mala, se publica una corrección.

9. **Con el gestor desactualizado no carga ningún add-in** (Juan, 2026-10-29). El archivo también anota la última
   versión del gestor (clave `gestor`); cada add-in lee la versión del componente del gestor que carga Revit (la carpeta
   a la que apunta `CQEING.zz-gestor-revit.addin`, `...\CQEING\Gestor\revit\<versión>\<año>\`). Sin componente tampoco
   carga: borrarlo no sirve para esquivar la regla. Uno que apunta a otra carpeta es de desarrollo y no se juzga. El
   aviso dentro de Revit muestra el gestor como una novedad más y dice que se actualiza con «Actualizar y reiniciar».
10. **Versiones de la primera publicación** (Juan, 2026-10-29): la última de cada add-in, ya con licencia; como las
    etiquetas viejas ya existen en el repo del código, cada una sube la versión menor.

11. **Gestor 0.3.1: se borra todo lo viejo y queda solo la última** (Juan, 2026-10-29). El repo del código queda
    público unos 3 días para que los gestores viejos se actualicen con la release puente; antes se borraron allí todas
    las releases viejas (etiquetas y código quedan), así que el gestor viejo no encuentra ningún add-in, solo su
    actualización. Al abrir el gestor y al abrir Revit, `LimpiezaVersionesViejas` borra las versiones anteriores a la
    primera con licencia (vigas 1.1.0, columnas 1.5.0, cuadro 1.9.0, imágenes 1.1.0) con su `.addin`, los `.addin` de
    antes del gestor y toda versión que no esté activa. Una compilación de desarrollo no se toca.
12. **Copia en el registro que nunca baja** (Juan, 2026-10-29). El gestor guarda las últimas versiones también en
    `HKCU\Software\CQEING\Gestor` y los add-ins usan la más alta entre el archivo y el registro. Borrar el archivo o
    instalar un `.exe` viejo del gestor no desbloquea nada: el componente viejo queda por debajo de lo anotado y ningún
    add-in carga, y el gestor viejo no encuentra versiones viejas para instalar. Lo que no tiene arreglo: un equipo que
    nunca abre el gestor nuevo conserva lo que ya tenía.

## Código

- `addins/compartido/ActualizacionObligatoria.cs`: formato del archivo, carpeta de instalación y comparación de
  versiones. Lo compilan los add-ins y el núcleo del gestor (una sola fuente).
- `addins/compartido/BloqueoPorVersion.cs`: la parte de Revit (no crear botones, vigilar el archivo, desactivar).
- `GestorAddins.ConsultarAsync` y `NovedadesAsync` anotan las últimas versiones.
- Cada `IExternalApplication`: `if (!BloqueoPorVersion.AlDia(app)) return Result.Succeeded;` después de la licencia.
  `TodosLosAddinsExigenLicenciaTests` vigila que ninguno se olvide.
- `VentanaAviso` dice que actualizar es obligatorio.

## Cómo probarlo

- `dotnet test CQEING-Addins.sln` (pruebas de `ActualizacionObligatoriaTests` y `GestorAddinsTests`).
- En Revit: instalar un add-in desde el gestor, escribir a mano en `ultimas-versiones-2026.json` una versión más nueva y
  abrir Revit: no aparecen sus botones. Con Revit abierto, cambiar el archivo: sus botones se desactivan. Borrar el
  archivo y reabrir: vuelven.

## Estado

- [x] Código y pruebas (2026-10-29): 1340 pruebas pasan; compila para 2026 y 2027.
- [x] Prueba en Revit 2026 (2026-10-29, con Juan): vigas instalado como lo deja el gestor en `0.0.1`.
  1. Con `0.0.2` anotada, al abrir Revit no se crea «Acero en vigas» (journal: carga la DLL de `0.0.1`, sin botón).
  2. Con `0.0.1` anotada, al reabrir Revit el botón vuelve.
  3. Con Revit abierto, al anotar `0.0.2` el botón queda gris al momento.
  Hallazgo: abrir el gestor durante la prueba actualizó vigas a `1.0.1` y apartó la `0.0.1`: es el camino normal para
  salir del bloqueo. El aviso «obligatorio» del componente del gestor no se pudo ver: el instalado es el `0.2.2` y
  `CQEING-releases` aún no tiene releases. Se verá al publicar.
- [ ] Publicar las etiquetas de los add-ins y del gestor (Tarea 8 de `2026-10-28-licencias-hoja-google.md`).
