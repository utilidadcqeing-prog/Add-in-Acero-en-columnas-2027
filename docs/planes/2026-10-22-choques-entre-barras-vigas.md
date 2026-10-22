# Plan: puntos donde las barras se tocan, en el alzado y en la vista previa 3D (vigas)

## Contexto

- **Qué se pide** (reunión con Carlos del 22-oct, segunda tarea; Juan, 22-oct en la tarde).
  - Que el tablero muestre **dónde se tocan las barras**, con puntos en el **alzado** y en la **vista previa 3D**, para
    ver qué choca y poder corregirlo.
  - En la reunión, Juan probó dos formas y no le gustaron porque "no daban información". Lo que busca es que se entienda
    cómo arreglarlo.
  - Antecedente medido el 22-oct: la grapa de 180° doblada a 6 d se metía 4.9 mm en las barras vecinas (H8 del plan
    `2026-10-22-estribo-180-y-grapa-s-vigas.md`). Lo encontró una revisión hecha a mano en Revit, no el tablero.
- **Fuera de alcance:**
  - los choques viga-columna en el nudo (Carlos, min 39:07–40:47: "ese punto no va a ser un punto a corregir"); el
    add-in no conoce el acero de la columna;
  - corregir solo: el tablero propone arreglos y el usuario decide (decisión 7);
  - bloquear el generar: solo avisa (decisión 8);
  - el corte 1-1, que puede venir después con el mismo cálculo;
  - avisar en la confirmación de "Generar".
- **Entorno:** Revit 2026 y 2027, `addins/acero-vigas`. Una rama nueva sale de `vigas-estribo-180`, porque necesita la
  grapa en S y el corte con gancho real.

## Criterios de aceptación

- [x] En el pórtico de la maqueta **sin bastones no hay ningún punto**: lo que se toca a propósito no cuenta (el doblez
      del estribo abrazando su barra, la barra de esquina apoyada en la curva del estribo, las dos barras de un traslapo
      lado a lado).
- [x] Con su armado por defecto (con bastones) salen **7 puntos**: la cola del 135° del cerrado se mete 2.58 mm en el
      bastón de arriba junto a la esquina, ×8 estribos en cada zona confinada (H6).
- [x] Con la grapa de 180° doblada a 6 d entre barras que no le dan espacio, aparecen los puntos donde se mete en las
      vecinas, con su medida (H7).
- [ ] **Alzado:**
  - cada choque es un punto rojo sobre la barra longitudinal afectada, en su x;
  - al pasar el mouse dice qué toca a qué, cuánto se mete y qué se puede hacer;
  - al hacer clic se selecciona esa barra o bastón, y se abre su panel de edición para corregirla.
- [ ] **Vista previa 3D:** un marcador rojo en cada punto de choque, visible entre los tubos.
- [x] ~~La lista de avisos del tablero dice cuántos choques hay~~: Juan, 22-oct, lo quitó; los choques se ven solo como puntos en el alzado.
- [ ] `rtk dotnet test CQEING-Addins.sln -c Release` en verde.

## Hallazgos del código

**H1. Ya existe una sola geometría para todo.** `PreviaAcero.Trazos(plan, ubicación, reglas, doblados)` da el eje de
cada pieza (barra, bastón y cada estribo de cada corrida) en mm del modelo, con sus ganchos. Lo usan:
- la vista previa 3D (`Revit/PreviaAcero3D.cs:59`);
- el corte 1-1, con `PreviaAcero.EnElCorte`.

Revit crea las barras con esa misma lógica, así que **los choques se calculan sobre los trazos** y salen iguales en el
alzado y en el 3D.

**H2. A `Trazo` le falta saber de qué pieza es.** `Trazo(Puntos, Numero, Diametro)` no dice de qué pieza viene.

**H3. Los doblados del tablero no son los de la vista previa.** La vista previa usa los del tipo de barra del modelo
(`PreviaAcero3D.Doblados`). Sin modelo, `PreviaAcero` pone 4 d. Para que el tablero marque lo mismo que se ve en 3D, la
sesión tiene que recibir esos doblados de Revit, como ya recibe `nominalesDelModelo`.

**H4. El alzado no está a escala en vertical.** Cada fila suma 14 px (`traslapos-vigas-notas.md` §13). El punto no se
puede poner en la z real: va sobre la barra longitudinal que lo sufre, en su x. Esa barra ya se dibuja y se selecciona
con `onPick(id)` (`alzado.js:128`).

