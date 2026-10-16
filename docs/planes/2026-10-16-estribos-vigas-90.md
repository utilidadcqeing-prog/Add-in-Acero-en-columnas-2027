# Plan: estribos en vigas (#90)

> **Para agentes:** SUB-SKILL REQUERIDA: superpowers:subagent-driven-development (recomendada) o superpowers:executing-plans, tarea por tarea. Los pasos usan casillas (`- [ ]`).

**Objetivo:** que el motor ponga los estribos de un pórtico con sus zonas (el cerrado de las esquinas y los adicionales que el usuario elige con clics), con su forma, su largo y sus kilos, y que mueva las barras que deben quedar alineadas.

**Arquitectura:** un motor nuevo, `MotorEstribos`, en la librería sin Revit (`CQEING.Vigas.Calculo`). Necesita la sección, cosa que `MotorVigas` no recibe, y por eso va aparte, como `RepartidorFilas` (#88) y `UbicadorBastones` (#89).
- `DistribuidorEstribos` reparte los estribos a lo largo del pórtico.
- `AlineadorEstribos` acomoda los adicionales en la sección y mueve las barras.
- El total que muestra el tablero es `ResultadoVigas.Kg + ResultadoEstribos.Kg`.
- La pantalla es de #83, el dibujo del corte de #91 y la creación en Revit de #85.

**Tecnología:** C# `net8.0` y xUnit.

**Fuente:**
- la issue [#90](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/90);
- la transcripción del 16-oct (`Downloads\Meeting Transcription (15).txt`, min 59:58–1:08:34 y 1:21:21–1:24:02);
- la primera reunión (min 24:57: los 5 cm del primer estribo se miden *"teniendo en cuenta el extremo"*);
- `Desktop\addin vigas\traslapos-vigas-notas.md` §0 "Estribos";
- la maqueta, paso "3 Estribos" y corte 1-1 (https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh, versión 16).

## Contexto

- **Referencia:** la maqueta, que ya trae estas decisiones (versión 16). Donde no coincide con Carlos, manda Carlos.
- **Lo que pinta la maqueta:**
  - **paso 3:**
    - el alcance: "Se aplica a todo el pórtico" o "a N tramos marcados";
    - Estribo [#3 | #4] [135° | 90°];
    - "Junto a las columnas": largo de la zona y "un estribo cada";
    - "En el centro": "un estribo cada";
    - "Estribos adicionales": la lista con "Quitar", los botones "+ Cerrado" y "+ Abierto", y el aviso que pide los clics en el corte con "Agregar" y "Cancelar";
  - **corte 1-1:** las barras se pueden pulsar; las que mandan se marcan en naranja oscuro y las demás en naranja claro; se dibujan los adicionales;
  - **lista:** filas para el estribo cerrado y cada adicional (cantidad, largo y kg); los estribos suman al total.
- **Ya existe:**
  - `GeometriaPortico`, con las luces libres entre caras;
  - `RepartidorFilas` (las barras en el corte) y `Reglas.PorDiametro`, con `Estribo135`, `Gancho90`, los kg/m y los diámetros.
- **Rama:** `vigas-estribos-90`, que sale de `main` (ya trae #80, #88, #81, #82 y #89).

## Decisiones (Juan y Carlos, 2026-10-16)

1. **El cerrado de las esquinas es obligatorio** y sale solo (Carlos: *"una obligación absoluta"*). Abraza las barras de esquina por fuera: va del recubrimiento libre de cada cara hacia adentro.
2. **Zonas** (Carlos, min 1:21:21):
   - por luz, una zona junto a cada columna de `LargoZona` (800) con un estribo cada `SeparacionZona` (100), y en el centro un estribo cada `SeparacionCentro` (200), como máximo;
   - el **primer estribo va a 5 cm de la cara del apoyo, medidos hasta la cara del estribo** (Carlos, primera reunión, min 24:57), así que su centro queda a `50 + dEstribo/2`;
   - en cada zona van a la separación exacta, desde el primero hasta el final de la zona;
   - en el centro se reparten a la misma distancia entre el último de cada zona, sin pasar de `SeparacionCentro`.
3. **Luz corta** (Carlos, min 1:22:43): si la luz libre es menor que `2 × LargoZona`, toda la luz va a la misma distancia, sin pasar de `SeparacionZona`, entre el primero y el último (cada uno a 5 cm de su cara).
4. **Voladizo** (recomendación, con el visto bueno de Juan): todo con `SeparacionZona`, desde 5 cm de la cara del apoyo hasta 5 cm de la punta del concreto.
5. **Diámetro y gancho:** por defecto #3 con 135°; se puede elegir 90° (Carlos, min 1:23:18). Gancho de 135° = `Estribo135.B`; de 90° = `Gancho90.B`. **Los ganchos van corridos**, todos en la misma esquina (min 1:24:02); eso es de #85.
6. **Por tramo** (Carlos, min 1:08:34): hay una configuración del pórtico y otra opcional por tramo (`"A-B"`, `"Voladizo D"`).
   - Cada una tiene zonas, diámetro, gancho y adicionales.
   - La sección usa el diámetro del pórtico; el de un tramo solo cambia el largo y los kilos de sus estribos.
7. **Adicionales** (Carlos, min 1:02:16–1:06:16):
   - el **cerrado** necesita exactamente 2 barras arriba y 2 abajo; el **abierto**, 1 y 1. Si no, aviso `EstriboIncompleto`;
   - **las primeras que se eligen mandan** (2 en el cerrado, 1 en el abierto): sus x son las verticales;
   - las demás se ordenan por x y se mueven a esas verticales (si hay tantas como verticales, en orden; si no, a la más cercana);
   - **la barra que se mueve lo hace en todo el pórtico**, porque es continua (Carlos: *"las barras… son continuas"*). El adicional va solo en el tramo de su configuración;
   - **no se viola la distancia libre** (Carlos: *"vamos a respetar siempre la distancia"*): si la barra movida queda a menos de la distancia libre de su vecina, aviso `NoSePuedeAlinear` y ese adicional no se pone;
   - **una barra de esquina de la fila 1 no se mueve**, porque la abraza el cerrado obligatorio: aviso `MueveUnaEsquina`;
   - una barra que no existe (fila o número fuera de rango) da el aviso `BarraNoExiste`.
8. **Forma y largo** (en mm, por el eje del estribo):
   - **cerrado:** `2 × (ancho + alto) + 2 × gancho`;
   - **abierto:** `alto + 2 × gancho`.
   - En el principal, `ancho = Ancho − 2·recLados − dE` y `alto = Alto − recArriba − recAbajo − dE`.
   - En el adicional cerrado, `ancho = xDerecha − xIzquierda + dBarraMayor + dE`.
9. **Kilos:** `cantidad × Largo / 1000 × KgPorMetro` del diámetro del estribo. Suman al total del tablero.
10. **Valores no válidos:** con `SeparacionZona` o `SeparacionCentro` ≤ 0, o `LargoZona` < 0, sale el aviso `EstriboValorNoValido` con el nombre del tramo (o `"general"`) y ese tramo queda sin estribos. Un diámetro que no está en las reglas da `DiametroSinReglas`. `LargoZona` = 0 es válido: sin zona, toda la luz va con `SeparacionCentro`.

## Restricciones generales

- Posiciones y medidas de la sección en mm `double`; los kilos en kg/m de `Reglas`.
- Nombres, mensajes y comentarios en español. Commits sin firma. Comandos con `rtk`.
- `rtk dotnet test CQEING-Addins.sln -c Release` en verde al terminar cada tarea.

## Lo que más puede fallar y ninguna prueba "normal" toca

1. **Una luz más corta que los dos primeros estribos** (luz libre ≤ 109.5 mm): sale un solo estribo, al centro, sin reventar. Prueba en la tarea 1.
2. **`LargoZona` mayor que media luz, pero sin llegar a ser luz corta** (la luz es igual a `2 × LargoZona`): cuenta como zona y centro, y no sale un centro negativo. Prueba en la tarea 1.
3. **Dos adicionales que mueven la misma barra a sitios distintos:** el segundo da `NoSePuedeAlinear`. Prueba en la tarea 3.
4. **La sección ya tiene un error de la #88:** los adicionales no se ponen (`NoSePuedeAlinear`), pero el cerrado principal y sus kilos siguen. Prueba en la tarea 3.
5. **El mismo número elegido dos veces:** cuenta como una sola barra, así que da `EstriboIncompleto`. Prueba en la tarea 3.

---

### Tarea 1: estribos a lo largo del pórtico

**Archivos:**
- Crear: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Modelo/Estribos.cs`
- Crear: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Estribos/DistribuidorEstribos.cs`
- Modificar: `.../Modelo/Resultado.cs`, para agregar `TipoAviso.EstriboValorNoValido`
- Prueba: `.../CQEING.Vigas.Calculo.Tests/DistribuidorEstribosTests.cs`

**Interfaces:**
- Produce:
  - `public enum TipoEstribo { Cerrado, Abierto }` y `public enum GanchoEstribo { De135, De90 }`;
  - `public sealed record PuntoBarra(Posicion Cara, int Fila, int Numero);`, donde `Fila` y `Numero` empiezan en 1 y `Numero` va de izquierda a derecha dentro de la fila;
  - `public sealed record EstriboAdicional(TipoEstribo Tipo, IReadOnlyList<PuntoBarra> Barras);`, con las barras en el orden en que se eligieron;
  - `public sealed record ConfigEstribos(int Diametro = 3, GanchoEstribo Gancho = GanchoEstribo.De135, int LargoZona = 800, int SeparacionZona = 100, int SeparacionCentro = 200, IReadOnlyList<EstriboAdicional>? Adicionales = null);`
  - `public sealed record Tramo(string Nombre, double CaraIzquierda, double CaraDerecha, bool EsVoladizo);`, donde en un voladizo una de las "caras" es la punta del concreto;
  - `internal static IReadOnlyList<Tramo> DistribuidorEstribos.Tramos(Portico p)`: las luces (`"A-B"`) y los voladizos (`"Voladizo A"`, `"Voladizo D"`), de izquierda a derecha;
  - `internal static IReadOnlyList<double> DistribuidorEstribos.Posiciones(Tramo t, ConfigEstribos c, double diametroEstribo)`: el centro de cada estribo, en mm desde el eje A.

Datos de prueba: `Datos.PorticoMaqueta()` y estribo #3 (9.5), así que el primero queda a 54.75 de la cara.
- Luces libres: A-B de 200 a 4200, B-C de 4600 a 7700 y C-D de 8100 a 12100.
- Voladizo D: de 12500 a 14000.

- [ ] **Paso 1: pruebas que fallan:**
  - `LosTramosDelPorticoSonSusLucesYSuVoladizo`: `["A-B", "B-C", "C-D", "Voladizo D"]`. El voladizo va de 12500 a 14000, con `EsVoladizo` true.
  - `EnLaLuzAB_OchoPorZonaYDoceEnElCentro`: 28 estribos.
    - El primero en 254.75 y el último en 4145.25.
    - La zona izquierda va de 254.75 a 954.75, cada 100.
    - El centro, entre 954.75 y 3445.25, lleva 12 cada 2490.5 / 13.
  - `EnLaLuzBC_SieteEnElCentro`: 23 estribos, con el centro cada 1590.5 / 8.
  - `ElVoladizoVaTodoConLaSeparacionDeColumnas`: 15 estribos de 12554.75 a 13945.25, cada 1390.5 / 14.
  - `UnaLuzCortaVaTodaConLaSeparacionDeColumnas`: con un pórtico A 0 y B 1900 (columnas de 400), la luz es de 1500 < 1600. Lleva 15 estribos de 254.75 a 1645.25, cada 1390.5 / 14.
  - `UnaLuzIgualADosZonas_NoEsCorta`: en una luz de 1600 con zona de 800, cada zona lleva 8 y el centro, entre 954.75 y 1045.25, no lleva ninguno. Total 16.
  - `SinZona_TodaLaLuzVaConLaSeparacionDelCentro`: con `LargoZona` 0, A-B lleva 21 estribos, cada 3890.5 / 20.
  - `UnaLuzMenorQueDosPrimeros_UnSoloEstriboAlCentro`: una luz libre de 100 lleva 1 estribo en su centro.
- [ ] **Paso 2:** `rtk dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests --filter DistribuidorEstribosTests`. Debe fallar porque `ConfigEstribos` no existe.
- [ ] **Paso 3:** implementar.
  - Repartir "a la misma distancia sin pasar de s" sobre un tramo de largo `L` da `n = ceil(L / s) + 1` estribos, cada `L / (n − 1)`.
  - En cada zona: `nz = floor((LargoZona − c0) / SeparacionZona) + 1`, con `c0 = 50 + dE/2`. Si `LargoZona < c0`, no hay zona.
- [ ] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde.
- [ ] **Paso 5:** commit `feat(vigas): estribos a lo largo del pórtico (#90)`.

### Tarea 2: el estribo cerrado del pórtico, su largo y sus kilos

**Archivos:**
- Crear: `.../CQEING.Vigas.Calculo/Estribos/MotorEstribos.cs`
- Modificar: `.../Modelo/Estribos.cs`, para agregar `EntradaEstribos`, `EstriboEnPortico`, `EstriboEnSeccion` y `ResultadoEstribos`
- Prueba: `.../CQEING.Vigas.Calculo.Tests/MotorEstribosTests.cs`

**Interfaces:**
- Consume: `DistribuidorEstribos` (tarea 1) y `EntradaSeccion` (de la #88).
- Produce:
  - `public sealed record EntradaEstribos(Portico Portico, EntradaSeccion Seccion, ConfigEstribos General, IReadOnlyDictionary<string, ConfigEstribos>? PorTramo = null);`, donde las claves son los nombres de `Tramos`;
  - `public sealed record EstriboEnPortico(string Id, TipoEstribo Tipo, string Tramo, int Diametro, GanchoEstribo Gancho, IReadOnlyList<double> Posiciones, double Largo, double Kg);`
    - `Id` es `"E"` para el cerrado de las esquinas y `"EA1"`, `"EA2"`… para los adicionales;
    - hay uno por tramo y por estribo;
  - `public sealed record EstriboEnSeccion(string Id, TipoEstribo Tipo, double Izquierda, double Derecha, double Abajo, double Arriba);`, con las caras exteriores del estribo en el corte, en mm;
  - `public sealed record ResultadoEstribos(IReadOnlyList<EstriboEnPortico> Estribos, ResultadoSeccion Seccion, IReadOnlyList<EstriboEnSeccion> EnSeccion, IReadOnlyList<Aviso> Avisos) { public int Cantidad => Estribos.Sum(e => e.Posiciones.Count); public double Kg => Estribos.Sum(e => e.Kg); }`
  - `public static ResultadoEstribos MotorEstribos.Calcular(EntradaEstribos e)`: la sección se reparte con `e.Seccion with { DiametroEstribo = e.General.Diametro }`.

Sección de la maqueta: 300 × 500, recubrimiento 40 y estribo #3.
- Eje del estribo: ancho 210.5, alto 410.5.
- Perímetro: 1242. Con los ganchos de 135° (2 × 113), el largo es **1468**.

- [ ] **Paso 1: pruebas que fallan:**
  - `ElCerradoDeLasEsquinasSaleSoloEnCadaTramo`: 4 `EstriboEnPortico` con `Id` "E" (A-B, B-C, C-D y Voladizo D), con `Cantidad` 94.
  - `ElLargoDelCerradoEsElPerimetroMasDosGanchos`: `Largo` 1468.
  - `ConGanchoDe90_UsaElGancho90DeLasReglas`: `Largo` = 1242 + 2 × 166 = 1574.
  - `LosKilosSonCantidadPorLargoPorPeso`: el `Kg` de A-B es 28 × 1.468 × 0.560 y `ResultadoEstribos.Kg` es la suma de los cuatro.
  - `EnLaSeccionElCerradoVaDelRecubrimientoHaciaAdentro`: `EstriboEnSeccion("E", Cerrado, 40, 260, 40, 460)`.
  - `UnTramoConSuPropiaConfiguracion`: con `PorTramo["B-C"]` = `General with { SeparacionCentro = 150 }`, B-C lleva 26 y los demás siguen igual.
  - `UnTramoConOtroDiametro_SoloCambiaSuLargoYSusKilos`: con `PorTramo["A-B"]` = `General with { Diametro = 4 }`, A-B va con #4. La sección sigue con #3: el principal en el corte sigue en 40..260.
  - `UnaSeparacionEnCero_AvisaYEseTramoQuedaSinEstribos`: con `PorTramo["B-C"]` y `SeparacionZona` 0, sale el aviso `EstriboValorNoValido` con "B-C" y no hay estribos en B-C.
  - `UnDiametroSinReglas_Avisa`: con `General` y `Diametro` 9, sale el aviso `DiametroSinReglas` con "#9" y no hay estribos.
- [ ] **Paso 2:** `--filter MotorEstribosTests` → FALLA.
- [ ] **Paso 3:** implementar.
- [ ] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde.
- [ ] **Paso 5:** commit `feat(vigas): el estribo cerrado del pórtico con su largo y sus kilos (#90)`.

### Tarea 3: estribos adicionales y barras alineadas

**Archivos:**
- Crear: `.../CQEING.Vigas.Calculo/Estribos/AlineadorEstribos.cs`
- Modificar: `.../Estribos/MotorEstribos.cs` y `.../Modelo/Resultado.cs`, para agregar `TipoAviso.EstriboIncompleto`, `BarraNoExiste`, `MueveUnaEsquina` y `NoSePuedeAlinear`
- Prueba: `.../CQEING.Vigas.Calculo.Tests/AlineadorEstribosTests.cs`

**Interfaces:**
- Consume: `RepartidorFilas.Repartir` y `ConfigEstribos.Adicionales` (tarea 1), y `MotorEstribos` (tarea 2).
- Produce: `internal static (ResultadoSeccion Seccion, IReadOnlyList<EstriboEnSeccion> Figuras) AlineadorEstribos.Alinear(EntradaSeccion e, IReadOnlyList<(string Id, EstriboAdicional Adicional)> adicionales, List<Aviso> avisos)`.
  - Los adicionales se numeran `EA1, EA2…`: primero los de `General` y después los de `PorTramo`, en el orden de los tramos.
  - `MotorEstribos` agrega a `ResultadoEstribos.Estribos` uno por adicional válido y por tramo donde aplica, con las posiciones del cerrado de ese tramo. `ResultadoEstribos.Seccion` es la sección con las barras movidas.

Sección: 300 × 500, recubrimiento 40, estribo #3, arriba `[5 #5]` (x 57.45, 103.725, 150, 196.275 y 242.55) y abajo `[4 #5]` (x 57.45, 119.15, 180.85 y 242.55). La distancia libre entre dos #5 pide 40.9 entre centros.

- [ ] **Paso 1: pruebas que fallan:**
  - `AbiertoMandaLaDeArriba_LaDeAbajoSeMueve`:
    - con `Abierto` y `[S·1·2, I·1·2]`, la barra de abajo 2 pasa a x = 103.725 y las demás no se mueven;
    - la figura es `EstriboEnSeccion("EA1", Abierto, 103.725, 103.725, 40, 460)`;
    - el largo es 410.5 + 226 = 636.5.
  - `CerradoMandanLasDosPrimeras`:
    - con `Cerrado` y `[S·1·2, S·1·4, I·1·2, I·1·3]`, las de abajo 2 y 3 pasan a 103.725 y 196.275;
    - la figura va de 86.275 a 213.725, entre 40 y 460;
    - el largo es 2 × (117.95 + 410.5) + 226 = 1282.9.
  - `ElAdicionalVaSoloEnSuTramo`: con el abierto en `PorTramo["A-B"]`, hay un `EstriboEnPortico` "EA1" solo en A-B, con 28 posiciones. La barra movida sale en `ResultadoEstribos.Seccion`, que es una sola para todo el pórtico.
  - `SinLasBarrasQuePide_EstriboIncompleto`: un abierto con `[S·1·2, S·1·3]`.
  - `LaMismaBarraDosVeces_EstriboIncompleto`: un abierto con `[S·1·2, S·1·2]`.
  - `UnaBarraQueNoExiste_BarraNoExiste`: con `[S·1·2, I·1·9]`.
  - `NoSeMueveUnaEsquina`: un abierto con `[S·1·2, I·1·1]` da `MueveUnaEsquina`, y la barra de abajo 1 sigue en 57.45.
  - `SiNoQuedaLaDistanciaLibre_NoSePuedeAlinear`: abajo `[3 #8]` (62.2, 150 y 237.8) y un abierto `[S·1·2, I·1·2]`. Moverla a 103.725 deja 41.525 < 50.4 de su vecina, así que da `NoSePuedeAlinear` y nada se mueve.
  - `DosAdicionalesQueMuevenLaMismaBarra_ElSegundoNoSePuede`: el primero `[S·1·2, I·1·2]` y el segundo `[S·1·4, I·1·2]` → `NoSePuedeAlinear` para "EA2".
  - `ConLaSeccionConError_NoHayAdicionalesPeroSiElCerrado`: abajo `[1 #5]` (`MenosDeDosBarras`) → `NoSePuedeAlinear` para "EA1", con el "E" del pórtico igual.
- [ ] **Paso 2:** `--filter AlineadorEstribosTests` → FALLA.
- [ ] **Paso 3:** implementar sin cambiar `RepartidorFilas`: se parte de su `ResultadoSeccion` y se devuelve otro con las x movidas.
- [ ] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde.
- [ ] **Paso 5:** commit `feat(vigas): estribos adicionales con barras alineadas (#90)`.

### Tarea 4: documentación

- [ ] **Paso 1:** en `traslapos-vigas-notas.md` §0 "Estribos", agregar las decisiones 1 a 10, con los minutos de Carlos.
- [ ] **Paso 2:** marcar los criterios de aceptación de abajo con su prueba y hacer el commit `docs(vigas): criterios de los estribos cumplidos (#90)`.
- [ ] **Paso 3 (con la PR):** actualizar la issue #90 con las decisiones.

## Criterios de aceptación

- [ ] El cerrado de las esquinas sale solo en todos los tramos.
- [ ] Zonas junto a las columnas y centro, con el primero a 5 cm de la cara, medido hasta la cara del estribo.
- [ ] Luz corta y voladizo, todo con la separación de columnas.
- [ ] #3 con 135° por defecto; se puede cambiar el diámetro y el gancho (90°).
- [ ] Configuración por pórtico y por tramo.
- [ ] Adicionales cerrados (2 + 2) y abiertos (1 + 1): mandan las primeras barras elegidas, las demás se alinean en todo el pórtico, sin violar la distancia libre ni mover esquinas.
- [ ] Largo y kilos de cada estribo; suman al total.
- [ ] `rtk dotnet test CQEING-Addins.sln -c Release` en verde.
