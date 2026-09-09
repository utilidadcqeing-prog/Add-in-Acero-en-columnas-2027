# Diagnóstico: corrimiento lateral real en los traslapos

## Contexto

Luego de implementar la longitudinal continua (ver `docs/planes/2026-09-09-selector-visual-columnas-y-longitudinal-continua.md`), el usuario probó contra el modelo de referencia de William y reportó que **no se veían traslapos** — todo salía recto. Cuestionó si el concepto de traslapo estaba bien entendido.

Diagnóstico: la geometría de `CrearBarraConTraslapos` sí generaba el solape en Z (dos segmentos de `Rebar` que se superponen la distancia de `EstandarCQ.Varilla.Traslapo`), pero ambos segmentos usaban la MISMA X/Y — así que visualmente eran dos líneas perfectamente coincidentes, indistinguibles de una sola barra recta.

## Medición contra el modelo real

Para no seguir adivinando, se armó un comando temporal de diagnóstico (`ComandoDiagnosticoRebar.cs`, botón "Diagnóstico de Rebar" en el panel Herramientas) que reporta las coordenadas de la geometría (`Rebar.GetCenterlineCurves`) de las barras que se seleccionen. El usuario lo corrió sobre dos barras de un mismo traslapo en el modelo de William:

```
Rebar 153388 (host 152829): (-4.736, -4.700, -2.971) -> (-4.736, -4.700, 6.340)
Rebar 153399 (host 152853): (-4.736, -4.681, 5.154)  -> (-4.736, -4.681, 14.040)
```

- **X idéntico** (-4.736) en ambas.
- **Y corrido +1.9cm** (-4.700 → -4.681) — coincide casi exacto con el diámetro de una barra #6 (19.1mm de `EstandarCQ`). Se corren su propio diámetro, no una distancia arbitraria.
- Se superponen en Z entre 5.154 y 6.340 (1.19m) — el traslapo real.
- Ocurre justo en el límite entre dos hosts (elementos) distintos de Revit, coincidiendo con un cambio de piso.

La barra medida es una barra de cara X (su Y es la coordenada fija de esa cara, su X varía por fila) — que su Y sea la que se corre (no su X) tiene sentido: se corre la coordenada FIJA de la cara, no la que define su posición/fila.

## Punto que el usuario corrigió

Se le preguntó si TODO cambio de piso debía llevar un traslapo real con corrimiento (en vez de junta a tope). Respuesta: no — depende de la alternancia T1/T2 y de si la barra "se acaba o no" (algoritmo voraz, ya implementado). Ese criterio ya estaba bien en el código (`PlanificarCortes` ya distingue corte forzado de traslapo real, y los coalesce cuando coinciden); lo único que faltaba era el corrimiento lateral en sí.

## Fix implementado

`Logica/GeneradorLongitudinal.cs`:
- Nueva función local `Corrimiento(barra, ejeY, coordenadaFija)`: calcula un vector de corrimiento de magnitud = diámetro de la barra, a lo largo del eje "fijo" de esa posición (Y para esquinas y barras de cara X, X para barras de cara Y), con signo hacia el centro de la sección.
- `CrearBarraConTraslapos` ahora recibe ese vector y lo aplica alternando: arranca en la posición de diseño, y cada vez que pasa un traslapo REAL (no un corte forzado sin traslapo) alterna entre la posición original y la corrida -- así no se acumula corrimiento tramo tras tramo, va y vuelve.

**Supuesto sin confirmar todavía (corregido el 9-sep, ver más abajo)**: el criterio de qué eje corre (Y para esquinas/cara-X, X para cara-Y) solo estaba medido con un dato real (una barra de cara X). Para esquinas se aplicó el mismo criterio por analogía (correr en Y); para cara Y se asumió que corría en X (la coordenada fija de esa cara), sin medición real.

## Corrección (9-sep, más tarde en la misma sesión): el supuesto de cara Y estaba mal

Se midió una barra de cara Y real contra el mismo modelo de referencia de la empresa (misma columna que la medición original, un piso más arriba: `Rebar 153393`/`153394`, hosts 152853/152865, huella de columna 0.50×0.50m). Resultado: **X idéntico, Y corrido 1.9cm** -- exactamente el mismo patrón que la barra de cara X, NO el eje contrario que se había supuesto.

Con esta medición más la de cara X original, y coincidiendo además con lo que calcula automáticamente `RebarSpliceShiftOption.BarPlane` de la API de Revit en todas las pruebas hechas durante la investigación de migración a `RebarSpliceUtils` (ver `docs/planes/2026-09-09-empalmes-rebarspliceutils.md`), el criterio real confirmado es: **el corrimiento siempre va en Y, sin importar la cara** (esquina, cara X o cara Y) -- no alterna según la cara como se había asumido.

`Logica/GeneradorLongitudinal.cs`: `Corrimiento` se simplificó -- ya no recibe un eje (`ejeY`), siempre corre en Y contra el centro Y de la sección. Los tres grupos de llamadas (esquinas, cara X, cara Y) se actualizaron para pasar la coordenada Y real de cada posición.

## Estado

Compilado. Pendiente: desplegar y retest visual del usuario contra Revit real (generar una columna con traslapo esperado en una posición de cara Y y confirmar que el corrimiento ahora se ve, y en el eje correcto).

## Comando temporal de diagnóstico

`ComandoDiagnosticoRebar.cs` + botón "Diagnóstico de Rebar" en el ribbon (panel Herramientas) quedan en el add-in por ahora -- sacar una vez que se termine de confirmar toda la geometría de traslapos (incluyendo esquinas y cara Y).
