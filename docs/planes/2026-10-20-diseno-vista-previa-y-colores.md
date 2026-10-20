# Diseño: vista previa de la columna y colores de barra por diámetro

Conversación con Luciano del 20-oct-2026. Las decisiones de Luciano van
marcadas **(Luciano)**. Las que tomé yo, sin consultarlas, van marcadas
**(decisión mía)** para que se revisen.

Bocetos aprobados: `.superpowers/brainstorm/2850-1792500446/content/`
(`vista-previa.html` para el contenido de las vistas y
`pestana-vista-previa.html`, opción 2, para el acomodo).

## Por qué

Hoy, mientras se configura una columna en el panel, no se sabe cuántas
varillas quedan por cara ni cómo van los estribos hasta generarla en Revit y
mirarla **(Luciano)**. Además, la empresa pinta las barras por diámetro con
una tabla de colores fija, y hoy eso no se hace ni en el panel ni en el
modelo.

Son tres cambios que se pueden hacer por separado, en este orden:

1. Reparto mitad y mitad en las listas de cantidad de barras (chico).
2. Pestaña «Vista previa» (mediano).
3. Colores por diámetro en el modelo de Revit (mediano; necesita verificación
   contra Revit real).

La tabla de colores (sección 0) la usan el 2 y el 3.

## Qué se quiere lograr

- Ver, antes de generar, la sección desde arriba con cada barra y cuántas
  hay por cara, y el tramo de frente y de costado con los estribos y los
  estribos menores.
- Ver en las listas «N. barras X / Y» cuántas barras van en cada una de las
  dos caras enfrentadas.
- Las barras con el color de su diámetro, igual en la vista previa y en
  todas las vistas del modelo donde aparece la columna.

Fuera de alcance:

- Vista en 3D: no se pidió **(Luciano, al no pedirla cuando se ofreció)**.
- Editar la tabla de colores desde el panel: la tabla es fija en el add-in
  **(Luciano, confirmó la suposición)**.
- Interacción en la vista previa (clic, arrastre, selección): es solo para
  mirar. Las piezas se ponen en la ventana de estribos menores
  (`2026-10-20-diseno-estribos-menores-visual-y-64.md`).

Sería un fracaso, aunque funcione:

- que la vista previa muestre una cantidad o una posición de estribos
  distinta de la que después genera Revit;
- que los colores en Revit no coincidan con los del panel;
- que pintar las barras rompa o ensucie vistas que el usuario ya tenía
  configuradas (por ejemplo, cambiando una plantilla de vista sin avisar).

## 0. Tabla de colores (Luciano)

