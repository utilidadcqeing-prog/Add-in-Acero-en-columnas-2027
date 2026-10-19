# Plan: ajustes del tablero tras la prueba del 17-oct (#100)

> **Para quien lo ejecute:** usar superpowers:subagent-driven-development o superpowers:executing-plans, tarea por tarea. Los pasos llevan casillas (`- [ ]`). Juan pide las tareas de a una ("dale tarea N"): no seguir sola con la siguiente.

**Objetivo:** el tablero se ve como lo pidió Carlos con el video de DC-CAD: rótulos sobre cada barra, estribos y luz; colores por diámetro con su leyenda; capas que se prenden y apagan; columnas a escala y altura que crece con las barras; todo sin scroll. Además se corrigen los cuatro fallos que vio al probarlo (nombre del pórtico, voladizo leído como columna, grosor de línea y scroll).

**Arquitectura:** **no cambia ninguna regla del motor** (§13). Lo que sale de una cuenta (textos de los rótulos, largo `L=` de los estribos, color de cada diámetro) lo arma C# en la vista (`ArmadorVista` / `Rotulo`), para que #85 use lo mismo al generar en Revit. La página solo pinta y decide qué capas se ven. Los dos fallos de lectura se corrigen en `AgrupadorPorticos` (librería sin Revit, con pruebas).

**Tecnología:** C# (.NET, xUnit) en `addins/acero-vigas/calculo`, y la página Preact + htm de `addins/acero-vigas/Tablero/web`.

**Fuentes:** manda lo que dice Carlos (`Desktop\addin vigas`):
- reunión del 17-oct, `Meeting Transcription (11).txt`: min 42:20–49:36, 54:16 y 1:35:10 (rótulos), 56:09 y 1:13:46 (colores), 1:00:08 (recubrimiento), 1:06:04–1:08:17 (altura, columnas, capas), 1:11:23 (grosor), 1:28:07–1:29:10 y 1:33:57 (voladizo, scroll y nombre);
- `traslapos-vigas-notas.md` §13;
- la issue https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/100;
- la maqueta, opción 1: https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh.

## Contexto

- **Issue:** #100 (épica #78). Va después de #84 y **antes de #85**.
- **Qué dijo Carlos:**
  - 1:06:04: *"me interesaría que fueran escalables… la altura… de acuerdo a la cantidad de barras que tengamos acá dentro"*.
  - 1:07:24: *"la columna no está escalable, entonces… ¿cómo sabes que no está metiendo una columna?"*; *"quitar capas… esta línea gris que está diciéndome cuál es la mitad… el corte uno uno"*.
  - 1:08:17: *"un desplegable… capas… quiera quitar tan tan tan… meta general… que no tenga casi que salir"*.
  - 1:11:23: *"si le doy más zoom el grosor aumenta… la idea sería que no"*.
  - 1:28:31: *"está generando esto como si fuera una columna"* (punta del voladizo del eje A, con dos voladizos y tres luces).
  - 1:29:10: *"que se ha cerrado la vista y que lo que esté aquí es lo que se pueda ver"*; *"dice pórtico sin eje… no está tomando el nombre de la rejilla"*.
  - 1:35:10: *"estamos generando mucho ruido visual"* (los estribos uno por uno).
