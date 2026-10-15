# Gancho de la longitudinal invertido, y estribo con lado diagonal ("T1")

## Contexto

Después de comitear la tanda de UI del 11-sep (#56-#63, commits `d753351` y
`b474c67`), Luciano generó armado real en Revit 2027 (ya con licencia
funcionando) y midió dos cosas con `ComandoDiagnosticoRebar`, que la tanda
anterior había ampliado justamente para esto (reporta la orientación del
gancho de una barra vertical contra el centro de la sección):

1. **Orientación del gancho de la longitudinal (#59/#43)**: dos barras
   medidas -- una con gancho arriba (cara X), otra con gancho abajo (cara
   Y) -- las dos con la pata hacia **AFUERA** de la sección.
2. **Un estribo con dos líneas diagonales por dentro**, en un tramo con
   apariencia de nudo (barras de viga entrando por los costados). Luciano
   sospechaba un resto de alguna prueba manual suya en el modelo, pero pidió
   revisar el código antes de asumir nada.

Las dos cosas se investigaron leyendo el código real antes de tocar nada
(regla del CLAUDE.md), con un agente de exploración para el segundo punto.

## Hallazgo 1 — swap de orientación

`OrientacionesHaciaAdentro` (`Logica/GeneradorLongitudinal.cs`) calcula
`haciaDondeDobla = BasisZ × normal` y elegía `OrientacionPositiva=Left` /
`OrientacionNegativa=Right` según el signo de la proyección hacia el centro
de la sección.

Con las dos mediciones reales (una barra de cara X, normal=BasisX; otra de
cara Y, normal=BasisY):

- Cara X: `haciaDondeDobla=(0,1,0)`, proyección negativa → usó `Right`. La
  pata midió +Y real → `Right` dobla hacia **+haciaDondeDobla**.
- Cara Y: `haciaDondeDobla=(-1,0,0)`, proyección positiva → usó `Left`. La
  pata midió +X real (= −haciaDondeDobla) → `Left` dobla hacia
  **−haciaDondeDobla**.

Las dos mediciones piden el mismo swap, independientemente del eje: `Left`/
`Right` de Revit son relativos al par (tangente, normal) local de cada barra,
no a los ejes globales del mundo.

**Fix**: se intercambiaron los valores de las dos constantes
(`OrientacionPositiva = Right`, `OrientacionNegativa = Left`). El comentario
de `OrientacionesHaciaAdentro` (que decía "SIN confirmar todavía contra Revit
real") se actualizó con la medición y el razonamiento.

## Hallazgo 2 — `BuscarFormaEstribo` elige por nombre, sin validar geometría

`GeometriaSeccion.BuscarFormaEstribo` filtra por `RebarStyle.StirrupTie` +
coincidencia de **nombre** (`Contains("T1")`, `== "51"`, `Contains("M_T1")`,
`Contains("Estribo")`, `Contains("Stirrup")`) -- nunca mira la geometría real
de la forma, a diferencia de `BuscarFormaGrapa`, que sí valida estructura
(`UnSoloSegmento`).

Del documento real, con decenas de formas `StirrupTie` ("S1".."S6", "T1",
"T2", "T6".."T9", "21","31","41","44","75", "Forma de armadura 1".."167",
"Rebar Shape 1"), la **única** que hace match con cualquiera de esas
condiciones es "T1" -- el add-in la usa de forma determinística para todos
los estribos de este documento.

El patrón "T1, T2, T6-T9" es típico de catálogos de detallado sísmico
(tipos de estribo con crucetas de confinamiento según cuántas ramas necesita
la sección) -- muy distinto de un simple rectángulo perimetral, que es lo
único que este add-in sabe escalar con `CreateFromRebarShape`+`ScaleToBox`
(las crucetas/grapas las genera aparte, como `Rebar` independientes vía
`CrearGrapa`). Encaja con la imagen: rectángulo bien formado por fuera (la
parte perimetral de "T1", escalada bien) + dos diagonales por dentro (la
pata de cruceta que "T1" trae incorporada, escalada junto con el resto -- de
ahí el codo visible en una de ellas).

Descartado con confianza alta, leyendo el código real:
- **Creación duplicada**: el camino de forma y el de curvas son mutuamente
  excluyentes por diseño (`GeneradorArmado.cs`, un solo `if (estribo is
  null)`).
- **Interferencia de cajas/grapas en el nudo**: se saltean explícitamente
  los tramos `DentroDeViga` (`GeneradorLongitudinal.cs`), esa línea ya
  existía antes del cambio del #58.
- **Código de prueba olvidado**: nada encontrado en `Logica/`, `Vistas/`,
  `Modelo/` ni en `docs/planes/*.md`.

### El fix: validar la geometría creada, no el nombre

En vez de afinar las reglas de nombre (frágil, específico de este
documento), se valida la geometría que Revit devolvió después de crear y
escalar el `Rebar` candidato -- mismo espíritu que `BuscarFormaGrapa`.

Nuevo `GeometriaSeccion.TieneLadoDiagonal(Rebar)`: lee
`GetCenterlineCurves` (sin ganchos ni radios) y si algún lado recto se mueve
en X **y** en Y a la vez (más de 1mm, para no confundir con ruido de punto
flotante), es una diagonal. `ScaleToBox` es un escalado afín -- si aparece
una diagonal, viene heredada de la forma elegida, no de una distorsión del
escalado.

Se llama en los dos lugares que usan `CreateFromRebarShape` para el contorno
del estribo:

- `GeneradorArmado.CrearDesdeForma`: si da diagonal, se borra el `Rebar`
  (best-effort) y se devuelve `null` -- el respaldo YA EXISTÍA (`CrearGrupo`
  cae a `Rebar.CreateFromCurves` cuando `CrearDesdeForma` devuelve `null`),
  no hizo falta ningún camino nuevo.
- `GeneradorLongitudinal.CrearCajaDesdeForma`: mismo chequeo. Acá no hay
  respaldo a `CreateFromCurves` (se sacó a propósito el 10-sep, dejaba
  `Rebar` sin resolver en el modelo) -- si sale diagonal, la caja
  simplemente no se crea, mismo `Descartar` + aviso que ya usan los demás
  casos de rechazo de esa función. Se refactorizó el tramo final de
  `CrearCajaDesdeForma` para que el seteo de gancho/orientación (antes con
  dos `return caja;` separados según si había gancho) converja en un solo
  punto de salida, donde se hace la validación una sola vez.

Para no repetir crear+escalar+medir+borrar en cada estribo/caja de la corrida
una vez que ya se sabe que la forma es mala, y para no repetir el mismo
aviso decenas de veces: un `HashSet<ElementId> formasRechazadas` nuevo,
declarado una vez en cada `Generar` (mismo patrón que el `ganchosYaAvisados`
que ya existía), pasado hacia abajo por la misma cadena de llamadas que ya
usa `formaEstribo`. Si `forma.Id` ya está en el set, ni se intenta crear.

No se tocó `BuscarFormaEstribo` en sí -- la validación geométrica decide si
el resultado sirve, sin importar cuál nombre lo produjo, así que el fix
protege igual a cualquier documento futuro con otra forma "T1"-like.

## Hallazgo 3 (infraestructura) — `Rev2026/` sin excluir contaminaba el build de 2027

Al verificar el deploy contra Revit 2027, medí (no asumí) la versión de
ensamblado que `practicacq.dll` referencia de verdad
(`[Reflection.Assembly]::ReflectionOnlyLoadFrom(...).GetReferencedAssemblies()`)
y encontré `RevitAPI, Version=26.3.0.0` -- la de **Revit 2026**, no la
27.2.0.0 real de `C:\Program Files\Autodesk\Revit 2027\`, a pesar de que
`dotnet build -p:RevitVersion=2027` **compilaba sin ningún error**. Un build
en verde no era garantía (mismo espíritu que la regla del `CLAUDE.md` sobre
la API de Revit, aplicada acá a la infraestructura del build).

Causa, confirmada con `--getItem:None`: `Rev2026/` (agregado a este repo
para poder compilar contra Revit 2026 sin tenerlo instalado, ver
`.gitignore`) no estaba en `DefaultItemExcludes` de `practicacq.csproj`, así
que el SDK lo barría como `<None>` del proyecto. Eso contaminaba la
resolución de referencias del paso interno "wpftmp" del compilador de WPF
(que resuelve referencias por su cuenta, sin necesariamente respetar el
`HintPath` explícito del `.csproj`), y terminaba enlazando el ensamblado
final contra `Rev2026\RevitAPI.dll` sin importar qué `RevitVersion` se
pidiera.

**Fix**: se agregó `Rev2026/**;Rev2026\**` a `DefaultItemExcludes`, mismo
patrón que las demás carpetas ajenas al proyecto (`CQEING/**`, `Dymanos de
referencia/**`, etc.). Verificado con un rebuild completamente limpio
(`obj`/`bin` borrados, `dotnet build-server shutdown` antes de cada intento
para descartar reutilización de procesos) que:
- `-p:RevitVersion=2027` (sin `RevitInstallDir` explícito) referencia
  27.2.0.0.
- `-p:RevitVersion=2026 -p:RevitInstallDir=Rev2026\` referencia 26.3.0.0
  (la correcta para esa carpeta).

El `.dll` desplegado en
`%APPDATA%\Autodesk\Revit\Addins\2027\practicacq.dll` se volvió a verificar
de la misma forma después del deploy final: 27.2.0.0, correcto.

## Estado

Compilado sin errores contra Revit 2027 (verificado además que referencia
la versión de ensamblado correcta, 27.2.0.0, no solo que compile), contra
Revit 2026 (con `Rev2026/RevitAPI.dll` local, referencia 26.3.0.0) y contra
`tools/PreviewUI`. Desplegado a Revit 2027.

## Pendiente de retest en Revit real

1. Regenerar la misma columna/nudo que mostró la diagonal: el estribo tiene
   que salir como un rectángulo simple. Si "T1" sigue siendo la forma
   elegida, debe aparecer el aviso una sola vez por corrida.
2. Correr `ComandoDiagnosticoRebar` de nuevo sobre las mismas dos barras ya
   medidas (deben decir "ADENTRO" ahora) y sobre una barra de **esquina**
   (mezcla X e Y, todavía no probada).
3. Confirmar que la pieza superior de una barra con traslapo real (después
   de `SpliceRebar`) sigue mostrando el gancho correcto -- pendiente desde
   la tanda anterior.
