# Convenciones de este repo

Monorepo de los add-ins de Revit de CQEING (ribbon "CQEING"): un add-in
por carpeta en `addins/`, mas el gestor de instalacion en `gestor/`.
El de armado de columnas genera refuerzo longitudinal, estribos y
cajas/grapas a partir de la geometria de la seccion. El usuario
(Luciano) no es experto en ingenieria civil ni en la API de Revit --
las explicaciones y los planes tienen que ser en lenguaje llano.

## Regla general: primero un plan .md, y se sigue al pie de la letra

Todo trabajo (issue, feature, fix o investigacion) arranca con un plan
en `docs/planes/AAAA-MM-DD-tema.md`, armado con la plantilla
`docs/planes/PLANTILLA.md`, ANTES de tocar codigo:

- El plan dice que se va a hacer, en que archivos, en que orden y como
  se prueba cada paso. Se implementa siguiendolo paso por paso, sin
  improvisar fuera de el.
- Si algo cambia (una decision del usuario, un hallazgo en Revit real,
  un error), primero se actualiza el plan (seccion Decisiones o
  Hallazgos, con fecha y por que) y despues el codigo. El plan siempre
  describe lo que hay de verdad en el codigo.
- Tiene que poder reutilizarse: alguien que no estuvo en la
  conversacion (otra persona, otra PC u otra sesion de Claude) debe
  poder retomar o repetir el trabajo solo leyendo el plan. Por eso
  lleva rutas, comandos, valores probados y evidencia (capturas en
  `docs/planes/imagenes/`), no solo conclusiones.
- Al cerrar, la seccion Estado dice que se probo, que quedo pendiente y
  que criterios de aceptacion se cumplen.

Ejemplo completo: `docs/planes/2026-10-13-addin-imagenes-por-actividad.md`.

## Regla de proceso: verificar contra Revit real antes de implementar a mano

Antes de programar a mano algo que "se ve" como un concepto propio de
Revit (geometria de estribos, empalmes/traslapos, ganchos de anclaje,
nudos, etc.), verificar primero si la API de Revit ya tiene una
herramienta nativa para ese concepto exacto -- y probarla contra un
modelo real (con un comando de diagnostico temporal si hace falta)
antes de asumir como se comporta. No usar la primera solucion que
compile; buscar si existe la solucion "oficial" de la API, y no dar
por buena ninguna geometria sin medirla contra Revit real.

Precedentes que motivaron esta regla:

- El estribo con una dobladura visible en una esquina se resolvio
  yendo a `Rebar.CreateFromRebarShape` (familia `RebarShape`) en vez
  de seguir ajustando curvas a mano con `Rebar.CreateFromCurves`
  (`docs/planes/2026-09-09-dobladura-estribo-rebarshape.md`).
- El corrimiento lateral de los traslapos se dio por bueno sin
  verificar, y salio mal (las dos barras quedaban en el mismo X/Y,
  invisibles como traslapo); se corrigio midiendo coordenadas reales
  del modelo de referencia con un comando de diagnostico temporal, no
  asumiendo (`docs/planes/2026-09-09-diagnostico-corrimiento-traslapo.md`).

## Regla de proceso: no usar API interna de Revit

Antes de usar un metodo o propiedad de la API de Revit, verificar en la
documentacion oficial que sea publico y soportado. Revit expone
miembros marcados como internos/obsoletos ("for internal use only",
"not intended to be used in your code") que **compilan sin error** y
recien fallan en tiempo de ejecucion, dentro de Revit -- o sea que el
build en verde no alcanza como comprobacion.

Que compile no es garantia. Si la documentacion no aclara si el
miembro es publico, buscar una alternativa documentada antes que
arriesgar un fallo que solo aparece al correrlo.

## Flujo de trabajo con el usuario

- Nunca hacer `git commit` ni `git push` sin pedido explicito del
  usuario en ese momento. "Haz commit" es solo commit -- avisar
  explicitamente que no se pusheo y preguntar si tambien lo quiere.
- Nunca `git add -A` -- agregar archivos especificos por nombre.
- Sin marca de agua de Claude: nada de `Co-Authored-By: Claude ...` en
  los commits ni de "Generated with Claude Code" en PRs, issues o
  comentarios.
- Antes de desplegar (`tools/desplegar-local.ps1`), verificar que Revit este
  cerrado con `Get-Process -Name Revit -ErrorAction SilentlyContinue`
  -- no confiar en que el usuario diga "ya cerre" sin comprobarlo,
  puede tardar en cerrar de verdad.
- Build de un add-in: `dotnet build addins/armado-columnas/practicacq.csproj -c Debug -p:RevitVersion=2027`.
- Deploy local: `powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027`.
- Publicar una version: etiqueta anotada `<id>-vX.Y.Z` (ver README).
- Cada cambio/investigacion relevante se documenta en un archivo nuevo
  bajo `docs/planes/AAAA-MM-DD-tema.md` (contexto, que se encontro,
  fix implementado, estado/pendiente de retest).
- Responder en espanol neutro/estandar, sin voseo.
