# Borde de las barras en "colores coherentes" (#144)

## Contexto

En la reunión del 21-10 (#129, punto 15) se vio que, en la vista
"colores coherentes", las barras generadas no muestran aristas y no se
distingue dónde termina una y empieza otra. En "sombreado" sí se ven.
Pasa también en vigas. En otra reunión, Carlos aclaró lo que quería: que
las barras tengan un borde para que se noten las separaciones, y que ese
borde es un ajuste que se puede poner desde Revit.

## Qué se encontró

El add-in pinta las barras por diámetro con filtros de vista
(`Logica/ColoresEnVistas.cs`), y `ConColor` ponía las **líneas de
proyección y de corte del mismo color que el relleno**. El borde quedaba
del color de la barra y desaparecía. En "sombreado" igual se notaba
porque Revit oscurece las caras con la luz; en "colores coherentes" no
hay sombras.

El ajuste que mencionó Carlos es el color de las líneas de proyección
del filtro (Visibilidad/Gráficos → Filtros → Líneas de proyección).

Vigas ya lo había resuelto el 21-oct (Juan): `acero-vigas/Revit/FiltrosColor.cs`
pone las líneas en `ColoresDiametro.RgbContorno` = (30, 30, 30), casi
negro, en todas las vistas. No negro puro porque, con el fondo oscuro,
Revit pinta de blanco las líneas negras.

## Fix

- Decisión de Luciano (22-oct): **borde negro**. Se usa el mismo casi
  negro de vigas, (30, 30, 30), por lo del fondo oscuro.
- Solo en vistas 3D (`ViewType.ThreeD`, también las plantillas 3D). En
  plantas, cortes y alzados las líneas siguen del color del diámetro: ahí
  una barra puede dibujarse solo con líneas, y en negro se perdería el
  color. No está medido todavía; es una precaución.
- Vale para los filtros propios "CQEING #N" y para los "ACERO #N" del
  proyecto que el add-in repinta.
- Como el add-in actualiza esos filtros cada vez que genera, las vistas
  existentes se corrigen al volver a generar.

## Estado

Compila. **Pendiente de probar en Revit**:

1. En "3D ACEROS", con "colores coherentes": las barras tienen el borde
   negro y se distinguen entre sí, y el relleno sigue con el color del
   diámetro.
2. En una planta y un corte: las barras siguen con su color.
3. Si Carlos también lo quiere en 2D, verlo en Revit antes de ampliarlo
   (vigas ya lo hace en todas las vistas: comparar).

Los colores nuevos por diámetro (#143) y lo del borde se le pasaron a
Juan en #154 (vigas).