- **Qué hay hoy** (rama `vigas-edicion-84`):
  - el alzado se dibuja dentro de un `<g transform="translate(…) scale(z)">` (`alzado.js:279`): por eso líneas y textos crecen con el zoom;
  - las alturas del alzado son fijas: viga de y = 40 a 130, barras en 52/58 y 112/118, bastones en 70/100 (`alzado.js:102-135`, `343`);
  - las columnas ya van de `caraIzquierda` a `caraDerecha` (`alzado.js:145`), es decir, a escala en horizontal; lo que no está a escala es el alto (`vertical ×3`) y no se ve su sección;
  - los colores separan corrida (`#3F4A57`), bastón (`#0F7B6C`) y traslapo (`#C77A12`), con su leyenda arriba a la izquierda (`alzado.js:389-393`);
  - cada estribo es una raya en su posición real (`alzado.js:138-142`, #91);
  - todo apoyo se dibuja como columna, aunque sea una viga (`alzado.js:145`, `349`);
  - el rótulo de cada barra es `S1 · 3.00` (`alzado.js:113`); `Rotulo.De` (#84, decisión 10) ya arma `3#5L= 4.00/4.28`, pero solo se ve al arrastrar.
- **Fuera de alcance:**
  - generar en Revit (#85): aquí solo queda el estándar de colores en C# para que #85 lo use;
  - el botón 3D sigue como ahora (decidido el 17-oct);
  - el corte en su pestaña + ventana flotante y la línea de estribo en estribo ya están hechos (#91);
  - cualquier tipo de viga (vigas de muro), en vivo con Revit: después.
- **Entorno:** monorepo `C:\Users\LENOVO\Desktop\practicacq`, Revit 2026. Rama nueva `vigas-ajustes-100`, que sale de `vigas-edicion-84`. Modelos: `PROY_JULIO_BETO.rvt` y **el modelo con voladizos de Carlos** (hace falta para las tareas 1 y 2). Los cambios sin commit del instalador manual (`README.md`, `tools/empaquetar-manual.ps1`, `tools/instalador-manual/`, `docs/planes/2026-10-17-instalador-manual-bat.md`) **no se tocan ni se suman**.

## Criterios de aceptación

**Maqueta**
- [ ] La maqueta tiene todo lo de abajo (sin scroll, ventana flotante del corte, rótulos, colores con leyenda, capas, columnas y altura dinámica) y el tablero se ve igual a ella.

**Fallos**
- [ ] El pórtico toma el nombre de la rejilla (`Pórtico A`), también si la viga no está justo sobre el eje.
- [ ] La punta de un voladizo con una viga de borde no se dibuja ni se calcula como apoyo.
- [ ] El grosor de las líneas no cambia con el zoom.
- [ ] El tablero no tiene scroll: todo se ve sin bajar ni subir.

**Rótulos**
- [ ] Cada barra y bastón muestra `1#4L= 2.30/2.50` (sin gancho, un solo largo), sobre la barra.
- [ ] El diámetro se puede ver como `#4` o como `½"`.
- [ ] Cada traslapo muestra su largo con 2 decimales.
- [ ] Los estribos de cada zona se dibujan como una línea con rayitas, con `9e#3` encima, `@.10` debajo y `L=…` (de la cara de la columna al último estribo).
- [ ] Arriba, en la mitad de cada luz, la sección de la viga (`30×50`).

**Colores**
- [ ] Cada diámetro tiene su color (estándar único) y arriba hay una leyenda con bolita + diámetro.
- [ ] El corte usa los mismos colores y cada barra va con su diámetro a escala.
- [ ] El recubrimiento del corte se entiende (no más azul oscuro sobre negro).

**Capas y escala**
- [ ] Un desplegable **Capas** prende y apaga: media luz, corte 1-1, textos, sección de columnas (y estribos).
- [ ] La sección de las columnas se puede prender como capa.
- [ ] Columnas con su ancho real; se ve si un traslapo se mete en la columna.
- [ ] La altura de la viga en el dibujo crece cuando hay más filas o bastones.

**Para todo**
- [ ] `rtk dotnet test CQEING-Addins.sln -c Release` en verde.
- [ ] Probado en Revit 2026 con `PROY_JULIO_BETO.rvt` y el modelo con voladizos de Carlos.

## Hallazgos del código

1. **"Pórtico sin eje"** sale cuando ninguna rejilla es **colineal** con la línea de vigas a 50 mm o menos (`AgrupadorPorticos.cs:36` y `51`, `ToleranciaLinea = 50`). Si la viga está corrida del eje (al borde de la columna, excéntrica), no lo encuentra aunque la rejilla pase por todas sus columnas. **Confirmado** en el modelo de Carlos (`PROY_JULIO_BETO`, 19-oct): las vigas `VIG_25X35` de los ejes **A** y **C** (niveles N+3.70 y N+6.70) están justificadas a la izquierda / derecha, con su línea en la cara de las columnas `COL_25X30` y a **125 mm** de la rejilla, que pasa por el centro de las columnas. Las de B y 1–4 van centradas (0 mm). Las viguetas `VT_10X35` / `VB_10X35` no van sobre ningún eje y siguen "sin eje".
2. **Voladizo como columna:** una viga que cruza la línea cuenta como apoyo si una viga del pórtico **termina sobre ella** sin columna ni muro (`TerminaSobre`, `AgrupadorPorticos.cs:260`). La viga de borde en la punta de un voladizo cumple eso, así que el voladizo queda como luz y su punta como apoyo. **Hipótesis**, se confirma con el modelo de Carlos.
3. Además el alzado pinta **todo** apoyo como columna de alto completo (`alzado.js:349`), aunque `PorticoLeido.TiposDeApoyo` dice si es columna, muro o viga.
4. Grosor: los trazos están dentro del `scale(z)` (`alzado.js:279`). `vector-effect="non-scaling-stroke"` deja el grosor fijo sin tocar la geometría. Los textos también crecen (pregunta 1).
5. Scroll: la columna de la derecha (`tablero.js:857`, `overflow: auto`) y la tabla de barras (`tablero.js:823`, `max-height: 150px`) tienen scroll propio; el tamaño de la página sale de `pageW` / `pageH` (`tablero.js:647`). Se mide en Revit cuál es el que vio Carlos (paso 1 de la tarea 4).
6. `Rotulo.De` (#84) ya arma el texto `{n}#{d}L= {recto}/{total}` con 2 decimales y junta varios tipos con `+`.
7. El alzado recibe `posicionesEstribos` (#91); la línea 1-1 se pega a ellas. Si se dejan de dibujar una por una, la línea sigue usándolas.

## Decisiones

> "Carlos" = lo dijo él. "Nuestra" = la tomamos a partir de lo que dijo; se le muestra al probar y va a #79 si es pregunta.

1. **Maqueta primero** (Juan, 17-oct): la tarea 0 pone en la maqueta **todo** lo visual de esta issue, no solo el scroll y la ventana flotante. El tablero se programa igual a ella.
2. **Nombre del pórtico** (nuestra): si no hay rejilla colineal, se toma la rejilla **paralela** más cercana que pase por dentro de las huellas de los apoyos del pórtico. Si no hay ninguna, sigue "Pórtico sin eje N" con su aviso.
3. **Viga de borde en la punta del voladizo** (Carlos 1:28:31, *"es un armazón, no un apoyo"*; Juan, 19-oct: *"debe ser más por voladizo"*): lo que pasa de la **primera / última columna o muro** del pórtico es voladizo. Una viga que cruza ahí es armazón, no apoyo. Una viga que cruza solo puede ser apoyo **entre** dos columnas o muros del pórtico (como hoy, con `TerminaSobre`).
   - ~~Una viga que cruza solo es apoyo si ella misma descansa en al menos una columna o muro.~~ Cambiada el 19-oct por Juan: la regla va por el voladizo, no por lo que carga la viga.
4. **Apoyo que es viga** (nuestra): se dibuja como la sección de la viga que cruza (a la altura de la viga), no como columna de alto completo.
5. **Grosor y textos fijos** (Carlos 1:11:23; Juan, 19-oct: *"los textos también"*): `non-scaling-stroke` en todo trazo del alzado y del corte, y los textos con el mismo tamaño en pantalla a cualquier zoom (`font-size` y el borde blanco del texto divididos por `z`). La posición del texto sí sigue al dibujo.
6. **Sin scroll** (Carlos 1:29:10): la página, el alzado y el panel del elemento caben en la ventana desde **1366 × 768**. La lista de pórticos de la izquierda puede tener scroll propio (crece con el modelo). Lo que no quepa en el panel de la derecha se compacta o pasa a un desplegable, no a scroll.
7. **Rótulos** (Carlos, DC-CAD):
   - barra y bastón: `Rotulo.De` encima de la barra (las de abajo, debajo), con su color de diámetro; se esconden con la capa *Textos*;
   - traslapo: su largo con 2 decimales (`1.20`) en el empalme;
   - estribos: por zona, una línea con rayitas en sus dos puntas, `9e#3` encima, `@.10` debajo y `L=0.95` al lado. `L` se mide de la cara de la columna al último estribo de la zona. Lo arma C# (`RotuloEstribos`);
   - sección: `30×50` arriba, en la mitad de cada luz (y de cada voladizo).
8. **Diámetro en número o en pulgadas** (Carlos): un interruptor `#4 · ½"` en el menú Capas. C# manda los dos textos de cada rótulo; la página elige. En pulgadas, la cantidad va con `Ø`: `3Ø⅝"L= 2.80/3.00`. Pulgadas: `#n` = n/8", simplificado (`#2 ¼"`, `#3 ⅜"`, `#4 ½"`, `#5 ⅝"`, `#6 ¾"`, `#7 ⅞"`, `#8 1"`, `#9 1⅛"`, `#10 1¼"`, `#11 1⅜"`).
9. **Colores por diámetro** (Carlos: #5 rojo, #4 naranja; el resto, nuestra, aprobada por Juan el 19-oct "por ahora"; se le muestra a Carlos al probar): `#3 amarillo`, `#4 naranja`, `#5 rojo`, `#6 magenta`, `#7 morado`, `#8 azul`, `#9 cian`, `#10 verde`, `#11 café`. Viven en C# (`ColoresDiametro`) y viajan en la vista; #85 los usa en Revit. Corrida y bastón se distinguen por el trazo (bastón un poco más delgado), no por el color. La selección deja de ser azul: va con un halo gris oscuro detrás de la barra, para no confundirse con el #8.
10. **Leyenda** (Carlos): arriba, una bolita por cada diámetro que hay en el pórtico + su texto (`● #5  ● #4`), en lugar de la de corrida/bastón/traslapo.
11. **Recubrimiento del corte** (Carlos 1:00:08): la línea del recubrimiento en gris medio punteada sobre el fondo claro del corte, con su texto `r = 4 cm`.
12. **Capas** (Carlos 1:07:24–1:08:17): botón **Capas ▾** junto al zoom, con casillas: *Media luz*, *Corte 1-1*, *Textos*, *Estribos*, *Sección de columnas*, y el interruptor de la decisión 8. Lo elegido se guarda en la página (por usuario, `localStorage` con try/catch) y no viaja a C#.
13. **Columnas** (Carlos 1:07:24; Juan, 19-oct: *"lo que recomiendes"*): el ancho ya es real (hallazgo 3), así que el dibujo de la columna ya es su sección a lo largo de la viga. La capa *Sección de columnas* agrega solo el **texto** `40×50` (ancho × fondo) debajo de cada columna; un dibujo de la sección en planta repetiría lo que ya se ve y le quitaría espacio a los rótulos. La vista gana `Apoyo.Ancho` / `Apoyo.Fondo` en m (de la huella).
14. **Altura dinámica** (Carlos 1:06:04): la altura de la viga en el dibujo = base + un escalón por cada fila extra arriba o abajo y por cada nivel de bastones. Las `y` fijas pasan a salir de una función `niveles(p)`. Sigue sin ser a escala (*"no es real"*).

### Preguntas (respondidas por Juan el 19-oct)

1. ~~¿Los textos también quedan del mismo tamaño a cualquier zoom?~~ Sí, también los textos (decisión 5).
2. ~~¿Qué color lleva cada diámetro?~~ Los propuestos, por ahora (decisión 9). Se le muestran a Carlos al probar.
3. ~~¿Una viga que no descansa en columnas nunca es apoyo?~~ La regla va por el voladizo (decisión 3).
4. ~~¿Sección de columnas: texto, dibujo o los dos?~~ Lo recomendado: el texto (decisión 13).

## Global Constraints

- La página no calcula acero: textos de rótulos, `L=` de estribos y colores salen de C#. La página solo pinta y decide qué capas se ven.
- Unidades: la vista en **m**, como el alzado.
- Nombres y textos en español, como el resto del add-in. Comentarios con la misma densidad del código de alrededor.
- Commits **sin** `Co-Authored-By` ni `Claude-Session`. Todos los comandos con `rtk` delante.
- No tocar los archivos del instalador manual que están sin commit.
- Ningún `alert` / `confirm` del navegador.
- Se prueba en **Revit**, no en Chrome con la demostración (Juan, 18-oct). `demo.js` se mantiene al día para que no se rompa.

## Review Focus

1. **Nombre del pórtico:** un eje paralelo que no pasa por las columnas no se toma. Prueba `UnaVigaCorridaDelEjeTomaElNombreDeLaRejilla` y `UnaRejillaParalelaQueNoPasaPorLosApoyosNoSeToma` (tarea 1).
2. **Viga sobre viga legítima:** una viga cargadora entre dos columnas del pórtico sigue siendo apoyo. Prueba `UnaVigaQueCruzaEntreDosColumnasSigueSiendoApoyo` (tarea 2).
3. **Línea 1-1 sin rayas de estribos:** sigue pegándose de estribo en estribo aunque ya no se dibujen uno por uno. Revisión en Revit (tarea 8).
4. **Rótulos que se tapan** en luces cortas: no se montan sobre otro rótulo ni sobre los botones de gancho. Revisión en Revit (tarea 8).
5. **Altura dinámica y el corte:** la línea 1-1, los 3 puntos y los botones de gancho siguen a las nuevas alturas. Revisión en Revit (tarea 11).

---

### Tarea 0: la maqueta con todo lo de #100

**Archivos:** el artifact https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh (se lee con `Artifact read` y se publica en la misma URL).

- [ ] **Paso 1:** leer la maqueta y ubicar el alzado, la leyenda, la barra de zoom, el corte y el panel de la derecha.
- [ ] **Paso 2:** agregar, con los mismos colores, tipografía y tamaños de la maqueta:
  - el tablero **sin scroll** a 1366 × 768 (decisión 6);
  - la **ventana flotante del corte** al arrastrar la línea 1-1 (como quedó en #91);
  - los **rótulos** de la decisión 7 y el interruptor `#4 · ½"`;
  - los **colores por diámetro** con la leyenda de bolitas (decisiones 9 y 10) y la selección con halo;
  - el **menú Capas** (decisión 12);
  - la **sección de columnas** y el apoyo que es viga (decisiones 4 y 13);
  - la **altura dinámica** con un pórtico de 2 filas arriba (decisión 14);
  - el **recubrimiento** del corte con los colores nuevos (decisión 11).
- [ ] **Paso 3:** publicar en la misma URL y pedir a Juan que la revise. **No se sigue con la tarea 1 hasta que la apruebe.**

### Fase A: fallos de lectura (#81) — necesita el modelo de Carlos

### Tarea 1: el pórtico toma el nombre de la rejilla

**Archivos:** `calculo/CQEING.Vigas.Calculo/Porticos/AgrupadorPorticos.cs`, `calculo/CQEING.Vigas.Calculo.Tests/AgrupadorPorticosTests.cs`.

- [x] **Paso 1:** abrir el modelo de Carlos y medir por el MCP de Revit la distancia entre la línea de las vigas del eje A y su rejilla. Anotar en Hallazgos si confirma el hallazgo 1 o es otra cosa.
- [x] **Paso 2:** pruebas que fallan: `UnaVigaCorridaDelEjeTomaElNombreDeLaRejilla` (viga a 150 mm del eje, columnas centradas en el eje) y `UnaRejillaParalelaQueNoPasaPorLosApoyosNoSeToma`.
- [x] **Paso 3:** en `Agrupar`, si no hay eje colineal, buscar la rejilla paralela más cercana que pase por dentro de las huellas de los apoyos (decisión 2).
- [x] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release` en verde. Commit `fix(vigas): el pórtico toma el nombre de la rejilla aunque la viga esté corrida del eje (#100)`.

### Tarea 2: la viga de borde del voladizo no es apoyo

**Archivos:** `AgrupadorPorticos.cs`, `AgrupadorPorticosTests.cs`, `Tablero/web/alzado.js`.

- [ ] **Paso 1:** en el modelo de Carlos, mirar qué hay en la punta del voladizo del eje A (viga de borde, otra cosa). Anotar en Hallazgos.
- [ ] **Paso 2:** pruebas que fallan: `LaVigaDeBordeDeUnVoladizoNoEsApoyo` (queda voladizo, no luz) y `UnaVigaQueCruzaEntreDosColumnasSigueSiendoApoyo`.
- [ ] **Paso 3:** en `Agrupar`, después de `Unir`, quitar de `propios` las vigas que quedan antes de la primera o después de la última columna o muro (decisión 3). Si no queda ninguna columna ni muro, se avisa `SinApoyos` como hoy.
- [ ] **Paso 4:** en el alzado, un apoyo de tipo viga se dibuja como sección de viga (decisión 4); la vista ya trae `TiposDeApoyo`, se pasa al apoyo si hace falta.
- [ ] **Paso 5:** pruebas en verde. Commit `fix(vigas): la viga de borde de un voladizo no se lee como apoyo (#100)`.

### Fase B: base del dibujo

### Tarea 3: grosor y textos fijos a cualquier zoom

**Archivos:** `Tablero/web/alzado.js`, `Tablero/web/tablero.js` (corte).

- [ ] **Paso 1:** `vector-effect="non-scaling-stroke"` en cada `line`, `polyline`, `rect`, `path` y `circle` dentro del grupo con `scale(z)`; los grosores quedan en px de pantalla (decisión 5).
- [ ] **Paso 1b:** los textos del grupo con `font-size` (y su `stroke-width` de borde) divididos por `z`, para que se lean igual a cualquier zoom; los círculos de los ejes y los 3 puntos, con el radio dividido por `z`.
- [ ] **Paso 2:** el rayado de zonas de la maqueta (`stroke-width="80"` con `dasharray`, `alzado.js:346`) no puede quedar fijo: pasa a rayas sueltas o sale con la tarea 8.
- [ ] **Paso 3:** `node --check` de los dos. Commit `fix(vigas): líneas y textos del mismo tamaño a cualquier zoom (#100)`.

### Tarea 4: el tablero sin scroll

**Archivos:** `Tablero/web/tablero.js`, `Tablero/VentanaTablero.xaml(.cs)` si el tamaño mínimo de la ventana lo pide.

- [ ] **Paso 1:** en Revit, con la ventana a 1366 × 768 y el pórtico de Carlos, ver qué tiene scroll (hallazgo 5). Anotarlo.
- [ ] **Paso 2:** el alzado toma el alto que sobra; el panel del elemento se compacta como en la maqueta (decisión 6). Solo la lista de pórticos conserva scroll propio.
- [ ] **Paso 3:** `node --check`. Commit `fix(vigas): el tablero cabe en la ventana sin scroll (#100)`.

### Tarea 5: colores del recubrimiento en el corte

**Archivos:** `Tablero/web/tablero.js` (dibujo del corte).

- [ ] **Paso 1:** la línea del recubrimiento como en la decisión 11 y la maqueta.
- [ ] **Paso 2:** commit `fix(vigas): el recubrimiento del corte se ve sobre el fondo (#100)`.

### Fase C: colores por diámetro

### Tarea 6: el estándar de colores en C#

**Archivos:** `calculo/CQEING.Vigas.Calculo/Modelo/ColoresDiametro.cs` (nuevo), `ArmadorVista`, pruebas `ColoresDiametroTests.cs` y `ArmadorVistaTests.cs`.

- [ ] **Paso 1:** pruebas: cada diámetro del #3 al #11 tiene un color distinto; uno fuera de la tabla devuelve un gris; la vista trae `Color` en cada barra, bastón y barra del corte, y `Diametros` (los del pórtico, para la leyenda).
- [ ] **Paso 2:** `ColoresDiametro.De(int diametro)` con la tabla de la decisión 9, y la vista con los colores.
- [ ] **Paso 3:** pruebas en verde. Commit `feat(vigas): un color por diámetro, el mismo para el tablero y Revit (#100)`.

### Tarea 7: colores y leyenda en el alzado y el corte

**Archivos:** `alzado.js`, `tablero.js`, `demo.js`.

- [ ] **Paso 1:** barras y bastones con `Color`; bastón más delgado; selección con halo (decisión 9).
- [ ] **Paso 2:** la leyenda de bolitas arriba (decisión 10).
- [ ] **Paso 3:** el corte con los mismos colores y cada barra con su diámetro a escala.
- [ ] **Paso 4:** `demo.js` manda los colores. `node --check`. Commit `feat(vigas): colores por diámetro con su leyenda en el alzado y el corte (#100)`.

### Fase D: rótulos

### Tarea 8: rótulos en C# y en el alzado

**Archivos:** `Rotulo.cs` (o donde quedó `Rotulo.De` en #84), `ArmadorVista`, `alzado.js`, `demo.js`, pruebas.

- [ ] **Paso 1:** pruebas en C#: cada barra y bastón trae `Rotulo` y `RotuloPulgadas`; cada zona de estribos trae `9e#3`, `@.10` y `L=0.95` (de la cara de la columna al último estribo); cada luz trae su sección `30×50`; el traslapo, su largo a 2 decimales.
- [ ] **Paso 2:** C# los arma (decisiones 7 y 8).
- [ ] **Paso 3:** el alzado los pinta: rótulo sobre cada barra, largo en cada traslapo, la línea con rayitas de los estribos en lugar de cada raya (la línea 1-1 sigue usando `posicionesEstribos`) y la sección arriba de cada luz.
- [ ] **Paso 4:** `node --check` y pruebas en verde. Commit `feat(vigas): rótulos de barras, traslapos, estribos y sección como DC-CAD (#100)`.

### Fase E: capas y escala

### Tarea 9: el menú Capas

**Archivos:** `alzado.js`, `tablero.js`.

- [ ] **Paso 1:** botón **Capas ▾** junto al zoom con las casillas y el interruptor `#4 · ½"` de la decisión 12; lo elegido se guarda en `localStorage` con try/catch.
- [ ] **Paso 2:** cada capa esconde lo suyo (media luz, línea 1-1, textos, estribos, sección de columnas).
- [ ] **Paso 3:** `node --check`. Commit `feat(vigas): menú de capas en el alzado (#100)`.

### Tarea 10: la sección de las columnas

**Archivos:** `AgrupadorPorticos.cs` / `ModeloLeido.cs` (ancho y fondo del apoyo desde la huella), `ArmadorVista`, `alzado.js`, pruebas.

- [ ] **Paso 1:** prueba: un apoyo de 400 × 500 trae `Ancho = 0.40` y `Fondo = 0.50`.
- [ ] **Paso 2:** C# lo manda y el alzado pinta `40×50` debajo de cada columna, en la capa *Sección de columnas* (decisión 13).
- [ ] **Paso 3:** pruebas en verde. Commit `feat(vigas): sección de las columnas como capa (#100)`.

### Tarea 11: altura dinámica

**Archivos:** `alzado.js`.

- [ ] **Paso 1:** función `niveles(p)` que da la `y` de la viga, de cada fila de barras, de los bastones y de los traslapos según cuántas filas y bastones hay (decisión 14). Reemplaza las `y` fijas (52/58, 112/118, 70/100, 45/106, 40–130).
- [ ] **Paso 2:** la línea 1-1, los 3 puntos, los botones de gancho, los rótulos y el ajuste a la vista usan esas `y`.
- [ ] **Paso 3:** `node --check`. Commit `feat(vigas): la viga del alzado crece con las filas y los bastones (#100)`.

### Tarea 12: prueba en Revit y cierre

- [ ] **Paso 1:** `rtk dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2026` y `powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026`.
- [ ] **Paso 2:** en Revit 2026, con `PROY_JULIO_BETO.rvt` y el modelo de Carlos, recorrer los criterios uno por uno y mirarlos por el MCP de Revit. Anotar en "Estado".
- [ ] **Paso 3:** actualizar `traslapos-vigas-notas.md` §13 (con las respuestas del 19-oct), mostrarle a Carlos los colores de la decisión 9 y dejar un comentario en #100 con lo hecho.
- [ ] **Paso 4:** commit `docs(vigas): ajustes del tablero probados en Revit (#100)`.

## Cómo probarlo

```
rtk dotnet test CQEING-Addins.sln -c Release
rtk dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026
```

1. Modelo de Carlos, eje A: dice `Pórtico A`; dos voladizos y tres luces; la punta del voladizo no tiene columna.
2. Acercar al máximo: las líneas no engordan y los textos no crecen.
3. Ventana a 1366 × 768: nada tiene scroll salvo la lista de pórticos.
4. Rótulos: `#4 ↔ ½"`; traslapos con 2 decimales; estribos `9e#3 / @.10 / L=…`; `30×50` en cada luz.
5. Colores: #5 rojo, #4 naranja, leyenda arriba; el corte igual.
6. Capas: apagar una por una y prender de nuevo; cerrar y abrir el tablero, siguen como se dejaron.
7. Un pórtico con 2 filas arriba y bastones: la viga se ve más alta; la línea 1-1 y los 3 puntos siguen en su sitio.

## Estado

- [ ] Plan revisado con Juan.
- [x] Tarea 0: maqueta aprobada por Juan (19-oct, versión 28).
  - Componente del alzado (`Beam`): líneas con `non-scaling-stroke` y textos/círculos divididos por el zoom; rótulo `3#5L= 2.80/3.00` en cada barra y bastón; largo de cada traslapo con 2 decimales; estribos por zona **debajo de la viga** (`8e#3` / `@.10 L=0.75`) en lugar del rayado; `30×50` arriba de cada luz; `40×40` debajo de cada columna (capa apagada por defecto); leyenda de bolitas por diámetro; menú **Capas** con el interruptor `#4 · ½"`; altura que crece 14 por cada fila de más.
  - Ruling: el **traslapo pasa a gris** (`#5E6670`): el naranja de antes chocaba con el #4.
  - Ruling: en pulgadas el rótulo queda `3Ø⅝"L= 2.80/3.00` (la `Ø` separa la cantidad de la fracción; `3⅝"` se leería como tres y cinco octavos).
  - Ruling: la selección de una barra va con un halo gris detrás; el arrastre sigue en azul / ámbar (es el elemento vivo, no un diámetro).
  - Pantalla (`Main`): tamaño variable para probar 1366 × 768; el selector de fila va en el mismo renglón del diámetro; el dibujo del recubrimiento queda detrás de *Ver el dibujo* (estribo gris claro, barra con el color de su diámetro, cotas en gris oscuro y ámbar); el corte con colores por diámetro y estribo gris; la ventana flotante del corte se mueve por su barra y se oculta con ✕.
  - Ruling: la **lista de barras** conserva su scroll interno, como la de pórticos: crece con el pórtico.
  - Artboards nuevos: *Sin scroll a 1366 × 768*, *Corte 1-1 en ventana flotante*, *Menú Capas y diámetro en pulgadas*, *La viga crece con 2 filas arriba*.
  - Versión 24 (Juan: *"Ojalá sobre la misma barra… falta esto"*): el rótulo va **pegado a su barra**, encima de las de arriba y debajo de las de abajo, con un borde del color del fondo para que se lea sobre las líneas; antes salía fuera de la viga. Los bastones también llevan el suyo (`1#5L= 2.00`), del lado de adentro de la viga.
  - Versiones 25–28 (Juan: *"no lo veo"*): **el motor de la maqueta no pinta un `{{hueco}}` dentro de un `<text>` de SVG** (probado con textos de muestra: los fijos salen y los de hueco no). Por eso los rótulos nunca se vieron, ni siquiera el del arrastre de #84. Ahora van como etiquetas HTML encima del dibujo, en px de pantalla (así también quedan fijos al zoom). Visto en Chrome: rótulos de barras, bastones y traslapos, estribos y `30×50`. **En el tablero real (Preact) esto no pasa**: allí siguen siendo `<text>` con `font-size / z`.
  - No está en la maqueta: el apoyo que es viga (decisión 4), porque la maqueta no tiene ese caso.
- [ ] Fase A (tareas 1–2).
  - Tarea 1 (pasos 2–4): `EjeQuePasaPorLosApoyos` toma la rejilla paralela más cercana que pasa por dentro de **todas** las columnas y muros del pórtico (no cuenta las vigas que cruzan). `Soporte` guarda también su rango de lado a lado (`NormalDesde`–`NormalHasta`); `Unir` junta esos rangos. `UnaRejillaParalelaQueNoPasaPorLosApoyosNoSeToma` pasó desde el inicio: queda como guarda para que el arreglo no tome ejes de más. 578 pruebas en verde.
  - Paso 1 hecho después del commit del arreglo (Revit no respondía al principio): el hallazgo 1 se confirma en el modelo de Carlos (ejes A y C a 125 mm). La prueba `UnaVigaCorridaDelEjeTomaElNombreDeLaRejilla` pasó a usar ese caso exacto: viga de 25 con su línea en la cara de columnas de 25.
- [ ] Fase B (tareas 3–5).
- [ ] Fase C (tareas 6–7).
- [ ] Fase D (tarea 8).
- [ ] Fase E (tareas 9–11).
- [ ] Probado en Revit (tarea 12).
