# Al regenerar, reemplazar el acero generado antes (#145)

## Contexto

En la reunión del 21-10 (#129, punto 16), Carlos pidió que, al volver a
generar una columna, el add-in borre el acero que generó antes y la arme
completa. La opción de no borrar venía de cuando se armaba por partes
(#2, 2-sep: por defecto no se borraba nada y se apilaba un juego sobre
otro).

Hasta ahora había dos caminos:

- Casilla destildada (por defecto): no se borraba nada.
- Casilla "Borrar TODO el armado existente en estas columnas (incluido
  el que no generó el addin)": se borraba todo el acero hosteado en esas
  columnas, también el ajeno. #61 (William) pedía sacarla porque el
  acero de una viga a veces queda metido en la columna y se borraría.

Cada barra que crea el add-in (estribos, longitudinales y sus piezas de
traslapo, cajas y grapas) ya llevaba una marca invisible
(`MarcaEstribos`, Extensible Storage) con la columna que la originó.

## Decisiones (Luciano, 22-oct)

- El acero que generó el add-in se borra **siempre** al generar, sin
  preguntar.
- La casilla **se queda**, pero ahora solo agrega borrar también el acero
  que no generó el add-in. Destildada por defecto. (Va en contra de lo
  que pedía #61: queda anotado ahí.)
- Con algunos tramos sin ✓: las longitudinales se reemplazan en toda la
  columna (siempre se arman de punta a punta), pero los estribos, cajas y
  grapas de los tramos sin ✓ **se conservan**, porque no se regeneran.

## Cómo quedó

`GeneradorArmado.BorrarAnteriores(doc, plan, tambienAjeno)`, al principio
de la generación y en la misma transacción (si algo falla, se deshace y
el acero anterior vuelve):

- Columnas de la corrida: las que tienen algún tramo con ✓. Se juntan los
  hosts de todos sus tramos y, aparte, los de los tramos con ✓.
- Acero con la marca del add-in (filtro de Extensible Storage):
  - transversal (estribo, caja, grapa): se borra solo si su host es un
    tramo con ✓;
  - longitudinal: se borra en toda la columna.
- Acero sin la marca: se borra en toda la columna solo con la casilla.

Para distinguir transversal de longitudinal se usa el plano de la barra:
estribos, cajas y grapas se crean con normal vertical (Z, o
BasisX/BasisY), y las longitudinales con normal horizontal
(`BarraContinua`). `EsTransversal` = `IsRebarShapeDriven()` y
`|GetShapeDrivenAccessor().Normal.Z| > 0,9`; ambos son API pública. Una
barra que no es de forma cuenta como longitudinal.

Otros cambios:

- `OpcionesArmado.BorrarAnteriores` → `BorrarArmadoAjeno`.
- Casilla: "Borrar también el armado que no generó el add-in en estas
  columnas", con un texto de ayuda que explica que lo propio se
  reemplaza siempre.
- Resumen al terminar: "N elemento(s) de acero anterior(es)
  reemplazado(s)".
- `tools/PreviewUI`: el mensaje simulado.

Conviene saber: una barra del add-in retocada a mano (movida, por
ejemplo) sigue teniendo la marca, así que también se reemplaza.

## Estado

Compila (add-in y PreviewUI). **Pendiente de probar en Revit**:

1. Generar una columna dos veces: queda un solo juego.
2. Regenerar solo un tramo: las longitudinales no se duplican y los
   estribos de los otros tramos siguen siendo los mismos elementos.
3. Con acero modelado a mano en la columna: sin la casilla se conserva;
   con la casilla se borra.
