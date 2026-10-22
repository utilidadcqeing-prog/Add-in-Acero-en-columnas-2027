# Plan: estribo con gancho de 180° y grapa en C o en S (vigas)

## Contexto

- **Qué se pide.** Reunión con Carlos del 22-oct-2026 (transcripción `Meeting Transcription (22).txt` en
  `Desktop\addin vigas`), min 49:58–51:37. Carlos pidió hacer un estribo interno (estribo menor), vio que el gancho
  sale a 135° y dijo: *"manejemos el gancho a 180"*. También dijo que este mismo estribo, *"como lo tienes actualmente,
  pero con 180"*, se va a reutilizar en el add-in de viguetas. Juan agrega que la grapa (estribo abierto) puede ir
  **en S**.
- **Fuera de alcance.**
  - Los ganchos de las barras longitudinales: ya tienen 90°, 135° y 180° (#84).
  - La alerta de choque entre barras: es la otra tarea del 22-oct y lleva su propio plan.
  - Los choques entre el acero de vigas y el de columnas en el nudo: Carlos dijo (min 39:07–40:47) que no se corrigen.
  - Los colores de #4, #5 y #6: Carlos los dejó para después (min 49:14).
- **Entorno.** Revit 2026 y 2027, monorepo `C:\Users\LENOVO\Desktop\practicacq`, add-in `addins/acero-vigas`. Rama de
  trabajo: `vigas-estribo-180`, que sale de `main` (`6e9c9c3`).

## Criterios de aceptación

- [ ] El selector "Gancho del estribo" del paso de estribos ofrece **135° · 90° · 180°**. Vale para el pórtico o para
      los tramos marcados, igual que hoy.
- [ ] Con 180°, el largo y los kilos de cada estribo usan el **B del gancho de 180°** del cuadro de Carlos (142 mm en
      el #3).
- [ ] Cada estribo adicional **abierto** tiene un control **C / S**. Por defecto es C, como hoy.
- [ ] La vista previa del tablero dibuja el cerrado y el abierto con gancho de 180°, y el abierto en C o en S.
- [ ] En Revit, el cerrado y el abierto salen con gancho de 180°, la cola con la E del cuadro (65 mm en el #3) y la
      grapa en C o en S según se eligió. En S, cada gancho abraza su barra.
- [ ] La pantalla de Reglas muestra la columna **Gancho 180°** y se puede editar como las demás.
- [ ] Los pórticos guardados con 135° o 90° salen igual que antes.
- [ ] `rtk dotnet test CQEING-Addins.sln -c Release` en verde.

## Hallazgos del código y de Revit real

**H1. Los valores de 180° ya están en las reglas.** `Reglas.cs:39-45`, campo `Gancho180`, del cuadro "Dimensiones
mínimas para ganchos estándar". Para el #3: B 142, E 65, D 57 y A 131. `Reglas.LargoGancho` ya lo devuelve para
`TipoGancho.De180` (`Reglas.cs:52-58`). Hoy solo lo usan las barras longitudinales.

**H2. Los cuadros de Carlos no traen un 180° para estribos.** Se revisaron las dos imágenes de WhatsApp del 16-oct. En
el cuadro de ganchos y traslapos, "ESTRIBOS" es el de 135°. En el de dimensiones mínimas, el bloque de estribos es solo
de 135°. El único 180° que hay es el de barras, y es el que se usa (decisión 3).

**H3. Los estribos solo conocen 135° y 90°.** Se sabe por cinco lugares del código:
- `GanchoEstribo` tiene solo `De135` y `De90` (`Estribos.cs:11-15`).
- `MotorEstribos.Gancho` (`MotorEstribos.cs:116`) elige entre `Estribo135.B` y `Gancho90.B`.
- `PreviaAcero.Trazos` (`PreviaAcero.cs:48-49`) toma 135 o 90, con la cola de `Estribo135.E` o 6 diámetros.
- En `TiposAcero.GanchoEstribo` y `GanchoGrapa` (`TiposAcero.cs:71-92`), todo lo que no es 135 pasa a 90.
- En el tablero, `tablero.js:51` convierte todo lo que no es `'135°'` en `'De90'`, y el selector (`tablero.js:1137-1138`)
  ofrece solo 135° y 90°.

**H4. La grapa de vigas hoy siempre sale en C.** Se ve en tres lugares:
- `CreadorEstribos.Abierto` (`CreadorEstribos.cs:124-148`) crea la rama del lado del centro de la viga, con los dos
  ganchos `Right/Right`.
- `CorregirAbierto` (`CreadorEstribos.cs:157-169`) voltea después de regenerar los ganchos que no abrazan su barra.
- La vista previa (`PreviaAcero.Abierto`, `PreviaAcero.cs:123-148`) dibuja los dos ganchos hacia el lado de las barras.

`EstriboAdicional` (`Estribos.cs:24`) no tiene dónde guardar la forma.

**H5. Revit respeta la orientación de cada gancho que se le pide al crear la barra.** Es un dato de la API de Revit,
medido en Revit 2027 el 20-oct (`docs/planes/2026-10-20-diagnostico-grapa-s.md`, casos 1-6). De ese diagnóstico se
toma solo este dato de Revit, no la interfaz ni el código de columnas:
- La orientación la decide `BarTerminationsData` al crear la barra, y Revit no cambia la forma.
- **C** = `Left/Left` o `Right/Right`; **S** = `Left/Right` o `Right/Left`.
- Con el multiplicador por defecto, la cola de 180° sale de 4 d (38 mm en el #3), no de los 65 mm del cuadro. Por eso
  el tipo de gancho propio tiene que llevar E/d.

**H6. Geometría de la S, pendiente de medir.** En C, la rama va al lado de las dos barras, del lado del centro, y cada
gancho dobla hacia su barra. En S, los ganchos doblan hacia lados opuestos. Si la rama queda recta al lado de las
barras, uno de los dos ganchos dobla hacia afuera y no abraza nada. Para que cada gancho abrace su barra, la rama
tiene que pasar a un lado de la barra de arriba y al otro lado de la de abajo, o sea, un poco en diagonal (2 radios de
doblado en todo el alto). Se mide en Revit en la tarea 4 antes de darlo por bueno.

**H8. Medido en Revit 2026 el 22-oct, viga 388501 (`VIG_25X35`), con el MCP de Revit.**

*Generada con 135° y grapa en S:*
- Ganchos `Right/Left` y rama diagonal: abajo en lat −149, arriba en −101.
- Cada doblez abraza su barra: su centro queda a 14 mm del de la barra, con 6 mm de holgura por dentro.
- Choque que ya había antes: la cola de 135° de arriba (57 mm) pasa a 2.9 mm del eje de la barra de esquina #395778;
  harían falta 12.7. Es igual en C.

*Generada otra vez con 180°:*
- Tipos `CQEING Estribo 180° #3` y `CQEING Grapa 180° #3`, colas de 65.0 mm y arcos de 104.5 mm (180° con R 33.3).
  Las colas quedan paralelas a la rama.
- El cerrado tomó el gancho de 180° **con la forma `CQ Estribo`** del documento, así que la decisión 7 no hizo falta.
  Sus ganchos van en la esquina de abajo a la izquierda, como manda la forma, y no chocan.
- En las luces 1 y 3 (barras de abajo a −60.7 / −125 / −189.3) no hay choques.
- **En las luces 2 y 4**, las barras de abajo van a −76.6 / −125 / −173.4, a 48.4 mm entre ejes. Ahí el gancho de
  180° de abajo de la grapa se mete **4.9 mm** en las dos vecinas de la barra que abraza (#395887/#395897 y
  #395893/#395903), por el doblez de un lado y por la cola del otro. Son 9 grupos de grapas.
- La causa es el doblado: la grapa se crea con estilo de barra (`RebarStyle.Standard`), así que Revit usa el
  `StandardHookBendDiameter` del tipo de barra (57 mm = 6 d en el #3). El gancho ocupa 2R + d ≈ 76 mm y no cabe entre
  barras a 48 mm.

**H7. La pantalla de Reglas no muestra el 180°.** Hoy tiene solo Traslapo, Gancho 90°, Estribo 135° y kg/m. Las piezas
involucradas son:
- `ReglaVista` (`VistaTablero.cs:159`).
- `ArmadorVista.Reglas` / `ReglasDe` (`ArmadorVista.cs:97-127`).
- En `tablero.js`: el envío de `guardarReglas` (línea 303), las filas (línea 653) y la tabla (líneas 1258-1268).
- En `demo.js:14-15`, la constante `G180`.

## Decisiones

1. **El 180° va en el selector que ya existe** (2026-10-22, Juan). "Gancho del estribo" pasa a ofrecer 135° · 90° ·
   180°. Vale para el cerrado y los adicionales del pórtico o de los tramos marcados, como hoy. Así queda listo para
   viguetas.
2. **La forma de la grapa la elige el usuario** (2026-10-22, Juan). Cada adicional abierto tiene un control C / S;
   por defecto C. El cerrado no tiene forma.
3. **El estribo de 180° usa la fila "Ganchos de 180°" del cuadro** (2026-10-22, por H2). El largo y los kilos se
   calculan con B; la cola de Revit y de la vista previa, con E. El doblado lo sigue poniendo el tipo de barra del
   modelo, como en los de 135°. Si Carlos manda un 180° propio para estribos, se agrega como columna aparte.
4. **La pantalla de Reglas muestra el Gancho 180°** (2026-10-22, Juan). Si los estribos lo usan, tiene que poder verse
   y editarse. Se agrega como columna entre Gancho 90° y Estribo 135°.
5. ~~**Primero la maqueta.** Como en #100, la maqueta se actualiza antes de programar el tablero, porque es la
   referencia de las vistas.~~ **Sin maqueta** (2026-10-22, Juan: "sin la maqueta dale con 1"). La tarea 0 no se hace;
   los controles nuevos siguen el estilo de los que ya hay al lado (el selector de gancho y la fila del adicional).
7. **Si la forma del documento no toma el gancho elegido, el cerrado sale por curvas** (2026-10-22, tarea 4). El
   cerrado se crea primero con la forma de estribo del documento, que trae sus ganchos (casi siempre de 135°). Si
   después de regenerar no tiene el gancho elegido, se borra y ese grupo sale con el lazo por curvas. La forma no se
   marca como mala, porque sigue sirviendo a los grupos de 135°. Antes, si `SetHookTypeId` fallaba, el error se callaba
   y el estribo se quedaba con lo que trajera la forma.
8. **La grapa se crea con estilo de estribo** (2026-10-22, Juan eligió la opción 1 por H8). Pasa de
   `RebarStyle.Standard` a `StirrupTie`, como refuerzo transversal: la NSR-10 C.7.2 permite doblar estribos y grapas
   del #5 o menores a 4 d. Así toma el `StirrupTieBendDiameter` del tipo de barra (38 mm en el #3) en vez del
   `StandardHookBendDiameter` (57 mm).
   - **Probado en Revit antes de programar** (SubTransaction que se deshizo, viga 388501): Revit acepta la grapa de una
     sola línea con estilo de estribo. Salió con la forma `CQ Grapa C`, ganchos `Left/Right`, arcos de R 23.8 y colas
     de 65.
   - **Cambios:** `CreadorEstribos.Abierto` usa `StirrupTie` y `StirrupTieBendDiameter`. `TiposAcero.GanchoGrapa` crea
     `"CQEING Grapa 180° #3 estribo"`, con estilo de estribo y otro nombre, para no cambiarle el estilo a los tipos de
     barra que ya usan las grapas generadas antes. `PreviaAcero.Abierto` dobla con `DobladoBarra.Estribo`.
   - **Prueba:** `PreviaAceroTests.ElAbiertoSeDoblaComoEstriboNoComoGancho`, primero en rojo y después en verde. La
     suite da 814 en verde.
   - **Medido en Revit el 22-oct**, después de desplegar y generar de nuevo la viga 388501 con 180° y S:
     - ganchos `CQEING Estribo 180° #3` y `CQEING Grapa 180° #3 estribo`, todo con `StirrupTie`;
     - grapas `Right-Left` (S), arcos de R 23.8 y colas de 65.0;
     - **0 choques en las 222 posiciones** de estribos y grapas contra las longitudinales, incluidas las luces 2 y 4.
   - **Detalle pendiente:** Revit reutiliza la forma `CQ Grapa C` del documento aunque la grapa esté en S, porque la
     orientación la da la barra (H5). En una planilla de despiece la S figuraría como C.
   - **Aviso de norma para Carlos:** en zona sísmica, NSR-10 C.21 pide 135° en al menos un extremo del gancho
     suplementario.
9. **El corte 1-1 dibuja los adicionales con su gancho real** (2026-10-22, Juan: "que dependa del ángulo el dibujo
   del ganchito de los estribos adicionales… en el dibujo de la 1-1").
   - `PreviaAcero.EnElCorte(grupo, anchoViga, reglas, doblado?)` da el trazo por el eje con los ganchos. Lo usan la
     vista previa 3D y `ArmadorVista`, que lo manda en `FiguraSeccion.Puntos` (cm, la y desde arriba). Así el corte
     dibuja lo mismo que Revit.
   - El cerrado de las esquinas sigue como rectángulo (`Puntos` null). `PlanificadorAcero.RamaDe` pasa a `internal`
     para reutilizarlo.
   - En `tablero.js`, los adicionales se dibujan como `<polyline>` en un SVG encima de la sección y desaparecen las
     rayitas de la tarea 5.
   - En la demostración, las figuras no traen `Puntos` y se ven con la figura simple.
   - Pruebas: `ElCorteDibujaElGanchoDelAdicionalSegunSuAngulo` (colas de 5.7 / 6.5 / 5.7 cm),
     `EnElCorteLaGrapaEnSTieneLasColasAUnLadoYAlOtroDeSusBarras` y `ElCerradoDeLasEsquinasNoTraeTrazo`. No compilaban
     y después pasaron. La suite da 817 en verde.
6. **La forma viaja con el estribo** (2026-10-22, tarea 1). `EstriboEnPortico` lleva la `Forma` del adicional y
   `PlanificadorAcero` la pasa a `GrupoEstribos`. Así el plan no tiene que volver a buscar el adicional por su id. El
   cerrado siempre queda en C.

## Plan paso a paso

Se hace una tarea a la vez y Juan da el visto bueno antes de seguir con la próxima.

0. **Maqueta.** Archivos: el artifact https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh. Cambios:
   - en el selector del paso de estribos, la opción 180°;
   - en la fila de cada adicional abierto, el control C / S;
   - en Reglas, la columna "Gancho 180°";
   - en el corte, dibujar la grapa en S.

   Cómo se prueba: Juan revisa las tres vistas.
1. **Modelo y motor.** Archivos: `Modelo/Estribos.cs`, `Estribos/MotorEstribos.cs` y `Generacion/PlanAcero.cs`, más el
   armado del plan que crea `GrupoEstribos`. Cambios:
   - `GanchoEstribo.De180`;
   - `enum FormaGrapa { C, S }`;
   - `EstriboAdicional(..., FormaGrapa Forma = FormaGrapa.C)`;
   - `GrupoEstribos.Forma`, que se pasa del adicional al grupo;
   - `MotorEstribos.Gancho` devuelve `Gancho180.B` con `De180`.

   Cómo se prueba, en `MotorEstribosTests`:
   - con 180° y #3, el largo del cerrado de la maqueta es 1242 + 2 × 142 = 1526;
   - el abierto suma 2 × 142;
   - un adicional sin `forma` en el JSON queda en C.
2. **Vista previa.** Archivo: `Generacion/PreviaAcero.cs`. Cambios:
   - grados 180, con la cola de `Gancho180.E`;
   - `Abierto` en S: la rama en diagonal (H6) y los ganchos hacia lados opuestos.

   Cómo se prueba, en `PreviaAceroTests`:
   - la cola de 180° mide 65 en el #3;
   - en S, el gancho de arriba y el de abajo quedan a lados opuestos de la rama y cada uno rodea su barra;
   - en C queda igual que hoy.
3. **Tipos de gancho de Revit.** Archivo: `Revit/TiposAcero.cs`. Cambios:
   - `GanchoEstribo` y `GanchoGrapa` aceptan 180, con multiplicador `Gancho180.E / d`;
   - los nombres pasan a ser `"CQEING Estribo 180° #3"` y `"CQEING Grapa 180° #3"`
     (`GanchoPropio.Nombre` ya arma el del estribo).

   Cómo se prueba: compila, y se mide en la tarea 4.
4. **Creación en Revit.** Archivo: `Revit/CreadorEstribos.cs`. Cambios:
   - en `Abierto`, en S, los ganchos `Right/Left` y la línea de la rama según H6;
   - `CorregirAbierto` deja en S un gancho para cada lado (en C sigue igual).

   Cómo se prueba, en Revit 2026, con el pórtico de prueba y #3:
   - cerrado de 180°;
   - abierto en C de 180°;
   - abierto en S de 180°;
   - abierto en C de 135° (control).

   Se mide la cola (65 mm), que cada gancho abrace su barra y que la S no cruce el cerrado. Las capturas van a
   `docs/planes/imagenes/2026-10-22-estribo-180-*.png`.
5. **Tablero, paso de estribos.** Archivos: `Tablero/web/tablero.js` y `Tablero/web/demo.js`. Cambios:
   - en el selector, la opción `180°`;
   - `estribos()` envía `'De180'`;
   - el adicional abierto lleva `forma: 'C' | 'S'` en el estado y en lo que se envía (líneas 53 y 621);
   - el control C / S en la fila del adicional, como lo dibuje la maqueta;
   - la demostración también.

   Cómo se prueba, en Revit:
   - elegir 180°, ver el largo y los kilos en la lista;
   - cambiar a S y ver el corte;
   - generar.
6. **Pantalla de Reglas.** Archivos: `Tablero/VistaTablero.cs` (`ReglaVista` + campo de 180°), `Tablero/ArmadorVista.cs`
   (`Reglas` y `ReglasDe`) y `Tablero/web/tablero.js` (encabezado, input y `guardarReglas`). Cambios: el texto de ayuda
   dice que el estribo de 180° sale de la columna Gancho 180°.

   Cómo se prueba:
   - en `ArmadorVistaTests`, ida y vuelta de la columna;
   - en Revit, editar el 180° del #3, guardar y ver que el largo del estribo cambia.
7. **Notas y cierre.** Archivos: `Desktop\addin vigas\traslapos-vigas-notas.md`, porque cambia la línea "El gancho de
   180° se guarda, pero no aparece en la pantalla". Además se completa la sección Estado de este plan.

## Cómo probarlo

```
rtk dotnet test CQEING-Addins.sln -c Release
rtk dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026
```

Antes de desplegar, comprobar que Revit esté cerrado:
`Get-Process -Name Revit -ErrorAction SilentlyContinue`.

1. Abrir el pórtico de prueba y el tablero. En el paso de estribos, elegir #3 y 180°: la lista muestra el largo con
   2 × 142.
2. "+ Abierto" con una barra arriba y una abajo. En su fila, elegir S: el corte la dibuja en S.
3. Generar. En 3D y en el corte de Revit:
   - el cerrado con 180°;
   - la grapa en S abrazando las dos barras;
   - las colas de 65 mm.
4. Volver a 135°: todo queda como antes.
5. En Reglas, cambiar el Gancho 180° del #3 y guardar: cambia el largo del estribo.

## Estado

- [x] Plan revisado con Juan (22-oct).
- [x] ~~Tarea 0 (maqueta)~~: no se hace (decisión 5).
- [x] **Tarea 1 (modelo y motor)**, 22-oct, rama `vigas-estribo-180`, sin commit. Cambios:
  - `GanchoEstribo.De180` y `enum FormaGrapa { C, S }` en `Modelo/Estribos.cs`.
  - `EstriboAdicional`, `EstriboEnPortico` y `GrupoEstribos` llevan `Forma`, por defecto C.
  - `MotorEstribos.Gancho` usa `Gancho180.B` con 180°.
  - `PlanificadorAcero` pasa la forma al grupo.

  Pruebas nuevas (primero en rojo, después en verde):
  - `MotorEstribosTests`: `ConGanchoDe180_UsaElGancho180DeLasReglas` (1242 + 2 × 142) y
    `ElAbiertoLlevaLaFormaElegidaYElCerradoVaEnC`.
  - `ProtocoloTableroTests`: `CalcularTraeElGancho180YLaGrapaEnS`; sin `forma` en el JSON queda C.
  - `PlanificadorAceroTests`: `Estribos_LaGrapaEnSLlegaAlPlanConSuGancho`.

  Comprobación: `rtk dotnet test CQEING-Addins.sln -c Release` da 803 en verde. El build de `CQEING.Vigas.csproj` para
  2026 da 0 errores y 0 advertencias.

  Ojo: hasta la tarea 3, la vista previa y Revit todavía tratan el 180° como 90° (`PreviaAcero.cs:48`,
  `TiposAcero.cs:73` y `:88`). El tablero aún no lo envía, así que no se ve.
- [x] **Tarea 2 (vista previa)**, 22-oct, sin commit. Cambios en `PreviaAcero.Trazos`:
  - el 180° tiene su cola de `Gancho180.E` (65 en el #3);
  - el 135° conserva la de `Estribo135.E`;
  - el 90° conserva la de 6 d.

  En `PreviaAcero.Abierto`, en S, abajo la rama pasa al otro lado de la barra. La rama queda en diagonal (de X 175
  arriba a X 225 abajo en la prueba) y cada gancho abraza su barra desde un lado distinto. En C sale igual que antes;
  las pruebas viejas no cambiaron.

  Pruebas nuevas (primero en rojo, después en verde):
  - `ElCerradoDe180DaLaVueltaALaEsquinaConLaColaDelCuadro`: colas de 65 que terminan en (135, 405) y (95, 365).
  - `ElAbiertoDe180TieneLasColasParalelasALaRama`: colas en X 225.
  - `ElAbiertoEnSAbrazaCadaBarraDesdeUnLadoDistinto`.

  Comprobación: la suite da 806 en verde y el build de 2026 da 0 errores y 0 advertencias.

  Decisión de la tarea: en S se voltea el lado del gancho de **abajo** y el de arriba queda como en C. Si en Revit
  conviene el otro (tarea 4), se cambia aquí y en `CreadorEstribos` juntos.
- [x] **Tarea 3 (tipos de gancho de Revit)**, 22-oct, sin commit.
  - `GanchoPropio.DeEstribo(gancho, regla)` da el ángulo y la cola E del cuadro: 135 → `Estribo135.E`, 180 →
    `Gancho180.E` y 90 → sin cola, que pasa a 6 d. Lo usan la vista previa y los dos tipos de Revit, así que la cola
    sale de un solo lugar.
  - `TiposAcero.GanchoEstribo` crea `"CQEING Estribo 180° #3"` y `GanchoGrapa` crea `"CQEING Grapa 180° #3"`, con
    multiplicador E/d (65 / 9.5 en el #3). Sin ese multiplicador, Revit deja la cola en 4 d (H5).

  Prueba nueva: `GanchoPropioTests.DelEstribo_DaElAnguloYLaColaDelCuadro` (primero en rojo, después en verde).

  Comprobación: la suite da 807 en verde y los builds de 2026 y 2027 dan 0 errores y 0 advertencias. La medida en Revit
  queda para la tarea 4.
- [x] **Tarea 4 (creación en Revit), código hecho**, 22-oct, sin commit.
  - `Rama.Lados(ancho, forma)` (`PlanAcero.cs`) da de qué lado de cada barra va la rama. La usan la vista previa y
    `CreadorEstribos`, así que las dos dibujan la misma S.
  - En `CreadorEstribos.Abierto`, en S, la línea va en diagonal: abajo del otro lado de la barra. Los ganchos se crean
    `Left` (abajo) y `Right` (arriba).
  - `CorregirAbierto` usa el lado de cada punta.
  - En `Cerrado`, el cambio de la decisión 7.

  Prueba nueva: `RamaTests` (C, S y la barra en el centro exacto), primero en rojo y después en verde.

  Comprobación: la suite da 810 en verde y los builds de 2026 y 2027 dan 0 errores y 0 advertencias.

  **Falta la prueba en Revit.** Necesita la tarea 5, porque el tablero todavía no deja elegir 180° ni S. Se prueban las
  dos juntas.
- [x] **Tarea 5 (tablero, paso de estribos)**, 22-oct, sin commit. Cambios en `tablero.js`:
  - el selector ofrece 135° · 90° · 180°, y `estribos()` envía `De180`;
  - cada adicional lleva `forma`, y la fila del abierto tiene un control C | S (`aria-pressed`, mismo azul que las
    pestañas);
  - el corte dibuja el abierto con una raya por gancho hacia donde termina. En C las dos van hacia la cara de la viga;
    en S, la de abajo va hacia el centro, el mismo lado que `Rama.Lados`.

  En C#, `FiguraSeccion.Forma` (`"C"`/`"S"`) sale de `EstriboEnPortico.Forma`. En `demo.js`, el largo del estribo de
  180° usa `G180`.

  Prueba nueva: `ArmadorVistaTests.ElCorteDiceLaFormaDeLaGrapa` (primero en rojo, después en verde).

  Comprobación: la suite da 811 en verde, el build de 2026 da 0 errores y 0 advertencias, y `node --check` pasa en los
  dos `.js`. Desplegado en Revit 2026 con `desplegar-local.ps1`; Revit estaba cerrado (comprobado con `Get-Process`).
- [x] **Tarea 7 (notas)**, 22-oct. En `Desktop\addin vigas\traslapos-vigas-notas.md`:
  - la línea de la pantalla de Reglas (§0) y la tabla de datos (§10) ya incluyen el Gancho 180°;
  - la decisión 5 de estribos (§0) suma el 180°;
  - hay una sección nueva, §14, con la reunión del 22-oct: lo pedido, lo decidido, los valores y lo que queda fuera.
- [ ] Prueba en Revit de las tareas 4, 5 y 6 (Juan).

### Criterios de aceptación (al 22-oct, antes de la prueba en Revit)

- ✅ Selector 135° · 90° · 180°: tarea 5.
- ✅ Largo y kilos con el B de 180°: tarea 1 (`ConGanchoDe180_UsaElGancho180DeLasReglas`).
- ✅ Control C / S en cada adicional abierto, C por defecto: tareas 1 y 5.
- ✅ Vista previa con 180° y C/S: tarea 2 (pruebas de `PreviaAceroTests`).
- ⏳ En Revit, 180° con cola de 65 y S abrazando cada barra: falta la prueba de Juan.
- ✅ Reglas con la columna Gancho 180° editable: tarea 6.
- ✅ Los pórticos de 135°/90° no cambian: las pruebas de antes siguen en verde. En Revit, con el control del paso 4 de
  "Cómo probarlo".
- ✅ `rtk dotnet test CQEING-Addins.sln -c Release`: 813 en verde.
- [x] **Tarea 6 (pantalla de Reglas)**, 22-oct, sin commit.
  - `ReglaVista` suma `G180`, que en el JSON va como `g180`. La pantalla tiene la columna "Gancho 180°" entre Gancho 90°
    y Estribo 135°, y la grilla pasa de 4 a 5 columnas.
  - `ReglasDe` cambia solo la B del gancho de 180°: E, D y A del cuadro quedan, así que la cola del estribo sigue en 65.
    Un valor vacío o que no es número no se puede guardar ("El gancho de 180° del #3 debe ser mayor que 0.").
  - `guardarReglas` envía `g180`. El texto de ayuda nombra la columna.
  - En `demo.js`, el 180° pasa a `RULES0` (`g180`) y desaparece la constante `G180`.

  Pruebas:
  - `LasReglasVanConLosDecimalesDeLaMaqueta` ahora espera 0.142 en el #3.
  - Nuevas: `ElGancho180DeLaPantallaCambiaSoloSuLargo` y `UnGancho180VacioHaceFallarLasReglas`.

  Comprobación: la suite da 813 en verde, el build de 2027 da 0 errores y 0 advertencias, y `node --check` pasa en los
  dos `.js`.
- [ ] Probado en Revit.
