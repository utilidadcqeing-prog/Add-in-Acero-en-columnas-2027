# Corrimiento del traslapo: paralelo a la cara de cada barra

## Contexto

En los traslapos cada barra sale de a pares (la barra y la que se le
traslapa). El usuario reportó, con un dibujo del antes/después, que el par
se corría siempre en el **mismo eje horizontal para todas las barras**:

- en las barras de la cara de **arriba/abajo** en planta (las de
  `BarrasPorCaraX`) ese corrimiento cae **paralelo** a su borde y se ve bien;
- en las de las caras **laterales** (las de `BarrasPorCaraY`) cae
  **perpendicular**, y la segunda barra del par **se despega del borde y se
  mete hacia adentro del hormigón**.

Lo que pidió: que el par se corra siempre **a lo largo de la cara donde apoya
esa barra**, de modo que las dos queden pegadas a su borde. Las de esquina,
igual que hoy (en su dibujo el par de esquina no cambia).

Nota de proceso: la primera hipótesis fue que la columna estaba girada en
planta (la captura inicial venía de una vista 3D en perspectiva y lo parecía),
y el usuario la descartó. El dibujo esquemático fue lo que permitió cerrar el
diagnóstico sin adivinar.

## Hallazgo: hay dos caminos que producen el corrimiento

**1. Empalme formal de Revit** — `RebarSpliceUtils.SpliceRebar`, en
`IntentarPartirConSplice`. Se usa cuando el documento tiene un
`RebarSpliceType` cargado y hay un solo traslapo real dentro del host. Acá el
corrimiento **lo calcula Revit**, y sale a lo largo de la **normal de la
barra**. Esa normal era `XYZ.BasisX`, **fija para todas las barras** — de ahí
que todas se corrieran en el mismo eje.

**2. Corrimiento a mano** — la función local `Corrimiento` en `GenerarColumna`.
Se usa cuando no hay tipo de empalme cargado, o cuando hay 2+ traslapos reales
en el mismo host. Devolvía siempre `(0, ±diámetro)`, también un eje único para
todas.

Los dos tenían el mismo error de fondo: **un eje único para todas las barras,
en vez de un eje por cara.**

### Corrección de un dato anterior

`docs/planes/2026-09-09-diagnostico-corrimiento-traslapo.md` había concluido
"el corrimiento siempre va en Y, sin importar la cara", a partir de dos
mediciones sobre el modelo de referencia de la empresa. Pero las dos
mediciones dieron **exactamente la misma firma** (misma X, Y corrida ~1.9cm),
lo que encaja mejor con que las dos barras fueran de la misma cara y no de
caras distintas, como se había supuesto al elegirlas. Esa conclusión queda
corregida por este documento.

## Fix implementado

`Logica/GeneradorLongitudinal.cs`:

- `Corrimiento` ahora recibe **cuál es el eje a lo largo de la cara** de esa
  posición (`correrEnY`) y la coordenada sobre ese eje, y devuelve el
  desplazamiento sobre él, con el signo hacia el centro de la sección para que
  la barra corrida nunca se pase del borde. Cara X → corre en X; cara Y →
  corre en Y; esquinas → en Y, igual que antes.
- `CrearBarraVertical` recibe la **normal** de la barra en vez de usar
  `XYZ.BasisX` fija, y `CrearBarraConTraslapos` la propaga desde donde se
  arma cada posición: `BasisX` para esquinas y cara X (sin cambio de
  comportamiento), `BasisY` para las de cara Y. Así el corrimiento que calcula
  Revit en el camino del empalme formal también queda paralelo a la cara.
  `BasisY` sigue siendo perpendicular a una barra vertical, así que no
  reintroduce el fallo viejo de `CreateFromCurves` devolviendo `null`.

## Estado

Compilado (`dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027`,
0 errores). Desplegado a Revit 2027.

## Verificación pendiente (retest del usuario)

- **Lo reportado**: generar una columna con traslapo y mirar la sección desde
  arriba. Los pares de las caras laterales tienen que quedar corridos
  verticalmente, con las dos barras pegadas a su borde (dibujo de la derecha
  del usuario).
- **Regresión**: las barras de arriba/abajo y las de esquina no deben cambiar
  respecto de como salían antes.
- **Gancho de anclaje en la zapata** (#37) en las barras de cara Y: la
  orientación del gancho se interpreta respecto del plano de la barra, y ese
  plano cambió justo en esas barras. Confirmar que sigue apuntando hacia
  adentro.
- Con el botón "Diagnóstico de Rebar", medir un par de traslapo de una barra
  lateral y confirmar que la componente perpendicular a su cara es ~0.

## Fuera de alcance

- Columnas **giradas** en planta: todo el generador usa el rectángulo
  envolvente en ejes globales (`GeometriaSeccion.Huella`), así que una columna
  girada quedaría mal armada. Limitación ya conocida y documentada a propósito
  (ver `docs/planes/2026-09-09-selector-visual-columnas-y-longitudinal-continua.md`);
  conviene abrirle un issue aparte.
- El caso de **2+ traslapos reales en el mismo host** sigue cayendo al
  corrimiento a mano y sigue sin verificarse contra Revit real (ver
  `docs/planes/2026-09-09-empalmes-rebarspliceutils.md`).
