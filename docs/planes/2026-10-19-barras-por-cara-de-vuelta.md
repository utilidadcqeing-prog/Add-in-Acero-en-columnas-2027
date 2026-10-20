# Número de barra por posición (esquinas / cara X / cara Y), de vuelta

Fecha: 19-oct-2026. Estado: **implementado, pruebas sin Revit en verde; falta probar en Revit real.**

## Contexto

El diseño de la metodología de traslapos
(`2026-10-17-diseno-metodologia-traslapos.md`, punto 6 y sección 7) cambió la
elección del número de barra longitudinal: en vez de por posición (esquinas,
cara X, cara Y) se elegía por tipo A/B. Luciano aclaró el 19-oct que fue un
malentendido suyo: el número se elige por posición, como antes. Se mantiene
todo lo demás de esa metodología.

## Lo que se decidió con Luciano

- Vuelven los tres combos: "Tipo barra cara X", "cara Y" y "esquinas".
- **La alternancia de traslapos se mantiene**: "una sí, una no" dando la
  vuelta al perímetro; las posiciones pares (tipo A) traslapan en los tramos
  libres pares y las impares (tipo B) en los impares. A/B ya no decide el
  número de barra, solo dónde traslapa.
- Se queda la opción "Solape mínimo". "El mayor de los dos tipos (A y B)"
  pasa a "El mayor de la columna": el mayor solape del cuadro entre los
  números que se usan.
- Configuraciones guardadas con A/B (17 al 19-oct): esquinas = A, caras X e
  Y = B. Las guardadas antes del 17-oct ya tienen las de cara y no se tocan.

## Consecuencia en el cálculo

Dentro de un grupo puede haber barras de distinto diámetro (por ejemplo,
esquinas #6 y caras #4: hay barras A de los dos números). Cada diámetro tiene
su solape y su gancho, así que `PlanTraslapos` ahora calcula **un plan por
grupo y diámetro** ("A · #6", "A · #4", "B · #6", "B · #4"). Si todas las
barras son del mismo número, siguen siendo dos planes.

Cuando A no tiene tramos propios (columna de un piso), sus planes van en los
huecos que dejan **todos** los planes de B.

## Implementación

- `Modelo/PosicionBarra.cs` (nuevo): `Esquina`, `CaraX`, `CaraY`.
- `Modelo/ConfigLongitudinal.cs`: los campos por cara vuelven a ser los
  activos; `TipoBarraANombre`/`TipoBarraBNombre` quedan obsoletos, solo para
  leer. `MigrarBarrasPorCara()` reemplaza a `MigrarBarrasAB()`. Nuevos
  `Anillo()` (la posición de cada barra en el orden del anillo; pares = A) y
  `BarraDe(posicion)`. Ventana y generador usan el mismo `Anillo()`.
- `Modelo/PlanTraslapos.cs`: `Resultado` pasa a tener listas `A` y `B` (un
  `PlanTipo` por diámetro, con `Grupo` y `DiametroMm`), `Todos` y
  `De(grupoA, diametro)`. `Calcular` recibe los diámetros de cada grupo; se
  conserva un atajo con un diámetro por grupo.
- `Logica/GeneradorLongitudinal.cs`: cada posición del anillo toma la barra
  de su cara y el plan de su grupo y diámetro. Ganchos propios para cada
  barra usada. Grapas y cajas vuelven a mirar las barras de cara X / Y.
- `Vistas/VentanaConfiguradorEstribos.xaml(.cs)`: los tres combos; cambiar
  la cantidad de barras X / Y también recalcula la pestaña "Traslapos"
  (cambia qué números caen en A y en B).
- `Vistas/PanelTraslapos.xaml(.cs)`: un dibujo por plan, con scroll
  horizontal si no entran.
- `tools/PruebasTraslapos`: pruebas adaptadas y cuatro nuevas (migración
  A/B → cara, orden del anillo, varios diámetros por grupo, varios diámetros
  con A sin tramos propios). 16 de 16 OK.

## Pendiente de probar en Revit

1. Panel: aparecen los tres combos con la #4 por defecto.
2. Esquinas #5 y caras #4: la pestaña "Traslapos" muestra cuatro dibujos, y
   lo generado coincide (número de cada barra y alturas de traslapo).
3. Dos barras vecinas nunca traslapan a la misma altura.
4. Una configuración guardada entre el 17 y el 19-oct se carga con el aviso
   y las barras repartidas como arriba.
5. Grapas y cajas siguen saliendo.
