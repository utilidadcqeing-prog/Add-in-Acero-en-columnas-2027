# Plan: generar el acero de vigas en Revit (#85)

> **Para agentes:** SUB-SKILL REQUERIDA: superpowers:subagent-driven-development (recomendada) o superpowers:executing-plans, tarea por tarea. Los pasos usan casillas (`- [ ]`).

**Objetivo:** que "Generar en Revit" cree en el modelo, en una sola transacción, las barras corridas, los bastones y los estribos que muestra el tablero, para uno o varios pórticos, sin duplicar al volver a generar.

**Arquitectura:**
- Todo lo que se puede calcular sin Revit va a la librería `CQEING.Vigas.Calculo`, carpeta nueva `Generacion/`. `PlanificadorAcero` convierte el resultado del motor en un `PlanAcero`: cada barra física, cada bastón y cada grupo de estribos con su geometría en **mm, en el marco local del pórtico**. Se prueba con xUnit.
- El lado de Revit (`addins/acero-vigas/Revit/`) solo traduce ese plan a `Rebar`: pasa del marco local a coordenadas del modelo, busca tipos de barra y gancho, marca lo creado y lo borra al regenerar.
- El tablero ya calcula todo en `ArmadorVista.Armar`. Ese cálculo se saca a `CalculoPortico` para que la vista y la generación usen exactamente lo mismo: lo generado coincide con el tablero por construcción.

**Tecnología:** C# `net8.0` (librería) y el add-in de Revit 2026/2027, xUnit, la página `Tablero/web/tablero.js`.