**H5. Los traslapos van lado a lado** (decisión 2 de #85, `PlanificadorAcero.cs:14`), separados un diámetro. Quedan
tangentes, no se meten. Lo mismo pasa con el doblez que abraza su barra (`PreviaAcero.Abierto`) y con la barra de
esquina contra la curva del estribo. Con una tolerancia, ninguno cuenta como choque.

**H6. La maqueta por defecto sí tiene choques: la cola del 135° contra el bastón** (paso 2, 22-oct). El segundo gancho
del cerrado sale de la curva de la esquina de arriba a la izquierda (centro en 68.5, 431.5 y radio 23.75), a 45°. Su
cola baja en diagonal hacia el núcleo y pasa a 10.12 mm del bastón de arriba, que está en (105.34, 442.55). Harían
falta 4.75 + 7.95 = 12.7 mm, así que **se mete 2.58 mm**. Pasa en los 8 estribos de cada zona junto a una columna: 7
puntos. Sin bastones da 0. **Juan, 22-oct: es un choque real y se muestra.**

**H7. El H8 del plan del 180° no se reproduce con la misma separación** (paso 2, 22-oct). Con la grapa de 180° doblada a
6 d y barras a 48 mm de centro a centro, los trazos de `PreviaAcero` no chocan.
- El doblez queda a 20.5 mm del centro de la barra que abraza; en Revit quedó a 14 mm.
- Con estos trazos empieza a chocar con las barras a unos 41 mm. A 32 mm se mete 7.8 mm.
- Las barras que se midieron en Revit (#395887…) ya no están en el modelo.
- **Juan, 22-oct:** la prueba se hace con las barras a 32 mm, y `PreviaAcero` no se cambia.

## Decisiones

1. **Qué es un choque** (2026-10-22, decisión mía, a revisar con Juan): dos trazos distintos cuyos ejes quedan a menos
   de `r1 + r2 − 0.5 mm`, o sea, uno **se mete** en el otro más de medio milímetro.
   - Tocarse justo, como el estribo que abraza o el traslapo lado a lado, no cuenta.
   - Se revisa todo contra todo (barras, bastones, estribos y grapas), pero dos trazos de la misma pieza física no se
     revisan entre sí.
   - Es el mismo criterio que se usó en Revit el 22-oct (H8 del plan del 180°).
2. **Un punto por par y por lugar.** Cada choque es el punto medio del tramo donde se meten, con las dos piezas y lo
   que más se mete.
   - Los estribos de una misma corrida que chocan igual con la misma barra se juntan en **un punto por corrida**, con
     "×8 estribos", para no llenar el alzado de puntos.
3. **Dónde va el punto en el alzado:** sobre la barra o el bastón longitudinal del choque, en la x del choque. Si
   chocan dos estribos entre sí (no debería pasar), va sobre la línea del estribo.
4. **Qué dice el punto (el "cómo arreglarlo"):**
   - Qué toca a qué: "La grapa E1 de A-B (#3) se mete 4.9 mm en S2 (#5) · ×8 estribos".
   - Una pista según el caso:
     - **estribo o grapa contra barra:** "Prueba la grapa en C, otro gancho o mover la barra";
     - **barra contra barra:** "Mueve o acorta S2";
     - **bastón contra barra:** "Mueve o acorta el bastón".
   - Al hacer clic se selecciona la pieza longitudinal y se abre su panel, el de siempre.
5. **3D:** un octaedro de 50 mm de radio, cian fluorescente (`#00E5FF`, Juan 22-oct: de un color que no usa ningún diámetro y que se vea fácil), más grande que las barras para que se vea entre ellas, en cada punto,
   dibujado por el mismo servidor de `PreviaAcero3D`.
7. **Botones para arreglarlo** (2026-10-22, Juan). Cada punto ofrece hasta 3 arreglos concretos.
   - **C# los prueba antes de ofrecerlos.** Aplica el arreglo a las opciones, vuelve a planificar y a detectar, y solo lo
     ofrece si:
     - ese choque desaparece;
     - no aparecen más choques de los que había;
     - el tablero lo deja generar.

     Así nunca se ofrece un botón que no arregla nada o que rompe una regla, porque pasa por el mismo `CalculoPortico`
     de siempre.
   - **Candidatos según el caso**, en este orden:

     | Caso | Arreglos |
     |---|---|
     | **Grapa (adicional abierto)** contra barra | pasarla a C (o a S); gancho de 135° en vez de 180°; quitar ese adicional |
     | **Cerrado adicional** contra barra | otro gancho; quitarlo |
     | **Cerrado de las esquinas** contra barra | otro gancho |
     | **Bastón** que choca | acortarlo por la punta del lado del choque hasta despejarlo; correrlo entero para sacarlo del choque, con el múltiplo de corte |
     | **Pieza de una corrida** que choca | correr su traslapo un múltiplo de corte hacia un lado o hacia el otro |

   - **Cómo viajan:** cada arreglo es un cambio que la página ya sabe aplicar, con su texto (`CambioArreglo`, paso 6).
     El estribo va por dónde está en la página (`donde`: `"est"` o `"estMarc"`) y por su lugar en la lista (`indice`),
     no por su id, para que la página no tenga que volver a numerar los adicionales:
     - `{ tipo: "forma", donde: "est", indice: 0, valor: "C" }`;
     - `{ tipo: "gancho", donde: "est", valor: "135°" }`, en el pórtico o en los tramos marcados, donde esté el estribo;
     - `{ tipo: "quitar", donde: "est", indice: 0 }`;
     - `{ tipo: "salto", saltos: [CambioArrastre] }` para las piezas longitudinales: el mismo cambio del arrastre
       (`"puntas"` de un bastón y `"movido"` de un traslapo), que la página ya aplica con `aplicarSalto`.
   - **Cuándo se calculan** (paso 6, 22-oct): al abrir el punto, no con la vista. Cada prueba planifica y detecta
     otra vez (unos 100 ms), y un punto de la maqueta prueba 5 candidatos o más: con todos los puntos en cada cálculo,
     el tablero tardaría segundos en cada cambio. Con la búsqueda, un punto tarda unos 500 ms.
   - **El texto dice cómo queda:** "Acortar BS1: 2.40 → 2.10 m", "Grapa E1 en C", "Correr el traslapo de S2 a 3.50 m".
   - **Antes de aplicarlo:** al pasar el mouse por un arreglo, el punto y la pieza se resaltan. Al aplicarlo, el
     tablero recalcula, el punto desaparece y sale un aviso "Aplicado · Deshacer", que vuelve el pórtico como estaba (la
     página guarda una copia de su estado antes del arreglo).
   - **Sin ningún arreglo que sirva**, el punto lo dice ("No hay un arreglo automático: corrígelo a mano") y deja la
     pista de la decisión 4.
8. **Avisar al generar** (2026-10-22, Juan). El aviso lo da C# para todos los pórticos que se generan, no solo el
   abierto.
   - El mensaje `generar` pasa a llevar `forzar`.
   - Sin `forzar`, `SesionTablero` revisa los choques de cada pórtico. Si hay, en vez de generar contesta
     `{ tipo: "choques", porticos: [{ id, cantidad }] }`.
   - La página muestra "Hay N choques en Pórtico A (y M en …). Revisar / Generar de todos modos".
     - **Revisar** abre el primer pórtico con choques.
     - **Generar de todos modos** vuelve a mandar `generar` con `forzar: true`.
   - No bloquea (Juan descartó bloquear el generar).
6. **Dónde vive el cálculo:** `Generacion/DetectorChoques.cs`, en Calculo y sin Revit, con pruebas. Lo usan
   `SesionTablero`, para la vista, y `PreviaAcero3D`, para el 3D.

## Plan paso a paso

1. **`Trazo` con su pieza.** `Trazo(Puntos, Numero, Diametro, Pieza = "", Tipo = …)`, donde `Tipo` es barra, bastón,
   estribo o grapa, más el tramo del estribo. `PreviaAcero.Trazos` lo llena.
   - Pruebas en `PreviaAceroTests`: cada trazo dice su pieza.
2. **`DetectorChoques.Detectar(trazos)`.** Recibe los trazos y devuelve una lista de
   `Choque(Punto3 Punto, PiezaChocada A, PiezaChocada B, double SeMeteMm, int Veces)`, con `Tipo` (`TipoChoque`)
   calculado. `PiezaChocada` lleva pieza, tipo de trazo, tramo y diámetro, para el texto de la decisión 4. `A` es la
   que se mete: el estribo o la grapa antes que el bastón, y este antes que la barra.
   - Primero descarta pares por caja (cajas de cada trazo, ensanchadas por su radio).
   - Después mide distancias segmento a segmento.
   - Pruebas (`DetectorChoquesTests`):
     - el pórtico de la maqueta sin bastones da 0 choques; con bastones, los 7 de H6;
     - el traslapo lado a lado da 0;
     - la grapa de 180° a 6 d entre barras #5 a 32 mm se mete unos 7.8 mm en las 4 vecinas (H7);
     - dos barras cruzadas dan 1;
     - los estribos de una corrida se juntan.
3. **Sesión y vista.**
   - `SesionTablero` recibe `dobladosDelModelo` (H3). `PuenteTablero` se los da con `PreviaAcero3D.Doblados`, que pasa a
     ser `internal`.
   - En `Vista()` arma el plan y los trazos del pórtico abierto, y detecta.
     - Sin ubicación en el modelo o sin plan, no revisa: `choques` va en null.
   - `VistaPortico.Choques`: lista de `{ x (m desde el eje A), elemento (id del alzado), texto, pista, veces, seMeteMm }`,
     de izquierda a derecha. La arma `Tablero/ArmadorChoques.cs`, con `MarcoPortico.EnPortico` para la x.
   - Sin aviso en la lista (Juan, 22-oct): solo los puntos.
   - Pruebas en `SesionTableroTests` y `ArmadorChoquesTests`.
4. **Alzado** (`alzado.js` y `tablero.js`):
   - un punto rojo por choque sobre su barra, con `title`, la pista y `onPick(elemento)` al clic;
     - al pasar el mouse, el punto crece y la línea de abajo del alzado dice el texto y la pista, en rojo;
     - uno sin barra en el alzado (dos estribos entre sí) va sobre la línea de los estribos;
     - mientras se arrastra un punto no se pintan, porque son del pórtico de antes del salto;
   - "choque" en la leyenda, si hay alguno;
   - una capa "Choques" en el selector de capas, encendida por defecto;
   - el aviso `Choques` en su propia línea roja se quitó (Juan, 22-oct): sobraba con los puntos.
5. **Vista previa 3D:** `PreviaAcero3D.Mostrar` detecta sobre los mismos trazos y dibuja los marcadores. Se prueba en
   Revit.
   - `PreviaAcero3D.Marcador`: un octaedro cian fluorescente (`#00E5FF`) de 50 mm, con su contorno. Juan (22-oct) pidió un color que no use ningún diámetro (ni los de vigas ni los nuevos de columnas) y que se vea fácil.
     - Cada cara va en los dos sentidos, para que se vea aunque Revit descarte un lado.
     - Cada cara lleva su normal, para que se lean las aristas.
   - La caja de la vista previa se ensancha por lo menos 50 mm, para que quepan los marcadores.
6. **Arreglos (decisión 7).** `Tablero/ArreglosChoque.cs` arma y prueba los candidatos de un choque. La página los pide
   al abrir el punto con el mensaje `arreglos` (`PedirArreglos`: id, opciones, n y el índice del choque), y C# contesta
   `{ tipo: "arreglos", arreglos: { n, choque, arreglos: [{ texto, cambio }] } }`. No guarda las opciones, como la
   vista previa.
   - **Cómo prueba:** aplica el cambio con `ArreglosChoque.Aplicar` (lo mismo que hace la página), planifica, detecta y
     lo ofrece si:
     - el choque ya no está, es decir, no queda otro de las mismas piezas a menos de 1.5 m;
     - el plan existe;
     - no quedan más choques que antes, contando cada estribo de una corrida.
   - **Acortar o correr un bastón:** prueba los saltos del arrastre (`EditorArrastre.Saltos`) de menor a mayor cambio.
     Busca por mitades el primero que despeja y desde ahí revisa unos pocos.
   - **Correr un traslapo:** prueba el salto de un múltiplo hacia cada lado, en la punta de la barra más cercana al
     choque.
   - Ofrece 3 como mucho, en el orden de la tabla: primero los del estribo y después los de las piezas longitudinales.
   - El detector descarta antes los pares de segmentos que no se acercan, con la distancia exacta entre segmentos.
     Antes tardaba de 50 a 70 ms y ahora unos 40.
   - Pruebas (`ArreglosChoqueTests`, `SesionTableroTests` y `ProtocoloTableroTests`):
     - el bastón BS2 de la maqueta ofrece "Gancho de 180°", "Gancho de 90° en los estribos del pórtico" y
       "Acortar BS2: 2.00 → 1.00 m";
     - cada arreglo ofrecido, aplicado, quita su choque, deja generar y no deja más choques (en la maqueta y con la
       grapa de H7);
     - la grapa en S que en C también choca no ofrece "en C": solo "Quitar la grapa E1";
     - sin arreglos posibles (el cerrado de 6 d contra sus barras de esquina), la lista va vacía;
     - el gancho de los tramos marcados cambia solo esos;
     - el mensaje `arreglos` se lee y se contesta con su número.
   - En la página:
     - **Abrir:** el clic en el punto elige la pieza y abre el globo, que dice el texto, la pista y "Buscando
       arreglos…" mientras llegan.
     - **Antes de aplicar:** al pasar el mouse por un arreglo se resaltan el punto y la pieza.
     - **Aplicar:** guarda la página como estaba y sale "Aplicado: … · Deshacer" por 10 s.
     - **Sin ninguno que sirva:** el globo dice "No hay un arreglo automático: corrígelo a mano".
     - **Cerrar:** con otra vista, el globo se cierra.
7. **Aviso al generar (decisión 8).** `Generar(Ids, Estados, Forzar)` y el mensaje `choques`.
   - `SesionTablero` arma lo que se genera y, sin `forzar`, busca los choques del plan de cada pórtico, con los
     doblados del modelo. Si hay, contesta `{ tipo: "choques", porticos: [{ id, cantidad }] }`: la cantidad es la de
     puntos del alzado.
     - En ese caso no se genera nada y la vista previa 3D se queda.
   - Las pruebas que ya generaban la maqueta, que tiene los 7 choques de H6, ahora mandan `Forzar: true`, porque
     prueban otras cosas.
   - Pruebas en `SesionTableroTests`:
     - con choques y sin forzar no genera y contesta la lista;
     - con forzar genera;
     - sin choques genera directo;
     - con choques, la vista previa 3D no se quita.
   - Pruebas en `ProtocoloTableroTests`: lee `forzar` y escribe `choques`.
   - En la página:
     - `generar(ids, forzar)` recuerda qué se pidió.
     - El cuadro "Hay barras que se tocan" dice "Hay 7 choques entre barras en Pórtico 1 (y 3 en Pórtico 2). Puedes
       revisarlos antes o generar de todos modos".
     - **Revisar** abre el primero con choques, en el alzado.
     - **Generar de todos modos** manda lo mismo con `forzar: true`.
8. **Prueba en Revit:**
   - en la viga 388501 (luces 2 y 4, barras a 48 mm), poner la grapa de 180°;
   - para forzar el choque de H8, hacer una prueba temporal con el doblado a 6 d, o usar dos bastones que se crucen;
   - ver los puntos en el alzado y en el 3D;
   - aplicar un arreglo y ver que el punto desaparece, y probar "Deshacer";
   - generar con un choque y ver el aviso "Revisar / Generar de todos modos".

## Cómo probarlo

```
rtk dotnet test CQEING-Addins.sln -c Release
rtk dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026
```

Antes de desplegar, comprobar que Revit esté cerrado: `Get-Process -Name Revit -ErrorAction SilentlyContinue`.

## Estado

- [x] Decisiones 1-4 aprobadas por Juan ("muy bien", 22-oct). Las decisiones 7 y 8 salen de lo que eligió.
- [x] Paso 1: `Trazo` con su pieza (rama `vigas-choques-barras`).
- [x] Paso 2: `DetectorChoques` (H6 y H7).
- [x] Paso 3: los choques en la vista del tablero, con su aviso.
- [x] Paso 4: los puntos rojos en el alzado (falta verlos en Revit).
- [x] Paso 5: los marcadores en la vista previa 3D (falta verlos en Revit).
- [x] Paso 6: los arreglos, pedidos al abrir el punto (falta verlos en Revit).
- [x] Paso 7: el aviso al generar (falta verlo en Revit).
- [ ] Probado en Revit.
