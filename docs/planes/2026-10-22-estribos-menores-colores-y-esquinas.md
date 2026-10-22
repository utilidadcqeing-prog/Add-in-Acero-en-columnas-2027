# "Editar estribos menores": colores de Revit y ocultar esquinas

Fecha: 22-oct-2026. Estado: **implementado, falta verlo en Revit.**

## Contexto

Pedido de Luciano al probar el #131: que el panel de estribos menores
muestre las barras con los colores que van a tener en Revit, y un switch para
no mostrar las barras sobre las que no se trabaja (las esquinas: no llevan
estribos menores).

## Decisiones (Luciano, 22-oct)

- **Todo con el color de su diámetro**, como en Revit
  (`ColoresVarilla.ColorDe`, la misma tabla de los filtros de vista y de la
  vista previa): barras longitudinales, piezas de traslapo y también las
  grapas y los estribos cerrados (antes iban por forma: C verde, S morado,
  cerrado azul). La forma se distingue por el dibujo.
- **Switch "Ocultar barras de esquina"**: con el switch activado no se dibujan
  las barras de esquina, ni sus piezas de traslapo ni las marcas de choque
  sobre ellas.

## Implementado

`Vistas/VentanaEstribosMenores.xaml(.cs)`: `ColorDeDiametro`, `EsDeEsquina` y
el `CheckOcultarEsquinas` junto al selector "Sección normal / Zona de
traslapo".

Se mantiene:

- **La grapa tapada:** en gris.
- **El cerrado que no se dobla:** en rojo oscuro (`Paleta.Error`). Es parecido al rojo del #5
  (#EA4335); si confunde, se puede cambiar por un contorno.
- **El halo de la pieza seleccionada.**
- **El color por forma:** sigue en los íconos de la paleta y en el aro bajo el mouse.

## Pendiente

Verlo en Revit.
