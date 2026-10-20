# Vista previa de la columna y colores por diámetro — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que el panel muestre la columna desde arriba, de frente y de
costado (con colores por diámetro y el reparto real de estribos), que las
listas «N. barras X/Y» digan «4 (2 + 2)», y que las barras salgan con esos
mismos colores en las vistas de Revit donde aparece la columna.

**Architecture:** Toda la cuenta (colores, posiciones en planta, alturas de
estribos) va en `Modelo/` sin Revit, probada en la consola
`tools/PruebasVistaPrevia`; la usan la ventana y el generador, así lo que se ve
es lo que se genera. El dibujo es un `UserControl` nuevo
(`Vistas/PanelVistaPrevia`) como `PanelTraslapos`. Los colores en Revit van
con filtros de vista por diámetro, después de un comando de diagnóstico
contra un modelo real.

**Tech Stack:** C# / .NET 10, WPF, Revit API 2027.

**Spec:** `docs/planes/2026-10-20-diseno-vista-previa-y-colores.md`

## Global Constraints

- Build: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027`.
- **Hay otra sesión trabajando en paralelo** (estribos menores / paquetes /
  #64) con cambios sin commit en `Logica/Generador*.cs`,
  `Modelo/ConfigLongitudinal.cs`, `Modelo/CuadroVarillas.cs`,
  `Vistas/VentanaConfiguradorEstribos.xaml(.cs)`, `Vistas/VentanaEstribosMenores.*`
  y `tools/PruebasTraslapos/*`. Regla: lo de este plan va en **archivos
  nuevos**; los archivos compartidos se tocan solo con ediciones chicas y
  puntuales (Edit, nunca reescribir el archivo), releyendo justo antes, y
  avisando a la otra sesión. `tools/PruebasTraslapos` no se toca.
- Pruebas sin Revit de este plan: proyecto propio
  `tools/PruebasVistaPrevia` (consola, mismo patrón `Probar`/`Igual`/`Cierto`
  que `PruebasTraslapos`, archivos de `Modelo/` enlazados):
  `dotnet run --project tools/PruebasVistaPrevia`, sale con código 1 si
  alguna falla. `practicacq.csproj` tiene que excluir la carpeta (revisar
  `DefaultItemExcludes`). Los `Vistas/` nuevos se enlazan en
  `tools/PreviewUI/PreviewUI.csproj`.
- Interfaz sin Revit: `dotnet run --project tools/PreviewUI` (ventana con
  datos falsos).
- Deploy: `powershell -ExecutionPolicy Bypass -File tools/deploy.ps1 -Versions 2027 -TargetFramework net10.0-windows`,
  **antes** comprobar `Get-Process -Name Revit -ErrorAction SilentlyContinue`
  vacío.
- **Nunca `git commit` ni `git push` sin pedido explícito de Luciano en ese
  momento.** Los pasos "Commit" de este plan son "pedir permiso para el
  commit"; nunca `git add -A`, siempre archivos por nombre.
- Solo miembros públicos y documentados de la API de Revit (CLAUDE.md).
- Textos para el usuario en español neutro, sin voseo.
- Colores (exactos, del spec): #3 `#B8860B`, #4 `#E8710A`, #5 `#D32F2F`,
  #6 `#C2185B`, #7 `#7B1FA2`, #8 `#1565C0`, #9 `#00838F`, #10 `#2E7D32`,
  #11 `#6D4C41`, otro diámetro (incluido #2) `#5E6670`.
- Cada tarea con hallazgos contra Revit se documenta en un archivo nuevo
  `docs/planes/AAAA-MM-DD-tema.md`.

## Review Focus

1. **Tramo demasiado corto o sin config de estribos** (altura < 10 cm + diámetro, o `ConfigTramo` vacío): la vista previa muestra el tramo sin estribos y el mismo aviso que el generador, sin excepción. Prueba en Tarea 3.
2. **Confinado sin zona central** (2·L1 ≥ altura libre): un solo grupo confinado de punta a punta, igual que `GeneradorArmado`. Prueba en Tarea 3.
3. **Barras de diámetro fuera del cuadro** (métricas, #2, #12): gris, nunca excepción ni color de la varilla vecina. Prueba en Tarea 1.
4. **Cantidades 0 o impares guardadas en configuraciones viejas** (BarrasPorCaraX = 0): la lista muestra «0 (0 + 0)» y la planta solo esquinas. Prueba en Tareas 2 y 4.
5. **Columna sin tramo activo o sin barras elegidas**: la pestaña muestra un mensaje, como «Traslapos», en vez de dibujar a medias. Verificación en Tarea 5.

---

### Task 1: Tabla de colores por diámetro

**Files:**
- Create: `Modelo/ColoresVarilla.cs`
- Create: `tools/PruebasVistaPrevia/PruebasVistaPrevia.csproj` y `Program.cs` (proyecto nuevo, copiando el molde de `PruebasTraslapos.csproj`; enlazar `CuadroVarillas.cs` y `ColoresVarilla.cs`)
- Test: `tools/PruebasVistaPrevia/Program.cs`

**Interfaces:**
- Produces:
  - `public static class ColoresVarilla`
  - `public readonly record struct ColorRgb(byte R, byte G, byte B)` (anidado o en el mismo archivo)
  - `public static int? NumeroDe(double diametroMm)` — primero `CuadroVarillas.PorDiametro`; si no, los diámetros extra #9 28.7, #10 32.3, #11 35.8 mm con `CuadroVarillas.ToleranciaMm`.
  - `public static ColorRgb ColorDe(double diametroMm)` — gris `#5E6670` si `NumeroDe` es null o el número no está en la tabla (el #2 está en el cuadro pero no en la tabla: gris).
  - `public static ColorRgb Gris { get; }`
  - `public static IReadOnlyList<(int Numero, double DiametroMm, ColorRgb Color)> Tabla { get; }` — #3 a #11, para la leyenda y los filtros de Revit.

- [ ] **Step 1: Escribir las pruebas que fallan** en `Program.cs`, con el patrón `Probar(...)` existente:
  - `ColorDe(9.5)` == (184,134,11); `ColorDe(15.9)` == (211,47,47); `ColorDe(25.4)` == (21,101,192)
  - `ColorDe(28.7)` == (0,131,143); `ColorDe(32.3)` == (46,125,50); `ColorDe(35.8)` == (109,76,65)
  - `ColorDe(6.4)` (#2) == Gris; `ColorDe(16.0)` (métrica 16, a 0,1 mm del #5) — **ojo**: cae dentro de la tolerancia y sale rojo; se deja así a propósito (mismo criterio que el resto del add-in) y la prueba lo fija: == (211,47,47)
  - `ColorDe(12.0)` == Gris; `ColorDe(0)` == Gris
  - `NumeroDe(19.1)` == 6; `NumeroDe(40)` == null
  - `Tabla.Count` == 9 y `Tabla[0].Numero` == 3
- [ ] **Step 2:** `dotnet run --project tools/PruebasVistaPrevia` → FALLA de compilación (`ColoresVarilla` no existe).
- [ ] **Step 3:** Implementar `Modelo/ColoresVarilla.cs` y enlazarlo en el csproj de pruebas.
- [ ] **Step 4:** `dotnet run --project tools/PruebasVistaPrevia` → todas `OK`; `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027` → sin errores.
- [ ] **Step 5:** Pedir permiso para commit (`Modelo/ColoresVarilla.cs`, `tools/PruebasVistaPrevia/Program.cs`, `tools/PruebasVistaPrevia/PruebasVistaPrevia.csproj`).

### Task 2: «4 (2 + 2)» en las listas de cantidad de barras

**Files:**
- Create: `Modelo/TextoBarras.cs` (función de texto; no se toca `ConfigLongitudinal.cs`, lo está editando la otra sesión)
- Create: `Vistas/ConvertidorBarrasPorCara.cs` (`IValueConverter`)
- Modify: `Vistas/VentanaConfiguradorEstribos.xaml:305-310` (`ItemTemplate` de `ComboBarrasX` y `ComboBarrasY`)
- Modify: `tools/PreviewUI/PreviewUI.csproj` (enlazar el convertidor)
- Test: `tools/PruebasVistaPrevia/Program.cs`

**Interfaces:**
- Produces: `public static string TextoBarras.PorCara(int total)` → `"4 (2 + 2)"`; `"0 (0 + 0)"` para 0. Los combos siguen teniendo `int` como ítems (no cambian las líneas que leen `SelectedItem as int?`).

- [ ] **Step 1: Pruebas que fallan:** `TextoBarras.PorCara(2)` == `"2 (1 + 1)"`, `(4)` == `"4 (2 + 2)"`, `(20)` == `"20 (10 + 10)"`, `(0)` == `"0 (0 + 0)"`.
- [ ] **Step 2:** Correr pruebas → FALLA.
- [ ] **Step 3:** Implementar `TextoBarras.PorCara`; `ConvertidorBarrasPorCara.Convert` la llama con el `int`; en el XAML, recurso del convertidor e `ItemTemplate` con `<TextBlock Text="{Binding Converter={StaticResource ...}}"/>` en los dos combos (sirve para la lista desplegada y para el valor elegido).
- [ ] **Step 4:** Pruebas → OK; build → OK; `dotnet run --project tools/PreviewUI`, abrir los dos combos y comprobar que cada opción y la elegida se leen «N (n + n)», y que cambiar la cantidad sigue recalculando «Traslapos».
- [ ] **Step 5:** Pedir permiso para commit.

### Task 3: Alturas de estribos sin Revit (y comparación con Revit)

**Files:**
- Create: `Modelo/RepartoEstribos.cs`
- Modify: `Logica/GeneradorArmado.cs` (usar las mismas constantes y comparar con Revit, ~líneas 290-355 y 483-510)
- Modify: `tools/PruebasVistaPrevia/PruebasVistaPrevia.csproj`, `tools/PruebasVistaPrevia/Program.cs`
- Create: `docs/planes/AAAA-MM-DD-verificacion-reparto-estribos.md`

**Interfaces:**
- Consumes: `Tramo`, `ConfigTramo` (Modelo).
- Produces:
  - `public static class RepartoEstribos`
  - `public const double SeparacionExtremosMetros = 0.05;` (`GeneradorArmado` pasa a usar esta, borrando la suya)
  - `public sealed record Zona(string Nombre, double ZDesde, double ZHasta, double Espaciado, int Cantidad);` — metros; `Nombre` ∈ {"uniforme", "confinado inferior", "zona central", "confinado superior", "confinado (sin zona central)"}, los mismos rótulos que `CrearGrupo`.
  - `public sealed record Resultado(IReadOnlyList<double> Alturas, IReadOnlyList<Zona> Zonas, string? Aviso);`
  - `public static Resultado Calcular(double zInferior, double zSuperior, ConfigTramo cfg, PaqueteEstribos.Paquete paquete)` — metros; `Alturas` son las del eje del **principal**.

Regla (copia de `GeneradorArmado` tal como lo dejó la otra sesión con los paquetes, a confirmar en Revit): `zInf = ZInferior + SeparacionExtremosMetros + paquete.Debajo + paquete.DesplazamientoPrincipal`, `zSup = ZSuperior − SeparacionExtremosMetros − paquete.Encima + paquete.DesplazamientoPrincipal` (sin menores, el paquete del principal solo da 5 cm + medio diámetro, como antes); uniforme = un grupo; confinado = tres grupos `[zInf, zInf+L1)`, `[zInf+L1, zSup-L1)`, `[zSup-L1, zSup]` (los dos primeros sin el último estribo); si `2·L1 ≥ altura`, un grupo confinado con el mismo aviso que el generador. Dentro de un grupo de largo `L` y espaciado máximo `s`: `n = ceil(L/s − 1e-9) + 1` posiciones a `L/(n−1)`, quitando la última si el grupo no la incluye. **Esa fórmula de `SetLayoutAsMaximumSpacing` es una suposición** hasta el Step 6.

- [ ] **Step 1: Pruebas que fallan** (diámetro 9.5 mm, tramo 0 → 3.00 m):
  - Uniforme 0.20: primera altura 0.05475, última 2.94525, todas las separaciones iguales y ≤ 0.20, `Zonas.Count == 1`.
  - Confinado L1 0.50 @0.07, centro @0.14: `Zonas` con los tres nombres en orden; ninguna altura repetida; la primera de la zona central == `zInf + L1`; separación de cada zona ≤ su espaciado.
  - Confinado con L1 1.50 en tramo de 3.00: una zona `"confinado (sin zona central)"` y `Aviso` no nulo.
  - Tramo de 0.08 m: `Alturas` vacía y `Aviso` no nulo, sin excepción.
  - `cfg` con espaciados en 0: `Alturas` vacía, sin excepción.
- [ ] **Step 2:** Correr → FALLA.
- [ ] **Step 3:** Implementar `RepartoEstribos.Calcular` y enlazarlo.
- [ ] **Step 4:** Pruebas → OK.
- [ ] **Step 5:** En `GeneradorArmado`: usar `RepartoEstribos.SeparacionExtremosMetros`; después de `doc.Regenerate()` en `CrearGrupo`, si `NumberOfBarPositions` difiere de la `Cantidad` de la zona con ese nombre que da `RepartoEstribos.Calcular`, agregar a `res.Detalles` `"{etiquetaTramo} ({etiquetaGrupo}): la vista previa calculó {n} estribos y Revit puso {posiciones}."` (no cambia lo que se genera). Build → OK.
- [ ] **Step 6: Verificar contra Revit real.** Comprobar Revit cerrado, deploy, y pedir a Luciano que genere tres columnas: uniforme, confinado normal y confinado sin zona central (L1 grande). Revisar el detalle de la generación: no debe aparecer ningún "la vista previa calculó". Si aparece, ajustar la fórmula en `RepartoEstribos` (con prueba nueva que fije el caso) y repetir. Documentar casos y resultado en `docs/planes/AAAA-MM-DD-verificacion-reparto-estribos.md`.
- [ ] **Step 7:** Pedir permiso para commit.

### Task 4: Posición de las barras en planta sin Revit

**Files:**
- Create: `Modelo/SeccionPlanta.cs`
- Modify: `Logica/GeneradorLongitudinal.cs:225` (`Inset`) y `:784` (`PosicionIntermedia`) para que llamen a `SeccionPlanta`
- Modify: `tools/PruebasVistaPrevia/PruebasVistaPrevia.csproj`, `tools/PruebasVistaPrevia/Program.cs`

**Interfaces:**
- Consumes: `ConfigLongitudinal.Anillo()`, `PosicionBarra`.
- Produces:
  - `public static class SeccionPlanta`
  - `public static double Inset(double recubrimiento, double diametroEstribo, double diametroBarra)` — `recubrimiento + diametroEstribo + diametroBarra/2` (mismas unidades que reciba; el generador le pasa pies, la ventana metros).
  - `public static double PosicionIntermedia(double desde, double hasta, int n, int indice)` — la fórmula de hoy.
  - `public sealed record BarraEnPlanta(PosicionBarra Posicion, double X, double Y, double DiametroMm);` — metros, origen en la esquina inferior izquierda de la sección.
  - `public static IReadOnlyList<BarraEnPlanta> Calcular(double ladoX, double ladoY, double recubrimiento, double diametroEstriboMm, ConfigLongitudinal cfg, Func<PosicionBarra, double> diametroMm)` — en el orden de `cfg.Anillo()`; esquinas de referencia con la barra más gruesa, como el generador (`insetEsquinas`).

- [ ] **Step 1: Pruebas que fallan** (40 × 60 cm, recubrimiento 0.04, estribo 9.5, esquinas 25.4, caras 19.1, X = 4, Y = 2):
  - `Calcular(...).Count` == 10; la primera es `Esquina` en (0.04 + 0.0095 + 0.0127, ídem).
  - Las dos intermedias de abajo tienen la misma Y y X repartidas en tercios entre las esquinas.
  - Con X = 0 e Y = 0: 4 barras, todas `Esquina`.
  - `PosicionIntermedia(0, 3, 2, 0)` == 1.
- [ ] **Step 2:** Correr → FALLA.
- [ ] **Step 3:** Implementar y enlazar; en `GeneradorLongitudinal` reemplazar el cuerpo de `Inset` y borrar `PosicionIntermedia` llamando a `SeccionPlanta` (sin cambiar resultados).
- [ ] **Step 4:** Pruebas → OK; build → OK.
- [ ] **Step 5:** Pedir permiso para commit.

### Task 5: Pestaña «Vista previa»

**Files:**
- Create: `Vistas/PanelVistaPrevia.xaml`, `Vistas/PanelVistaPrevia.xaml.cs`
- Modify: `Vistas/VentanaConfiguradorEstribos.xaml:173-176` (nueva `TabItem Header="Vista previa"` después de «Traslapos»)
- Modify: `Vistas/VentanaConfiguradorEstribos.xaml.cs` (llamar al panel donde hoy se llama `RecalcularTraslapos` y `RevalidarPanel`, y al cambiar de tramo activo / columna / recubrimiento / estribos menores)
- Modify: `tools/PreviewUI/PreviewUI.csproj` (enlazar el panel)

**Interfaces:**
- Consumes: `ColoresVarilla` (T1), `TextoBarras.PorCara` (T2), `RepartoEstribos.Calcular` (T3), `SeccionPlanta.Calcular` (T4), `PaqueteEstribos.EstaTapada`, `ColumnaContexto.BaseX/BaseY`, `Tramo`.
- Produces:
  - `public void Mostrar(ColumnaContexto columna, Tramo tramo, ConfigTramo cfgTramo, ConfigLongitudinal cfgLong, double recubrimiento, Func<string?, double?> diametroDeBarraMm)` — `diametroDeBarraMm` traduce un nombre de tipo de barra a mm (la ventana ya tiene la lista de `TipoBarra`).
  - `public void MostrarMensaje(string texto)` — mismo uso que en `PanelTraslapos`.

Contenido (spec, sección 2; acomodo = boceto `pestana-vista-previa.html`, opción 2):
- Arriba, sección: contorno, estribo, barras de `SeccionPlanta.Calcular` con radio a escala y `ColoresVarilla.ColorDe`, número de intermedias junto a cada cara, estribos menores de `cfgLong.EstribosMenores` (tapadas en gris).
- Debajo, lado a lado: frente (cara X: esquinas + intermedias de cara X) y costado (cara Y). Tramo activo y nudos vecinos pintados como «Tramos» (`#3B82F6` nudo, `#B45309` zapata); barras verticales con su color; una línea por altura de `RepartoEstribos.Calcular`; estribos menores a la misma altura que el principal (línea si corren en el ancho de esa vista, punto si la atraviesan); cotas por zona ("L1 @ 7 cm", "@ 14 cm").
- Leyenda chica solo con los números presentes. Si se exagera el ancho, nota "ancho exagerado".
- Resumen: total de barras; "esquinas: 4 #8 · caras X: 2 + 2 #6 · caras Y: 1 + 1 #6"; estribos (cantidad, número, separaciones por zona); estribos menores por forma; `Aviso` de `RepartoEstribos` si lo hay.
- Mensajes en vez de dibujo: sin columna activa → "Elige una columna para ver la vista previa."; falta alguna barra longitudinal → "Elige la barra de las esquinas, de la cara X y de la cara Y para ver la vista previa."

- [ ] **Step 1:** Crear el `UserControl` (Canvas + TextBlocks, mismo estilo que `PanelTraslapos`) y la pestaña; cablear las actualizaciones.
- [ ] **Step 2:** Build del add-in → OK; `dotnet run --project tools/PruebasVistaPrevia` → OK.
- [ ] **Step 3:** `dotnet run --project tools/PreviewUI`: comprobar con datos falsos (a) una columna con X = 4, Y = 2 muestra 10 barras y "2 + 2"/"1 + 1"; (b) cambiar el espaciado o pasar a confinado redibuja al instante; (c) cambiar de tramo activo cambia frente y costado; (d) sin columna elegida, el mensaje; (e) poner un estribo menor en «Editar estribos menores…» aparece en las tres vistas. Captura de pantalla para Luciano.
- [ ] **Step 4:** Pedir permiso para commit.

### Task 6: Diagnóstico de filtros de color contra Revit real

**Files:**
- Create: `ComandoDiagnosticoFiltrosColor.cs` (temporal, como los otros `ComandoDiagnostico*.cs`) y su botón temporal en `App.cs`
- Create: `docs/planes/AAAA-MM-DD-diagnostico-filtros-color.md`

**Interfaces:**
- Consumes: `ColoresVarilla.Tabla`, `ColoresVarilla.Gris`.
- Produces: el resultado documentado que decide la Tarea 7.

- [ ] **Step 1:** Confirmar en la documentación de Revit 2027 (rvtdocs / ayuda de Autodesk) que son públicos y no marcados internos/obsoletos: `ParameterFilterElement.Create`, `ParameterFilterRuleFactory.CreateGreaterOrEqualRule`/`CreateLessRule` (doubles), `ElementParameterFilter`, `LogicalAndFilter`, `View.SetFilterOverrides`, `View.GetFilters`, `View.IsFilterApplied`, `View.CanApplyFilter` (o equivalente), `View.ViewTemplateId`, `OverrideGraphicSettings.SetProjectionLineColor`, `SetCutLineColor`, `SetSurfaceForegroundPatternColor`/`Id`, `SetCutForegroundPatternColor`/`Id`, `BuiltInParameter.REBAR_BAR_DIAMETER`. Anotar en el doc cualquiera que no lo sea y su alternativa.
- [ ] **Step 2:** Comando que, en la vista activa: crea o reutiliza un filtro "CQEING #N" por fila de `ColoresVarilla.Tabla` (categoría `OST_Rebar`, diámetro en `[d − tol, d + tol)` en pies) y uno "CQEING otro" (toda la armadura) **al final** del orden; pone los colores; y escribe en un `TaskDialog` qué hizo y cualquier excepción.
- [ ] **Step 3:** Revit cerrado → deploy. Pedir a Luciano que lo corra en: una planta normal, una planta con plantilla de vista asignada, un corte, una vista 3D, con barras #3 a #8 propias y alguna barra de otro diámetro. Preguntar/mirar: ¿cada barra sale con su color? ¿las cortadas en planta también? ¿el gris queda solo para las de otro diámetro (o pisa a las demás)? ¿qué pasa en la vista con plantilla?
- [ ] **Step 4:** Documentar en `docs/planes/AAAA-MM-DD-diagnostico-filtros-color.md` lo medido.
- [ ] **Step 5: Punto de control con Luciano:** con lo medido, que decida qué hacer en vistas con plantilla (agregar los filtros a la plantilla o saltarlas y avisar) y si el gris "otro" se queda. Anotar la respuesta en el doc del diseño.
- [ ] **Step 6:** Pedir permiso para commit del doc (el comando temporal se borra en la Tarea 7).

### Task 7: Colores al generar

**Files:**
- Create: `Logica/ColoresEnVistas.cs`
- Modify: `Logica/GeneradorArmado.cs` (o el punto de entrada de la generación, después de crear todas las barras, dentro de la misma transacción)
- Delete: `ComandoDiagnosticoFiltrosColor.cs` y su botón en `App.cs`
- Create: `docs/planes/AAAA-MM-DD-colores-por-diametro.md`

**Interfaces:**
- Consumes: `ColoresVarilla.Tabla`/`Gris`, lo decidido en la Tarea 6.
- Produces: `public static int Aplicar(Document doc, IReadOnlyCollection<ElementId> columnas, List<string> avisos)` — devuelve cuántas vistas pintó.

Comportamiento (spec, sección 3): filtros "CQEING #N" (y "CQEING otro" si quedó) creados una vez y reutilizados (regla actualizada si ya existen); vistas = todas las `View` que no son plantilla, admiten filtros y donde al menos una de `columnas` es visible (`new FilteredElementCollector(doc, vista.Id)` filtrado por esos ids); vistas con plantilla según la Tarea 6; no se tocan otros filtros ni sustituciones de la vista; un aviso por vista que falle, sin cortar la generación.

- [ ] **Step 1:** Implementar `ColoresEnVistas.Aplicar` y llamarlo al final de la generación con los ids de las columnas generadas; agregar a los detalles "Colores por diámetro aplicados en N vistas."; borrar el comando de diagnóstico.
- [ ] **Step 2:** Build → OK; pruebas sin Revit → OK.
- [ ] **Step 3:** Revit cerrado → deploy. Pedir a Luciano que genere una columna y revise: planta, corte, 3D y una vista donde la columna no aparece (no debe tener los filtros). Volver a generar: los filtros no se duplican.
- [ ] **Step 4:** Documentar en `docs/planes/AAAA-MM-DD-colores-por-diametro.md` (contexto, qué se hizo, resultado del retest).
- [ ] **Step 5:** Pedir permiso para commit.
