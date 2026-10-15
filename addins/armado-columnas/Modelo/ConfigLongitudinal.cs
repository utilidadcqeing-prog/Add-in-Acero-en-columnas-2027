namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// Gancho del anclaje inferior, donde la longitudinal entra a la zapata
    /// (issue #37, normativa colombiana confirmada por Carlos Quintero: ese
    /// gancho SIEMPRE va hacia adentro de la sección, nunca hacia afuera).
    /// Solo aplica al extremo que ancla en la zapata -- el resto de la barra
    /// sigue recta, sin gancho (ver comentario de <see cref="Logica.GeneradorLongitudinal"/>).
    /// </summary>
    public string? TipoGanchoLongitudinalNombre { get; set; }

    /// <summary>
    /// Filas (posiciones Y, 0-based) con una grapa horizontal de borde a
    /// borde -- conecta la barra intermedia de la cara izquierda con la de
    /// la cara derecha, a esa altura, con gancho a cada punta.
    /// </summary>
    public List<int> GrapasFila { get; set; } = new();

    /// <summary>
    /// Columnas (posiciones X, 0-based) con una grapa vertical de borde a
    /// borde -- conecta la barra intermedia de la cara superior con la de
    /// la cara inferior, en esa posición.
    /// </summary>
    public List<int> GrapasColumna { get; set; } = new();

    /// <summary>Las cajas que arma L4 cruzando filas y columnas tildadas.</summary>
    public List<CajaMenor> Cajas { get; set; } = new();

    /// <summary>
    /// Longitud de la barra comercial disponible, en metros -- Fase 5 (#7):
    /// solo existen barras de 6 o 12 m de stock, no un largo arbitrario. Una
    /// corrida de barra longitudinal más larga que esto necesita traslapo;
    /// el tramo final de una corrida puede quedar más corto que esto (se
    /// recorta), eso no requiere traslapo.
    /// </summary>
    public double LongitudComercialBarra { get; set; } = 12.0;

    /// <summary>
    /// Tipo de empalme de Revit (<c>RebarSpliceType</c>, por nombre) con el que
    /// se parten las barras longitudinales en sus traslapos -- issue #41. Vacío
    /// = el por defecto del add-in: un tipo propio por varilla, con el solape
    /// del cuadro de la empresa (<see cref="CuadroVarillas.Varilla.Traslapo"/>).
    /// </summary>
    public string? TipoEmpalmeNombre { get; set; }

    /// <summary>
    /// Copia independiente, listas incluidas. Hace falta para las
    /// configuraciones guardadas (<see cref="ConfiguracionGuardada"/>): si la
    /// configuración compartiera las listas de grapas/cajas con la columna de
    /// la que salió, seguir editando los estribos menores de esa columna
    /// cambiaría también la configuración guardada, sin que nadie lo pida.
    /// </summary>
    public ConfigLongitudinal Copiar() => new()
    {
        BarrasPorCaraX = BarrasPorCaraX,
        BarrasPorCaraY = BarrasPorCaraY,
        TipoBarraEsquinasNombre = TipoBarraEsquinasNombre,
        TipoBarraCaraXNombre = TipoBarraCaraXNombre,
        TipoBarraCaraYNombre = TipoBarraCaraYNombre,
        TipoGanchoLongitudinalNombre = TipoGanchoLongitudinalNombre,
        LongitudComercialBarra = LongitudComercialBarra,
        TipoEmpalmeNombre = TipoEmpalmeNombre,
        GrapasFila = new List<int>(GrapasFila),
        GrapasColumna = new List<int>(GrapasColumna),
        Cajas = Cajas.Select(c => new CajaMenor { Eje = c.Eje, Desde = c.Desde, Hasta = c.Hasta }).ToList(),
    };
}
