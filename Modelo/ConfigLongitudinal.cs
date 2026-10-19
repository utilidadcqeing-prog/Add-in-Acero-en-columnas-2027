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

    /// <summary>
    /// Barra de las cuatro esquinas. El número de barra se elige por posición
    /// (esquinas / cara X / cara Y) y no por tipo A/B: elegirlo por A/B fue un
    /// malentendido, corregido por Luciano el 19-oct. La alternancia A/B sigue,
    /// pero solo decide en qué tramos traslapa cada barra, no su número --
    /// docs/planes/2026-10-19-barras-por-cara-de-vuelta.md.
    /// </summary>
    public string? TipoBarraEsquinasNombre { get; set; }

    /// <summary>Barra de las intermedias de las caras paralelas al eje X (inferior y superior).</summary>
    public string? TipoBarraCaraXNombre { get; set; }

    /// <summary>Barra de las intermedias de las caras paralelas al eje Y (izquierda y derecha).</summary>
    public string? TipoBarraCaraYNombre { get; set; }

    /// <summary>
    /// Todo largo de corte de varilla (ganchos incluidos) es múltiplo de esto,
    /// en metros, para que en obra sea fácil de cortar. Ver
    /// <see cref="PlanTraslapos.Redondeos"/>.
    /// </summary>
    public double RedondeoVarillas { get; set; } = PlanTraslapos.RedondeoPorDefecto;

    /// <summary>
    /// true: todas las barras de la columna traslapan con el mayor solape del
    /// cuadro entre los números que se usan; false: cada una con el de su
    /// diámetro.
    /// </summary>
    public bool SolapeMayorDeLosDos { get; set; }

    /// <summary>OBSOLETO: solo para leer configuraciones guardadas con barras por tipo A/B (17 al 19-oct). Ver <see cref="MigrarBarrasPorCara"/>.</summary>
    public string? TipoBarraANombre { get; set; }

    /// <summary>OBSOLETO: ver <see cref="TipoBarraANombre"/>.</summary>
    public string? TipoBarraBNombre { get; set; }

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
    /// OBSOLETO: el tipo de empalme ya no se elige. Cada traslapo puede tener
    /// un largo distinto y el add-in crea el tipo que corresponde
    /// (<c>Logica/TiposEmpalme</c>). Queda solo para leer configuraciones viejas.
    /// </summary>
    public string? TipoEmpalmeNombre { get; set; }

    /// <summary>
    /// Pasa una configuración guardada con barras por tipo A/B al formato por
    /// posición: esquinas = A, caras X e Y = B (en la esquina inferior
    /// izquierda, que es A, arranca la alternancia). Devuelve true si cambió
    /// algo, para avisar.
    /// </summary>
    public bool MigrarBarrasPorCara()
    {
        if (TipoBarraEsquinasNombre is not null || TipoBarraCaraXNombre is not null || TipoBarraCaraYNombre is not null) return false;
        if (TipoBarraANombre is null && TipoBarraBNombre is null) return false;

        TipoBarraEsquinasNombre = TipoBarraANombre ?? TipoBarraBNombre;
        TipoBarraCaraXNombre = TipoBarraCaraYNombre = TipoBarraBNombre ?? TipoBarraANombre;
        return true;
    }

    /// <summary>
    /// Posición de cada barra en el anillo del perímetro, en el orden en que se
    /// recorre: esquina inferior izquierda, cara inferior de izquierda a
    /// derecha, esquina inferior derecha, cara derecha de abajo hacia arriba,
    /// esquina superior derecha, cara superior de derecha a izquierda, esquina
    /// superior izquierda y cara izquierda de arriba hacia abajo. Las pares son
    /// tipo A y las impares tipo B (alternancia de traslapos del 11-sep). Lo
    /// usan la ventana (qué números caen en A y cuáles en B) y el generador
    /// (qué barra va en cada posición): el mismo orden en los dos lados.
    /// </summary>
    public IReadOnlyList<PosicionBarra> Anillo()
    {
        int columnas = System.Math.Max(0, BarrasPorCaraX / 2);
        int filas = System.Math.Max(0, BarrasPorCaraY / 2);

        var anillo = new List<PosicionBarra>();
        for (int vuelta = 0; vuelta < 2; vuelta++)
        {
            anillo.Add(PosicionBarra.Esquina);
            anillo.AddRange(Enumerable.Repeat(PosicionBarra.CaraX, columnas));
            anillo.Add(PosicionBarra.Esquina);
            anillo.AddRange(Enumerable.Repeat(PosicionBarra.CaraY, filas));
        }

        return anillo;
    }

    /// <summary>El nombre de la barra elegida para esa posición.</summary>
    public string? BarraDe(PosicionBarra posicion) => posicion switch
    {
        PosicionBarra.Esquina => TipoBarraEsquinasNombre,
        PosicionBarra.CaraX => TipoBarraCaraXNombre,
        _ => TipoBarraCaraYNombre,
    };

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
        TipoBarraANombre = TipoBarraANombre,
        TipoBarraBNombre = TipoBarraBNombre,
        RedondeoVarillas = RedondeoVarillas,
        SolapeMayorDeLosDos = SolapeMayorDeLosDos,
        TipoGanchoLongitudinalNombre = TipoGanchoLongitudinalNombre,
        LongitudComercialBarra = LongitudComercialBarra,
        TipoEmpalmeNombre = TipoEmpalmeNombre,
        GrapasFila = new List<int>(GrapasFila),
        GrapasColumna = new List<int>(GrapasColumna),
        Cajas = Cajas.Select(c => new CajaMenor { Eje = c.Eje, Desde = c.Desde, Hasta = c.Hasta }).ToList(),
    };
}
