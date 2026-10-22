# Avisos de choque (#131) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que el add-in marque en la sección, y liste al tocar "Generar", las piezas que chocan (barras demasiado juntas, grapas sobre barras, cerrados que no se doblan, traslapos apretados), sin impedir generar.

**Architecture:** Un cálculo puro en `Modelo/ChoquesSeccion.cs` (sin Revit, con pruebas) recibe la sección de un tramo y devuelve los choques en "sección normal" y "zona de traslapo". La elección de hacia dónde se corre cada pieza de traslapo sale de `GeneradorLongitudinal` a una función pura que usan el generador y la ventana, para que lo que se ve sea lo que se genera. Las dos ventanas dibujan los choques; `BotonGenerar_Click` los junta en un diálogo.

**Tech Stack:** C# / .NET 10, WPF, Revit API 2027. Pruebas: consola `tools/PruebasVistaPrevia` (`Probar`, `Cierto`, `Igual`).

**Spec:** `docs/planes/2026-10-22-avisos-de-choque-131-diseno.md`

## Global Constraints

- Distancia libre mínima entre longitudinales (borde a borde): `max(1,5 × diámetro de la más gruesa de las dos, 40 mm)`, en una sola constante/función, comentada "NSR-10, a confirmar con Carlos".
- Nunca se bloquea ni se corrige nada: solo se avisa.
- Las dos piezas de una misma varilla en su traslapo no chocan entre sí.
- No se revisa barra contra estribo principal.
- Textos en español neutro, sin voseo, medidas en cm con una decimal y nombre de la barra; dicen qué pieza choca con cuál.
- Diálogo de Generar: título "Hay choques en N columna(s)", texto "Si lo generas así, ten en cuenta que:", botones "Volver a revisar" y "Generar igual". Sin choques, se genera directo.
- Generar revisa todas las columnas del `PlanArmado` que arma `ConstruirPlanArmado`.
- Sin miembros internos de la API de Revit (regla del CLAUDE.md). Los avisos de después de generar se mantienen.
- Build: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027` (en `addins/armado-columnas`). Pruebas: `dotnet run --project tools/PruebasVistaPrevia` y `dotnet run --project tools/PruebasTraslapos` (en la raíz de `addins-130`), las dos con "0 fallas".
- Commits solo de los archivos de la tarea, por nombre (nunca `git add -A`). Sin push.

## Review Focus

- Columna sin barras intermedias (solo 4 esquinas) y sección grande: cero choques, ningún contorno rojo.
- Barras de distinto diámetro vecinas (#4 esquina con #6 de cara): la mínima usa la más gruesa de las dos.
- Grapa cuyo tramo recto pasa a ras de una barra vecina sin tocarla (holgura 1 mm): no es choque.
- Tramo sin estribo (`ContraZapata`) o nudo (`DentroDeViga`): no hay menores; se revisan solo las longitudinales.
- Columna sin traslapo (una sola pieza por varilla): el selector "Zona de traslapo" queda deshabilitado y no hay choques de traslapo.

---

### Task 1: Separación entre longitudinales

**Files:**
- Create: `addins/armado-columnas/Modelo/ChoquesSeccion.cs`
- Modify: `tools/PruebasVistaPrevia/PruebasVistaPrevia.csproj` (enlazar el archivo), `tools/PruebasVistaPrevia/Program.cs`

**Interfaces:**
- Produces:
  - `public sealed record PiezaRedonda(double X, double Y, double DiametroMm, int Varilla, double ZInf = double.NegativeInfinity, double ZSup = double.PositiveInfinity)` (metros; `Varilla` = índice en el anillo).
  - `public sealed record Choque(string Motivo, IReadOnlyList<(double X, double Y, double Radio)> Marcas)` (marcas = círculos a rodear en rojo, metros).
  - `public static double SeparacionMinimaMm(double diametroMm1, double diametroMm2)`
  - `public static IReadOnlyList<Choque> BarrasJuntas(IReadOnlyList<PiezaRedonda> piezas, Func<int, string> nombreVarilla)`
  - `public static string NombreBarra(IReadOnlyList<SeccionPlanta.BarraEnPlanta> barras, int indice)` — "la esquina inferior izquierda" / "la barra 2 de la cara inferior" (cara inferior/superior contadas de izquierda a derecha; izquierda/derecha de abajo hacia arriba; esquinas no cuentan).

- [ ] **Step 1: Pruebas que fallan**, en `Program.cs`:
  - `choques_separacion_minima`: `SeparacionMinimaMm(15.9, 15.9) == 40`; `SeparacionMinimaMm(25.4, 19.1) == 40` (1,5 × 25,4 = 38,1 < 40); `SeparacionMinimaMm(32.3, 12.7) == 48.45`.
  - `choques_barras_juntas`: dos #5 a 5,0 cm entre ejes (libre 3,41 cm) → 1 choque cuyo `Motivo` contiene "3,4 cm" y "4,0 cm", con 2 marcas; a 6,0 cm entre ejes → 0.
  - `choques_misma_varilla_no_cuenta`: dos piezas #5 de `Varilla = 3` pegadas → 0.
  - `choques_alturas_que_no_se_cruzan`: piezas de varillas distintas a 2 cm pero con `ZInf/ZSup` 0–1 y 2–3 → 0.
  - `choques_nombre_barra`: con `SeccionPlanta.Calcular` de una 40×40 con 2 por cara X → índice 0 = "la esquina inferior izquierda", índice 1 = "la barra 1 de la cara inferior".
- [ ] **Step 2:** `dotnet run --project tools/PruebasVistaPrevia` → no compila (`ChoquesSeccion` no existe).
- [ ] **Step 3:** Implementar en `ChoquesSeccion.cs`. Un choque por par (no por pieza); libre = distancia entre centros − suma de radios; tolerancia 1 mm (constante `ToleranciaMm = 1`).
- [ ] **Step 4:** Pruebas → "0 fallas".
- [ ] **Step 5:** Commit: `Modelo/ChoquesSeccion.cs`, csproj, `Program.cs` — "Choques: separación mínima entre longitudinales (#131)".

### Task 2: Grapas y cerrados

**Files:**
- Modify: `addins/armado-columnas/Modelo/ChoquesSeccion.cs`, `addins/armado-columnas/Vistas/VentanaEstribosMenores.xaml.cs` (`Imposible` pasa a usar el nuevo), `tools/PruebasVistaPrevia/Program.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `public static string? CerradoImposible(EstriboMenor pieza, TipoBarra barra, IEnumerable<GanchoGrapa.Pieza> abrazadas, string donde)` — mismo cálculo y mismo texto que hoy `VentanaEstribosMenores.Imposible` (líneas 419-441); la ventana queda llamándolo.
  - `public static Choque? MenorSobreBarra(IReadOnlyList<(double X, double Y)> eje, double diametroMm, IReadOnlyList<PiezaRedonda> barras, ISet<int> abrazadas, string nombrePieza, Func<int, string> nombreVarilla)` — el trazo (de `PlantaArmado.Menor`) con su grosor se mete en una barra que no está en `abrazadas`; marca esa barra.

