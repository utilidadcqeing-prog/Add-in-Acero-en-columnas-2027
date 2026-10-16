# Plan: motor de cálculo de traslapos en vigas (#80)

## Contexto

- **Qué se pide:** la issue [#80](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/80) (épica [#78](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/78)). Es una librería **sin Revit**. Recibe el pórtico, el armado, las reglas y los cambios manuales. Devuelve las barras de cada capa con sus largos, traslapos, kilos y avisos.
- **Para qué:** la salida tiene que alcanzar para pintar **tal cual** el tablero de la maqueta (opción 1): https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh. La vista solo pinta, no calcula nada. Los largos y kilos de la maqueta son de ejemplo, **no son valores esperados**.
- **Origen de las reglas:** reuniones con Carlos del 08-oct y 16-oct-2026, resumidas en #78 y #80.
- **Fuera de alcance:**
  - filas y capacidad (#88);
  - bastones (#89);
  - estribos (#90);
  - corte (#91);
  - cambio de sección (#92);
  - leer el pórtico de Revit (#81);
  - tablero (#83);
  - generar en Revit (#85);
  - zona de 2h (pendiente con Carlos en #79).
- **Entorno:**
  - .NET SDK 8 y 10;
  - librería `net8.0`, que la pueden usar tanto el add-in de Revit 2026 (`net8.0-windows`) como el de 2027 (`net10.0-windows`);
  - pruebas con xUnit;
  - rama `vigas-motor-80`.

## Criterios de aceptación

Copiados de #80:

Cada criterio cumplido lleva entre paréntesis la prueba que lo comprueba.

- [x] Núcleo, con las reglas del ejemplo de la reunión (traslapo 1.00, gancho 0.20, múltiplo 0.50):
  - columna de 3 barras: 2.585 · 8.00 · 3.20 → 2.50 · 8.00 · 3.50 (`Ejemplo1_TresBarras_DaLasDeCarlos`);
  - columna de 2 barras: 6.085 · 6.70 → 6.00 · 7.00, con traslapo 1.215 (`Ejemplo2_DosBarras_MandaLaDeArribaYElTraslapoCrece`).
- [x] Pórtico aéreo de la maqueta con la tabla real: sin traslapo en el voladizo, kg de inicio y final distintos y la más económica marcada (`LaUltimaBarraEntraAlVoladizoConGancho`, `LaCapaDeLaCara_TraeLasDosOpcionesYUsaLaMasEconomica`).
- [x] Pórtico de cimentación: capas invertidas; los ids siguen la posición física (`EnCimentacion_LaCapaInferiorVaAlCentroYLosIdsNoCambian`).
- [x] Capa con `3 #5 + 2 #4` (`ConDosDiametros_LaCapaUsaElMinimoYElGanchoDelMayor`).
- [x] Traslapo que supera el mínimo en más de 0.30: aviso con causa y ahorro (`TraslapoQuePasaElMinimoEnMasDe30cm_AvisaConCausaYAhorro`).
- [x] Tramo corto que obliga a usar 12 m (`TramoCortoDondeNoCabeElTraslapo_UsaVarillaDe12`, `BarraQueNoSaleDeLaVarillaDe6_AvisaQueUsaLaDe12`).
- [x] Barra intermedia que manda (`BarraIntermediaQueManda_SeReparteEntreSusDosTraslapos`).
- [x] Vista previa:
  - subir bloqueado por la varilla (`SubirUnaBarraQueYaMide6_SeBloqueaPorLaVarilla`);
  - traslapo por debajo del mínimo bloqueado (`TraslapoMasCortoQueElMinimo_SeBloquea`);
  - traslapo alargado (`TraslapoAlargado_CreceLaBarraDeLaDerechaAlMultiplo`).
- [x] Un cambio manual se conserva al recalcular (`LosCambiosManualesSeConservanAlRecalcular`).
- [x] `dotnet test CQEING-Addins.sln -c Release` en verde (lo corre el CI en cada PR).

## Hallazgos del código y de Revit real

1. **El CI corre las pruebas de toda la solución.** `.github/workflows/compilar.yml` ejecuta `dotnet test CQEING-Addins.sln -c Release`. Los proyectos nuevos se agregan al `.sln` y sus pruebas corren en cada PR.
2. **`addins/Directory.Build.props` se aplica a todo lo que está debajo de `addins/`.** Le pone las referencias de Revit, WPF, `net*-windows` y los `.cs` de `addins/compartido/`. Una librería sin Revit dentro de `addins/` necesita su propio `Directory.Build.props` para cortar esa herencia.
3. **Los add-ins se descubren por `addins/<id>/addin.json`**, tanto en el CI como en el publicador (`tools/CQEING.Publicador/ConfiguracionAddin.cs`). Una carpeta sin `addin.json` se ignora. Por eso se puede crear `addins/acero-vigas/` ahora y agregarle el `addin.json` cuando exista el add-in (#86).
4. **Ya hay un patrón de librería sin Revit con pruebas:** `gestor/CQEING.Gestor.Core` y `CQEING.Gestor.Core.Tests` (`net8.0`, xUnit 2.9.3, `Nullable` e `ImplicitUsings` activados).
5. **Regla "verificar contra Revit real":** Revit tiene `RebarSpliceUtils` (ver `docs/planes/2026-09-09-empalmes-rebarspliceutils.md`).
   - Decide dónde partir una barra con un largo máximo y uno mínimo.
   - No sabe de múltiplos de corte, ni de traslapos pegados a la cara, ni de kilos.
   - Por eso el **dónde** lo decide este motor. **Cómo** se crea el empalme en Revit se resuelve en #85.
6. **Cálculo a mano de los ejemplos con la búsqueda de la decisión 2** (en mm, M = 500, traslapo mínimo 1000, gancho 200):
   - **Ejemplo 1:** barras de abajo hacia arriba C, B, A; base 0 y corona 11 385.
     - El acero mínimo es 14 000. Gana C = 3500, B = 8000, A = 2500, con traslapos C–B = 1215 y B–A = 1000.
     - Desvíos: 192.5 y 85, suma 277.5.
     - Otras combinaciones con el mismo acero suman más desvío: 3000 · 8000 · 3000 da 507.5, y 3500 · 7500 · 3000 da 607.5.
   - **Ejemplo 2:** B abajo, A arriba.
     - El acero mínimo es 13 000, con traslapo 1215.
     - Gana B = 7000 y A = 6000, con desvío 192.5. La otra opción, 6500 · 6500, da 307.5.
   - Los dos resultados coinciden con lo que concluyó Carlos.
   - Las pruebas `Ejemplo1_TresBarras_DaLasDeCarlos` y `Ejemplo2_DosBarras_MandaLaDeArribaYElTraslapoCrece` lo confirman con el código.
7. **Rendimiento** (2026-10-16, medido con `dotnet test --logger "console;verbosity=normal"`).
   - La primera versión del núcleo probaba cada par de holguras (holgura² por fila) y tardaba **2 s** por capa en el pórtico de la maqueta. Es demasiado para un tablero en vivo.
   - Ahora cada fila se procesa entera de una vez, con mínimos acumulados y una ventana que solo crece: tarda **16 ms** por capa.
   - Las pruebas no cambiaron.

## Decisiones

1. **Todo en milímetros enteros** (2026-10-16, Juan).
   - Los múltiplos y los restos salen exactos, sin errores de coma flotante (2.585 entre 0.50 no da exacto en `double`).
   - La interfaz convierte a metros con 2 decimales.
2. **El motor busca la mejor solución en vez de repetir paso a paso el procedimiento de la reunión** (2026-10-16, Juan).
   - Una combinación es válida si:
     - cada barra es múltiplo del módulo y no pasa de su varilla;
     - cada traslapo cumple el mínimo y queda dentro de su zona.
   - Entre las válidas, elige en este orden:
     1. menos barras de 12 m (cuando la varilla base es de 6 m);
     2. menos traslapos;
     3. menos acero (suma de largos);
     4. traslapos más cerca de sus puntos ideales (suma de desvíos);
     5. desempate fijo: la primera que encuentra la búsqueda (decisión de "empates" de #78).
   - Por qué: reproduce los dos ejemplos de Carlos (hallazgo 6) y resuelve sola la barra intermedia (regla 6 de #80) y el punto de partida, sin casos especiales.
3. **Núcleo de una línea con "ranuras" de traslapo** (2026-10-16, Juan).
   - El núcleo recibe:
     - las dos puntas fijas con su gancho;
     - una lista ordenada de ranuras. Cada ranura tiene una zona permitida, un punto ideal y qué parte del traslapo se mide contra ese punto: inicio, centro o fin.
   - Devuelve la mejor solución con **exactamente** esas ranuras.
   - El adaptador de vigas decide qué luces llevan traslapo (prueba 0, 1, 2… ranuras).
   - Por qué: el ejemplo de columna fija sus traslapos en el piso 1 y en el 3, y en la viga hay que elegir en qué luces van.
4. **Punto ideal y zona por capa** (2026-10-16, Juan; reglas de #80).
   - **Capa del centro:** el centro del traslapo va en la mitad de la luz libre.
   - **Capa de la cara, al inicio de la luz:** el traslapo empieza a 50 mm de la cara izquierda.
   - **Capa de la cara, al final de la luz:** el traslapo termina a 50 mm de la cara derecha.
   - **Zona permitida de toda ranura:** de la cara izquierda + 50 a la cara derecha − 50, dentro de una misma luz.
   - **El voladizo no tiene ranuras.**
5. **Puntas de las barras** (2026-10-16, Juan).
   - Las barras van de punta a punta del pórtico, menos el **recubrimiento de puntas**. Es un dato de entrada, 50 mm por defecto, como en la maqueta.
   - Llevan gancho de 90° en las dos puntas: apoyo de borde o punta de voladizo.
6. **Varios diámetros en una capa** (2026-10-16, supuesto; #79, pendiente 5).
   - El traslapo mínimo y el gancho son los del diámetro mayor, para toda la capa.
   - Todas las barras de esa capa miden lo mismo.
7. **Cambio manual de una barra (Subir / Bajar)** (2026-10-16, Juan).
   - Se fija el largo de esa barra y se vuelve a optimizar el resto, con **las mismas luces de traslapo** de la solución automática. Así los ids no cambian.
   - Si no hay solución, el cambio no se aplica y se avisa.
8. **Cambio manual de un traslapo** (2026-10-16, Juan; regla 14 de #80).
   - Se aplica sobre la solución ya calculada: `inc` = (largo escrito − largo actual), redondeado hacia arriba al múltiplo.
   - La barra de la derecha empieza `inc` antes y crece `inc`.
   - Se bloquea si:
     - el largo escrito es menor que el mínimo;
     - la barra de la derecha no sale de su varilla;
     - el traslapo se sale de su zona (entraría al nudo).
9. **Aviso de traslapo largo** (2026-10-16, Juan; regla 15 de #80).
   - Sale si el traslapo supera el mínimo en más de **300 mm**. Es una constante del motor.
   - Causa "vecina de frontera" si el traslapo toca la primera o la última barra de la capa.
   - En la capa de la cara: si la otra opción no tiene ese aviso, se dice cuántos kg se ahorran con ella.
10. **Cara de la capa de la cara: una por pórtico** (2026-10-16, supuesto; #79, pendiente 6).
    - Si viene vacía, se usa la de menos kg; si empatan, la de inicio.
    - Se devuelven las dos soluciones completas.
11. **Ubicación** (2026-10-16, Juan).
    - Carpeta: `addins/acero-vigas/calculo/`.
    - Proyectos: `CQEING.Vigas.Calculo` (`net8.0`) y `CQEING.Vigas.Calculo.Tests` (xUnit).
    - Un `Directory.Build.props` vacío en `calculo/` evita heredar lo de Revit (hallazgo 2).
    - Los dos proyectos se agregan a `CQEING-Addins.sln`.
12. **Una barra sola no se redondea** (2026-10-16, Juan, al programar el paso 3).
    - Si la capa no necesita traslapos, la barra va de punta a punta. Sus dos puntas son fijas, así que su largo no se puede llevar a un múltiplo.
    - Va derecha si cabe en la varilla (Carlos: "si la barra llega sin pasar de 6 m, va derecha").
13. **A lo sumo un traslapo por luz** (2026-10-16, Juan, paso 5).
    - El núcleo necesita ranuras que no se pisen.
    - Una luz más larga que una varilla no se resuelve con 6 m y pasa a 12 m (regla 8 de #80).
14. **Empate al correr una barra del medio: se reparte** (2026-10-16, Juan, paso 3).
    - Si la barra del medio se puede correr sin cambiar el desvío total (por ejemplo 0 y 200, o 100 y 100), se elige repartir: la menor suma de cuadrados.
    - Es la regla fija de empates de #78 y la opción "repartir" de Carlos (regla 6 de #80).
    - Prueba: `BarraIntermediaQueManda_SeReparteEntreSusDosTraslapos`.
15. **El traslapo se corre a lo sumo un módulo (0.50 m) de su punto ideal** (2026-10-16, Juan, paso 6).
    - **Hallazgo que la motivó:** sin este límite, la prueba de las dos caras falló. "Al inicio" y "al final" daban los mismos kg (76.824). Si el traslapo puede ir a cualquier punto de la luz, el acero total depende solo de cuántos traslapos hay, no de dónde caen, y la elección de cara nunca cambiaría los kilos.
    - En el procedimiento de Carlos el traslapo solo se corre lo que exige el redondeo, menos de un módulo. Por eso él sí obtiene kilos distintos (38 frente a 42).
    - **Consecuencia:** en la capa del centro del pórtico de la maqueta (varilla de 6 m) hacen falta 3 traslapos, uno por luz, igual que dibuja la maqueta. Sin el límite bastaban 2, pero uno quedaba lejos de la mitad de su luz.
    - Pruebas: `ElTraslapoNoSeAlejaDeSuPuntoIdealMasDeLoPermitido`, `PorticoDeLaMaqueta_ConVarillaDe6_UnTraslapoPorLuzYNingunoEnElVoladizo` y `LaCapaDeLaCara_TraeLasDosOpcionesYUsaLaMasEconomica`.
16. **Cambios manuales: qué se bloquea y cómo se avisa** (2026-10-16, Juan, paso 8).
    - Una barra que salía de la varilla de 6 m no puede pasar de 6 m a mano, y una que ya era de 12 m no puede pasar de 12 m. Es el botón "Subir" desactivado de la maqueta.
    - Un traslapo escrito con un largo menor o igual al actual (sin bajar del mínimo) no cambia nada. La vista previa responde "sin cambio" y Aplicar queda desactivado, como en la maqueta.
    - Un cambio guardado que ya no se puede aplicar (por ejemplo, porque cambió la geometría) se ignora y deja un aviso `CambioNoAplicado` con su motivo. La vista previa usa ese mismo aviso para decir por qué no deja aplicar.
    - Un traslapo alargado a mano no genera el aviso de "traslapo largo", porque es largo a propósito.
    - Los cambios de barra respetan también la decisión 15. Por ejemplo, bajar S1 a 2.50 en la maqueta no tiene solución, porque su traslapo quedaría a más de 0.50 de la mitad de A-B.
    - La comparación de kg entre las dos caras se hace sin cambios manuales; los cambios se aplican solo a la cara en uso.

## Plan paso a paso

Se trabaja con pruebas primero: cada paso escribe su prueba, la ve fallar y después escribe el código.

1. **Rama y proyectos**
   - Archivos: `addins/acero-vigas/calculo/Directory.Build.props`, los dos `.csproj` y `CQEING-Addins.sln`.
   - Cómo se prueba: `dotnet test` compila y corre 0 pruebas.
2. **Modelo de datos** (records inmutables)
   - Archivos: `Modelo/*.cs`.
   - Contenido:
     - `Reglas`: por diámetro, traslapo, gancho 90° y kg/m; además múltiplo y varilla.
     - `Portico`: apoyos con eje y caras, y voladizos.
     - `Armado`: grupos de cantidad + diámetro por capa.
     - `EntradaVigas` y `ResultadoVigas`.
     - `Barra`, `Traslapo` y `Aviso`.
   - Cómo se prueba: compila.
3. **Núcleo de una línea**
   - Archivos: `Nucleo/Ranura.cs`, `Nucleo/ProblemaLinea.cs`, `Nucleo/SolucionLinea.cs`, `Nucleo/ResolvedorLinea.cs`.
   - Cómo funciona:
     - Prueba los largos de cada barra (múltiplos del módulo, hasta la varilla) cuya suma da el acero total.
     - Empieza por el acero mínimo y lo va subiendo de a un módulo.
     - Para cada combinación de largos, ubica las barras intermedias, milímetro a milímetro, con programación dinámica.
     - La holgura total de los traslapos es menor que un módulo por cada paso de acero, así que el rango a recorrer es corto.
   - Cómo se prueba:
     - ejemplos 1 y 2 de columna;
     - una ranura imposible devuelve "sin solución";
     - una barra con largo fijo (decisión 7).
4. **Vigas: geometría**
   - Archivo: `Vigas/GeometriaPortico.cs`.
   - Calcula luces, voladizos y puntas de las barras, y arma las ranuras de cada capa según el tipo de viga.
   - Cómo se prueba:
     - el pórtico de la maqueta da 3 luces con sus zonas y el voladizo sin ranura;
     - en cimentación se invierten las capas.
5. **Vigas: elegir las luces y la varilla de 12 m**
   - Archivo: `Vigas/ResolvedorCapa.cs`.
   - Prueba ranuras en cantidad creciente y elige en el orden de la decisión 2.
   - Cómo se prueba:
     - el pórtico aéreo de la maqueta no tiene traslapo en el voladizo;
     - un tramo corto obliga a usar 12 m.
6. **Ids, kg, resumen y las dos opciones de cara**
   - Archivo: `MotorVigas.cs`.
   - Cómo se prueba:
     - ids `S`/`I` según la posición, también en cimentación;
     - kg = largo × Σ (cantidad × kg/m);
     - los kg de inicio y final son distintos, y la más económica queda marcada;
     - la capa `3 #5 + 2 #4` usa el mínimo y el gancho del #5.
7. **Avisos y zonas de traslapo para el corte**
   - Archivo: `Vigas/Avisos.cs`.
   - Cómo se prueba: un traslapo de más de mínimo + 0.30 genera el aviso con su causa y el ahorro de la otra opción.
8. **Cambios manuales y vista previa**
   - Archivos: `MotorVigas.cs`, `Modelo/CambiosManuales.cs`, `Vigas/AplicadorCambios.cs` y `Vigas/CapaCalculada.cs`. Esta última convierte la solución del núcleo a lo que pinta el tablero; salió del paso 6 al ordenar el código.
   - Funciones: `Calcular` con cambios, `PrevisualizarBarra` (subir o bajar) y `PrevisualizarTraslapo`.
   - Cómo se prueba:
     - subir bloqueado por la varilla;
     - traslapo por debajo del mínimo bloqueado ("No puede ser menor que el mínimo");
     - traslapo alargado (con su efecto);
     - un cambio que se conserva al recalcular.
9. **Cierre**
   - Correr `dotnet test` de toda la solución.
   - Llenar la sección Estado.
   - Comentar en #80.

## Cómo probarlo

```
dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests -c Debug
dotnet test CQEING-Addins.sln -c Release
```

En esta issue no hay pruebas en Revit, porque el motor no usa Revit. Se prueba en Revit desde #85 y #87.

## Estado

- [x] Plan revisado con el usuario (aprobado el 2026-10-16, sin cambios).
- [x] Pasos 1 a 8 implementados (2026-10-16), con las pruebas escritas antes que el código.
- [x] Pruebas en verde (2026-10-16):
  - `dotnet test CQEING-Addins.sln -c Release` → Vigas 47/47, Gestor 34/34, Publicador 8/8;
  - compilación en Release con 0 advertencias.
- **Rendimiento:** un pórtico completo (3 capas) tarda unos 65 ms.
- **Cómo quedó la maqueta con el motor real** (tabla real, varilla de 6 m, 3 #5 arriba y abajo):
  - arriba: 3.00 · 5.00 · 5.00 · 4.50, con un traslapo por luz;
  - abajo, al inicio de la luz: 6.00 · 4.50 · 6.00 (76.82 kg), igual que la maqueta;
  - abajo, al final de la luz: 4.50 · 4.50 · 5.00 · 3.50 (81.48 kg);
  - la más económica es "al inicio", como muestra la maqueta.
- **Pendiente:**
  - commit y PR (a pedido del usuario);
  - los supuestos de las decisiones 6 y 10 (#79, pendientes 5 y 6);
  - la zona de 2h (#79).
  - El motor se usa desde #83 (tablero) y #85 (generar en Revit).
