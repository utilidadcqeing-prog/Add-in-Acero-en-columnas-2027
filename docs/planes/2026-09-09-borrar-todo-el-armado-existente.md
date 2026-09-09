# Cambio: la casilla de borrado ahora borra TODO el armado existente, no solo el del addin

## Contexto

Pedido explícito del usuario (9-sep): la casilla "Borrar estribos generados
antes en estas columnas" solo borraba `Rebar` que el propio addin había
creado antes (identificados por la marca interna `MarcaEstribos`) -- lo
modelado a mano o por otra herramienta en esas mismas columnas se dejaba
intacto. El usuario pidió que, al tildar la casilla, se borre TODO el
armado existente en esas columnas, sin importar quién lo puso.

## Cambio

- `Logica/GeneradorArmado.cs` (`BorrarAnteriores`): ya no filtra por
  `MarcaEstribos.Filtro()` -- ahora borra cualquier `Rebar` cuyo
  `GetHostId()` esté entre los hosts de las columnas del plan de hoy, sin
  mirar quién lo creó.
- `Vistas/VentanaConfiguradorEstribos.xaml`: texto de la casilla actualizado
  a "Borrar TODO el armado existente en estas columnas (incluido el que no
  generó el addin)", para que no sea una sorpresa al usarla.

## Riesgo

Es una operación destructiva a propósito: con la casilla tildada, generar
borra cualquier barra de acero ya modelada en esas columnas (a mano o no),
antes de crear la nueva. Sigue acotada a las columnas de la corrida actual
(no toca el resto del modelo) y corre dentro de la transacción normal de
Revit (se puede deshacer con Ctrl+Z si no se guardó todavía, igual que
cualquier otro cambio).

## Estado

Compilado. Pendiente: desplegar y retest del usuario.