- [ ] **Step 1: Pruebas que fallan:**
  - `choques_grapa_sobre_barra_que_no_abraza`: trazo recto de (0,05; 0,10) a (0,35; 0,10), #3, barra #5 no abrazada en (0,20; 0,11) → choque con "pasa encima"; en (0,20; 0,125) (holgura > 1 mm) → null.
  - `choques_grapa_no_choca_con_las_que_abraza`: misma barra con su índice en `abrazadas` → null.
  - `choques_cerrado_imposible_igual_que_antes`: el caso de la 25×30 del #132 da el mismo texto que antes del cambio (copiar el texto esperado del método actual).
- [ ] **Step 2:** Pruebas → fallan.
- [ ] **Step 3:** Implementar; mover el cálculo de `Imposible` sin cambiar su texto.
- [ ] **Step 4:** Pruebas → "0 fallas"; build del add-in sin errores.
- [ ] **Step 5:** Commit: "Choques: grapas sobre barras y cerrado imposible en Modelo (#131)".

### Task 3: Acomodo de traslapos sin Revit

**Files:**
- Modify: `addins/armado-columnas/Modelo/AcomodoTraslapos.cs`, `addins/armado-columnas/Logica/GeneradorLongitudinal.cs:545-588`, `tools/PruebasTraslapos/Program.cs`

