# Plan: gancho de anclaje en zapata (longitudinal) + fix real de nudo viga/vigueta

## Contexto

Después de implementar la longitudinal continua y el corrimiento lateral en traslapos, quedan dos pendientes que el usuario priorizó explícitamente para esta sesión, en este orden:

1. **Gancho de anclaje en la zapata** (issue [#37](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/37)) — requisito normativo explícito de Carlos Quintero (ingeniero estructural): el gancho inferior de la barra longitudinal, donde ancla dentro de la zapata, debe existir (hoy la longitudinal es 100% recta, sin ningún gancho) y debe ir **siempre hacia adentro**, nunca hacia afuera — sin excepción según la normativa colombiana. Es el bloqueante real para que Carlos pueda validar el elemento de referencia que pidió.
2. La dobladura visible en el estribo principal (el "quiebre" a mitad de lado, efecto secundario de un fix anterior) — el usuario la marcó como "lo siguiente", después de esto. **No entró en el alcance de este plan.**

Además, el usuario señaló que la transcripción del 8-sep habla de que los nudos (tramos tipo `DentroDeViga`) deben salir donde hay una **viga** real, no donde hay una **vigueta** (elemento más chico). Al explorar el código para confirmarlo, se encontró que **esto no estaba implementado** — un documento de una sesión anterior (`docs/planes/2026-09-08-reunion-traslapos-ganchos-normativa.md`) afirmaba incorrectamente que sí lo estaba. Ese documento se corrige una vez confirmado este fix en Revit real.

## Hallazgo A: nudo por viga/vigueta — gap real confirmado y corregido

`Logica/LectorContexto.cs`, método `BandasDeViga`: generaba un nudo para **cualquier** elemento `OST_StructuralFraming` que tocara la columna, sin mirar nunca su sección. El umbral de 20cm que existía en el código (`ColumnaResumen.UmbralMetros`) era para clasificar la COLUMNA como "columneta" (issue #33), una regla distinta no conectada a esto.

**Fix implementado**: `BandasDeViga` ahora calcula la altura real de la viga (extensión Z del sólido, ya se leía) y una aproximación de su ancho (el menor de los dos lados de su huella en planta — válido en cualquier grilla ortogonal normal, ya que el lado más largo de esa huella es su longitud/tramo, no su sección) y descarta la viga como generadora de nudo (no crea `TipoTramo.DentroDeViga`, ese tramo queda libre/continuo) si base O altura < 20cm — mismo umbral y mismo criterio "OR" que `ColumnaResumen.UmbralMetros`, reutilizado directamente.

## Hallazgo B: gancho de anclaje en zapata — implementado

- `Modelo/ConfigLongitudinal.cs`: nuevo campo `TipoGanchoLongitudinalNombre` (string, igual patrón que `ConfigTramo.TipoGanchoNombre`).
- `Vistas/VentanaConfiguradorEstribos.xaml`/`.xaml.cs`: nuevo combo "Gancho de anclaje en zapata (issue #37)" en el panel de acero longitudinal, mismo patrón que el resto de combos de esa sección.
- `Logica/GeneradorLongitudinal.cs`:
  - `GenerarColumna` resuelve el gancho por nombre (no bloqueante: si falta, avisa y esa barra sale recta, igual que hoy).
  - `CrearBarraConTraslapos` recibe el id de la zapata y el gancho resuelto, y solo se los pasa a `CrearBarraVertical` cuando el segmento que está creando es justo el hosteado en la zapata (identificado por `hostEntry.IdHost == idZapata`, el host sintético que ya se inserta desde el fix de la longitudinal continua).
  - `CrearBarraVertical` arma el `BarTerminationsData` con `HookTypeIdAtStart` (el extremo inferior, ya que las líneas siempre van de Z bajo a Z alto) solo cuando le llega un gancho — el extremo superior y el resto de los segmentos de la barra siguen sin gancho, igual que antes.

**Sin confirmar todavía**: la orientación (`RebarTerminationOrientation.Left`, primer intento) — no hay ninguna convención ya probada en el código para "hacia adentro" en una barra vertical (los únicos usos existentes son loops horizontales de estribos, geometría distinta). Hay que mirarlo en Revit real y, si sale hacia afuera, cambiar a `Right` — puede que el signo correcto ni siquiera sea el mismo para las 4 esquinas y las caras, dado que "adentro" es una dirección distinta en cada posición.

## Estado

Compilado. Desplegado pendiente de que el usuario cierre Revit.

## Verificación pendiente

- **Nudo viga/vigueta**: en un modelo real con una vigueta (base o altura < 20cm) cruzando una columna, confirmar que ya NO aparece como tramo tipo nudo, y que una viga normal (≥20cm en ambas dimensiones) sigue generando nudo igual que antes.
- **Gancho de zapata**: generar longitudinal en una columna con zapata, acercarse en 3D al extremo inferior de una barra y confirmar que tiene gancho (no recto) y que apunta hacia ADENTRO — en las 4 esquinas y en las barras de cara, no solo en una posición. Si alguna sale hacia afuera, avisar para ajustar la orientación de esa posición específica.

Una vez confirmados ambos, corregir la afirmación falsa en `docs/planes/2026-09-08-reunion-traslapos-ganchos-normativa.md` sobre viguetas.
