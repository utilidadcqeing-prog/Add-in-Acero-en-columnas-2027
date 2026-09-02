namespace practicacq.Modelo;

using System.Collections.Generic;

/// <summary>
/// Lo que arma la ventana de L4 ("Configurador de Estribos Menores") para el
/// acero longitudinal de una columna: cantidad de barras por cara, tipo de
/// barra por posición, y la rejilla de estribos menores.
///
/// Mismo criterio que <see cref="ConfigTramo"/>: los tipos de barra viajan por
/// nombre, no como <c>RebarBarType</c>, para que esto compile y se serialice
/// sin Revit.
/// </summary>
public sealed class ConfigLongitudinal
{
    /// <summary>Barras por cara paralela al eje X global. Siempre par: se reparte mitad y mitad.</summary>
    public int BarrasPorCaraX { get; set; }

    /// <summary>Barras por cara paralela al eje Y global. Siempre par.</summary>
    public int BarrasPorCaraY { get; set; }

    /// <summary>Las cuatro esquinas siempre llevan barra — no es opcional, por eso no hay un booleano acá.</summary>
    public string? TipoBarraEsquinasNombre { get; set; }

    public string? TipoBarraCaraXNombre { get; set; }
    public string? TipoBarraCaraYNombre { get; set; }

    /// <summary>La rejilla que arma L4 a partir de <see cref="BarrasPorCaraX"/>/<see cref="BarrasPorCaraY"/>.</summary>
    public List<EstriboMenor> EstribosMenores { get; set; } = new();
}
