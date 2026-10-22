# La sección de cada tramo con su medida real (dado debajo de la columna)

## Qué se vio

Luciano, 22-oct: en un tramo de 30×30, la ventana mostraba 40×40. Debajo
había un dado más grande.

## Qué se encontró (medido con el MCP en el modelo)

La pila de esa columna tiene tres elementos, los tres de la categoría
"Pilares estructurales":

| Elemento | Tipo | Z (m) |
|---|---|---|
| 781599 (el dado) | (0.40 x 0.40) | -0,65 a 0,00 |
| 782105 | (0.30 x 0.30) | 0,00 a 3,50 |
| 782736 | (0.30 x 0.30) | 3,50 a 7,00 |

`LectorContexto` leía la sección de toda la columna (`BaseX`/`BaseY`) del
elemento de más abajo (`pila[0]`), así que con el dado salía 40×40 para
todos los tramos. Ya avisaba "Se usó la del tramo más bajo para toda la
columna", pero no lo corregía.

Esa medida la usaban solo las vistas de la ventana y los avisos de
choque (#131). **El generador no**: cada estribo sale con la forma de su
propio elemento, y las longitudinales van en la zona común a toda la
pila (`GeneradorLongitudinal`, #42). El acero en Revit estaba bien; la
ventana no lo mostraba igual.

## Fix

- `ColumnaContexto.BaseX/BaseY` = la **zona común** en planta de toda la
  pila (la intersección de las huellas): donde el generador pone las
  barras. Con la misma sección en toda la columna, es esa sección.
- Cada `Tramo` guarda su medida (`LadoX`, `LadoY`) y dónde le cae la zona
  común (`ComunDesdeX/Y`). `Tramo.Seccion(columna)` devuelve los lados y
  el corrimiento; sin medida propia (proveedor falso, configuraciones
  viejas), la de la columna sin corrimiento.
- Vista previa (`PanelVistaPrevia`): hormigón, estribo y cotas con la
  medida del tramo; barras, estribos menores, choques y piezas de
  traslapo calculados en la zona común y corridos a su lugar.
- Avisos de choque (`ChoquesSeccion`): todo en la zona común; el estribo
  del tramo se corre a ese marco.
- Ventana de estribos menores, pestaña de traslapos y planta de
  selección: la zona común (los menores se configuran por columna y
  abrazan las barras, que están en la zona común).
- El aviso "la sección cambia" ahora explica que cada tramo se dibuja con
  su medida y las barras van en la zona común.

Supuesto: las secciones son rectangulares y alineadas con los ejes, como
en el resto de la ventana.

Limitación conocida: en la elevación de la vista previa, la línea del
estribo va de barra a barra; en un tramo más ancho que la zona común con
estribos (columna que se angosta arriba), el estribo real es más ancho
que esa línea.

## Estado

- Compila el add-in y PreviewUI. Pasan `PruebasVistaPrevia` (3 pruebas
  nuevas: la medida del tramo y que, con la misma sección en toda la
  columna, los choques no cambian) y `PruebasTraslapos`.
- **Pendiente de probar en Revit**: en la columna del dado, los tramos de
  30×30 tienen que mostrar 30×30 y el del dado, 40×40 con las barras en
  la zona común.
