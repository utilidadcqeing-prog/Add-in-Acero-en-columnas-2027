# Plan de implementación: panel visual de estribos menores, paquetes y formas propias (#64)

> **Para agentes:** sub-skill obligatoria: superpowers:subagent-driven-development (recomendada) o superpowers:executing-plans, tarea por tarea. Los pasos usan casillas (`- [ ]`).

**Objetivo:** que los estribos menores se pongan dibujándolos sobre la sección, que salgan apilados junto al estribo principal (paquetes) y que estribos, cajas y grapas usen formas y ganchos propios del cuadro de la empresa.

**Arquitectura:** el modelo de piezas (`EstriboMenor`) y el cálculo del paquete (`PaqueteEstribos`) van en `Modelo/` sin Revit y con pruebas, y los usan el panel y los dos generadores. Las formas y los ganchos propios se resuelven una vez por corrida en `Logica/TiposPropios.cs`, con el mismo molde que `Logica/TiposEmpalme.cs`. La grapa en S se mide primero contra Revit real con un comando de diagnóstico temporal.

**Tecnologías:** C# / .NET 10, WPF, API de Revit 2027.

**Diseño:** `docs/planes/2026-10-20-diseno-estribos-menores-visual-y-64.md`, y lo que sigue valiendo de `docs/planes/2026-10-09-diseno-formas-ganchos-propios-64.md`. Leer los dos antes de empezar.

## Restricciones globales

