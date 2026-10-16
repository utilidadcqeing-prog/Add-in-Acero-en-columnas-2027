# Plan: bastones en vigas (#89)

> **Para agentes:** SUB-SKILL REQUERIDA: superpowers:subagent-driven-development (recomendada) o superpowers:executing-plans, tarea por tarea. Los pasos usan casillas (`- [ ]`).

**Objetivo:** que el motor ponga solo los bastones de cada pórtico (en los apoyos y en el centro de cada luz), con su largo, gancho, acero y kilos, y que diga en qué puesto de la sección va cada uno.

**Arquitectura:** todo va en la librería sin Revit (`CQEING.Vigas.Calculo`).
- `CalculadorBastones` los ubica a lo largo del pórtico con la `GeometriaPortico` que ya existe.
- `MotorVigas` los agrega al `ResultadoVigas`, con sus kilos en el total.
- `UbicadorBastones` les busca puesto en la sección, sobre el reparto de filas de la #88.
- La pantalla es de la #83, el dibujo del corte de la #91 y la creación en Revit de la #85.

**Tecnología:** C# `net8.0` y xUnit.

**Fuente:**
- la issue [#89](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/89);
- la transcripción de la reunión del 16-oct (`Downloads\Meeting Transcription (15).txt`, min 49:33–51:42, 56:37–57:13 y 1:18:26–1:20:30);
- `Desktop\addin vigas\traslapos-vigas-notas.md` §0 "Bastones";
- la maqueta, paso "2 Bastones" (https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh, `project/Main.dc.html` y `project/Beam.dc.html`).

## Contexto

- **Referencia:** la maqueta, opción 1. Donde la maqueta y Carlos no coincidían, manda Carlos, porque es el experto (Juan, 2026-10-16). La maqueta ya se puso al día con estas decisiones (versión 15, 2026-10-16): el bastón en el apoyo de borde A, el de la luz B-C, el gancho solo en la punta que llega al borde y el acero de arriba y de abajo.
- **Lo que pinta la maqueta:**
  - paso 2: "Largo de todos" (2.00 m); "Acero", con Arriba y Abajo, cada uno con [cantidad] [#] (*"Por defecto, 1 del diámetro de las esquinas; con 0, esa cara no lleva bastones."*); Gancho "Sin gancho | Con gancho" (por defecto, sin gancho), con *"El gancho solo va en la punta que llega al borde de la viga o del voladizo."*; y el texto *"Van en el primer puesto libre de la fila, empezando por la esquina."*;
  - el dibujo: 4 bastones arriba (A, B, C y D; el de A se corta en la punta de la viga) y 3 abajo (A-B, B-C y C-D);
  - al hacer clic en un bastón: "Bastón BS1 · Centrado en la columna B · ahora 2.00 m", el campo Largo, "Usar el largo general", "Aplicar" y *"Queda 1.00 m a cada lado de su eje. No se redondea."*;
  - en la lista: "Bastón · 1 #5 · 2.00 m · Centrado en la columna B · kg". Los kilos suman al total.
- **Ya existe:**
  - `GeometriaPortico`, con `InicioBarras`, `FinBarras` (que ya descuentan el recubrimiento de las puntas) y `Luces` (las luces libres entre caras);
  - `RepartidorFilas`, que reparte la fila 1 de esquina a esquina y usa los puestos de la fila llena en las demás;
  - `Reglas.PorDiametro`, con el gancho de 90° y los kg/m.
- **Rama:** `vigas-bastones-89`, que sale de `main` (ya trae #80, #88, #81 y #82).
- **Fuera de alcance:**
  - el panel, el dibujo y la lista: #83;
  - qué bastones salen en el corte según dónde se ponga: #91;
  - crearlos en Revit: #85.

## Decisiones (Juan, 2026-10-16)

1. **En qué puesto de la sección van** (lo recomendado; Carlos solo dio el caso con la fila 1 llena):
   - **Fila 1:** el bastón va en el primer hueco entre dos barras donde quepa con la distancia libre a las dos vecinas, centrado en ese hueco. Los huecos se miran de los extremos hacia adentro, primero el izquierdo.
   - **Si no cabe en la fila 1:** va en la fila siguiente, en el primer puesto libre de los extremos hacia adentro (0, n−1, 1, n−2…). Los puestos son los de la fila llena, como en la #88. Carlos, min 1:20:30: *"en el extremo, izquierda o derecha, según la disponibilidad"*.
   - **Separación:** usa la misma separación entre filas del paso 1 (`SeparacionFilas`), porque la maqueta no tiene otro control.
   - **Si no hay lugar** en el máximo de filas: `ErrorCara.NoCaben`.
2. **Dónde van a lo largo del pórtico** (Carlos):
   - **En los apoyos:** en **todos**, incluidos los de borde, centrados en el eje. Carlos, min 50:13: *"tomando el eje de las columnas"*; min 1:19:08: *"un bastón superior que llega al apoyo de borde o voladizo"*.
   - **En las luces:** en el **centro de la luz libre** de cada luz, entre las caras de los apoyos. Carlos, min 50:38 y 1:20:28: *"en las mitades de los tramos"*, *"en el eje libre entre vigas"*. En el voladizo no van.
   - **Puntas:** ninguna se pasa de `InicioBarras` ni de `FinBarras`. Si se pasa, se corta ahí, y esa punta es la que **llega al borde** (el apoyo de borde o el final del voladizo).
3. **Gancho:** con "Con gancho", llevan gancho de 90° (`Gancho90.B` de su diámetro) **solo las puntas que llegan al borde**. Las demás quedan rectas. El largo escrito es el recto **antes de cortar**; `Largo` es el recto final más los ganchos. No se redondea.
4. **Acero:** el usuario lo elige aparte para los de arriba y para los de abajo (`ArmadoBastones.Superior` e `Inferior`).
   - Por defecto es **1 barra del diámetro de las esquinas** de esa capa, es decir, el primer grupo de las barras corridas.
   - Con cantidad 0, esa capa no lleva bastones.
5. **Viga de cimentación:** se invierte, como en la maqueta. Los bastones de los apoyos van **abajo** (`BI`) y los de las luces **arriba** (`BS`).
6. **Ids:** `BS1, BS2…` (arriba) y `BI1, BI2…` (abajo), de izquierda a derecha, según su posición física, igual que las barras corridas.

## Restricciones generales

- Medidas en mm (`int` a lo largo del pórtico, `double` en la sección), como el resto del motor; los pesos en kg/m.
- Kilos de un bastón = `Largo / 1000 × Cantidad × KgPorMetro`.
- Nombres, mensajes y comentarios en español.
- Commits sin firma de Claude. Comandos con `rtk`.
- `rtk dotnet test CQEING-Addins.sln -c Release` en verde al terminar cada tarea.

## Lo que más puede fallar y ninguna prueba "normal" toca

1. **Voladizo más corto que medio bastón:** el bastón del apoyo se corta en la punta del voladizo y esa punta lleva gancho. Prueba en la tarea 1.
2. **El diámetro del bastón no está en las reglas** (por ejemplo #9): no revienta, sale el aviso `DiametroSinReglas` y esa capa queda sin bastones. Prueba en la tarea 1.
3. **Largo de 0 o negativo**, general o de uno solo: no se generan barras de largo cero. El largo de uno solo se ignora con `CambioNoAplicado` y `LargoNoValido`; el general deja sin bastones con `BastonLargoNoValido`. Prueba en la tarea 2.
4. **La fila 1 tiene menos barras de las que caben, pero el hueco no alcanza** (4 barras donde caben 5): el bastón va a la fila 2 y no se mete apretado en la fila 1. Prueba en la tarea 3.
5. **Bastón más grueso que las barras corridas:** el hueco se mide con su diámetro y la fila nueva sube según el diámetro mayor. Prueba en la tarea 3.

---

### Tarea 1: bastones a lo largo del pórtico

**Archivos:**
- Crear: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Modelo/Bastones.cs` (`ArmadoBastones`, `Baston`)
- Crear: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Vigas/CalculadorBastones.cs`
- Modificar:
  - `.../Modelo/EntradaVigas.cs`: `ArmadoBastones? Bastones = null` al final; null = los valores por defecto;
  - `.../Modelo/Resultado.cs`: `ResultadoVigas` gana `IReadOnlyList<Baston> Bastones` al final, y `Kg` los suma; `TipoAviso.DiametroSinReglas`;
  - `.../MotorVigas.cs`: arma los bastones; en la salida temprana (`SinTraslapoMinimo`) va una lista vacía.
- Prueba: `.../CQEING.Vigas.Calculo.Tests/BastonesTests.cs`

**Interfaces:**
- Produce:
  - `public sealed record ArmadoBastones(GrupoBarras? Superior = null, GrupoBarras? Inferior = null, int Largo = 2000, bool ConGancho = false, IReadOnlyDictionary<string, int>? Largos = null);`, donde `Largos` son los largos por id escritos a mano, en mm.
  - `public sealed record Baston(string Id, Posicion Capa, string Donde, bool EnApoyo, int Eje, int Desde, int Hasta, int GanchoInicio, int GanchoFin, int Largo, bool Manual, GrupoBarras Grupo, double Kg) { public int LargoRecto => Hasta - Desde; }`
    - `Donde` es el eje del apoyo (`"B"`) o el nombre de la luz (`"A-B"`);
    - `Eje` es el punto donde se centra;
    - `Manual` es true si su largo viene de `Largos`.
  - `internal static IReadOnlyList<Baston> CalculadorBastones.Calcular(EntradaVigas e, GeometriaPortico g, List<Aviso> avisos)`
  - `ResultadoVigas.Bastones`

Datos de prueba: `Datos.PorticoMaqueta()`, con `RecubrimientoPuntas` 50. Así `InicioBarras` = −150 y `FinBarras` = 13950, y las luces libres son A-B 200–4200, B-C 4600–7700 y C-D 8100–12100. El armado es `[3 #5]` arriba y abajo, con `Reglas.Generales`.

- [ ] **Paso 1: pruebas que fallan**, en `BastonesTests`:
  - `EnLaVigaAerea_ArribaVanEnTodosLosApoyos`: ids `BS1..BS4` con `Donde` A, B, C y D, `EnApoyo` true y `Capa` Superior. BS2 va de 3400 a 5400 y su `Largo` es 2000.
  - `ElDelApoyoDeBordeSeCortaEnLaPuntaDeLaViga`: BS1 va de −150 a 1000, con `Largo` 1150 y sin gancho.
  - `AbajoVanEnElCentroDeLaLuzLibre`: `BI1..BI3` con `Donde` A-B, B-C y C-D. BI2 va de 5150 a 7150 (centro 6150). En el voladizo no hay.
  - `ConGancho_SoloLaPuntaQueLlegaAlBordeLoLleva`: con `ConGancho`, BS1 tiene `GanchoInicio` 278, `GanchoFin` 0 y `Largo` 1150 + 278. BS2 sigue en 2000 y sin gancho.
  - `ConVoladizoCorto_ElDelApoyoLlegaALaPuntaDelVoladizo`: con `VoladizoFinal` 500 y gancho, BS4 tiene `Hasta` 12950 y `GanchoFin` 278.
  - `EnCimentacion_SeInvierten`: con `TipoViga.Cimentacion`, los de los apoyos son `BI1..BI4` (Inferior) y los de las luces `BS1..BS3` (Superior).
  - `PorDefecto_UnaBarraDelDiametroDeLasEsquinas`: con `Superior [3 #5]` e `Inferior [2 #4, 1 #5]`, los BS llevan `new GrupoBarras(1, 5)` y los BI `new GrupoBarras(1, 4)`.
  - `ElUsuarioEligeElAceroDeLosDeArriba`: con `Superior = new GrupoBarras(2, 6)`, BS2 lleva `Grupo` (2, 6) y `Kg` = 2.0 × 2 × 2.235.
  - `ConCantidadCero_EsaCapaNoLlevaBastones`: con `Inferior = new GrupoBarras(0, 5)`, no queda ningún BI.
  - `UnLargoEscritoNoSeRedondea`: con `Largos["BS2"] = 2300`, BS2 va de 3250 a 5550, con `Manual` true. Los demás siguen en 2000.
  - `UnLargoImparQuedaCompleto`: con 2301, `Hasta − Desde` da 2301.
  - `LosKilosDeLosBastonesSumanAlTotal`: `r.Kg` = la suma de las capas más la suma de los `Baston.Kg`.
  - `UnDiametroSinReglas_AvisaYNoPoneEsosBastones`: con `Superior = new GrupoBarras(1, 9)`, aparece el aviso `DiametroSinReglas` con "#9", no hay BS y los BI siguen.
- [ ] **Paso 2:** `rtk dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests --filter BastonesTests`. Debe fallar porque `ArmadoBastones` no existe.
- [ ] **Paso 3:** implementar.
  - Por cada apoyo, el centro es `PosicionEje`; por cada `Luz`, el centro es `(CaraIzquierda + CaraDerecha) / 2`.
  - `Desde = centro − L / 2` y `Hasta = Desde + L`, recortados a `[InicioBarras, FinBarras]`.
  - El gancho va en la punta recortada, si `ConGancho`.
  - Los ids se numeran por capa, ordenados por `Desde`.
- [ ] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde. Si las pruebas de kilos que ya existen esperaban solo las barras corridas, se ajustan para que sumen los bastones (por defecto, el pórtico los lleva).
- [ ] **Paso 5:** commit `feat(vigas): bastones a lo largo del pórtico (#89)`.

### Tarea 2: cambiar el largo de un bastón

**Archivos:**
- Modificar:
  - `.../MotorVigas.cs`: `PrevisualizarBaston`;
  - `.../Modelo/CambiosManuales.cs`: `MotivoBloqueo.LargoNoValido`;
  - `.../Modelo/Resultado.cs`: `TipoAviso.BastonLargoNoValido`;
  - `.../Vigas/CalculadorBastones.cs`.
- Prueba: `.../CQEING.Vigas.Calculo.Tests/BastonesTests.cs`

**Interfaces:**
- Consume: `ArmadoBastones.Largos` y `Baston` (tarea 1).
- Produce: `public static Previsualizacion MotorVigas.PrevisualizarBaston(EntradaVigas entrada, string id, int largo)`.
  - `NoExiste` si no hay un bastón con ese id;
  - `SinCambio` si el largo es igual al actual;
  - `LargoNoValido` si es ≤ 0;
  - si no, `SePuede` con el `Resultado` que tiene el largo nuevo en `Largos`.

"Usar el largo general" (#83) quita la entrada de `Largos`, y no pasa por aquí.

- [ ] **Paso 1: pruebas que fallan:**
  - `PrevisualizarBaston_DevuelveElPorticoConElLargoNuevo`: BS2 en 2300 → `SePuede` y BS2 de 3250 a 5550.
  - `PrevisualizarBaston_ConElMismoLargo_NoCambiaNada`: 2000 → `SinCambio`.
  - `PrevisualizarBaston_EnCero_NoSePuede`: 0 → `LargoNoValido`.
  - `PrevisualizarBaston_UnIdQueNoExiste`: "BS9" → `NoExiste`.
  - `UnLargoGuardadoEnCero_SeIgnoraYAvisa`: con `Largos["BS2"] = 0`, sale el aviso `CambioNoAplicado` con `Elemento` "BS2" y `Motivo` `LargoNoValido`, y BS2 sigue en 2000.
  - `UnLargoGeneralEnCero_NoPoneBastonesYAvisa`: con `Largo = 0`, no hay bastones y sale el aviso `BastonLargoNoValido`.
- [ ] **Paso 2:** el filtro `BastonesTests` → FALLA.
- [ ] **Paso 3:** implementar.
- [ ] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde.
- [ ] **Paso 5:** commit `feat(vigas): cambiar el largo de un bastón (#89)`.

### Tarea 3: puesto del bastón en la sección

**Archivos:**
- Crear: `.../CQEING.Vigas.Calculo/Seccion/UbicadorBastones.cs`
- Modificar: `.../Modelo/Seccion.cs` (`BastonEnSeccion`, `UbicacionBastones`)
- Prueba: `.../CQEING.Vigas.Calculo.Tests/UbicadorBastonesTests.cs`

**Interfaces:**
- Consume: `EntradaSeccion`, `CaraSeccion` y `RepartidorFilas.Repartir` (de la #88).
- Produce:
  - `public sealed record BastonEnSeccion(int Fila, BarraEnSeccion Barra);`, con `Fila` empezando en 1 (1 = junto a la cara);
  - `public sealed record UbicacionBastones(IReadOnlyList<BastonEnSeccion> Barras, ErrorCara Error);`
  - `public static UbicacionBastones UbicadorBastones.Ubicar(EntradaSeccion e, Posicion cara, IReadOnlyList<GrupoBarras> bastones)`, donde `bastones` son los que pasan por el corte en esa cara. Qué bastones pasan por el corte lo decide la #91.

El algoritmo es la decisión 1:
- en la fila 1 se recalculan los huecos después de cada bastón puesto;
- la `y` de un bastón es `bordeFila + d/2`, medida desde la cara, igual que en `RepartidorFilas`;
- si la fila es nueva, su borde es el de la anterior más el diámetro mayor de esa fila más `SeparacionFilas`.

Sección de la maqueta: 300 × 500, recubrimiento de 40 mm, estribo #3 y barras #5.
- Caben 5 por fila.
- Puestos de la fila llena: 57.45, 103.725, 150, 196.275 y 242.55.
- Hueco que necesita un #5 entre dos #5: 7.95 + 25 + 15.9 + 25 + 7.95 = 81.8.

- [ ] **Paso 1: pruebas que fallan:**
  - `ConTresBarras_ElBastonVaEnLaFila1EnElHuecoDeLaIzquierda`: abajo con `[3 #5]` y bastón `[1 #5]` → `Fila` 1, x = 103.725 y la misma y de la fila 1 (57.45).
  - `DosBastones_UnoEnCadaExtremo`: `[2 #5]` → x 103.725 y 196.275.
  - `ConLaFila1Llena_VaALaFila2EnElPuestoDelExtremo`: `[5 #5]` → `Fila` 2, x = 57.45 e y = 73.35 (pegada).
  - `ConSieteBarras_VaAlPrimerPuestoLibreDeLaFila2`: `[7 #5]`, con la fila 2 en los puestos 0 y 4 → x = 103.725.
  - `ConCuatroBarras_ElHuecoNoAlcanzaYVaALaFila2`: `[4 #5]` → `Fila` 2, x = 57.45.
  - `ConFilasSeparadas_SeSumaLaSeparacion`: `SeparacionFilas` 10 y `[5 #5]` → y = 83.35.
  - `SinLugarEnElMaximoDeFilas_ErrorNoCaben`: `MaximoFilas` 1 y `[5 #5]` → `ErrorCara.NoCaben` y ninguna barra.
  - `ArribaSeMideDesdeLaCaraDeArriba`: `Posicion.Superior` con `[3 #5]` → y = 500 − 57.45.
  - `UnBastonMasGruesoMideElHuecoConSuDiametro`: `[3 #5]` y bastón `[1 #8]`. Necesita 7.95 + 25 + 25.4 + 25 + 7.95 = 91.3 entre centros, y el hueco entre 57.45 y 150 es de 92.55, así que cabe en la fila 1, en x = 103.725.
  - `UnBastonDel8EntreDosDel8NoCabe`: abajo `[3 #8]` (caben 4; la fila 1 queda en 62.2, 150 y 237.8) y bastón `[1 #8]`. El hueco es de 87.8 y necesita 12.7 + 25 + 25.4 + 25 + 12.7 = 100.8, así que va a la fila 2, en el puesto 0 (x = 62.2).
- [ ] **Paso 2:** `--filter UbicadorBastonesTests` → FALLA.
- [ ] **Paso 3:** implementar `UbicadorBastones` sin cambiar `RepartidorFilas`: parte de su `CaraSeccion` y agrega los bastones.
- [ ] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde.
- [ ] **Paso 5:** commit `feat(vigas): puesto del bastón en la sección (#89)`.

### Tarea 4: documentación

- [ ] **Paso 1:** en `traslapos-vigas-notas.md` §0 "Bastones", agregar las decisiones 1 a 6, con los minutos de Carlos.
- [ ] **Paso 2:** marcar los criterios de aceptación de abajo con su prueba y hacer el commit `docs(vigas): criterios de los bastones cumplidos (#89)`.
- [ ] **Paso 3 (al final, con la PR):** actualizar la issue #89 con las decisiones.

## Criterios de aceptación

- [x] Arriba van centrados en el eje de cada apoyo, incluidos los de borde; abajo, en el centro de cada luz libre (`EnLaVigaAerea_ArribaVanEnTodosLosApoyos`, `ElDelApoyoDeBordeSeCortaEnLaPuntaDeLaViga`, `AbajoVanEnElCentroDeLaLuzLibre`, `ConVoladizoCorto_ElDelApoyoLlegaALaPuntaDelVoladizo`).
- [x] El largo por defecto es 2.00 m y se puede cambiar uno por uno, siempre centrado y sin redondear (`UnLargoEscritoNoSeRedondea`, `UnLargoImparQuedaCompleto`, `PrevisualizarBaston_*`, `UnLargoGuardadoEnCero_SeIgnoraYAvisa`, `UnLargoGeneralEnCero_NoPoneBastonesYAvisa`).
- [x] El gancho lo elige el usuario; con gancho, solo lo llevan las puntas que llegan al borde (`ConGancho_SoloLaPuntaQueLlegaAlBordeLoLleva`).
- [x] El acero lo elige el usuario, aparte arriba y abajo; por defecto es 1 del diámetro de las esquinas (`PorDefecto_UnaBarraDelDiametroDeLasEsquinas`, `ElUsuarioEligeElAceroDeLosDeArriba`, `ConCantidadCero_EsaCapaNoLlevaBastones`, `UnDiametroSinReglas_AvisaYNoPoneEsosBastones`).
- [x] En la sección van en la fila 1 si hay hueco; si no, en la fila siguiente, en el puesto libre del extremo. Ocupan puestos y cuentan para la capacidad (`UbicadorBastonesTests`, 10 pruebas).
- [x] En cimentación se invierten (`EnCimentacion_SeInvierten`).
- [x] Los kilos suman al total (`LosKilosDeLosBastonesSumanAlTotal`).
- [x] `rtk dotnet test CQEING-Addins.sln -c Release` en verde: 188 pruebas, 0 advertencias.
