# Convenciones de este repo

Add-in de Revit (CQEING, ribbon "CQEING") para armado de columnas de
hormigon armado -- genera refuerzo longitudinal, estribos y
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

## Flujo de trabajo con el usuario

- Nunca hacer `git commit` ni `git push` sin pedido explicito del
  usuario en ese momento. "Haz commit" es solo commit -- avisar
  explicitamente que no se pusheo y preguntar si tambien lo quiere.
- Nunca `git add -A` -- agregar archivos especificos por nombre.
- Antes de desplegar (`tools/deploy.ps1`), verificar que Revit este
  cerrado con `Get-Process -Name Revit -ErrorAction SilentlyContinue`
  -- no confiar en que el usuario diga "ya cerre" sin comprobarlo,
  puede tardar en cerrar de verdad.
- Comando de build: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027`.
- Comando de deploy: `powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Versions 2027 -TargetFramework net10.0-windows`.
- Cada cambio/investigacion relevante se documenta en un archivo nuevo
  bajo `docs/planes/AAAA-MM-DD-tema.md` (contexto, que se encontro,
  fix implementado, estado/pendiente de retest).
- Responder en espanol neutro/estandar, sin voseo.