- `Modelo/` y `Vistas/` no referencian la API de Revit (`tools/PreviewUI` y `tools/PruebasTraslapos` compilan sin ella).
- Solo API pública de Revit, verificada en `C:\Program Files\Autodesk\Revit 2027\RevitAPI.xml` (regla del `CLAUDE.md`).
- Nada se da por bueno sin medirlo contra Revit real; lo que no se pudo medir se dice.
- Build: `dotnet build practicacq.csproj -c Debug -p:RevitVersion=2027`. Vista previa: `dotnet build tools/PreviewUI/PreviewUI.csproj`. Pruebas: `dotnet run --project tools/PruebasTraslapos` (termina en `0 fallas`).
- **Commits: solo cuando Luciano lo pida** (`CLAUDE.md`). Nunca `git add -A`. Al terminar cada tarea, dejar la lista de archivos para el commit.
- Deploy: antes, `Get-Process -Name Revit -ErrorAction SilentlyContinue` tiene que salir vacío.
- Textos al usuario en español neutro, sin voseo. Comentarios del código en el mismo tono y densidad que el código de alrededor.
- Separación de 5 cm en los extremos del tramo: `GeneradorArmado.SeparacionExtremosMetros = 0.05`.
- Medidas del cuadro 2 (#65), en cm, E / D (extensión recta / diámetro de doblado):
  - 135° (estribo), solo #2 a #5: #2 3,8/2,6 · #3 5,7/3,8 · #4 7,6/5,1 · #5 9,5/6,4
  - 90°: #2 7,7/3,8 · #3 11,4/5,7 · #4 15,2/7,6 · #5 19,1/9,5 · #6 22,9/11,5 · #7 26,6/13,3 · #8 30,5/15,2
  - 180°: #2 6,5/3,8 · #3 6,5/5,7 · #4 6,5/7,6 · #5 6,5/9,5 · #6 7,6/11,5 · #7 8,9/13,3 · #8 10,2/15,2

## Puntos de revisión

1. **Se baja la cantidad de barras por cara después de poner piezas.** Las piezas que apuntan a una fila o columna que ya no existe se descartan con aviso, y el generador nunca revienta por un índice fuera de rango (pruebas en la tarea 1, protección en la tarea 7).
2. **Tramo donde el paquete no entra** (tramo corto o separación chica). Ese tramo sale sin menores, con aviso, y los principales igual (prueba en la tarea 2, aviso en la tarea 7).
3. **Configuración guardada vieja** (casillas de grapas y cajas). Carga y genera la misma geometría en planta que antes (prueba en la tarea 1).
4. **La barra de una pieza no existe en este modelo** (configuración traída de otro). Se usa la barra del estribo del tramo, con aviso (tarea 7).
5. **Segunda corrida en el mismo modelo.** No se duplican formas ni ganchos `CQ …`, y las alturas de los paquetes salen iguales (tarea 6, paso de verificación en Revit).

---

## Tarea 1: piezas de estribos menores y migración

**Archivos:**
- Crear: `Modelo/EstriboMenor.cs`
- Modificar: `Modelo/ConfigLongitudinal.cs`, `Modelo/CajaMenor.cs` (solo comentarios: `CajaMenor` queda para leer configuraciones viejas)
- Modificar: `tools/PruebasTraslapos/PruebasTraslapos.csproj` (agregar `Modelo\EstriboMenor.cs`), `tools/PruebasTraslapos/Program.cs`

**Interfaces:**
- Produce:
  - `public enum FormaMenor { GrapaC, GrapaS, Cerrado }`
  - `public sealed class EstriboMenor { FormaMenor Forma; EjeCaja Eje; int Desde; int Hasta; bool Volteado; string? TipoBarraNombre; EstriboMenor Copiar(); }`. Usa el `EjeCaja` que ya existe (`Fila` = cruza en X sobre esa fila; `Columna` = cruza en Y). En una grapa, `Desde == Hasta`. En un cerrado, `Desde < Hasta`. `TipoBarraNombre == null` significa "Igual al estribo".
  - En `ConfigLongitudinal`: `List<EstriboMenor> EstribosMenores { get; set; } = new();` y `bool MigrarEstribosMenores()`, que pasa `GrapasFila`/`GrapasColumna`/`Cajas` a piezas (C sin voltear y cerrados), vacía las tres listas viejas y devuelve true si cambió algo. También `int DescartarMenoresFueraDeRango()`, que quita las piezas con `Hasta >= BarrasPorCaraY/2` (eje `Fila`) o `>= BarrasPorCaraX/2` (eje `Columna`) y devuelve cuántas quitó. `Copiar()` copia `EstribosMenores` pieza por pieza.

- [ ] **Paso 1: pruebas que fallan** en `Program.cs`:
  - `migracion_menores`: config con `GrapasFila = [0]`, `GrapasColumna = [1]`, `Cajas = [{Fila, 0, 1}]` → `MigrarEstribosMenores()` da true; quedan 3 piezas: `(GrapaC, Fila, 0, 0)`, `(GrapaC, Columna, 1, 1)` y `(Cerrado, Fila, 0, 1)`, todas con `Volteado == false` y `TipoBarraNombre == null`; las listas viejas quedan vacías; una segunda llamada da false.
  - `menores_fuera_de_rango`: `BarrasPorCaraX = 4`, `BarrasPorCaraY = 2` (2 columnas y 1 fila), con piezas `(GrapaC, Fila, 1, 1)` y `(Cerrado, Columna, 0, 1)` → `DescartarMenoresFueraDeRango()` da 1 y queda solo el cerrado.
  - `copiar_menores`: modificar una pieza de la copia no cambia el original.
- [ ] **Paso 2:** `dotnet run --project tools/PruebasTraslapos` → no compila (falta `EstriboMenor`).
- [ ] **Paso 3:** implementar lo de **Interfaces**. Marcar `GrapasFila`, `GrapasColumna` y `Cajas` como OBSOLETO en su comentario, igual que `TipoBarraANombre`.
- [ ] **Paso 4:** pruebas → `0 fallas`. Compilar también `practicacq.csproj` y `PreviewUI`: todavía tienen que compilar, porque las listas viejas siguen existiendo.

## Tarea 2: cálculo del paquete

**Archivos:**
- Crear: `Modelo/PaqueteEstribos.cs`
- Modificar: `tools/PruebasTraslapos/PruebasTraslapos.csproj`, `tools/PruebasTraslapos/Program.cs`

**Interfaces:**
- Consume: `EstriboMenor`, `FormaMenor`, `EjeCaja` (tarea 1).
- Produce (todo en **metros**, con desplazamientos medidos desde el punto medio del paquete y positivos hacia arriba):
  - `public static bool EstaTapada(EstriboMenor grapa, IEnumerable<EstriboMenor> piezas)`: una grapa (C o S) dentro del rango de un cerrado del mismo eje.
  - `public static bool Chocan(EstriboMenor a, EstriboMenor b)`: true si son de **distinto eje**, o del mismo eje con rangos `[Desde, Hasta]` que se cruzan (compartir un índice cuenta).
  - `public sealed record NivelPieza(EstriboMenor Pieza, int Nivel, double Desplazamiento)`
  - `public sealed record Paquete(double DesplazamientoPrincipal, double Debajo, double Encima, IReadOnlyList<NivelPieza> Piezas) { public double Alto => Debajo + Encima; }`. `Debajo` y `Encima` son las distancias del punto medio a la cara de abajo de la pieza más baja y a la cara de arriba de la más alta.
  - `public static Paquete Calcular(IReadOnlyList<EstriboMenor> piezas, double diametroPrincipalMm, Func<EstriboMenor, double> diametroMm)`. Ignora las tapadas. Sin piezas da `(0, d/2, d/2, [])`, que es exactamente el reparto actual.

Algoritmo (el diseño fija las reglas, no el método):
1. Se descartan las tapadas. Las piezas de eje `Fila` y las de eje `Columna` nunca comparten nivel. Dentro de un mismo eje, chocar es cruzarse de rango: se colorea un grafo de intervalos con el método codicioso por `Desde` ascendente, que es óptimo. Primero se reparten los cerrados y después las grapas, cada grapa en el primer nivel ya abierto donde no choque.
2. Orden de los niveles: primero los que tienen algún cerrado; después, eje `Fila` antes que `Columna`; después, por el menor `Desde`.
3. Ubicación: el nivel 1 va debajo del principal, el 2 encima, el 3 debajo del 1, y así. Entre ejes vecinos, la distancia es `(dA + dB) / 2`, con el mayor diámetro de cada nivel. El principal está en 0.
4. Se pasa todo a desplazamientos desde el punto medio, que es la mitad entre la cara más baja y la más alta.

- [ ] **Paso 1: pruebas que fallan** (principal #3 = 9,5 mm, salvo que se diga otra cosa):
  - `paquete_vacio`: `Calcular([], 9.5, _)` → `DesplazamientoPrincipal 0`, `Debajo 0.00475`, `Encima 0.00475`.
  - `paquete_una_grapa`: una C en la fila 0, #3 → `Alto 0.019`, `DesplazamientoPrincipal 0.00475`, desplazamiento de la pieza `-0.00475`.
  - `paquete_grapas_paralelas_comparten`: C en las filas 0 y 1 → un solo nivel, mismo resultado que la anterior.
  - `paquete_cruzadas`: C en la fila 0 y C en la columna 0 → 2 niveles: la de fila debajo (`-0.0095`) y la de columna encima (`+0.0095`); `Alto 0.0285`, `DesplazamientoPrincipal 0`.
  - `paquete_caja_mas_cerca`: cerrado de columnas 0–1 y C en la fila 0 → el cerrado en el nivel 1 (debajo) y la C en el 2 (encima).
  - `paquete_tapada`: cerrado de columnas 0–1 y C en la columna 0 → `EstaTapada` da true; un solo nivel.
  - `paquete_diametros_mixtos`: principal #3 y C en la fila 0 con #4 (12,7 mm) → distancia entre ejes `0.0111`, `Alto 0.0222`.
  - `paquete_cajas_que_comparten_barra`: cerrados de columnas 0–1 y 1–2 → chocan y van en 2 niveles.
- [ ] **Paso 2:** pruebas → no compila.
- [ ] **Paso 3:** implementar `PaqueteEstribos` según **Interfaces** y el algoritmo.
- [ ] **Paso 4:** pruebas → `0 fallas`.

## Tarea 3: el panel nuevo de estribos menores

**Archivos:**
- Reescribir: `Vistas/VentanaEstribosMenores.xaml`, `Vistas/VentanaEstribosMenores.xaml.cs`
- Modificar: `Vistas/VentanaConfiguradorEstribos.xaml.cs` (apertura del diálogo, alrededor de la línea 1279; `ResumenEstribosMenores`; carga de configuraciones guardadas: llamar a `MigrarEstribosMenores()` junto a `MigrarBarrasPorCara()` y avisar si `DescartarMenoresFueraDeRango()` > 0)

**Interfaces:**
- Consume: tarea 1 y `PaqueteEstribos.EstaTapada` (tarea 2).
- Produce: `public VentanaEstribosMenores(int barrasPorCaraX, int barrasPorCaraY, IEnumerable<EstriboMenor> piezas, IReadOnlyList<string> barrasAptas)` y `public List<EstriboMenor> Piezas { get; }` (válida si `DialogResult == true`). Por ahora, `barrasAptas` son todas las barras del documento; la tarea 5 la filtra.

Comportamiento: sección 1 del diseño, opción A del boceto (`.superpowers/brainstorm/567-1792498326/content/panel-menores.html`). Puntos que el implementador no puede deducir:
- La sección se dibuja en un `Canvas` con la misma escala y las mismas posiciones de barra que hoy (`Lado`, `Margen`, barras intermedias repartidas como `PosicionIntermedia`).
- Arrastrar desde la paleta usa `DragDrop` de WPF dentro de la misma ventana. La vista previa se dibuja con `Opacity = 0.45` en `DragOver` y en `MouseMove` (entre los dos clics del cerrado).
- Barra válida para una C o una S: cualquier intermedia. Su eje es `Fila` si está en la cara izquierda o derecha, y `Columna` si está en la de abajo o arriba. Para el segundo clic del cerrado: una intermedia del mismo eje y distinta de la primera. `Desde`/`Hasta` se ordenan.
- Esc y un clic fuera de la sección cancelan una pieza a medio poner. Supr borra la seleccionada.
- No se puede poner una C o S en una fila o columna que ya tiene grapa.
- Las tapadas (`EstaTapada`) se dibujan en gris, con una nota debajo: "La grapa de la fila N queda dentro de una caja y no se va a generar".
- Combo "# de varilla": primera opción "Igual al estribo" (= `null`), después `barrasAptas`. "Voltear" está desactivado en los cerrados.
- Rótulos visibles: "Grapa en C", "Grapa en S", "Estribo cerrado", "Pieza seleccionada", "# de varilla", "Voltear", "Eliminar (Supr)". Las filas y columnas se numeran desde 1 en pantalla.

- [ ] **Paso 1:** reescribir la ventana. Compilar `PreviewUI`: 0 errores.
- [ ] **Paso 2:** comprobar en `tools/PreviewUI` (proveedor falso), en este orden: poner una C arrastrando y una S con clic; la vista previa aparece solo sobre intermedias; un cerrado con dos clics y Esc a mitad; seleccionar, voltear una S, cambiar el número de barra y eliminar con Supr; una C dentro de una caja sale en gris con la nota; Aceptar, reabrir y que esté igual.
- [ ] **Paso 3:** cargar en `PreviewUI` una configuración vieja (con `GrapasFila`/`Cajas`) y comprobar que el panel muestra las piezas migradas. Bajar las barras por cara y comprobar el aviso de piezas descartadas.
- [ ] **Paso 4:** compilar `practicacq.csproj`: 0 errores. El generador todavía lee las listas viejas; la tarea 7 lo cambia. Mientras tanto, `GenerarMenores` llama a `MigrarEstribosMenores()` solo para leer.

## Tarea 4: diagnóstico en Revit de la grapa en S (antes de la tarea 6)

**Archivos:**
- Crear: `ComandoDiagnosticoGrapaS.cs` (temporal; mismo molde que el difunto `ComandoDiagnosticoFormasPropias.cs`, ver su historia en git)
- Modificar: `App.cs` (botón solo en Debug)
- Crear: `docs/planes/2026-10-20-diagnostico-grapa-s.md`

Qué tiene que medir, en el modelo de prueba (columna de 30×30, barra #3), cada caso en su transacción, informando en un `TaskDialog` y en un archivo de texto en `%TEMP%`:
1. Cómo crear con la API pública una forma de 1 segmento con 180° en las dos puntas y los ganchos **hacia lados opuestos**. Buscar en `RevitAPI.xml` cómo `RebarShapeDefinitionBySegments`, `RebarShape.Create` y `RebarShapeTerminationsData` fijan la orientación de cada gancho. Crear la forma y una barra con ella (`Rebar.CreateFromRebarShape`).
2. Si la orientación de cada gancho se puede invertir **al crear la barra**, sin otra forma (eso resolvería "Voltear" y quizás la S misma con la forma de la C).
3. Medir sobre la barra creada dónde quedan los dos ganchos (`GetCenterlineCurves` después de `Regenerate`): cada uno de un lado del eje.
4. Correr `MedirCorreccionDeGrapa` (hoy privado en `GeneradorLongitudinal`; para el diagnóstico, copiarlo) sobre la S: que la corrección salga parecida a la de la C.

- [ ] **Paso 1:** verificar en `RevitAPI.xml` que todo lo usado es público. Anotarlo en el documento.
- [ ] **Paso 2:** escribir el comando y compilar.
- [ ] **Paso 3:** deploy (Revit cerrado) y pedirle a Luciano que lo corra. Anotar los resultados en el documento.
- [ ] **Paso 4:** escribir en el documento la conclusión: cómo se crea la S y cómo se voltea. Si nada de lo público lo resuelve, **parar y volver a hablar con Luciano** antes de la tarea 6.

## Tarea 5: el cuadro completo y el filtro de barra de estribo

**Archivos:**
- Modificar: `Modelo/CuadroVarillas.cs`, `Modelo/TipoBarra.cs`, `Modelo/IProveedorTipos.cs`, `Logica/ProveedorTiposReal.cs`, `Modelo/ProveedorTiposFalso.cs`, `Vistas/VentanaConfiguradorEstribos.xaml(.cs)`, `Vistas/VentanaEstribosMenores.xaml.cs` (`barrasAptas`)
- Pruebas: `tools/PruebasTraslapos/Program.cs` (+ `TipoBarra.cs` en el csproj si falta)

**Interfaces:**
- Produce:
  - `public sealed record Gancho(double Extension, double DiametroDoblado)` (metros) y, en `CuadroVarillas.Varilla`, tres propiedades nuevas cargadas con la tabla de **Restricciones globales**: `Gancho? GanchoEstribo135` (null de la #6 a la #8), `Gancho Gancho90Cuadro` y `Gancho Gancho180Cuadro`. Los `GanchoEstribo`, `Gancho90` y `Gancho180` que ya existen (la "B", en metros) no cambian.
  - `TipoBarra` suma `double DobladoEstriboMm` y `double DobladoGanchoMm` (de `StirrupTieBendDiameter` y `StandardHookBendDiameter`).
  - `public enum AptitudEstribo { Apta, AptaConAviso, NoApta }` y `public static (AptitudEstribo Aptitud, string? Motivo) CuadroVarillas.EvaluarParaEstribo(TipoBarra barra)`. Regla del #64: no está en el cuadro o no tiene 135° → `NoApta`; si no, con la mayor diferencia entre `DobladoEstriboMm` y la D de 135°, y entre `DobladoGanchoMm` y la D de 180°: ≤ 1 mm → `Apta`; ≤ 1 diámetro de la barra → `AptaConAviso`; más → `NoApta`. El motivo dice qué doblado, cuánto mide y cuánto pide el cuadro, en cm.

- [ ] **Paso 1: pruebas que fallan:**
  - `apta_cqeing_3`: `TipoBarra("#3 CQEING", 9.5)` con doblados 38 mm (4 d) y 57 mm (6 d) → `Apta` (el cuadro dice 3,8 y 5,7 cm).
  - `apta_con_aviso_3_del_modelo`: #3 con doblados 38 mm y 63,4 mm (6,67 d, medido el 8-oct) → `AptaConAviso`, y el motivo contiene "6,3" y "5,7".
  - `no_apta_2_del_modelo`: #2 (6,4 mm) con doblado de estribo 50 mm (7,81 d) → `NoApta`.
  - `no_apta_6`: #6 → `NoApta`, con un motivo que menciona el gancho de 135°.
- [ ] **Paso 2:** pruebas → fallan.
- [ ] **Paso 3:** implementar. En la ventana principal: el combo de barra de estribo solo muestra `Apta` y `AptaConAviso`; debajo, una nota con las que quedaron fuera y los avisos (sección "Filtro de la barra de estribo" del #64). La barra por defecto sigue siendo #3: si no es apta, la primera apta, con el aviso que ya existe. El mismo filtro arma `barrasAptas` del panel de menores.
- [ ] **Paso 4:** pruebas `0 fallas`; compilar `PreviewUI` y comprobar el combo y la nota con el proveedor falso (sumarle doblados realistas: 4 d y 6 d, y la #2 con 7,81 d).

## Tarea 6: formas y ganchos propios (`TiposPropios`)

**Archivos:**
- Crear: `Logica/TiposPropios.cs`
- Borrar: nada todavía (lo viejo se quita en la tarea 7)

**Interfaces:**
- Consume: `CuadroVarillas` (tarea 5) y la conclusión del diagnóstico (tarea 4).
- Produce: `public sealed class TiposPropios(Document doc)` con:
  - `public Pieza<RebarHookType> GanchoEstribo()` → `CQ Estribo 135°` (135°, `StirrupTie`, multiplicador 6)
  - `public Pieza<RebarHookType> GanchoGrapa()` → `CQ Grapa 180°` (180°, `Standard`, multiplicador 4)
  - `public Pieza<RebarShape> FormaEstribo()` → `CQ Estribo`
  - `public Pieza<RebarShape> FormaGrapa(FormaMenor forma)` → `CQ Grapa` para la C, y para la S lo que haya concluido la tarea 4 (`CQ Grapa S` u orientación al crear)
  - `public string? AjustarExtension(RebarBarType barra, RebarHookType gancho, CuadroVarillas.Gancho cuadro)`: si `GetHookExtensionLength` difiere de la E en más de 1 mm, fija el largo de ese gancho en esa barra (`SetAutoCalcHookLengths(false)` + `SetHookLength`). Devuelve un aviso o null.
  - `public sealed record Pieza<T>(T? Tipo, string? Aviso)`: `Tipo == null` significa que no se pudo, y `Aviso` dice qué y por qué.
- Reglas del #64 que aplican acá: buscar por nombre y crear si falta; estilo y multiplicador se vuelven a fijar en cada corrida; una forma con el nombre propio pero alterada (otro estilo u otra cantidad de lados) no se usa; parámetros compartidos `CQ A`…`CQ D` desde un archivo temporal, **restaurando siempre** el archivo de parámetros compartidos del usuario (`try/finally`).

- [ ] **Paso 1:** verificar en `RevitAPI.xml` cada miembro usado (`RebarHookType.Create`, `Style`, `RebarShape.Create`, `RebarShapeDefinitionBySegments`, `GetHookExtensionLength`, `SetAutoCalcHookLengths`, `SetHookLength`, `Application.SharedParametersFilename`). Anotarlo en `docs/planes/2026-10-20-implementacion-estribos-menores-y-64.md` (documento de avance de este plan).
- [ ] **Paso 2:** implementar `TiposPropios`. Para crear ganchos y formas, partir del código del diagnóstico del 8-oct (`git show 515cff4:ComandoDiagnosticoFormasPropias.cs`), que ya se probó en Revit.
- [ ] **Paso 3:** compilar: 0 errores.

## Tarea 7: generación con paquetes, piezas y tipos propios

**Archivos:**
- Modificar: `Logica/GeneradorArmado.cs`, `Logica/GeneradorLongitudinal.cs`, `Modelo/OpcionesArmado.cs` (`ResultadoArmado`)
- Borrar: `GanchoAMedidaParaMenores`, `GanchoQueEntra` y el uso de `GeometriaSeccion.BuscarFormaEstribo/BuscarFormaGrapa/BuscarGanchoGrapa` como primera opción (quedan solo de respaldo si `TiposPropios` devuelve null)

**Interfaces:**
- Consume: tareas 1, 2, 5 y 6.
- Produce: en `ResultadoArmado`, `Dictionary<(string IdElementoHost, int IndiceTramo), PaqueteEstribos.Paquete> PaquetePorTramo`, que llena `GeneradorArmado` y lee `GeneradorLongitudinal`.

Lo que fija el diseño:
- **`GeneradorArmado`, por tramo no `DentroDeViga` de una columna con piezas:** se resuelve el diámetro de cada pieza (su barra; "Igual al estribo" o una barra inexistente = la del estribo del tramo; la inexistente con aviso) y se calcula el `Paquete`. Los límites del reparto del **eje del principal** pasan a ser `zInf = ZInferior + 0,05 + Debajo + DesplazamientoPrincipal` y `zSup = ZSuperior − 0,05 − Encima + DesplazamientoPrincipal` (en pies). Sin piezas da exactamente los límites de hoy (prueba de la tarea 2, `paquete_vacio`). Se guarda el paquete en `PaquetePorTramo`. Los tramos `DentroDeViga` no cambian.
- **Estribo principal:** forma `CQ Estribo` y gancho `CQ Estribo 135°`, con la extensión ajustada a la barra. El gancho configurado en el tramo queda solo de respaldo.
- **`GeneradorLongitudinal.GenerarMenores`:** recorre `cfg.EstribosMenores` (después de `MigrarEstribosMenores()` y `DescartarMenoresFueraDeRango()`) en lugar de las listas viejas. Para cada altura registrada del principal `z`, cada pieza va en `z − DesplazamientoPrincipal + Desplazamiento`. Antes de crear nada en un tramo: si la menor diferencia entre alturas consecutivas registradas es menor que `Alto`, ese tramo no lleva menores y se avisa: `"{Marca}: tramo {n}: el paquete de estribos menores mide {Alto} cm y la separación es de {sep} cm; se omiten los estribos menores de este tramo."`.
- Cerrados con `CQ Estribo` + `CQ Estribo 135°`; C y S con `FormaGrapa(...)` + `CQ Grapa 180°`; "Voltear" según la tarea 4. Cada pieza con su barra. La corrección de grapa (`MedirCorreccionDeGrapa`) se mide una vez por tramo, forma y barra.
- `AvisarSiGanchoNoCumpleNorma`: se reemplaza por la comparación de la extensión contra la E, y solo cuando se cayó a un gancho del modelo (#64).
- Se borra `ComandoDiagnosticoGrapaS.cs` y su botón.

- [ ] **Paso 1:** `GeneradorArmado` con paquetes y tipos propios. Compilar.
- [ ] **Paso 2:** `GenerarMenores` con piezas, niveles y tipos propios. Compilar.
- [ ] **Paso 3:** `dotnet run --project tools/PruebasTraslapos` → `0 fallas`; compilar `PreviewUI`.
- [ ] **Paso 4:** deploy (Revit cerrado) y retest con Luciano en Revit 2027, anotado en `docs/planes/2026-10-20-implementacion-estribos-menores-y-64.md`:
  1. Columna 30×30 con estribo #3: colas de 5,7 cm, dobleces de 4 d, forma `CQ Estribo`, sin avisos de gancho.
  2. Una C, una S y un cerrado en la misma columna: cada uno en su nivel, tocando al vecino, con el principal en el centro; la S con un gancho para cada lado; las grapas abrazan la longitudinal.
  3. Primer y último paquete del tramo a 5 cm de la cara, medidos al borde del paquete; ningún paquete en un nudo.
  4. Una pieza #2 CQEING y otra con "Igual al estribo".
  5. Configuración guardada vieja: genera sus grapas y cajas.
  6. Tramo corto con separación chica: sin menores, con el aviso.
  7. Segunda corrida: no se duplican tipos `CQ …` y las alturas son las mismas.
  8. El archivo de parámetros compartidos de Revit es el mismo antes y después.
- [ ] **Paso 5:** documentar lo que salió y lo pendiente; cerrar en el documento de avance qué issues quedan resueltos (#64, #30) para que Luciano decida si cerrarlos.
