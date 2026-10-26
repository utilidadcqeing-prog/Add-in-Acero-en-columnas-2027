# Ángulo del gancho por punta en las grapas — plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: superpowers:subagent-driven-development (recomendada) o superpowers:executing-plans, tarea por tarea. Los pasos usan casillas (`- [ ]`).

**Objetivo:** cada grapa de columna (C o S) tiene un gancho de 180°, 135° o 90° en cada punta, elegido en «Editar estribos menores», y la vista previa, la vista 3D y «Generar» muestran el mismo gancho.

**Arquitectura:** el ángulo vive en `EstriboMenor` (un valor por punta, 180 por defecto). La geometría sin Revit (`PlantaArmado`, `GanchoGrapa`) dibuja cada ángulo y calcula la línea con la medida de cada punta. El generador elige el gancho y la forma propios de cada combinación y mide en Revit dónde queda cada tipo de gancho, igual que hoy con el 180°.

**Tecnología:** C# / .NET (`net10.0-windows`), API pública de Revit 2027, WPF. Pruebas sin Revit en `tools/PruebasVistaPrevia` y `tools/PruebasTraslapos` (consolas con `Probar(...)`).

**Diseño:** `docs/planes/2026-10-26-diseno-grapas-angulo-por-punta.md` (leerlo junto con este plan).

