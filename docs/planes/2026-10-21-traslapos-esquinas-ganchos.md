# Traslapos: que las piezas corridas no choquen (esquinas y ganchos del estribo)

Fecha: 21-oct-2026. Rama `columnas/grapas-abrazan-130` (va en el mismo commit
que #130). Columna de prueba: 387983.

## Contexto

En cada traslapo Revit corre una de las dos piezas de la varilla un diámetro
hacia un costado, y la deja corrida en todo su largo. Esa pieza corrida
chocaba con las barras vecinas y con los ganchos del estribo.

Criterios de Luciano:

- "Lo suyo es hacer lo máximo posible para generarlas sin que choquen: se
  calcula todo antes y se busca la mejor opción." Si es imposible, se avisa
  (#131, más adelante también al configurar).
- "Las barras no pueden tocar los ganchos del estribo. La de la esquina donde
  están los ganchos no puede salir al lado porque el gancho la traspasa. Sí
  puede moverse en diagonal, pero pegada al gancho."
- "Que se calcule la más óptima, moviendo las que sean necesarias."
- "Que avise cuando traspase."

## Qué se midió contra Revit

1. **Quién se corre y hacia dónde.** Revit corre +1 diámetro, en el sentido de
   la normal de la barra, la pieza por donde **empieza el dibujo** (la de
   abajo si se dibuja de abajo hacia arriba, la de arriba si se dibuja al
   revés). Eligiendo el sentido del dibujo y cuál grupo queda en la posición
   pedida salen cuatro maneras de dejar cada varilla
   (`AcomodoTraslapos.Cuatro`).
2. **La cara "imposible" no lo era.** En la cara de arriba de 25 cm se
   concluyó al principio que no había lugar; era un error por usar posiciones
   teóricas. Lo que pasaba es que las esquinas de arriba corrían su pieza a lo
   largo de esa cara, contra la vecina. Con la esquina corriendo por la otra
   cara, la cara queda limpia.
3. **Los ganchos del estribo.** En la esquina donde cierra el estribo, las dos
   colas del gancho de 135° entran en diagonal, una a cada lado de la barra
   (unos 23,7 mm a cada lado de la diagonal). Una pieza corrida por cualquiera
   de las dos caras las atraviesa; lo único libre es la diagonal hacia adentro.
4. **Normal en diagonal.** Revit acepta una normal diagonal y corre la pieza
   un diámetro en diagonal, sin "pegarla" al estribo (con normal X o Y sí la
   pega unos 4 mm). Pero **la corre como si la normal tuviera siempre la Y
   positiva**: con (0,707; 0,707) hacia (+, +), con (−0,707; −0,707) también
   hacia (+, +), y con (0,707; −0,707) hacia (−, +).
5. **Revit no respeta mover la cadena en diagonal después.** Al alinear, se
   movía la cadena entera 18 mm en X y 18 en Y. A veces quedaba bien y a veces
   Revit la corría 36 mm solo en Y: una vez falló la esquina de arriba a la
   derecha y otra vez la de arriba a la izquierda, y la pieza quedaba
   atravesando el estribo 11 a 15 mm.
6. **La barra de esquina va dentro del doblez.** Pedida en la posición
   teórica, la barra de esquina quedaba 8,8 mm metida en el doblez del
   estribo (con normal diagonal Revit no la acomoda sola). Se mide el doblez
   real del estribo y se pide la barra tocando su cara interior.

## Fix implementado

- `Modelo/AcomodoTraslapos.cs`:
  - `Elegir` busca **la mejor combinación entre todas** (ramificación y poda,
    con los choques de a pares calculados una sola vez). Entre las que chocan
    lo mismo, la que cambia menos varillas. Tiene un tope de pasos
    (`UltimaFueCompleta`).
  - Recibe obstáculos: los ganchos del estribo, en trocitos.
  - `MaximaMetida`, para los avisos.
- `Logica/GeneradorLongitudinal.cs`:
  - `MedirEstribos`: mide en el estribo real los ganchos (obstáculos) y los
    dobleces (centro y radio interior).
  - `EnElDoblez`: la barra de esquina se pide dentro del doblez del estribo.
  - Las esquinas prueban tres ejes: el de siempre, el de la otra cara y la
    diagonal hacia adentro. La diagonal se pasa **con Y positiva** (punto 4).
    Si la barra lleva gancho no se prueba la diagonal: el gancho dobla
    perpendicular a la normal y no miraría al centro.
  - Con la diagonal y la pieza corrida hacia −normal, la varilla **se dibuja
    ya corrida** un diámetro hacia −normal (`Pedido.Desfase`): Revit corre la
    pieza donde empieza el dibujo +1 diámetro, que la deja en la esquina, y
    la otra queda donde se planeó. No hay que mover nada después (punto 5).
  - Avisos nuevos: "atraviesa el gancho del estribo" (más de 1 mm) y "una
    pieza quedó por fuera de las esquinas: atraviesa el estribo".
- `Logica/BarraContinua.cs`: `Acomodar` recibe los obstáculos;
  `Pedido.Desfase` se usa al dibujar.
- Pruebas (`tools/PruebasTraslapos`): `traslapos_esquina_solo_por_esta_cara`,
  `traslapos_la_esquina_corre_por_la_otra_cara`,
  `traslapos_no_atraviesan_el_gancho`, `traslapos_elige_la_mejor_de_todas`
  (200 casos al azar contra fuerza bruta). Se quitó `traslapos_cara_sin_lugar`,
  que partía de la premisa equivocada del punto 2. 0 fallas.

## Resultado en la columna 387983 (última corrida, 21-oct 20:50)

- Las cuatro esquinas: una pieza dentro del doblez y la otra corrida en
  diagonal hacia adentro, sin moverlas después.
- Ninguna pieza fuera de las esquinas.
- Avisos que quedan, esperados (de diseño, no hay lugar):
  - la #7 en (3,349; −0,085): la cola del gancho le pasa 11,7 mm;
  - la #6 en (3,309; −0,015): roce de 1 mm con el gancho.
- Holgura medida (MCP) entre la pieza diagonal y la vecina de la cara: 3,7 mm
  abajo a la izquierda y arriba a la derecha; **0,7 mm de roce arriba a la
  izquierda**, con la pieza 394513.

## Pendiente

- **El roce de 0,7 mm** viene de que Revit deja la #7 de la cara de arriba
  (X4) unos 8,6 mm a la izquierda de lo pedido (3,340 en vez de 3,349). La
  elección se hace con posiciones pedidas, así que no lo ve. Sub-issue aparte.
- #131: avisar ya al configurar.
- Confirmar con Carlos lo de "tipo A" en la esquina de abajo a la izquierda.