**Interfaces:**
- Produces: `public sealed record PedidoSeccion(double X, double Y, double Radio, double Diametro, bool Esquina, bool ConGancho, (double X, double Y) Normal, (double X, double Y) CentroSeccion, (double ZInf, double ZSup)? Abajo, (double ZInf, double ZSup)? Arriba, (double XMin, double XMax, double YMin, double YMax) Limites)` y `public static IReadOnlyList<(Manera? Manera, (double X, double Y) Eje)> PlanearSeccion(IReadOnlyList<PedidoSeccion> pedidos, IReadOnlyList<Pieza>? obstaculos)` en `AcomodoTraslapos`. Mismas unidades de entrada y salida (el generador pasa pies, la ventana metros). `Abajo/Arriba` null = varilla sin traslapo (una pieza).

- [ ] **Step 1: Pruebas que fallan** (en `PruebasTraslapos`):
  - `planear_seccion_igual_que_elegir`: 4 esquinas de una 40×40 con traslapo → el resultado coincide con armar a mano `Cuatro` (ejes normal / otro / diagonal según `Esquina`, `ConGancho`) + `Elegir`, como hace hoy el generador.
  - `planear_seccion_sin_traslapo`: pedido con `Abajo = null` → `Manera = null`.
  - `planear_seccion_con_gancho_no_prueba_diagonal`: esquina con `ConGancho = true` → el eje elegido es X o Y.
- [ ] **Step 2:** Pruebas → fallan.
- [ ] **Step 3:** Mover a `PlanearSeccion` la armada de ejes (incluida la diagonal "con la Y positiva" y su comentario) y la llamada a `Elegir`; el generador construye los `PedidoSeccion` y usa el resultado. Comportamiento del generador idéntico.
- [ ] **Step 4:** Pruebas → "0 fallas"; build sin errores.
- [ ] **Step 5:** Commit: "Acomodo de traslapos por sección sin Revit, compartido con la ventana (#131)".

### Task 4: Choques de un tramo (sección normal y zona de traslapo)

**Files:**
- Modify: `addins/armado-columnas/Modelo/ChoquesSeccion.cs`, `tools/PruebasVistaPrevia/Program.cs` (y enlazar en el csproj `AcomodoTraslapos.cs` si falta)

**Interfaces:**
- Consumes: Tasks 1-3, `SeccionPlanta.Calcular`, `PlanTraslapos.Calcular`, `PlantaArmado.Menor`, `GanchoGrapa.Elegir`.
- Produces:
  - `public sealed record Resultado(IReadOnlyList<Choque> Normal, IReadOnlyList<Choque> Traslapo, bool HayTraslapo, IReadOnlyList<PiezaRedonda> PiezasTraslapo)`
  - `public static Resultado DeTramo(ColumnaContexto columna, Tramo tramo, ConfigTramo cfgTramo, ConfigLongitudinal cfgLong, double recubrimiento, Func<string?, TipoBarra?> barraDe, double diametroEstriboPlantaMm)` — mismas entradas que `PanelVistaPrevia.Mostrar`. Normal: barras + menores del tramo. Traslapo: piezas de `PlanearSeccion` (obstáculos = colas de los ganchos del estribo principal, muestreadas de `PlantaArmado.EstriboCerrado`, radio = medio diámetro del estribo), barras juntas entre piezas cuyas Z se cruzan (el motivo dice "a X,XX m"), y "las dos piezas no entran en el doblez de la grapa" con `GanchoGrapa.Elegir`. Sin barras elegidas → `Resultado` vacío.

- [ ] **Step 1: Pruebas que fallan:**
  - `choques_tramo_sin_choques`: 40×40, rec. 0,04, 4 esquinas #5, estribo #3 → `Normal` y `Traslapo` vacíos.
  - `choques_tramo_muchas_barras`: 30×30, rec. 0,04, estribo #3, todas #6, 3 intermedias por cara (`BarrasPorCaraX = BarrasPorCaraY = 6`; entre ejes ≈ 4,55 cm, libre ≈ 2,6 cm) → `Normal` con choques entre barras de cara. Con 2 por cara (libre ≈ 4,2 cm) → vacío.
  - `choques_tramo_traslapo_mas_apretado`: una sección que pasa en normal y falla en traslapo → `Normal` vacío, `Traslapo` no vacío, `HayTraslapo` true.
  - `choques_tramo_contra_zapata`: tramo `ContraZapata` → no evalúa menores.
- [ ] **Step 2:** Pruebas → fallan.
- [ ] **Step 3:** Implementar `DeTramo`.
- [ ] **Step 4:** Pruebas → "0 fallas".
- [ ] **Step 5:** Commit: "Choques de un tramo: sección normal y zona de traslapo (#131)".

