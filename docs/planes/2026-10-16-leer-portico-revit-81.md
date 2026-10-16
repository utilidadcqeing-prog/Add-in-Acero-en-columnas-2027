# Plan: leer el pórtico desde Revit (#81)

## Contexto

- **Qué se pide:** la issue [#81](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/81) (épica #78, cuarta en el orden de trabajo). Leer del modelo todo lo que necesita el motor, sin que el usuario lo escriba, y agrupar las vigas en pórticos:
  - **pórticos:** vigas continuas sobre un mismo eje ("Pórtico 1"); si el eje tiene una interrupción, "Pórtico 1a" y "1b";
  - **tramos** ("Viga C-D") y **voladizos**;
  - **sección** de cada tramo; si cambia entre luces, avisarlo (#92);
  - **apoyos** (columnas, muros o vigas cargadoras), la posición de sus caras y su ancho;
  - **tipo de viga** por el nivel: si no se puede detectar, aérea y aviso.
- **Para qué:** es la columna izquierda de la maqueta (opción 1, https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh):
  - lista de pórticos con nombre, eje y tipo ("Pórtico 1 · Eje 1 · A a D + voladizo · Aérea");
  - al abrir uno, sus tramos (Viga A-B, …, Voladizo D);
  - el alzado con las luces y los ejes.
- **Referencia única:** la maqueta. No se toma nada del add-in de columnas.
- **Fuera de alcance:**
  - la ventana (#83);
  - "Seleccionar en Revit" y "En vivo" (#83);
  - generar barras (#85);
  - publicar con el gestor (#86);
  - el doblez por cambio de sección (#92): aquí solo se avisa.
- **Nombres de pórticos y vigas:** pendiente con Carlos (#79, pendiente 3). Por ahora el motor los calcula ("Pórtico 1", "Viga A-B") y **no los guarda en Revit**.
- **Entorno:**
  - Revit 2026 / 2027;
  - rama `vigas-portico-81`, que sale de `vigas-filas-88` (PR #94);
  - **modelo de prueba: hace falta uno real** (ver "Lo que necesito de Juan").

## Criterios de aceptación

De #81, con un modelo real:

Cada criterio cumplido dice si se comprobó en el modelo real o con pruebas (`AgrupadorPorticosTests`).

- [x] Pórtico de varias luces entre columnas: sus luces, apoyos y caras coinciden con el modelo (**modelo real**, Hallazgo 4; `VigasSeguidasSobreUnEje_FormanUnPorticoConSusApoyosYCaras`).
- [x] Pórtico con voladizo: el voladizo se detecta con su largo (prueba `ElPorticoDeLaMaqueta_SaleIgualAlQueUsaElMotor`; el modelo real no trae voladizos).
- [x] Eje con interrupción: salen dos pórticos, "1a" y "1b" (prueba `UnHuecoSinApoyo_PartePorticoEnAYB`; el modelo real no trae ese caso).
- [x] Viga apoyada en otra viga o en un muro: el apoyo se detecta con sus caras (pruebas `UnaVigaQueTerminaSobreOtraViga_LaTomaComoApoyo` y `UnMuroDeCargaQueCruzaLaViga_EsApoyo_YUnoALoLargoNo`; en el modelo real, el muro a lo largo se ignora bien).
- [x] Pórtico de cimentación: se marca como cimentación por el nivel (**modelo real**: Piso 1; `ElTipoSaleDelNivel_CotaMenorOIgualACeroOElNombre`).
- [x] Viga sin apoyos detectables: aviso claro (**modelo real**: 809244; `UnaVigaSinApoyos_NoArmaPorticoYSeAvisa`).
- [x] Cambio de sección entre luces: aviso (`UnCambioDeSeccionEntreTramos_SeAvisa`). El aviso falso del modelo real se corrigió (`DiferenciasDeDecimalesEnLaSeccion_NoSonCambioDeSeccion`).
- [x] El pórtico leído entra al motor de #80 sin cambios, con `MotorVigas.Calcular` (`ElPorticoDeLaMaqueta_SaleIgualAlQueUsaElMotor`).
- [x] `dotnet test CQEING-Addins.sln -c Release` en verde, y el add-in compila para 2026 y 2027.

## Hallazgos del código y de Revit real

1. **Cómo se declara un add-in en el monorepo:** `addins/<id>/addin.json` con id, nombre, proyecto y años de Revit. El CI compila cada uno para cada año, y `tools/desplegar-local.ps1 -Addin <id> -Revit <año>` lo instala en la PC.
   - El `.addin` con su `ClientId` va junto al `.csproj`.
   - La pestaña CQEING se arma con `addins/compartido/CintaCqeing.cs`, que es infraestructura común.
   - Visto en `addins/imagenes/`.
2. **El proyecto del add-in en `addins/acero-vigas/` tomaría también los `.cs` de `calculo/`**, porque los proyectos SDK incluyen las subcarpetas. Hay que excluir `calculo/**` y referenciar la librería como proyecto.
3. **Revit real** (2026-10-16). Diagnóstico corrido por Juan en "Prueba 080720sfsd26.rvt", Revit 2026. Informe: `%TEMP%\<sesión>\CQEING-vigas-diagnostico-20261016-144116.txt`, 382 líneas. Todo en mm.
   - **El modelo trae:**
     - ejes A (x = 0), 5 (3500) y 6 (7000) en un sentido; 1 (y = 0), 7 (4000), 8 (8000) y 9 (12000) en el otro;
     - niveles N-0.65 (−650), N+0.00 Piso 1 (0), N+3.50 Piso 2 (3500) y N+7.00 Cubierta (7000).
   - **Las vigas están partidas en cada eje:** una por luz, con las puntas en el cruce de ejes, es decir, en el centro de la columna. Ejemplo: 782224, de (0, 0) a (3500, 0).
   - **La línea de ubicación va por la cara de arriba.** La viga del nivel 0 ocupa z de −350 a 0.
   - **La sección medida en la geometría coincide con los parámetros `b` y `h` del tipo:**
     - Piso 1: 300 × 350;
     - Piso 2: 250 × 350;
     - Cubierta: 200 × 350.

     Se usa la geometría, porque no depende de cómo se llamen los parámetros de cada familia.
   - **Columnas:**
     - pedestales de 400 × 400 desde N-0.65 hasta 0;
     - columnas de 300 × 300 por piso (0–3500 y 3500–7000);
     - todas centradas en los cruces de ejes.

     **Una columna toca dos niveles de vigas, pero solo apoya la que se cruza con ella en el alto.** La viga del nivel 0 (z −350..0) se cruza con el pedestal (−650..0), no con la columna de arriba (0..3500), que solo la toca en z = 0. Por eso un apoyo tiene que **solaparse en z** con la viga, no basta con que su huella cruce la línea.
   - **Cubierta incompleta:**
     - en el eje 1 solo está A–5;
     - en el eje 6 falta el tramo 1–7;
     - en el eje 5 están las tres luces.
   - **Muros:**
     - 4 de "Cerramiento provisional" (uso `Bearing`, 0–2100) en el perímetro, en x = −3617 / 16283 e y = −8950 / 20950;
     - 5 de uso `NonBearing` (cerramiento del piso 1 e interior en el eje 8).
   - **Elementos "TEST" en el perímetro:**
     - vigas de 100 × 350 a z = 2100 apoyadas **a lo largo** de los muros de cerramiento: el muro va debajo de toda la viga, no la cruza;
     - columnitas de 100 × 200 en las esquinas;
     - la viga 809244 está en z −350..0 en x = 16283, sin nada que la cruce en z: es una **viga sin apoyos**.
   - **Casos que el modelo no trae:** voladizo, eje interrumpido (1a / 1b), viga apoyada en otra viga y viga cuyo muro la cruza. Se prueban con modelos sintéticos.
4. **Segunda corrida del diagnóstico, ya con los pórticos armados** (2026-10-16, informe `CQEING-vigas-diagnostico-20261016-145358.txt`):
   - **21 pórticos sobre ejes**, 7 por nivel (1, 7, 8, 9, A, 5 y 6), con apoyos, caras, tramos y secciones que coinciden con el modelo.
     - Piso 1: los apoyos son los pedestales de 400 (caras −200..200). Se ignora la columna de arriba.
     - Piso 2 y Cubierta: columnas de 300 (−150..150).
   - **Tipo:** Piso 1 sale como cimentación; Piso 2 y Cubierta, como aéreas.
   - **Cubierta incompleta bien leída:** Pórtico 1 solo tiene A-5, y Pórtico 6 va de 7 a 9, con el origen en el eje 7.
   - **Viga 809244:** aviso `SinApoyos`.
   - **Vigas TEST del perímetro:** 4 "Pórtico sin eje", apoyados en las columnitas de las esquinas. El muro que va a lo largo no cuenta.
     - Salen como cimentación porque su nivel es "N+0.00 Piso 1", aunque están a z = 2100. Es lo que dice la regla de la decisión 4; queda anotado por si aparece en un modelo real.
   - **Error encontrado:** aviso falso de `CambioDeSeccion` en "Pórtico A", que además no decía de qué nivel era.
     - Causa: ruido de decimales en el ancho medido en la geometría.
     - Arreglo: la sección se redondea al milímetro y los avisos de un pórtico llevan su nivel ("Pórtico A · N+3.50 Piso 2").
     - Prueba: `DiferenciasDeDecimalesEnLaSeccion_NoSonCambioDeSeccion`.

## Decisiones

1. **Dos partes separadas** (2026-10-16, Juan).
   - **Agrupar en pórticos** es lógica pura en la librería del motor (`calculo/CQEING.Vigas.Calculo/Porticos/`). Recibe un "modelo leído" sin tipos de Revit (vigas, ejes, apoyos y niveles en mm) y devuelve los pórticos listos para el motor. Se prueba sin Revit.
   - **Leer Revit** es un adaptador fino en el proyecto del add-in (`addins/acero-vigas/Revit/`): convierte elementos de Revit a ese modelo leído.
   - Por qué: lo delicado (agrupar, nombrar, detectar voladizos e interrupciones) queda con pruebas, y lo de Revit se verifica a mano con el diagnóstico.
2. **Proyecto del add-in desde ahora** (2026-10-16, Juan).
   - Se crea `addins/acero-vigas/` con `addin.json`, `CQEING.Vigas.csproj`, `.addin` y `AppVigas` (panel "Vigas" en la pestaña CQEING), porque hace falta para correr el diagnóstico en Revit.
   - #86 se encarga de publicarlo con el gestor.
3. **Comando de diagnóstico temporal "Pórticos (diagnóstico)"**, como pide la regla de verificar contra Revit real. Lee el modelo, arma los pórticos y escribe un informe de texto. El informe tiene:
   - los datos crudos: vigas, ejes, apoyos y niveles;
   - los pórticos armados.

   Se borra cuando el tablero (#83) tenga "Seleccionar en Revit".
4. **Reglas para agrupar** (supuestos a confirmar con el diagnóstico):
   - **Mismo pórtico:** vigas del mismo nivel, paralelas y sobre la misma línea (a menos de 50 mm), seguidas sin hueco o con el hueco tapado por un apoyo.
   - Un hueco sin apoyo corta el pórtico: "1a" y "1b".
   - **Nombre:** "Pórtico" + el eje de la grilla que coincide con la línea. Si no coincide ninguno, "Pórtico sin eje N" con aviso.
   - **Tramos:** entre apoyos seguidos, con el nombre de sus ejes ("Viga A-B"). Lo que sobresale del primer o del último apoyo es voladizo.
   - **Apoyos:** columnas y muros estructurales cuya huella cruza la línea del pórtico **y que se solapan en altura con la viga**. Lo del alto salió del modelo real: la columna de arriba toca la viga, pero no la apoya.
     - **Muros:** solo los de carga (uso distinto de `NonBearing`) y **solo si cruzan la viga**. Un muro que va a lo largo de la viga no es apoyo (decidido por Juan el 2026-10-16 con las vigas "TEST" del modelo).
     - **Viga cargadora:** una viga que cruza es apoyo solo donde una viga del pórtico termina sobre ella y no hay columna ni muro.
   - **Caras de un apoyo:** el rango de su huella medido sobre la línea del pórtico.
   - **Eje de un apoyo:** el eje de la grilla que lo cruza. Si no hay, su centro.
   - **Origen de posiciones:** el eje del primer apoyo (eje A), como espera el motor.
   - **Sección:** ancho y alto sacados de la geometría de la viga. Si cambia entre tramos, aviso de cambio de sección.
   - **Tipo** (decidido por Juan el 2026-10-16, tras ver que el modelo real no nombra la cimentación):
     - cimentación si la cota del nivel de la viga es **≤ 0** o si su nombre contiene "cimenta" o "fundac" (sin importar mayúsculas ni tildes);
     - aérea en los demás casos;
     - sin nivel: aérea, con aviso.

     En el modelo de prueba, las vigas de "N+0.00 Piso 1" quedan como cimentación, y las de Piso 2 y Cubierta como aéreas.
   - **Varios niveles sobre el mismo eje** son pórticos distintos. Cada uno lleva su nivel, que el tablero muestra junto al nombre.
5. **Solo API pública y documentada de Revit:**
   - `FilteredElementCollector` por categoría (`OST_StructuralFraming`, `OST_StructuralColumns`, `OST_Walls`);
   - `LocationCurve`, `Grid.Curve`, `Level.Elevation`, `Element.get_Geometry` y `UnitUtils` para pasar a mm.

   Cada miembro se revisa en la documentación oficial antes de usarlo (regla del `CLAUDE.md`).

## Plan paso a paso

Pruebas primero en la parte pura.

1. **Proyecto del add-in y diagnóstico de datos crudos**
   - Archivos:
     - `addins/acero-vigas/addin.json`, `CQEING.Vigas.csproj` (excluye `calculo/**` y referencia la librería), `CQEING.Vigas.addin` y `AppVigas.cs`;
     - `Revit/ComandoDiagnosticoPorticos.cs`, que por ahora solo vuelca los datos crudos;
     - `Resources/` con los iconos de la empresa.
   - Cómo se prueba:
     - compila para 2026 y 2027;
     - Juan lo instala con `desplegar-local.ps1`, lo corre en el modelo de prueba y comparte el informe;
     - lo encontrado se anota en Hallazgos.
2. **Modelo leído**
   - Archivo: `calculo/.../Porticos/ModeloLeido.cs`: `VigaLeida`, `EjeLeido`, `ApoyoLeido` y `NivelLeido`, en mm.
   - Cómo se prueba: compila.
3. **Agrupar en pórticos**
   - Archivo: `Porticos/AgrupadorPorticos.cs`.
   - Cómo se prueba, con modelos sintéticos armados con lo que muestre el modelo real:
     - pórtico de 3 luces con voladizo, el de la maqueta: da el mismo `Portico` que usan las pruebas del motor;
     - eje interrumpido da 1a y 1b;
     - viga sobre viga;
     - viga sobre muro;
     - cimentación por el nivel;
     - nivel no reconocido da aérea con aviso;
     - viga sin apoyos da aviso;
     - cambio de sección da aviso;
     - el pórtico leído pasa por `MotorVigas.Calcular` sin errores.
4. **Adaptador de Revit**
   - Archivo: `Revit/LectorModelo.cs`: elementos de Revit → `ModeloLeido`.
   - Cómo se prueba: el diagnóstico ahora muestra también los pórticos armados; Juan lo corre y se compara con el modelo, criterio por criterio.
5. **Cierre**
   - Correr `dotnet test` de toda la solución.
   - Compilar para 2026 y 2027.
   - Llenar la sección Estado con la evidencia del modelo real.
   - Comentar en #81.

## Lo que necesito de Juan

1. **Un modelo de Revit de prueba** (ruta y año de Revit). Idealmente tiene que traer:
   - un pórtico aéreo de varias luces con voladizo;
   - un eje con interrupción;
   - una viga apoyada en otra viga y otra en un muro;
   - vigas de cimentación.
2. **Correr el diagnóstico** en ese modelo (pasos 1 y 4) y pasarme el informe.
   - Otra opción: arreglar la conexión del servidor MCP de Revit, que en esta sesión no conectó. Así puedo leer el modelo directamente.

## Cómo probarlo

```
dotnet test CQEING-Addins.sln -c Release
dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2027
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2027
```

Antes de desplegar, verificar que Revit esté cerrado: `Get-Process -Name Revit -ErrorAction SilentlyContinue`.

En Revit: pestaña CQEING › Vigas › "Pórticos (diagnóstico)" en el modelo de prueba. Se compara el informe con el modelo.

## Estado

- [x] Plan revisado con el usuario (aprobado el 2026-10-16). Modelo de prueba: "Prueba 080720sfsd26.rvt", abierto en Revit 2026.4.
- [x] Pasos 1 a 4 implementados (2026-10-16):
  - proyecto del add-in `addins/acero-vigas/` (`addin.json`, `CQEING.Vigas.csproj`, `.addin`, `AppVigas`);
  - `Revit/LectorModelo.cs` y `Revit/ComandoDiagnosticoPorticos.cs`;
  - `calculo/.../Porticos/ModeloLeido.cs` y `AgrupadorPorticos.cs`.
- [x] Probado en Revit 2026.4 con "Prueba 080720sfsd26.rvt" (Hallazgos 3 y 4).
  - Después del arreglo del aviso falso, el diagnóstico no se volvió a correr en Revit; el arreglo está cubierto por la prueba nueva.
- [x] Pruebas (2026-10-16):
  - `dotnet test CQEING-Addins.sln -c Release` → Vigas 80/80, Gestor 34/34, Publicador 8/8;
  - el add-in compila para 2026 y 2027 con 0 advertencias (con Revit cerrado: si está abierto, la DLL de 2026 queda tomada).
- **Pendiente:**
  - commit y PR (a pedido del usuario);
  - nombres definitivos de pórticos y vigas, y si se guardan en Revit (#79, pendiente 3);
  - quitar el comando de diagnóstico cuando el tablero (#83) tenga "Seleccionar en Revit".
