# Plan: dobladura visible en el estribo principal (quiebre de esquina)

## Contexto

El estribo principal (y la "caja" de estribos menores) mostraba una dobladura diagonal visible cerca de una esquina que no aparece en el modelo de referencia de William. Se investigó a fondo (código actual + historial de git + el informe técnico `Analisis_API_Estribos.md`, recuperado del historial de un repo anidado en `Add-ins de referencia/`) para reconstruir la causa real, no solo lo que decían los comentarios actuales.

## Qué se encontró

Dos bugs distintos, descubiertos en dos momentos, y el código anterior era un compromiso entre ambos:

1. **Bug original** (documentado en `Analisis_API_Estribos.md`): cuando `Rebar.CreateFromCurves` arma un lazo cerrado de estribo con ganchos, Revit mete un fillet de 90° en los 3 vértices donde las curvas tienen continuidad — pero en el vértice donde arranca y cierra el lazo, ese fillet se reemplaza por la lógica del gancho. Si ese vértice de cierre es una esquina real, esa esquina se queda sin el fillet que debería atrapar la barra longitudinal de ahí — issue #31 original.
2. **Bug introducido al arreglar el #1** (`cd0f6e0`): partir un lado exactamente por la mitad (sin desviar) para que el cierre no cayera en una esquina. Confirmado sobre Revit real (`9206731`): dos curvas exactamente colineales ahí hacen que `Rebar.CreateFromCurves` devuelva null en secciones no cuadradas (`CurvesCollinear`).
3. **Estado previo** (`2b86fce`): se reintrodujo el corte con un desvío de 1.5cm para evitar el crash — evita el #2, pero ese desvío es la dobladura visible. Compromiso funcional correcto (las 4 esquinas reales conservan su fillet), costo puramente visual.

"Cerrar en una esquina real sin partir nada" no alcanza: cualquier esquina que se elija como cierre pierde su fillet iguial — solo cambia cuál esquina queda expuesta.

## Fix implementado

Mismo enfoque que usa el otro add-in de referencia del equipo (`Add-ins de referencia/Addin_Acero_Columnas/Services/ShapeBasedStirrupService.cs`) y que recomienda Autodesk: crear el estribo desde una familia `RebarShape` (`Rebar.CreateFromRebarShape` + `ScaleToBox`) en vez de armar curvas a mano — la geometría de esquinas/ganchos la resuelve la propia familia, no un cálculo nuestro.

- `Logica/GeometriaSeccion.cs`: nuevo `BuscarFormaEstribo(doc)` (busca una `RebarShape` con `RebarStyle.StirrupTie` por nombre — "T1"/"51"/"M_T1"/"Estribo"/"Stirrup", mismo criterio que el add-in de referencia), y una sobrecarga de `Huella` para `IList<Curve>` (además de la que ya existía para `CurveLoop`).
- `Logica/GeneradorArmado.cs` (`CrearGrupo`) y `Logica/GeneradorLongitudinal.cs` (`CrearCaja`): si el documento tiene una forma válida, se usa `CrearDesdeForma`/`CrearCajaDesdeForma` (nuevo helper en cada archivo, mismo patrón): `CreateFromRebarShape` + `ScaleToBox` al tamaño real de la sección + `SetHookTypeId`/`SetTerminationOrientation` (la API de Revit 2027 no tiene `SetHookOrientation`/`RebarHookOrientation`, que sí existía en la versión que usa el add-in de referencia -- se usa el reemplazo moderno, `Rebar.SetTerminationOrientation(int, RebarTerminationOrientation)`).
- Si no hay forma cargada, o si `CrearDesdeForma` falla por cualquier motivo (con aviso, no excepción): cae al camino de siempre (`ConQuiebreEnMedioDeLado` + `CreateFromCurves`), sin romper nada de lo que ya funcionaba.

## Estado

Compilado y desplegado. Pendiente de retest del usuario:
- Si el modelo tiene una `RebarShape` de estribo cerrado cargada: confirmar que las 4 esquinas del estribo ya no muestran la dobladura.
- Si no la tiene: confirmar que sigue funcionando igual que antes (con la dobladura conocida, ahora con un aviso nuevo explicando que hace falta cargar una forma para el resultado limpio).

No hay forma de saber de antemano si el modelo de prueba tiene una forma así cargada -- depende de las familias del proyecto real.

## Extra: aviso de recubrimiento distinto del normal

Pedido de William (9-sep, en respuesta a la consulta sobre por qué el recubrimiento sale del parámetro de la propia columna en Revit): no imponerlo, pero avisar cuando el valor asignado sea distinto del normal del equipo (4 cm) -- suele ser un dato mal cargado en el modelo, no una decisión real de diseño.

`Logica/LectorContexto.cs` (`Recubrimiento`): nueva constante `RecubrimientoNormalMetros = 0.04`; si el recubrimiento leído del parámetro `CLEAR_COVER_OTHER` de la columna difiere de ese valor (tolerancia 1mm), se agrega un aviso no bloqueante -- el valor real de la columna se sigue usando igual, esto es solo para que el equipo lo revise. Compilado y desplegado.
