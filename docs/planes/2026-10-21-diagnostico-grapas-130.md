# Diagnóstico #130: los ganchos de las grapas atraviesan las barras

Fecha: 21-oct-2026. Estado: **implementado (rama `columnas/grapas-abrazan-130`),
falta probarlo en Revit.**

## Contexto

En la reunión del 21-10 (`2026-10-21-observaciones-reunion-columnas.md`,
punto 1), Carlos vio en el 3D que los estribos menores no abrazan las barras
longitudinales: los ganchos las atraviesan.

Prueba de Luciano: columna 387983 de 25×30 cm (25 en X, 30 en Y), con
recubrimiento de 4 cm, esquinas #8, cara X #7 (2 por cara), cara Y #6 (2 por
cara), estribos #3 y una grapa en C por cada fila de la cara Y. Se midió con
el MCP de Revit (cajas de cada barra). Las coordenadas van en mm desde la
esquina inferior izquierda de la columna.

## Primera hipótesis: descartada

Se sospechaba que la pieza corrida del traslapo quedaba del lado contrario
al que supone la grapa (−X/−Y). **No es así**: en las 12 barras, la pieza de
arriba está un diámetro hacia −X/−Y de la de abajo, como supone el código.

## Lo que se midió

Barras #6 de la cara Y, que son las que enganchan las grapas:

| Barra | Pieza de abajo (X, Y) | Pieza de arriba (X, Y) |
|---|---|---|
| Fila 0, izquierda | 63,8; 115,5 (0 a 2,51 m) | 63,8; 96,4 (1,19 a 6,71 m) |
| Fila 0, derecha | 186,2; 120,7 (0 a 5,51 m) | 186,2; 101,6 (4,19 a 6,71 m) |
| Fila 1, izquierda | 68,5; 176,5 | 68,5; 157,4 |
| Fila 1, derecha | 181,5; 176,5 | 181,5; 157,4 |

Grapa de la fila 0 (por ejemplo la 393821, a 0,04 m de altura):

- Va de X = 40 a X = 210, es decir, de recubrimiento a recubrimiento.
- Tramo recto en Y = 109,2; el gancho abre hacia +Y (hacia el centro).
- Gancho de 180° doblado como el estribo: diámetro interior de 38 mm (radio
  al eje de 23,8 mm) y ancho total de 57 mm.
- Centro de cada gancho: (68,6; 133,0) a la izquierda y (181,4; 133,0) a la
  derecha.

Para que una barra #6 (radio 9,55 mm) quede **dentro** del gancho sin
chocar, su eje tiene que estar a 9,45 mm o menos del centro del gancho. Para
que quede **afuera** sin chocar, a 38,1 mm o más.

| Barra | Distancia al centro del gancho | Resultado |
|---|---|---|
| Fila 0, izquierda (pieza de abajo) | 18,1 mm | **choca**: el gancho la atraviesa |
| Fila 0, derecha (pieza de abajo) | 13,2 mm | **choca** |
| Fila 0, izquierda (pieza de arriba, en el traslapo) | 36,9 mm | choca de costado, no la abraza |

## Causa

1. **El gancho está pensado para cruzar la barra, no para rodearla.**
   `EstriboMenor.CorrimientoSobreBarra` (20-oct, "el gancho tiene que pasar
   sobre las varillas") calcula dónde poner el centro del gancho para que el
   **eje del arco pase justo por el eje de la barra**. En planta eso es
   choque seguro: la barra queda partida por el gancho. Lo correcto es que la
   barra quede **adentro** del doblez, tocando su cara interior.
2. **Las barras no están donde dice la cuenta teórica.** Revit las corre
   unos milímetros al crearlas y al cambiarlas de host. En esta columna, la
   izquierda de la fila 0 está a 115,5 y la derecha a 120,7 (la teórica es
   120,7). La grapa se calcula con la posición teórica, así que aunque la
   cuenta del gancho estuviera bien, en algunas barras no le acertaría.

Hallazgo aparte: el estribo principal queda con su cara exterior a 44,8 mm
del borde (no a 40), y las barras quedan pegadas a ese estribo. La punta de la
grapa sí llega a 40 mm, o sea 4,8 mm más afuera que el estribo. Revisarlo
junto con el arreglo.

## Decisión de Luciano (21-oct)

En la zona de traslapo, la grapa abraza **las dos piezas si entran juntas**
en el doblez. Si no entran, abraza solo la que sigue hacia arriba y se avisa.
Con una grapa #3 doblada a 4 d (38 mm por dentro) entran dos #4 o dos #5,
pero no dos #6, #7 ni #8.

## Fix implementado

- `BarraContinua.Resultado` trae `Posiciones`: dónde quedó **realmente**
  cada pieza (eje en planta y alturas), leído de Revit después de alinearla y
  reponerle los ganchos.
- `Modelo/GanchoGrapa.cs` (cálculo puro, con pruebas):
  - `Elegir`: qué piezas abraza el gancho, según la decisión de arriba.
  - `CentroDelDoblez`: de costado, en el medio de las piezas; a lo largo de la
    grapa, lo justo para que toquen la cara interior en la punta.
  - `Linea`: qué línea pedirle a Revit para que cada gancho quede con su centro
    ahí. Si las dos barras no están a la misma altura de la sección, la grapa
    queda unos milímetros en diagonal. Contempla C, S y "Voltear".
- `GeneradorLongitudinal.GenerarMenores`, a cada altura de grapa, busca la
  varilla real más cercana a cada punta (hasta 3 cm de la teórica) y usa sus
  piezas a esa altura. Si no la encuentra, usa la posición teórica.
- `MedirGanchoDeGrapa` (antes `MedirCorreccionDeGrapa`) sigue midiendo el
  gancho en una grapa de prueba, pero ahora solo devuelve lo medido: dónde
  queda el centro del gancho y el radio interior del doblez.
- Se borraron `EstriboMenor.CorrimientoSobreBarra` y su prueba
  `gancho_pasa_sobre_la_barra`, que validaba justamente el error. Las pruebas
  nuevas son `gancho_abraza_la_barra`, `gancho_en_el_traslapo` y
  `linea_de_la_grapa`.
- Como la barra queda pegada a la punta del doblez, la punta de la grapa
  queda alineada con la cara exterior del estribo (unos 44,7 mm del borde en
  esta columna), y ya no 4,8 mm más afuera.

No se tocó el estribo cerrado (caja): sigue con las posiciones teóricas.

## Pendiente de retest

- Generar de nuevo la columna 387983 y medir con el MCP: cada barra #6 a
  9,45 mm o menos del centro del gancho que la engancha.
- En "Detalles", la línea "gancho de grapa medido en Revit": doblez de unos
  38 mm por dentro.
- Probar una S y una grapa volteada.
- Revisar en el 3D que los ganchos no atraviesen ninguna barra.
- Cajas: medirlas igual y decidir si necesitan el mismo arreglo.
