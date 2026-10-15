# Cajas por eje (nueva regla) + orientación de ganchos de grapas + diagnóstico

## Contexto

Dos pedidos del usuario sobre los **estribos menores** (grapas y cajas):

1. **Cajas**: la semántica no era la que se quiso. El addin exigía tildar fila
   **y** columna, y la caja salía del cruce de ambos rangos (producto cruzado).
   Lo correcto, con imágenes del usuario: **cada lado arma sus propias cajas**,
   que cruzan de **extremo a extremo** en el eje perpendicular, y las de un lado
   se **superponen** con las del otro.
2. **Ganchos**: salen muy grandes y deformes. El usuario pidió buscar la forma
   nativa de Revit antes de dibujar geometría a mano, y decidió **diagnosticar
   primero**. Aparte, sobre una captura del modelo de referencia (que es **solo
   de grapas**), marcó que los ganchos **deberían mirar siempre hacia el centro**.

## La regla de cajas, confirmada con el usuario

Por cada lado (fila y columna se procesan por separado e independientemente), se
toman las casillas tildadas **en orden**. Los huecos **no cortan nada**:

- cantidad **par** → se emparejan de a dos consecutivas — (1ª,2ª), (3ª,4ª)… — y
  cada par arma una caja;
- cantidad **impar** → **una sola** caja, de la primera a la última;
- **una sola** tildada → ninguna caja (para eso están las casillas de grapa).

| Tildadas en ese lado | Resultado |
|---|---|
| 1ª y 3ª (la 2ª libre) | 1 caja, de la 1ª a la 3ª |
| 4 seguidas | 2 cajas: (1ª,2ª) y (3ª,4ª) |
| 5 seguidas | 1 caja, de la 1ª a la 5ª |
| 6 seguidas | 3 cajas |
| 1ª,2ª libres 3ª,4ª y 5ª,6ª | 2 cajas: (1ª,2ª) y (5ª,6ª) |
| 1ª,2ª,4ª | 1 caja, de la 1ª a la 4ª |

Esto **reemplaza** el criterio anterior de fusionar tildadas contiguas
(el viejo `VentanaEstribosMenores.Rangos`, que venía de la reunión del 4-sep).

## Fix implementado

**`Modelo/CajaMenor.cs`**: una caja pasa a describir **un solo eje**. Nuevo enum
`EjeCaja` (`Fila`/`Columna`) más `Desde`/`Hasta`; desaparecen los cuatro campos
`FilaDesde/FilaHasta/ColumnaDesde/ColumnaHasta`, porque el eje perpendicular
siempre va de extremo a extremo y no hay nada que guardar de él.

**`Vistas/VentanaEstribosMenores.xaml.cs`**:
- `Rangos` (fusión por contigüidad) reemplazada por `RangosDeCaja`, con la regla
  de arriba. Es la única función de agrupamiento, así que el cambio entra en un
  solo lugar y lo toman el dibujo, el resumen y el guardado.
- Se sacó el producto cruzado: cada lado dibuja y guarda sus propias cajas.
- `DibujarCaja` recibe eje + rango y dibuja de extremo a extremo en el
  perpendicular. El resumen cuenta la **suma**, ya no el producto.
- Al reabrir la ventana, el tildado se reconstruye marcando **solo las dos
  puntas** de cada caja guardada. Rellenar el rango entero cambiaría el
  resultado (una caja de la 1ª a la 4ª volvería como cuatro tildadas, y cuatro
  tildadas son dos cajas). Con las puntas, reabrir y aceptar sin tocar nada deja
  exactamente la misma geometría; lo que se pierde es cuáles casillas del medio
  había tildado el usuario, que ya no cambian el resultado.

**`Logica/GeneradorLongitudinal.cs`**:
- `GenerarMenores` geometriza la caja según su eje: para una de fila, los lados
  largos van de `xMin + insetEsquinas` a `xMax - insetEsquinas` y los cortos
  salen de `PosicionIntermedia` sobre el rango de filas; para una de columna, al
  revés. `CrearCaja` no cambió: ya recibía `x1, x2, y1, y2` sueltos.
- La prioridad **caja sobre grapa** se mantiene, ahora comparando también el eje.
- **Orientación del gancho de las grapas**: `CrearGrapa` fijaba
  `RebarTerminationOrientation.Left` para los dos extremos y para todas las
  posiciones. `Left`/`Right` se interpretan respecto del **sentido de recorrido
  de la curva** (mismo problema que ya documenta `GeometriaSeccion.Normalizar`),
  así que con una constante la grapa de arriba y la de abajo apuntan a lados
  opuestos. Ahora se calcula: se saca el vector "izquierda" real de la curva
  (`BasisZ × dirección`) y se elige `Left` o `Right` según cuál caiga del lado
  del centro de la sección.

**`ComandoDiagnosticoRebar.cs`**: el reporte ahora arranca con un catálogo del
documento — todas las formas (`RebarShape`) con su estilo y cantidad de
segmentos, y todos los tipos de gancho (`RebarHookType`) con sus parámetros.

## Estado

Compilado (0 errores) y desplegado a Revit 2027.

## Verificación pendiente (retest del usuario)

**En la ventana, antes de generar** (es el contrato más rápido de validar):
recorrer los casos de la tabla — sobre todo 4 seguidas → 2 cajas, 5 seguidas →
1 caja, y dos grupos separados → 2 cajas. Confirmar además que tildar de un solo
lado ya alcanza para que salga una caja (antes no salía ninguna), y que tildando
los dos lados se superponen en cruz.

**En Revit**: que las cajas reales coincidan con la previsualización y crucen de
borde a borde.

**Orientación de los ganchos de las grapas**: confirmar que apuntan hacia el
centro en las cuatro posiciones (fila de arriba, de abajo, columna izquierda y
derecha), no solo en una. Queda **sin confirmar** que `Left` sea el giro
antihorario visto desde +Z: si salen todos al revés, se invierte el signo en
`CrearGrapa` — está marcado con un comentario en el código.

**Ganchos de las cajas**: no se tocaron. Una caja es un lazo cerrado (sus dos
ganchos caen juntos en una esquina, como el estribo principal) y ya pasa por el
camino nativo de `RebarShape`, así que puede que ya estén bien. Mirar cómo
quedan y, si hace falta, corregirlos con su propio criterio en vez de copiar el
de las grapas.

**Diagnóstico**: correr el botón "Diagnóstico de Rebar" y traer el catálogo de
formas y ganchos, más la geometría de una grapa ya generada, para decidir el
tamaño/forma del gancho en la sesión siguiente.

## Fuera de alcance

- El **tamaño y la forma** del gancho de grapas y cajas: acá solo se diagnostica.
  Las dos pistas de la lectura del código: las grapas nunca pasan por
  `Rebar.CreateFromRebarShape` (las cajas sí), y grapas y cajas **heredan el tipo
  de gancho del estribo principal del tramo**, sin medida propia — un gancho
  dimensionado para el estribo grande sale desproporcionado en una grapa chica.
- Qué filas/columnas llevan grapa: no cambia.
- Columnas giradas en planta: limitación conocida, documentada aparte.