### Task 5: Choques en la vista previa del panel

**Files:**
- Modify: `addins/armado-columnas/Vistas/PanelVistaPrevia.xaml`, `addins/armado-columnas/Vistas/PanelVistaPrevia.xaml.cs`

**Interfaces:**
- Consumes: `ChoquesSeccion.DeTramo`, `Resultado`.

- [ ] **Step 1:** Selector de dos opciones "Sección normal" / "Zona de traslapo" sobre la planta (deshabilitado si `!HayTraslapo`). En "Zona de traslapo" se dibujan además las piezas corridas (`PiezasTraslapo`).
- [ ] **Step 2:** Cada `Marca` del modo elegido: círculo con borde rojo `Paleta.Error`, discontinuo, radio + 3 px, con `ToolTip` = `Motivo` (este elemento sí captura el mouse). Bajo la planta, una línea "⚠ N choque(s) (se puede generar igual)" si hay.
- [ ] **Step 3:** Build sin errores; `dotnet build tools/PreviewUI` sin errores.
- [ ] **Step 4:** Commit: "Choques marcados en la vista previa del tramo (#131)".

### Task 6: Choques en "Editar estribos menores"

**Files:**
- Modify: `addins/armado-columnas/Vistas/VentanaEstribosMenores.xaml`, `addins/armado-columnas/Vistas/VentanaEstribosMenores.xaml.cs`

- [ ] **Step 1:** Mismo selector y mismas marcas que la Task 5 (reutilizar el dibujo de marcas en `Vistas/DibujoArmado.cs` como `public static Shape MarcaChoque(...)` si ya está en la Task 5, si no crearlo acá y usarlo en las dos). Se recalcula en cada cambio de pieza (donde hoy se llama `Dibujar()`).
- [ ] **Step 2:** Las notas de `ActualizarNotas` suman los motivos del modo elegido.
- [ ] **Step 3:** Build sin errores; PreviewUI sin errores.
- [ ] **Step 4:** Commit: "Choques marcados en Editar estribos menores (#131)".

### Task 7: Diálogo al tocar Generar

**Files:**
- Create: `addins/armado-columnas/Vistas/VentanaChoques.xaml`, `.xaml.cs` (y enlazarla en `tools/PreviewUI/PreviewUI.csproj` como las demás ventanas)
- Modify: `addins/armado-columnas/Vistas/VentanaConfiguradorEstribos.xaml.cs` (`BotonGenerar_Click`, línea ~1913)

**Interfaces:**
- Produces: `VentanaChoques(IReadOnlyList<(string Columna, string Motivo)> choques)`; `ShowDialog() == true` = "Generar igual".

- [ ] **Step 1:** En `BotonGenerar_Click`, tras `ConstruirPlanArmado()`: por cada `ColumnaPlan` y cada tramo aceptado (`Tramos[i] != null`), `ChoquesSeccion.DeTramo` (Normal + Traslapo); motivos sin repetir por columna, con `Contexto.Marca`.
- [ ] **Step 2:** Si hay choques, `VentanaChoques` con `Owner = this`; "Volver a revisar" → no genera y deja `TextoEstado` "Generación cancelada: revisa los choques marcados en rojo."; "Generar igual" → sigue como hoy.
- [ ] **Step 3:** Build sin errores; PreviewUI sin errores.
- [ ] **Step 4:** Commit: "Aviso de choques antes de generar (#131)".

### Task 8: Verificar en Revit y cerrar

**Files:**
- Modify: `docs/planes/2026-10-22-avisos-de-choque-131-diseno.md` (sección "Resultado")

- [ ] **Step 1:** Comprobar Revit cerrado (`Get-Process -Name Revit`), desplegar con `tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027`.
- [ ] **Step 2:** Luciano abre una 30×30 con 3 intermedias por cara de #6 y grapas: comparar marcas de la ventana, diálogo de Generar y avisos de después de generar; medir con el MCP de Revit 2 pares marcados.
- [ ] **Step 3:** Regenerar una columna ya probada (782105) y confirmar que el acomodo de traslapos no cambió (mismas piezas corridas que antes de la Task 3).
- [ ] **Step 4:** Anotar resultado y diferencias en el documento de diseño; commit del documento.
