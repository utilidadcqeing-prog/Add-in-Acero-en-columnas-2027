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
    /// Barra de las posiciones de tipo A: las pares del anillo del perímetro,
    /// empezando por la esquina inferior izquierda. A y B se alternan "una sí,
    /// una no" y traslapan en tramos distintos (A en los pares, B en los
    /// impares) -- docs/planes/2026-10-17-diseno-metodologia-traslapos.md.
    /// Reemplaza a la elección por cara (esquinas / cara X / cara Y).
    /// </summary>
    public string? TipoBarraANombre { get; set; }

    /// <summary>Barra de las posiciones de tipo B: las impares del anillo. Ver <see cref="TipoBarraANombre"/>.</summary>
    public string? TipoBarraBNombre { get; set; }

    /// <summary>
    /// Todo largo de corte de varilla (ganchos incluidos) es múltiplo de esto,
    /// en metros, para que en obra sea fácil de cortar. Ver
    /// <see cref="PlanTraslapos.Redondeos"/>.
    /// </summary>
    public double RedondeoVarillas { get; set; } = PlanTraslapos.RedondeoPorDefecto;

    /// <summary>
    /// true: A y B traslapan con el mayor de sus dos solapes del cuadro;
    /// false: cada uno con el de su diámetro.
    /// </summary>
    public bool SolapeMayorDeLosDos { get; set; }

    /// <summary>OBSOLETO: solo para leer configuraciones guardadas antes de los tipos A/B. Ver <see cref="MigrarBarrasAB"/>.</summary>
    public string? TipoBarraEsquinasNombre { get; set; }

    /// <summary>OBSOLETO: ver <see cref="TipoBarraEsquinasNombre"/>.</summary>
    public string? TipoBarraCaraXNombre { get; set; }

    /// <summary>OBSOLETO: ver <see cref="TipoBarraEsquinasNombre"/>.</summary>
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
    /// OBSOLETO: el tipo de empalme ya no se elige. Cada traslapo puede tener
    /// un largo distinto y el add-in crea el tipo que corresponde
    /// (<c>Logica/TiposEmpalme</c>). Queda solo para leer configuraciones viejas.
    /// </summary>
    public string? TipoEmpalmeNombre { get; set; }

    /// <summary>
    /// Copia independiente, listas incluidas. Hace falta para las
    /// configuraciones guardadas (<see cref="ConfiguracionGuardada"/>): si la
    /// configuración compartiera las listas de grapas/cajas con la columna de
    /// la que salió, seguir editando los estribos menores de esa columna
    /// cambiaría también la configuración guardada, sin que nadie lo pida.
    /// </summary>
    /// <summary>
    /// Pasa una configuración guardada antes de los tipos A/B al formato
    /// nuevo: A = la barra de esquinas, B = la de cara X (o la de cara Y si no
    /// había de cara X). Devuelve true si cambió algo, para avisar.
    /// </summary>
    public bool MigrarBarrasAB()
    {
        if (TipoBarraANombre is not null || TipoBarraBNombre is not null) return false;
        if (TipoBarraEsquinasNombre is null && TipoBarraCaraXNombre is null && TipoBarraCaraYNombre is null) return false;

        TipoBarraANombre = TipoBarraEsquinasNombre ?? TipoBarraCaraXNombre ?? TipoBarraCaraYNombre;
        TipoBarraBNombre = TipoBarraCaraXNombre ?? TipoBarraCaraYNombre ?? TipoBarraEsquinasNombre;
        return true;
    }

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