**Fuente:**
- la issue [#85](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/85) y sus dos comentarios del 17-oct;
- `Desktop\addin vigas\traslapos-vigas-notas.md` §13 (colores por diámetro, diámetros disponibles, en vivo);
- la maqueta, opción 1 (https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh): el botón "Generar en Revit" de la barra de arriba;
- el add-in de columnas, que ya resolvió la API de refuerzo contra Revit real: `addins/armado-columnas/Logica/BarraContinua.cs`, `GeneradorArmado.cs`, `GeometriaSeccion.cs` y `MarcaEstribos.cs`.

## Contexto

- **Qué se pide (issue #85):**
  - barras corridas: una barra de Revit por cada barra calculada, con gancho de 90° en los apoyos de borde, en todas sus filas;
  - bastones, con o sin gancho según lo elegido;
  - estribos: el cerrado de esquinas, los adicionales y la separación por zonas;
  - recubrimiento libre por cara;
  - uno o varios pórticos en una sola acción;
  - todo en una transacción, que se deshace de una vez;
  - volver a generar reemplaza lo que creó el add-in en ese pórtico, sin duplicar.
- **Comentarios del 17-oct:**
  - el acero generado lleva los colores del tablero por diámetro (`ColoresDiametro`), como estándar para todos los proyectos;
  - los diámetros que se ofrecen deben ser los tipos de barra cargados en el modelo;
  - el botón 3D sigue como está: no se toca.
- **Fuera de alcance:**
  - cambio de sección con doblez a 135° (#92);
  - la prueba final de todo el add-in (#87);
  - generar "en vivo": con Dynamo eso tumbaba Revit. Aquí solo se genera con el botón.
- **Rama:** `vigas-generar-85`, que sale de `main` (ya trae #80 a #84, #88 a #91, #100 y #86).
- **Entorno:** Revit 2026 para probar; el modelo de Carlos con voladizos (#100) y un modelo con pórtico de cimentación.

## Criterios de aceptación

- [ ] Lo generado en Revit coincide con el tablero (largos, traslapos, bastones, estribos y corte) en un pórtico aéreo con voladizo.
- [ ] Lo mismo en un pórtico de cimentación.
- [ ] Lo mismo en un pórtico con varias filas.
- [ ] Varios pórticos se generan en una sola acción, y un solo Ctrl+Z lo deshace todo.
- [ ] Volver a generar no duplica: borra lo que el add-in creó en ese pórtico y lo crea de nuevo.
- [ ] Cada barra lleva el color de su diámetro, el mismo del tablero.
- [ ] El tablero solo ofrece los diámetros que tienen tipo de barra en el modelo.

## Hallazgos del código

- **El cálculo ya está completo.** `ArmadorVista.Armar` (`calculo/.../Tablero/ArmadorVista.cs:33`) llama a `MotorVigas.Calcular`, arma la `EntradaSeccion` y llama a `MotorEstribos.Calcular`. De ahí salen:
  - `ResultadoVigas`: barras con `Desde`, `Hasta`, `TipoInicio`, `TipoFin` y `Grupos`; bastones con `Desde`, `Hasta`, sus ganchos y su `Grupo`;
  - `ResultadoEstribos.Seccion`: las barras de cada fila con su `X` (desde la cara izquierda) e `Y` (desde la cara de abajo), ya movidas por los adicionales;
  - `ResultadoEstribos.Estribos`: por tramo e id (`E`, `E1`…), las posiciones en mm desde el eje A;
  - `ResultadoEstribos.EnSeccion`: cada estribo por sus caras exteriores en el corte.
- **`SePuedeGenerar`** ya se calcula ahí (línea ~83) y la página ya desactiva el botón con él (`tablero.js:598`). Hoy el botón solo dice "Esto llega con la #85" (`tablero.js:653`).
- **Falta ubicar el pórtico en el modelo.** El motor mide en mm desde el eje A sobre una recta. `AgrupadorPorticos.Armar` (`Porticos/AgrupadorPorticos.cs:78`) conoce la línea (`Ux`, `Uy`, `Desplazamiento`, `ZTope`, `ZAbajo`) y el origen (`posiciones[0]`), pero no lo guarda en `PorticoLeido`.
- **La línea de ubicación de Revit no es el centro de la viga.** `LectorModelo` toma `LocationCurve`, que depende de la justificación de la viga. Para el centro lateral hay que usar la geometría, como ya se hace con el ancho.
- **Unidades:** `LectorModelo` pasa las coordenadas internas de Revit (pies) a mm sin transformar. La vuelta es mm ÷ 304.8.
- **Columnas ya probó en Revit real** (comentarios en `BarraContinua.cs` y `GeneradorArmado.cs`):
  - `Rebar.CreateFromCurves` devuelve null, sin excepción, cuando no puede; hay que revisar y avisar;
  - la normal tiene que ser perpendicular a la barra o siempre devuelve null;
  - un `Rebar` puede salirse de su host sin que Revit lo recorte; sale una advertencia "outside of its host";
  - los estribos salen mejor con `CreateFromRebarShape` + `ScaleToBox` si el documento tiene una forma de estribo cerrado; si no, `CreateFromCurves` con el quiebre a mitad de lado;
  - después de `SetLayout…` hay que regenerar antes de leer `NumberOfBarPositions`;
  - la marca para regenerar sin duplicar se hace con un esquema de Extensible Storage (`MarcaEstribos.cs`).
- **Los diámetros de la página están fijos** del #3 al #8 en `tablero.js:908`, `1019`, `1026` y `1061`.

## Decisiones

> Las marcadas **(a confirmar)** son la recomendación; Juan las revisa con el plan antes de programar.

1. **Una barra de Revit por barra física** (2026-10-19, recomendado): cada barra calculada (`S1`, `I2`…) se crea una vez por cada barra de su capa en el corte, en todas las filas, con el diámetro de esa barra. Para `3 #5 + 2 #4` en una fila salen 5 `Rebar` sueltos por cada barra calculada. No se usan conjuntos de Revit, porque las barras de una fila no tienen siempre la misma separación ni el mismo diámetro.
2. ~~**Traslapo en la misma posición del corte**~~. **Traslapo al lado** (2026-10-19, Juan: *"al lado, ¿cómo vas a tener un traslapo abajo? Eso se amarra con alambre"*). Es la pregunta 2 de #79, que queda resuelta así:
   - en cada traslapo, una barra queda en su punto del corte y la otra **al lado, en contacto**: corrida un diámetro (el de la barra corrida) en la horizontal del corte;
   - se alterna por barra calculada: las impares (`S1`, `S3`…) quedan en su punto y las pares (`S2`, `S4`…) se corren. Así cada traslapo tiene una barra en su punto y una al lado;
   - se corre **hacia el centro de la viga**, para que no se salga del estribo; la barra que está justo en el centro se corre hacia `+XCorte`;
   - todas las filas de la capa hacen lo mismo (regla 9 de #80);
   - **límite conocido:** en Revit una barra recta corrida queda corrida en todo su largo, no solo en el traslapo. Fuera del traslapo queda un diámetro más cerca de su vecina, y el corte de Revit difiere del tablero en esa barra por un diámetro. Se anota en el resumen de la prueba (tarea 11) para mostrárselo a Carlos.
3. **Host** (recomendado): cada barra y cada bastón se hospeda en la viga que contiene el punto medio de su parte recta; cada estribo, en la viga que contiene su posición. Las advertencias "outside of its host" se descartan con un `IFailuresPreprocessor`, porque se sabe que la barra recorre varias vigas.
4. **Qué pórticos se generan** (2026-10-19, Juan: "lo que recomiendas"):
   - si la lista está filtrada con "Seleccionar en Revit", todos los de la lista;
   - si no, el pórtico abierto;
   - cada uno con las opciones que tiene guardadas en la sesión (o las de base si nunca se abrió);
   - antes de generar, la página pide confirmar: *"Se generará el acero de N pórticos. Lo que el add-in generó antes en ellos se reemplaza."* con **Generar** y **Cancelar**;
   - un pórtico con error (sin solución, filas que no caben, diámetro sin tipo en el modelo) se salta y se nombra en el resumen.
5. **Una transacción** (recomendado): `Transaction` "Vigas: generar acero" para todos los pórticos. Si una barra no se puede crear, se anota y se sigue; al final se confirma y la página muestra el resumen: barras, bastones, estribos y lo que no se pudo.
6. **Marca y regeneración** (recomendado): esquema de Extensible Storage nuevo `CQEING.Vigas.Acero` con el campo `Portico` (texto, el id del tablero) y `Pieza` (texto, `S1`, `BS2`, `E1`…). Se borran los `Rebar` que tienen la marca **y** cuyo host es una viga del pórtico. Así un cambio de nombre del pórtico no deja barras huérfanas en sus vigas, y nada creado a mano se borra.
7. **Tipo de barra por diámetro** (recomendado): se busca el `RebarBarType` cuyo `BarNominalDiameter` esté a menos de 0.5 mm de `ReglaDiametro.Diametro`; si hay varios, el que se llame `#n`. El tablero solo ofrece esos diámetros (comentario del 17-oct). Si un diámetro elegido no tiene tipo, el pórtico no se genera y se avisa: *"El modelo no tiene un tipo de barra para el #9."*
8. **Ganchos** (2026-10-19, Juan):
   - se busca un `RebarHookType` por ángulo y estilo (90° estándar para barras y bastones, 135° o 90° de estribo), no por nombre, porque cada plantilla los llama distinto;
   - si no hay, se crea `CQEING 90°` o `CQEING Estribo 135°`. Es un tipo nuevo: no cambia ninguno de los que ya tiene el proyecto;
   - ~~si el largo no coincide, se fija con `RebarBarType.SetHookLength`~~. **El add-in nunca cambia los tipos de barra ni de gancho del proyecto** (Juan: *"esto no debe pasar, el tablero debe decir en caso de que falle"*);
   - después de crear cada barra se compara el largo total que da Revit con el `Largo` del tablero. Si difieren en más de **10 mm**, la barra se crea igual y el resumen lo dice, una línea por pieza: *"S1 (Pórtico 2): Revit da 6.32 m y el tablero 6.28 m. El gancho del tipo de barra #5 en Revit no coincide con el cuadro de Reglas."* Las barras iguales de un mismo diámetro se agrupan en una sola línea para no llenar el aviso.
9. **Colores por diámetro** (recomendado): un filtro de vista por diámetro, `CQEING Acero #5`, sobre la categoría de armadura estructural con la regla "diámetro de barra = 15.9 mm", con el color de `ColoresDiametro` en líneas y relleno. Se aplica a la vista activa y a la vista `CQEING Vigas 3D`. Los filtros quedan en el proyecto y se pueden meter en las plantillas de vista, que es lo que hace estándar el color. Pintar elemento por elemento se pierde en cada vista nueva.
10. **Estribos en grupos** (recomendado): las posiciones de cada estribo de un tramo se parten en corridas de separación constante (tolerancia 1 mm). Cada corrida es un `Rebar` con `SetLayoutAsNumberWithSpacing`. Así quedan las zonas de confinamiento y la del centro como en el tablero, con pocas piezas en Revit.
11. **Código de columnas** (recomendado): no se referencia el ensamblado de columnas, que es otro add-in. Se trae a `acero-vigas/Revit/` solo lo necesario (la búsqueda de la forma de estribo, el quiebre a mitad de lado y la revisión del null), con un comentario que diga de dónde sale. El estribo de la viga es vertical y perpendicular al eje, no horizontal como el de la columna, así que la geometría cambia de todos modos.

## Restricciones generales

- Medidas en mm en la librería; pies solo en el lado de Revit, al final (mm ÷ 304.8).
- El marco local del pórtico: `x` a lo largo, en mm desde el eje A (el del motor); `x` crece del primer eje al último. La `X` del corte se mide desde la cara izquierda mirando hacia `+x`, y la `Y` desde la cara de abajo.
- Nada de lo que crea el add-in queda sin la marca de la decisión 6.
- Nombres, mensajes y comentarios en español.
- Commits sin firma de Claude. Comandos con `rtk`.
- `rtk dotnet test CQEING-Addins.sln -c Release` en verde al terminar cada tarea.

## Lo que más puede fallar y ninguna prueba "normal" toca

1. **Pórtico en diagonal o en dirección −X:** el marco local tiene que dar las mismas posiciones en planta que la viga real. Prueba en la tarea 2 con una línea a 30° y otra que `Linea` invierte.
2. **Viga con justificación lateral distinta del centro:** las barras quedan corridas hacia una cara. Prueba en la tarea 2 con el centro de la geometría a 100 mm de la línea de ubicación.
3. **Bastón o barra cuyo punto medio cae en un apoyo** (no en una viga): el host tiene que ser la viga más cercana, no fallar. Prueba en la tarea 3.
4. **Tramo corto con un solo estribo**, o zona con dos: la corrida con 1 o 2 posiciones no se puede crear con separación 0. Prueba en la tarea 4.
5. **Regenerar después de borrar una viga a mano:** la marca apunta a un host que ya no existe y no debe reventar. Se comprueba en la tarea 6 en Revit.

---

### Tarea 1: un solo cálculo para la vista y para la generación

**Archivos:**
- Crear: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Tablero/CalculoPortico.cs`
- Modificar: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Tablero/ArmadorVista.cs:33-90` (usa `CalculoPortico`)
- Test: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests/CalculoPorticoTests.cs`

**Interfaces:**
- Produce:
  - `public sealed record PorticoCalculado(ResultadoVigas Vigas, ResultadoEstribos Estribos, EntradaSeccion Seccion, IReadOnlyDictionary<string, ConfigEstribos>? PorTramo, IReadOnlyList<Aviso> Avisos, bool SePuedeGenerar);` (`PorTramo` lo usan las zonas de la vista)
  - `public static class CalculoPortico { public static PorticoCalculado? Calcular(PorticoLeido p, OpcionesTablero o, Reglas r); public static IReadOnlyList<int> DiametrosSinReglas(OpcionesTablero o, Reglas r); internal static EntradaVigas EntradaDe(...); internal static EntradaSeccion Seccion(...); }` → `Calcular` da null si falta un diámetro en las reglas (el caso que hoy devuelve la vista vacía).

- [x] **Paso 1: prueba que falla.** `CalculoPorticoTests.MismoResultadoQueLaVista` (con `3 #5 + 2 #4` arriba: kg, barras, estribos y `SePuedeGenerar` iguales a la vista), `NoSePuedeGenerarSiLasBarrasNoCaben` y `DiametroSinReglas`.
- [x] **Paso 2:** falla porque `CalculoPortico` no existe.
- [x] **Paso 3:** `CalculoPortico` tiene el cálculo, la entrada del motor, la sección y los avisos que bloquean. `ArmadorVista.Armar` lo llama; `EditorTablero` usa `CalculoPortico.EntradaDe`.
- [x] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release`: 737 pruebas en verde, sin advertencias.
- [x] **Paso 5:** commit `refactor(vigas): el cálculo del pórtico sale de la vista (#85)`.

### Tarea 2: dónde está el pórtico en el modelo

**Archivos:**
- Modificar: `calculo/.../Porticos/ModeloLeido.cs` (`VigaLeida` gana `Punto? Centro = null`; `PorticoLeido` gana `UbicacionPortico? Ubicacion = null`)
- Modificar: `calculo/.../Porticos/AgrupadorPorticos.cs:78-120` (llena `Ubicacion`)
- Modificar: `addins/acero-vigas/Revit/LectorModelo.cs:24-42` (llena `Centro`: el centro en planta de los puntos de la geometría)
- Crear: `calculo/.../Generacion/MarcoPortico.cs`
- Test: `calculo/CQEING.Vigas.Calculo.Tests/MarcoPorticoTests.cs` y un caso más en `AgrupadorPorticosTests.cs`

**Interfaces:**
- Produce:
  - `public sealed record VigaEnPortico(string Id, int Desde, int Hasta);` (mm desde el eje A)
  - `public sealed record UbicacionPortico(double Ux, double Uy, double OrigenX, double OrigenY, double ZAbajo, double ZArriba, double Ancho, double Alto, IReadOnlyList<VigaEnPortico> Vigas);`
    - `OrigenX`, `OrigenY`: el punto en planta del eje A sobre el **centro lateral** de la viga, en mm del modelo;
    - `ZAbajo`, `ZArriba`: las caras de abajo y de arriba de la viga.
  - `public readonly record struct Punto3(double X, double Y, double Z);` (mm del modelo)
  - `public static class MarcoPortico { public static Punto3 AlModelo(UbicacionPortico u, double x, double xCorte, double yCorte); public static string? VigaEn(UbicacionPortico u, double x); }`

- [x] **Hecho** (2026-10-19): pasos 1 a 5. Pruebas `MarcoPorticoTests` (3) y `AgrupadorPorticosTests.LaUbicacion_*` (2); 742 en verde y el add-in compila para 2026 y 2027. El centro de `LectorModelo` (la caja en planta de la geometría) se comprueba con el modelo real en la tarea 7.
- [x] **Paso 1: pruebas que fallan.**
  - `MarcoPorticoTests.EnX`: `Ux=1, Uy=0`, origen `(1000, 2000)`, `ZAbajo=3000`, `Ancho=300`. `AlModelo(u, 500, 0, 0)` = `(1500, 2150, 3000)`: la cara izquierda, mirando a `+x`, es la de `+y`. `AlModelo(u, 500, 300, 450)` = `(1500, 1850, 3450)`.
  - `MarcoPorticoTests.EnDiagonal`: con `Ux=cos30°, Uy=sin30°`, `AlModelo(u, 1000, 150, 0)` cae en `origen + 1000·(Ux, Uy)` (el centro), a ±0.01 mm.
  - `MarcoPorticoTests.VigaMasCercana`: con vigas `0–4000` y `4400–8000`, `VigaEn(u, 4200)` es la de `4400–8000` si está más cerca; `VigaEn(u, 2000)` es la primera.
  - `AgrupadorPorticosTests.UbicacionConJustificacion`: una viga con `Centro` corrido 100 mm de la línea de ubicación da `OrigenY` sobre el centro, no sobre la línea.
- [ ] **Paso 2:** correr → fallan.
- [ ] **Paso 3:** implementar. La fórmula del marco, con `n = (−Uy, Ux)` (la izquierda mirando a `+x`):

  ```
  planta = Origen + x·(Ux, Uy) + (Ancho/2 − xCorte)·n
  z      = ZAbajo + yCorte
  ```

  En `AgrupadorPorticos`, el centro lateral es el promedio de `linea.Normal(v.Centro)` de las vigas de la cadena (o `linea.Desplazamiento` si no traen `Centro`); el origen es `posiciones[0]` a lo largo. `Ancho` y `Alto` salen del primer tramo (el cambio de sección es #92).
- [ ] **Paso 4:** suite en verde.
- [ ] **Paso 5:** commit `feat(vigas): ubicación del pórtico en el modelo (#85)`.

### Tarea 3: el plan de barras corridas y bastones

**Archivos:**
- Crear: `calculo/.../Generacion/PlanAcero.cs` (los records)
- Crear: `calculo/.../Generacion/PlanificadorAcero.cs`
- Test: `calculo/CQEING.Vigas.Calculo.Tests/PlanificadorAceroTests.cs`

**Interfaces:**
- Consume: `CalculoPortico.Calcular` (tarea 1), `UbicacionPortico` y `MarcoPortico.VigaEn` (tarea 2), `UbicadorBastones.Ubicar`.
- Produce:
  - `public sealed record PiezaLongitudinal(string Pieza, Posicion Capa, int Diametro, double XCorte, double YCorte, int Desde, int Hasta, TipoGancho GanchoInicio, TipoGancho GanchoFin, string Host);`
    - `Pieza`: el id de la barra o del bastón (`S1`, `BI2`); se repite en todas sus barras físicas;
    - el gancho siempre dobla hacia adentro de la viga: hacia abajo en la capa superior y hacia arriba en la inferior.
  - `public sealed record PlanAcero(string Portico, IReadOnlyList<PiezaLongitudinal> Barras, IReadOnlyList<PiezaLongitudinal> Bastones, IReadOnlyList<GrupoEstribos> Estribos, IReadOnlyList<int> Diametros);`
  - `public static class PlanificadorAcero { public static PlanAcero? Planificar(string idPortico, PorticoLeido p, OpcionesTablero o, Reglas r); }` → null si `SePuedeGenerar` es falso o `p.Ubicacion` es null.

- [x] **Hecho** (2026-10-19): 10 pruebas en `PlanificadorAceroTests`. Cambio sobre lo previsto: `PlanAcero` todavía no trae `Estribos` (los agrega la tarea 4) y trae `BastonesSinLugar`, los ids de los bastones que no caben en el corte y no se crean.
- [x] **Paso 1: pruebas que fallan**, con el pórtico de la maqueta y una `UbicacionPortico` en `+X`:
  - `UnaPiezaPorBarraFisica`: con `3 #5` arriba y abajo, `Barras.Count` = (barras calculadas de las dos capas) × 3.
  - `VariasFilas`: con `7 #5` arriba en una viga donde caben 5, las piezas de `S1` tienen 5 `YCorte` iguales y 2 más bajos (fila 2), y las mismas `XCorte` que `Estribos.Seccion.Superior.Filas`.
  - `DosDiametros`: con `3 #5 + 2 #4`, cada pieza toma el `Diametro` de su barra del corte.
  - `GanchosDelBorde`: `S1` lleva `GanchoInicio = De90` y la última, `GanchoFin = De90`; las puntas de traslapo, `Sin`.
  - `Cimentacion`: con `Tipo = Cimentacion`, los bastones de los apoyos salen en `Capa = Inferior`.
  - `BastonEnSuPuesto`: cada bastón tiene la `XCorte`/`YCorte` que da `UbicadorBastones.Ubicar(seccion, capa, [grupo])`.
  - `HostEnApoyo`: un bastón centrado en el eje B (punto medio dentro de la columna) tiene `Host` = la viga más cercana.
  - `NoSePuedeGenerar`: con filas que no caben, `Planificar` devuelve null.
  - `TraslapoAlLado`: en una viga de 300 con `3 #5` arriba y dos barras `S1` y `S2`, las piezas de `S1` tienen la `XCorte` del corte; las de `S2` están corridas 15.9 mm hacia el centro (la de la izquierda hacia `+`, la de la derecha hacia `−`, la del centro hacia `+`), con la misma `YCorte`.
  - `SinTraslapoNoSeCorre`: un pórtico de una sola barra por capa no corre nada.
- [ ] **Paso 2:** correr → fallan.
- [ ] **Paso 3:** implementar. Una barra calculada × cada `BarraEnSeccion` de todas las filas de su capa (decisión 1); las pares se corren al lado (decisión 2). `Diametros` = los distintos de barras, bastones y estribos.
- [ ] **Paso 4:** suite en verde.
- [ ] **Paso 5:** commit `feat(vigas): plan de barras y bastones para Revit (#85)`.

### Tarea 4: el plan de los estribos

**Archivos:**
- Modificar: `calculo/.../Generacion/PlanAcero.cs` (agrega `GrupoEstribos`)
- Crear: `calculo/.../Generacion/CorridasEstribos.cs`
- Modificar: `calculo/.../Generacion/PlanificadorAcero.cs`
- Test: `CorridasEstribosTests.cs` y casos en `PlanificadorAceroTests.cs`

**Interfaces:**
- Produce:
  - `public sealed record Corrida(double Primero, int Cantidad, double Separacion);`
  - `public static class CorridasEstribos { public static IReadOnlyList<Corrida> Partir(IReadOnlyList<double> posiciones, double tolerancia = 1); }`
  - `public sealed record GrupoEstribos(string Pieza, TipoEstribo Tipo, int Diametro, GanchoEstribo Gancho, double Izquierda, double Derecha, double Abajo, double Arriba, Corrida Corrida, string Host);` (caras exteriores en el corte, como `EstriboEnSeccion`)

- [x] **Hecho** (2026-10-19): `CorridasEstribosTests` (4) y 3 casos más en `PlanificadorAceroTests`; 759 en verde. `GrupoEstribos` lleva también `Tramo`. La separación de cada corrida es la promedio, para que el ruido de decimales no se acumule. El host es la viga del centro de la corrida.
- [x] **Paso 1: pruebas que fallan.**
  - `Partir([100, 200, 300, 500, 700, 800, 900])` = `(100, 3, 100)`, `(500, 2, 200)`… Escribir la prueba con el reparto exacto de una luz de la maqueta (zona 0.80 @0.10, centro @0.20) sacado de `MotorEstribos`, y comprobar que la suma de `Cantidad` es igual a `Posiciones.Count`.
  - `Partir([350])` = `(350, 1, 0)`; `Partir([])` = vacío.
  - `PlanificadorAceroTests.EstribosPorTramo`: un `GrupoEstribos` por corrida de cada `EstriboEnPortico`, con las caras de su `EstriboEnSeccion` (mismo `Id`), y `Host` = la viga del tramo.
  - `PlanificadorAceroTests.Adicional`: un adicional `E1` abierto sale con `Tipo = Abierto` y `Izquierda = Derecha`.
- [ ] **Paso 2:** correr → fallan.
- [ ] **Paso 3:** implementar. Una corrida termina cuando la separación siguiente difiere de la de la corrida en más de `tolerancia`.
- [ ] **Paso 4:** suite en verde.
- [ ] **Paso 5:** commit `feat(vigas): plan de estribos por corridas (#85)`.

### Tarea 5: el mensaje "generar" y los diámetros del modelo

> **Cambio (2026-10-19, Juan):** para poder probar en Revit desde la tarea 7, esta tarea también conecta el botón "Generar en Revit" de forma mínima: envía `{tipo:'generar', ids:[P.id]}` con el pórtico abierto, sin confirmación, y muestra el aviso que llegue. La confirmación, varios pórticos y el resumen completo siguen en la tarea 10.

**Archivos:**
- Modificar: `calculo/.../Tablero/ProtocoloTablero.cs` (mensaje `generar`)
- Modificar: `calculo/.../Tablero/SesionTablero.cs` (atiende `Generar`; recibe los diámetros disponibles)
- Modificar: `calculo/.../Tablero/VistaTablero.cs` (`VistaTablero` gana `IReadOnlyList<int> DiametrosDisponibles`)
- Test: `ProtocoloTableroTests.cs`, `SesionTableroTests.cs`

**Interfaces:**
- Consume: `PlanificadorAcero.Planificar` (tarea 3).
- Produce:
  - `public sealed record Generar(IReadOnlyList<string> Ids) : MensajeTablero;` — JSON `{"tipo":"generar","ids":[…]}`
  - `public sealed record AGenerar(IReadOnlyList<PorticoAGenerar> Porticos, IReadOnlyList<string> Saltados) : Salida;`
  - `public sealed record PorticoAGenerar(string Id, IReadOnlyList<string> Vigas, PlanAcero Plan);`
  - `SesionTablero` recibe en el constructor `Func<IReadOnlyList<int>> diametrosDelModelo`.
  - `public static string Resumen(ResumenGeneracion r)` en `ProtocoloTablero` → `{"tipo":"generado",…}`
  - `public sealed record ResumenGeneracion(int Porticos, int Barras, int Bastones, int Estribos, IReadOnlyList<string> Fallas, IReadOnlyList<DiferenciaLargo> Diferencias);`
  - `public sealed record DiferenciaLargo(string Portico, string Pieza, int Diametro, int LargoRevit, int LargoTablero);` (mm; decisión 8)
  - `public static IReadOnlyList<string> Textos(IReadOnlyList<DiferenciaLargo> d)` en `ProtocoloTablero`: una línea por pórtico, pieza y diámetro, con el texto de la decisión 8.

- [x] **Hecho** (2026-10-19): 3 pruebas en `ProtocoloTableroTests`, 5 en `SesionTableroTests` y `DiametrosDisponiblesTests` (2); 769 en verde. Cambios sobre lo previsto:
  - la sesión recibe `Func<IEnumerable<double>> nominalesDelModelo` (el diámetro nominal en mm de cada tipo de barra) y los traduce a `#n` con sus reglas vigentes, con `DiametrosDisponibles.De` (tolerancia 0.5 mm);
  - `PorticoAGenerar` lleva también la `UbicacionPortico`, que necesita el lado de Revit;
  - `ProtocoloTablero.Resumen` manda `texto` ya armado (una línea por cosa), que la página muestra como aviso de 10 s;
  - mientras llegan las tareas 6 y 7, `PuenteTablero` contesta "generar" con lo que se generaría (barras, bastones y estribos por pórtico), marcado `TEMPORAL`.
- [x] **Paso 1: pruebas que fallan.**
  - `ProtocoloTableroTests.LeeGenerar`: `{"tipo":"generar","ids":["Pórtico 1","Pórtico 2"]}` → `Generar` con los dos ids.
  - `SesionTableroTests.GenerarUsaLasOpcionesGuardadas`: después de `Calcular("Pórtico 1", o)`, `Generar(["Pórtico 1"])` da un `AGenerar` cuyo plan se armó con `o`.
  - `SesionTableroTests.GenerarSaltaLosQueTienenError`: un pórtico con filas que no caben va a `Saltados`, no a `Porticos`.
  - `SesionTableroTests.DiametroSinTipoEnElModelo`: si `diametrosDelModelo` no trae el 5, el pórtico con `#5` va a `Saltados` con el motivo *"El modelo no tiene un tipo de barra para el #5."*
  - `SesionTableroTests.VistaConDiametrosDisponibles`: la vista trae los diámetros que devuelve `diametrosDelModelo`.
  - `ProtocoloTableroTests.TextoDiferenciaLargo`: `DiferenciaLargo("Pórtico 2", "S1", 5, 6320, 6280)` da *"S1 (Pórtico 2): Revit da 6.32 m y el tablero 6.28 m. El gancho del tipo de barra #5 en Revit no coincide con el cuadro de Reglas."*
- [ ] **Paso 2:** correr → fallan.
- [ ] **Paso 3:** implementar.
- [ ] **Paso 4:** suite en verde.
- [ ] **Paso 5:** commit `feat(vigas): mensaje generar y diámetros del modelo (#85)`.

### Tarea 6: tipos, marca y borrado en Revit

**Archivos:**
- Crear: `addins/acero-vigas/Revit/TiposAcero.cs`
- Crear: `addins/acero-vigas/Revit/MarcaAcero.cs`
- Crear: `addins/acero-vigas/Revit/GeneradorAcero.cs` (la transacción; en esta tarea solo borra)
- Modificar: `addins/acero-vigas/Tablero/PuenteTablero.cs` (pasa `diametrosDelModelo` a la sesión y atiende `AGenerar`)

**Interfaces:**
- Consume: `AGenerar`, `PorticoAGenerar` y `ResumenGeneracion` (tarea 5).
- Produce:
  - `internal sealed class TiposAcero { public static TiposAcero Leer(Document doc, Reglas r); public RebarBarType? Barra(int diametro); public IReadOnlyList<int> Diametros { get; } public RebarHookType Gancho(TipoGancho t); public RebarHookType GanchoEstribo(GanchoEstribo g); }` (decisiones 7 y 8; crea el gancho si falta, dentro de la transacción)
  - `internal static class MarcaAcero { public static void Marcar(Element e, string portico, string pieza); public static ElementFilter Filtro(); public static int BorrarAnteriores(Document doc, IEnumerable<string> vigas); }` (decisión 6; Guid nuevo, `VendorId` "CQEING")
  - `internal static class GeneradorAcero { public static ResumenGeneracion Generar(Document doc, IReadOnlyList<PorticoAGenerar> porticos, Reglas r); }`

- [ ] **Paso 1:** implementar `MarcaAcero`, `TiposAcero` y el esqueleto de `GeneradorAcero`: abre la transacción "Vigas: generar acero" con un `IFailuresPreprocessor` que borra las advertencias (decisión 3), borra lo anterior de cada pórtico y confirma.
- [ ] **Paso 2:** `rtk dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2026` sin errores ni advertencias nuevas.
- [ ] **Paso 3: en Revit.** Crear a mano un `Rebar` en una viga y marcarlo con un comando de diagnóstico temporal; generar; comprobar que se borra el marcado y no uno sin marca. Borrar la viga y regenerar: no revienta.
- [ ] **Paso 4:** commit `feat(vigas): tipos de barra, marca y borrado al regenerar (#85)`.

### Tarea 7: barras corridas y bastones en Revit

**Archivos:**
- Crear: `addins/acero-vigas/Revit/CreadorBarras.cs`
- Modificar: `addins/acero-vigas/Revit/GeneradorAcero.cs`

**Interfaces:**
- Consume: `PiezaLongitudinal`, `MarcoPortico.AlModelo`, `TiposAcero`, `MarcaAcero`.
- Produce: `internal static class CreadorBarras { public static Rebar? Crear(Document doc, UbicacionPortico u, PiezaLongitudinal p, TiposAcero tipos); }` → null si Revit no pudo; el generador lo anota en `Fallas` con el pórtico y la pieza.

- [ ] **Paso 1:** implementar. La curva es la línea entre `AlModelo(u, Desde, XCorte, YCorte)` y `AlModelo(u, Hasta, XCorte, YCorte)`, en pies. La normal del plano de la barra es la horizontal lateral `n` de la tarea 2 (perpendicular a la barra, como exige Revit). Ganchos con `BarTerminationsData`, como en `BarraContinua.cs:257-280`.
- [ ] **Paso 2: medir en Revit** con el pórtico de la maqueta. Anotar los valores en "Hallazgos" de este plan:
  - qué `RebarTerminationOrientation` hace que el gancho de arriba doble hacia abajo y el de abajo hacia arriba;
  - si el largo total de Revit (`TotalLength` ÷ cantidad) da el `Largo` del tablero, y cuánto difiere con los tipos de barra de la plantilla de Carlos.
- [ ] **Paso 2b:** comparar cada barra creada con su `Largo` y llenar `ResumenGeneracion.Diferencias` (decisión 8). No se toca ningún tipo del proyecto.
- [ ] **Paso 3:** generar el pórtico aéreo con voladizo. Comprobar en una vista de alzado que cada barra va de `Desde` a `Hasta` del tablero y que los traslapos miden lo mismo (cota de Revit contra la lista de barras del tablero).
- [ ] **Paso 4:** commit `feat(vigas): barras corridas y bastones en Revit (#85)`.

### Tarea 8: estribos en Revit

**Archivos:**
- Crear: `addins/acero-vigas/Revit/CreadorEstribos.cs` (con lo que se trae de columnas, decisión 11)
- Modificar: `addins/acero-vigas/Revit/GeneradorAcero.cs`

**Interfaces:**
- Consume: `GrupoEstribos`, `MarcoPortico.AlModelo`, `TiposAcero.GanchoEstribo`.
- Produce: `internal static class CreadorEstribos { public static Rebar? Crear(Document doc, UbicacionPortico u, GrupoEstribos g, TiposAcero tipos); }`

- [ ] **Paso 1:** implementar.
  - **Cerrado:** el rectángulo del eje del estribo (las caras exteriores menos medio diámetro) en el plano perpendicular a la viga en `Corrida.Primero`. Primero `CreateFromRebarShape` + `ScaleToBox` con la forma de estribo cerrado del documento; si no hay o falla, `CreateFromCurves` con el quiebre a mitad de lado.
  - **Abierto:** una rama vertical con ganchos en las dos puntas, con la forma de grapa del documento si la hay (como `GeometriaSeccion.BuscarFormaGrapa` de columnas).
  - **Reparto:** con `Cantidad > 1`, `SetLayoutAsNumberWithSpacing(Cantidad, Separacion, …)` en la dirección `+x`; con 1, sin reparto. Regenerar antes de leer cuántos quedaron.
- [ ] **Paso 2: en Revit.** En el pórtico aéreo: contar estribos por tramo contra la lista de estribos del tablero, incluido un tramo corto. Agregar un adicional en el corte y comprobar que abraza las mismas barras.
- [ ] **Paso 3:** commit `feat(vigas): estribos en Revit (#85)`.

### Tarea 9: colores por diámetro en Revit

**Archivos:**
- Crear: `addins/acero-vigas/Revit/FiltrosColor.cs`
- Modificar: `addins/acero-vigas/Revit/GeneradorAcero.cs`
- Modificar: `calculo/.../Modelo/ColoresDiametro.cs` (agrega `public static (byte R, byte G, byte B) Rgb(int diametro)`)
- Test: `ColoresDiametroTests.cs`

**Interfaces:**
- Produce: `internal static class FiltrosColor { public static void Aplicar(Document doc, IEnumerable<View> vistas, IEnumerable<int> diametros, Reglas r); }`

- [ ] **Paso 1: prueba que falla.** `ColoresDiametroTests.Rgb`: `Rgb(5)` = `(0xD3, 0x2F, 0x2F)`; `Rgb(99)` da el de `SinColor`.
- [ ] **Paso 2:** correr → falla; implementar `Rgb`; correr → pasa.
- [ ] **Paso 3:** implementar `FiltrosColor` (decisión 9). Si el filtro ya existe, se actualiza su color, no se crea otro. Se aplica a la vista activa y a `AccionesRevit.Nombre3d` si existe.
- [ ] **Paso 4: en Revit.** El #5 sale rojo y el #4 naranja en planta, alzado y 3D, y regenerar no duplica filtros.
- [ ] **Paso 5:** commit `feat(vigas): colores por diámetro en Revit (#85)`.

### Tarea 10: el botón en la página

**Archivos:**
- Modificar: `addins/acero-vigas/Tablero/web/tablero.js:598,653,677` (botón, confirmación y resumen)
- Modificar: `addins/acero-vigas/Tablero/web/tablero.js:908,1019,1026,1061` (las listas de diámetros salen de `diametrosDisponibles`)
- Modificar: `addins/acero-vigas/Tablero/web/demo.js` (los mismos campos en la demostración)
- Actualizar la maqueta con la tarjeta de confirmación y el resumen, antes de tocar la página (regla de #100: la maqueta es la referencia).

- [ ] **Paso 1:** la tarjeta de confirmación de la decisión 4, con el estilo de las tarjetas de aviso de la maqueta. **Generar** envía `{tipo:'generar', ids}`.
- [ ] **Paso 2:** al llegar `generado`, el aviso de la maqueta con el resumen: *"Se generaron 2 pórticos: 48 barras, 7 bastones y 112 estribos."* y, si hay, las fallas, los saltados y las diferencias de largo con Revit, una por línea.
- [ ] **Paso 3:** las listas de diámetros muestran solo `diametrosDisponibles`, con su texto en pulgadas como hoy.
- [ ] **Paso 4: en Revit.** Ningún texto se corta ni se parte en dos líneas; sin scroll (#100).
- [ ] **Paso 5:** commit `feat(vigas): Generar en Revit desde el tablero (#85)`.

### Tarea 11: prueba de aceptación en Revit

- [ ] **Pórtico aéreo con voladizo** (modelo de Carlos): largos, traslapos, bastones, estribos y colores iguales al tablero; ningún traslapo en el voladizo.
- [ ] **Pórtico de cimentación:** capas invertidas, bastones de apoyo abajo, recubrimiento de abajo distinto.
- [ ] **Pórtico con varias filas** y dos diámetros en una cara: el corte de Revit en la línea 1-1 coincide con el corte del tablero.
- [ ] **Varios pórticos** con "Seleccionar en Revit": se generan juntos y un Ctrl+Z los quita todos.
- [ ] **Regenerar** cambiando el armado: no queda nada del anterior.
- [ ] Anotar en "Estado" lo medido, con capturas en `Desktop\addin vigas`.

## Cómo probarlo

```
rtk dotnet test CQEING-Addins.sln -c Release
rtk dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026
```

## Estado

- [x] Plan revisado con Juan (2026-10-19): traslapo al lado, pórticos como se recomendó, y los tipos del proyecto no se tocan.
- [ ] Contarle a Carlos la decisión 2 (traslapo al lado) y su límite en Revit; cerrar la pregunta 2 de #79.
- [ ] Pendiente con Carlos antes de la tarea 8: el estribo de 135° del #6 al #8 (0.272, 0.316 y 0.362), según #82.
- [ ] Tareas 1 a 5 (librería, con pruebas).
- [ ] Tareas 6 a 10 (Revit y página).
- [ ] Tarea 11 (aceptación en Revit).