| Barra | Color | Hex | RGB |
|---|---|---|---|
| #3 | amarillo | #B8860B | 184, 134, 11 |
| #4 | naranja | #E8710A | 232, 113, 10 |
| #5 | rojo | #D32F2F | 211, 47, 47 |
| #6 | magenta | #C2185B | 194, 24, 91 |
| #7 | morado | #7B1FA2 | 123, 31, 162 |
| #8 | azul | #1565C0 | 21, 101, 192 |
| #9 | cian | #00838F | 0, 131, 143 |
| #10 | verde | #2E7D32 | 46, 125, 50 |
| #11 | café | #6D4C41 | 109, 76, 65 |
| Otro diámetro (incluido #2) | gris | #5E6670 | 94, 102, 112 |

- Vive en `Modelo/`, sin Revit, para que la usen el panel y el generador.
- El número de varilla de una barra se saca de su **diámetro**, con
  `CuadroVarillas.PorDiametro` (misma tolerancia que el resto del add-in), no
  del nombre del tipo **(decisión mía)**: así una barra del modelo que no se
  llame "#N CQEING" pero tenga el diámetro de un #5 sale roja igual.
- Lo que no está en la tabla sale gris.
- `CuadroVarillas` hoy solo llega hasta #8. Para #9, #10 y #11 la tabla de
  colores lleva su propio diámetro, solo para reconocerlas y pintarlas; no se
  agregan como varillas para armar (sin datos de traslapo ni gancho)
  **(Luciano, opción b)**.

## 1. Reparto en las listas «N. barras X / Y» (Luciano)

Hoy cada lista ofrece 2, 4, 6… 20, y el número es el total de barras
intermedias de las **dos** caras enfrentadas: se reparte mitad y mitad
(`ConfigLongitudinal.Anillo`).

- Cada opción de la lista, al desplegarla y también la elegida, se lee
  "4 (2 + 2)", "6 (3 + 3)", etc. **(Luciano: "también en el selector")**.
- Lo que se guarda sigue siendo el número (4, 6…); solo cambia cómo se
  muestra. Las configuraciones guardadas no se tocan.
- El texto es "N (n + n)" y no "n abajo + n arriba" **(decisión mía)**: en
  la lista no hay lugar, y la vista previa ya muestra qué cara es cuál.

## 2. Pestaña «Vista previa»

### Dónde y cómo (Luciano)

- Pestaña nueva **«Vista previa»** en el panel del medio, junto a «Tramos» y
  «Traslapos» (opción A).
- Acomodo (opción 2, "por ahora"): arriba la sección desde arriba; debajo, el
  frente y el costado lado a lado; al final, un resumen en texto.
- Se actualiza sola con cada cambio del panel (tramo activo, acero
  longitudinal, estribos, recubrimiento, estribos menores), como ya hace
  «Traslapos».
- Es un control propio (`Vistas/PanelVistaPrevia`), como `PanelTraslapos`
  **(decisión mía)**.
- Lleva una leyenda de colores chica, solo con los números que aparecen en la
  columna **(decisión mía)**.

### Vista 1: desde arriba (sección)

- Contorno de la columna, estribo principal (a la distancia del
  recubrimiento) y barras longitudinales: las de esquina y las intermedias de
  cada cara, cada una con el color de su diámetro y su tamaño a escala.
- Junto a cada cara, cuántas barras intermedias lleva (por ejemplo, "2"
  arriba y abajo, "1" a cada costado).
- Los estribos menores (grapas en C, en S y estribos cerrados) del tramo
  activo, con su color, en la posición que diga
  `ConfigLongitudinal.EstribosMenores`. Las grapas tapadas por una caja
  (`PaqueteEstribos.EstaTapada`) en gris, como en la ventana de estribos
  menores.
- La sección es la de la columna activa (`ColumnaContexto`, `LadoX` y
  `LadoY`). El add-in solo admite secciones rectangulares
  (`GeometriaSeccion.ValidarRectangular`), así que si llega otra la vista
  previa no dibuja nada y muestra un aviso.

### Vistas 2 y 3: de frente y de costado

- **Frente** = mirando la cara paralela al eje X (se ven las barras de
  esquina y las intermedias de las caras X). **Costado** = la cara paralela al
  eje Y. Mismo criterio de ejes que `ConfigLongitudinal`.
- Muestran **el tramo activo**, no la columna entera **(Luciano, confirmó la
  suposición)**, más el nudo de arriba y el de abajo si los hay, pintados
  como en «Tramos» (azul nudo, café zapata).
- Barras longitudinales verticales, con su color.
- Cada estribo como una línea horizontal a su altura real; los estribos
  menores, a la altura que les toque en su paquete (pegados encima o debajo
  del principal, `PaqueteEstribos.Calcular`). En la vista de frente se ven
  las piezas que corren en esa dirección como líneas; las que la cruzan, como
  un punto **(decisión mía, como en el boceto)**.
- Cotas a un costado: largo de cada zona de confinamiento (L1) y separación
  de cada zona ("@ 7 cm", "@ 14 cm").
- El tramo se dibuja a escala real en alto. Si el panel es demasiado angosto
  para que se lea el ancho, se puede exagerar el ancho y avisarlo con una
  nota chica **(decisión mía)**.

### Resumen

Debajo de las vistas, en texto:

- Total de barras longitudinales y desglose: esquinas (con número de
  varilla), caras X "n + n" y caras Y "n + n".
- Estribos del tramo activo: cantidad, número de varilla y separaciones.
- Estribos menores: cantidad por forma, con número de varilla.
- Los avisos que afecten a lo dibujado (paquetes que se pisan, grapas
  tapadas, tramo sin altura libre), con el mismo texto que dará el
  generador.

### Que la vista previa diga lo mismo que Revit

Es lo más importante de esta parte.

- Hoy las alturas de los estribos las decide Revit: el generador crea cada
  grupo con `SetLayoutAsMaximumSpacing` y Revit calcula cuántos entran
  (`Logica/GeneradorArmado.cs`, `CrearGrupo` y alrededores). La vista previa
  necesita ese mismo cálculo sin Revit.
- Se saca a `Modelo/` una función sin Revit que, dado el tramo y su
  configuración, devuelve las alturas de cada estribo (y de cada paquete,
  cuando entren los estribos menores). La usan la vista previa y el
  generador, o, si el generador tiene que seguir usando
  `SetLayoutAsMaximumSpacing`, se compara una contra otra.
- **Antes de darla por buena, se verifica contra Revit real** (regla del
  repo): generar dos o tres columnas con configuraciones distintas (uniforme,
  confinado, confinado sin zona central) y comparar la cantidad y las alturas
  de los estribos que puso Revit con las que calcula la función. Se documenta
  en `docs/planes/`.
- Lo mismo para las barras longitudinales: la posición en planta sale de la
  misma lógica que usa `GeneradorLongitudinal`, no de una copia.

### Relación con el trabajo de estribos menores en curso

Los estribos menores y los paquetes se están rediseñando
(`2026-10-20-diseno-estribos-menores-visual-y-64.md`, código sin terminar en
`Modelo/EstriboMenor.cs` y `Modelo/PaqueteEstribos.cs`). La vista previa
dibuja lo que haya en ese momento **(Luciano, confirmó la suposición)**:

- La vista previa se hace ya, sin esperar ese trabajo **(decisión mía)**.
  Mientras tanto dibuja los estribos menores a la misma altura que el
  principal, como se generan hoy. La función de alturas se arma desde el
  principio de modo que reciba el paquete (alto debajo y encima del
  principal), para que al entrar los paquetes solo haya que pasárselo.
- Cuando entren los paquetes, la vista previa usa `PaqueteEstribos` y la
  nueva función de alturas, sin cambiar nada más.

## 3. Colores por diámetro en el modelo de Revit

### Qué se quiere (Luciano)

- Se pintan **todas las barras del modelo según su diámetro**, no solo las
  del add-in, porque es más sencillo (Luciano, 20-oct, cambiando su primera
  respuesta). Eso incluye barras de vigas, zapatas, losas y las hechas a mano.
- Se pintan en **todas las vistas donde aparece la columna**: plantas,
  cortes, elevaciones y 3D.

### Cómo (a verificar contra Revit real)

En Revit el color no es de la barra: se pone por vista. Al pintar todas las
barras se puede usar la forma oficial, con filtros de vista:

- Un filtro por número de varilla (`ParameterFilterElement`, categoría
  armadura estructural, regla por diámetro de barra, por ejemplo
  `REBAR_BAR_DIAMETER` con un rango alrededor del diámetro del cuadro), con
  nombre fijo como "CQEING #5". Uno más para "otro diámetro" (gris), si la
  prueba muestra que se puede expresar con reglas; si no, las barras que no
  están en la tabla quedan con su color normal **(decisión mía)**.
- En cada vista, cada filtro con su color (`View.SetFilterOverrides` con un
  `OverrideGraphicSettings` de color de línea y de superficie, para que se vean
  también las barras cortadas y las de las vistas 3D).
- Ventaja: los filtros quedan en el modelo. Una vista creada después se puede
  pintar agregándole los filtros a mano, o volviendo a generar.

Fuentes: documentación de la API de Revit para `View.SetFilterOverrides`,
`View.AddFilter` y "View Filters".

Antes de implementar:

- confirmar en la documentación de Revit 2027 que todos los miembros usados
  son públicos (regla del repo);
- probar con un comando de diagnóstico temporal en un modelo real que el
  filtro por diámetro reconoce las barras (también las varillas propias "#N
  CQEING"), que pinta las barras cortadas en planta y las de la vista 3D, y
  qué pasa en una vista con **plantilla de vista** asignada (ahí los filtros
  los manda la plantilla y la API puede no dejar ponerlos en la vista).
  Documentar el resultado en `docs/planes/`.

### Cuándo se aplica

- Al generar, en cada vista del documento donde la columna es visible
  (vistas que no son plantillas, que admiten filtros y donde la columna
  aparece, por ejemplo con un `FilteredElementCollector` sobre la vista)
  **(decisión mía en cómo se detecta)**.
- Si los filtros ya existen, se reutilizan y se actualiza su regla; no se
  duplican. Si una vista ya tiene el filtro, se actualiza su color.
- No se tocan las demás sustituciones de gráficos ni los demás filtros de la
  vista.
- Vistas con plantilla: **pendiente de lo que muestre la prueba**. Opciones:
  agregar los filtros a la plantilla (cambia todas las vistas que la usan) o
  saltar esas vistas y avisar. Se decide con Luciano después de la prueba.

## Lo que queda al criterio de quien lo construya

- Escalas, márgenes, grosores de línea y tamaño de letra de las vistas.
- Cómo se reparte el alto disponible entre la sección y las elevaciones.
- El nombre exacto de los filtros y de las clases nuevas.
- Si la función de alturas reemplaza a `SetLayoutAsMaximumSpacing` en el
  generador o solo se compara con él, según lo que muestre la prueba contra
  Revit.

## Estado

Diseño. Nada implementado.