Worktree `C:\Proyecto\addins-angulo`, rama `columnas/grapas-angulo-por-punta` (encima del #167, commit `6fe5782`).

## Restricciones globales

- Ángulos posibles: **180°, 135°, 90°**. Por defecto **180° en las dos puntas**; configuraciones guardadas sin el dato se leen como 180/180.
- Colas: 180° → `CuadroVarillas.Gancho180Cuadro.Extension`; 135° → `GanchoEstribo135.Extension`, o 6 diámetros si falta (y aviso); 90° → **6 diámetros** siempre.
- Todos los ganchos de grapa se doblan **como el estribo** (estilo `StirrupTie`), como el 180° de hoy.
- Lados (hacia dónde abre cada gancho): solo `GanchoGrapa.Lados` (#167). "Voltear" cambia lados, nunca ángulos.
- El cerrado (caja) no cambia.
- Solo API pública de Revit, verificada en `RevitAPI.xml` de 2027 (regla del CLAUDE.md). Que compile no alcanza.
- Build: `dotnet build addins/armado-columnas/practicacq.csproj -c Debug -p:RevitVersion=2027`. Despliegue local: Revit cerrado (comprobar con `Get-Process -Name Revit`), `tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027`.
- Commits sin push; nunca `git add -A`. Responder en español neutro.

## Puntos a vigilar

1. **Configuración vieja** (JSON sin `AnguloInicio`/`AnguloFin`) → se lee como 180/180 y genera igual que hoy. Prueba en Tarea 2.
2. **Voltear una grapa con 135°/90°** → los ángulos se quedan en su punta, cambia solo el lado. Prueba en Tarea 3.
3. **Grapa en S con ángulos distintos** (135° inicio, 90° fin) → cada punta con su ángulo y su cola, sin quiebre. Prueba en Tarea 3.
4. **135° en una varilla sin `GanchoEstribo135` en el cuadro** → cola de 6 diámetros, sin excepción. Prueba en Tarea 3.
5. **Copiar la pieza** (la ventana edita copias) → los ángulos se copian. Prueba en Tarea 2.

---

### Tarea 1: Diagnóstico en Revit de los ganchos de 135° y 90° en grapas

Antes de tocar el generador (regla del repo). Comando TEMPORAL, solo en Debug.

**Archivos:**
- Crear: `addins/armado-columnas/ComandoDiagnosticoGanchosGrapa.cs`
- Modificar: `addins/armado-columnas/App.cs` (botón dentro del `#if DEBUG`, como `ComandoDiagnosticoPrevia3D`)
- Modificar: `addins/armado-columnas/Logica/TiposPropios.cs`
- Crear: `docs/planes/2026-10-26-diagnostico-ganchos-grapa.md`

**Interfaces:**
- Produce, en `TiposPropios` (las usan las Tareas 1 y 4):
  - `Pieza<RebarHookType> GanchoGrapaEstribo(AnguloGancho angulo)`: 180 → el `CQ Grapa 180° estribo` de hoy; 135 → `"CQ Grapa 135° estribo"` (multiplicador 6); 90 → `"CQ Grapa 90° estribo"` (multiplicador 6). Todos `RebarStyle.StirrupTie`. La sobrecarga sin parámetros de hoy queda como `GanchoGrapaEstribo(AnguloGancho.Grados180)`.
  - `Pieza<RebarShape> FormaGrapaEstribo(FormaMenor forma, AnguloGancho inicio, AnguloGancho fin)`: con 180/180 devuelve las formas de hoy (`CQ Grapa C` / `CQ Grapa en S`, mismo nombre); con otros ángulos, `"CQ Grapa C 135-90"`, `"CQ Grapa en S 180-135"`, etc. `CrearFormaGrapa` recibe los dos ángulos y los pone en `HookAngleAtStart/AtEnd`.
- `AnguloGancho` sale de la Tarea 2: si se hace esta tarea primero, crear ya el enum de la Tarea 2 (solo el enum).

- [ ] **Paso 1:** verificar en `RevitAPI.xml` de 2027 que `RebarShapeTerminationsData.HookAngleAtStart/AtEnd` acepta 90 y 135, y que `BarTerminationsData.HookTypeIdAtStart/AtEnd` es público. Anotar en el documento del diagnóstico.
- [ ] **Paso 2:** agregar las dos sobrecargas a `TiposPropios` (arriba). Compilar.
- [ ] **Paso 3:** escribir el comando: se elige una columna; a media altura, una grapa #3 de lado a lado en X (como `Teoricas` de `GeneradorLongitudinal`), cada caso en su transacción y a otra altura:
  1. 180/180 con `CQ Grapa C` (control);
  2. 135/135 con la forma 180/180 (¿Revit acepta un gancho que no coincide con la forma?);
  3. 135/135 con la forma `CQ Grapa C 135-135`;
  4. 90/90 con `CQ Grapa C 90-90`;
  5. 135/90 con `CQ Grapa C 135-90`;
  6. 135/180 en S con `CQ Grapa en S 135-180`;
  7. 135/135 con `Rebar.CreateFromCurves(..., useExistingShapeIfPossible: true, createNewShape: true)` (¿qué forma elige?).
  
  De cada caso informar: si se creó (o el error), con qué forma quedó (`RebarShapeId` → nombre) y, con `GetCenterlineCurves(false, false, false, IncludeAllMultiplanarCurves, 0)`, por punta: centro y radio del arco, ángulo barrido del arco, y dirección y largo de la cola. Con eso, **Axial** y **Lateral** del centro respecto de la punta de la línea pedida (como `MedirGanchoDeGrapa`). Informe en `DiagnosticoUI.MostrarTexto` y en `%TEMP%\practicacq-diagnostico-ganchos-grapa.txt`; al final preguntar si se dejan las grapas para mirarlas en 3D.
- [ ] **Paso 4:** build, desplegar (Revit cerrado). **PARAR**: Luciano lo corre en una columna (por ejemplo la 387983) y pasa el informe y una captura 3D.
- [ ] **Paso 5:** escribir los resultados y la conclusión en `docs/planes/2026-10-26-diagnostico-ganchos-grapa.md`: qué combinación de forma + gancho usar, y si Axial/Lateral dependen del ángulo. **Si Revit no acepta ganchos de 135°/90° en grapas, parar y hablarlo con Luciano antes de seguir.**
- [ ] **Paso 6:** commit (`ComandoDiagnosticoGanchosGrapa.cs`, `App.cs`, `TiposPropios.cs`, el documento).

### Tarea 2: El ángulo de cada punta en el modelo

**Archivos:**
- Crear: `addins/armado-columnas/Modelo/AnguloGancho.cs`
- Modificar: `addins/armado-columnas/Modelo/EstriboMenor.cs`
- Pruebas: `tools/PruebasVistaPrevia/Program.cs`

**Interfaces:**
- Produce: `public enum AnguloGancho { Grados90 = 90, Grados135 = 135, Grados180 = 180 }`; en `EstriboMenor`: `AnguloGancho AnguloInicio { get; set; } = AnguloGancho.Grados180;` y `AnguloFin` igual. Inicio = la punta de `Desde` (izquierda en una fila, abajo en una columna). `Copiar()` los copia.

- [ ] **Paso 1: pruebas que fallan**
  - `grapa_nueva_sale_con_180_en_las_dos_puntas`: `new EstriboMenor()` → `AnguloInicio == Grados180 && AnguloFin == Grados180`.
  - `configuracion_vieja_se_lee_con_180`: `JsonSerializer.Deserialize<EstriboMenor>("{\"Forma\":1,\"Eje\":0,\"Desde\":2,\"Hasta\":2,\"Volteado\":true}")` → 180/180, `Forma == GrapaS`, `Volteado`.
  - `copiar_conserva_los_angulos`: pieza con 135/90 → `Copiar()` con 135/90.
  - `angulos_sobreviven_a_guardar_y_leer`: serializar y deserializar una pieza 90/135 → 90/135.
- [ ] **Paso 2:** correr `dotnet run --project tools/PruebasVistaPrevia` → FALLA (no compila: falta `AnguloGancho`).
- [ ] **Paso 3:** implementar el enum y las propiedades.
- [ ] **Paso 4:** correr → todas OK, `0 fallas`.
- [ ] **Paso 5:** commit.

### Tarea 3: Dibujo de cada ángulo y línea con la medida de cada punta

**Archivos:**
- Modificar: `addins/armado-columnas/Modelo/PlantaArmado.cs`, `addins/armado-columnas/Modelo/GanchoGrapa.cs`
- Modificar (llamadas): `addins/armado-columnas/Logica/GeneradorLongitudinal.cs` (`LineaQueAbraza`), `tools/PruebasTraslapos/Program.cs` (línea ~714)
- Pruebas: `tools/PruebasVistaPrevia/Program.cs`

**Interfaces:**
- Consume: `AnguloGancho`, `EstriboMenor.AnguloInicio/AnguloFin` (Tarea 2).
- Produce:
  - `PlantaArmado.Grapa(centroInicio, centroFin, ladoInicio, ladoFin, AnguloGancho anguloInicio, AnguloGancho anguloFin, double diametro, double doblez, double colaInicio, double colaFin)`. Se quita la firma vieja; las dos pruebas existentes pasan `Grados180` y la misma cola.
  - `PlantaArmado.Cola(AnguloGancho angulo, double diametroMm)`: 180 → `Cola180`; 135 → `Cola135`; 90 → `6 * diametroMm / 1000`.
  - `GanchoGrapa.Linea(centroInicio, centroFin, (double Axial, double Lateral) inicio, (double Axial, double Lateral) fin, int ladoInicio, int ladoFin)`: cada punta con su medida. Se quita la firma vieja (las llamadas pasan la misma medida dos veces).
  - `PlantaArmado.Menor` usa los ángulos y las colas de la pieza.

Dibujo de un gancho de ángulo θ (generaliza `Gancho` dentro de `Grapa`). Con `u`/`l` del tramo recto, como hoy, y `afuera` = `-u` en el inicio, `+u` en el fin:

```
punto(g) = centro + r·(cos g · lado·l + sen g · afuera)
arco: g desde 180° (lado del tramo recto) bajando hasta 180° − θ
cola: desde punto(180° − θ), dirección sen g · lado·l − cos g · afuera, largo = cola de esa punta
```

Con θ = 180° es exactamente el dibujo de hoy (la cola hacia adentro); con 90°, la cola sale perpendicular hacia el lado que abre; con 135°, en diagonal hacia el núcleo.

- [ ] **Paso 1: pruebas que fallan**
  - `grapa_180_igual_que_antes`: las pruebas `grapa_abraza_la_barra` y `grapa_en_s_tramo_recto_tangente_como_revit` siguen pasando con la firma nueva (180/180).
  - `gancho_90_cola_perpendicular`: C horizontal `(0.07,0.12)→(0.21,0.12)`, lados 1/1, 90/90, #3 (d = 0.0095, doblez 0.038), cola 6 d = 0.057: la cola del inicio va de `g[1]` a `g[0]` con dirección `(0, 1)` y largo 0.057; el último punto del arco del inicio está en `centro + r·(−1, 0)`.
  - `gancho_135_cola_en_diagonal_hacia_adentro`: mismo caso con 135/135, cola 0.057 (el 135° de estribo de la #3 en el cuadro): dirección de la cola del inicio `(√2/2, √2/2)` (hacia +u y hacia el lado que abre); la del fin `(−√2/2, √2/2)`.
  - `s_con_angulos_distintos`: S `(0.07,0.12)→(0.21,0.12)`, lados 1/−1, 135 inicio / 90 fin: tramo recto tangente a los dos dobleces (como en la prueba del #167) y cada cola con su dirección.
  - `voltear_no_cambia_angulos`: `PlantaArmado.Menor` de una pieza 135/90 volteada y sin voltear: la cola del inicio mide lo del 135° y la del fin lo del 90° en los dos casos (largo del último tramo de cada punta).
  - `cola_135_sin_dato_del_cuadro`: `PlantaArmado.Cola(Grados135, 25.4)` (#8, sin `GanchoEstribo135`) = `6 * 0.0254`.
  - `linea_con_medidas_distintas_por_punta`: en `tools/PruebasTraslapos`, `GanchoGrapa.Linea` con inicio (5, 23.8) y fin (5, 23.8) da lo mismo que antes; con fin (8, 23.8) el `hasta` se corre 3 más hacia afuera.
- [ ] **Paso 2:** correr las dos consolas → FALLA (firmas nuevas).
- [ ] **Paso 3:** implementar las firmas de arriba; actualizar `LineaQueAbraza` y la llamada de `PruebasTraslapos` pasando la misma medida dos veces (el generador por ángulo es la Tarea 4).
- [ ] **Paso 4:** correr las dos consolas → `0 fallas`. Compilar el add-in.
- [ ] **Paso 5:** commit.

### Tarea 4: El generador con el gancho de cada punta

**Archivos:**
- Modificar: `addins/armado-columnas/Logica/GeneradorLongitudinal.cs` (bucle de grapas ~1030-1150, `CrearGrapa`, `IntentarGrapa`, `MedirGanchoDeGrapa`)

**Interfaces:**
- Consume: Tarea 1 (`GanchoGrapaEstribo(angulo)`, `FormaGrapaEstribo(forma, inicio, fin)`, conclusión del diagnóstico), Tarea 2 (ángulos de la pieza), Tarea 3 (`GanchoGrapa.Linea` por punta).
- `IntentarGrapa` recibe `RebarHookType ganchoInicio, RebarHookType ganchoFin` en vez de un solo `gancho`, y los pone en `HookTypeIdAtStart/AtEnd`.

- [ ] **Paso 1:** la medida del gancho (`juegos`) pasa a ser por `(barra, eje, ángulo)`: `MedirGanchoDeGrapa` mide una C de prueba con ese ángulo en las dos puntas y su forma. Para 135°, `AjustarExtension` con `varilla.GanchoEstribo135` si existe; si falta, aviso "sin 135° de estribo en el cuadro para «#N»: cola de 6 diámetros" (una vez por corrida). Para 90°, ajustar la extensión a 6 d con un `CuadroVarillas.Gancho(6 * d, doblado del estribo)`.
- [ ] **Paso 2:** al crear cada grapa: forma = `FormaGrapaEstribo(pieza.Forma, pieza.AnguloInicio, pieza.AnguloFin)`, ganchos de cada punta según su ángulo, y `LineaQueAbraza` con la medida de cada punta. Lo que el diagnóstico haya mostrado que hace falta (por ejemplo, si no se acepta una forma por combinación) se aplica acá, tal como quedó escrito en su documento.
- [ ] **Paso 3:** el respaldo de hoy (180° del cuadro en traslapos donde no entran las dos piezas, y `ganchoRespaldo`) se mantiene solo para las puntas de 180°; para 135°/90° sin respaldo, el aviso de "no se pudo crear una grapa" ya existente.
- [ ] **Paso 4:** compilar; correr las dos consolas de pruebas (`0 fallas`).
- [ ] **Paso 5:** commit.

### Tarea 5: La tirita de ángulos en «Editar estribos menores»

**Archivos:**
- Modificar: `addins/armado-columnas/Vistas/VentanaEstribosMenores.xaml.cs` (y `.xaml` solo si hace falta)

**Interfaces:**
- Consume: `EstriboMenor.AnguloInicio/AnguloFin` (Tarea 2); el dibujo ya sale de `PlantaArmado.Menor` (Tarea 3).

Como en vigas (`addins/acero-vigas/Tablero/web/alzado.js`, botones de gancho, ~líneas 256-282), en WPF:

- [ ] **Paso 1:** con una grapa seleccionada (no un cerrado), al dibujar agregar al `Lienzo` junto a cada punta un `Border` con tres botones planos **90° · 135° · 180°**, cada uno con un icono chico como los de vigas (`TIPOS` en `alzado.js`: 90 `M1 3h8v7`, 135 `M1 3h8l-4 6`, 180 `M1 3h7a3 3 0 010 6H5`). Ubicación: debajo de la punta en una grapa de fila, a un costado en una de columna, sin salirse del lienzo. `ToolTip`: "Gancho de la punta izquierda/derecha" (fila) o "de abajo/de arriba" (columna).
- [ ] **Paso 2:** colores **fijos**: no seleccionado, texto `#16191D` sobre blanco con borde `#C9CFD6`; seleccionado, texto blanco sobre `Paleta.GrapaC`/`Paleta.GrapaS` según la forma. Nunca heredar el color del tema (en el boceto salían invisibles).
- [ ] **Paso 3:** clic → cambia el ángulo de esa punta en `_seleccionada`, redibuja, actualiza notas, y el clic no llega al lienzo (no deselecciona). "Voltear" no toca los ángulos.
- [ ] **Paso 4:** `Descripcion(p)` (texto de "Pieza seleccionada") agrega los ángulos cuando no son 180/180, por ejemplo "Grapa en C, fila 2 — ganchos 135° / 90°".
- [ ] **Paso 5:** compilar; prueba visual con `tools/PreviewUI` si muestra esta ventana (si no, en Revit en la Tarea 6).
- [ ] **Paso 6:** commit.

### Tarea 6: Prueba en Revit y cierre

- [ ] **Paso 1:** Revit cerrado (comprobado), desplegar desde `C:\Proyecto\addins-angulo`.
- [ ] **Paso 2:** Luciano, en PROY_JULIO_BETO (columna 387983) y en una columna de 30×30: una C 135/135, una C 90/180, una S 135/90 volteada y una grapa vieja (180/180). En cada una: la vista previa 2D, la vista 3D y la planta de Revit muestran el mismo gancho en cada punta y los ganchos abrazan sus barras. Abrir una configuración guardada antes del cambio: sale igual.
- [ ] **Paso 3:** escribir `docs/planes/2026-10-26-grapas-angulo-por-punta.md` (contexto, qué se hizo, resultado del retest). Sacar el botón y el comando del diagnóstico solo si Luciano lo pide (los otros diagnósticos siguen en Debug).
- [ ] **Paso 4:** commit (sin push; preguntar).
