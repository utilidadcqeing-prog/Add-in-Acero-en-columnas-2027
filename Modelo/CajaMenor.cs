namespace practicacq.Modelo;

/// <summary>
/// Un estribo menor tipo "caja": un rectángulo más chico, nested adentro del
/// estribo principal, que arma la ventana de estribos menores (L4) cruzando
/// una fila tildada (posición Y) con una columna tildada (posición X) -- cada
/// cruce entre las tildadas es una caja.
///
/// Reemplaza al viejo <c>EstriboMenor</c> con un <c>Tipo</c> compartido entre
/// grapa y caja: las grapas ya no viven acá, son
/// <see cref="ConfigLongitudinal.GrapasFila"/>/<see cref="ConfigLongitudinal.GrapasColumna"/>,
/// porque una grapa no es una posición de grilla -- cruza de borde a borde en
/// una sola fila o columna entera, no en una intersección puntual como la caja.
/// </summary>
public sealed class CajaMenor
{
    /// <summary>Índice de columna (posición X, 0-based) -- ver <see cref="ConfigLongitudinal.BarrasPorCaraX"/>.</summary>
    public int Columna { get; set; }

    /// <summary>Índice de fila (posición Y, 0-based) -- ver <see cref="ConfigLongitudinal.BarrasPorCaraY"/>.</summary>
    public int Fila { get; set; }
}
