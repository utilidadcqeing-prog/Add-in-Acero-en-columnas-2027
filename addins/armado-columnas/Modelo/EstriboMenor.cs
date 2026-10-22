namespace practicacq.Modelo;

/// <summary>Las tres formas de estribo menor que se dibujan en el panel.</summary>
public enum FormaMenor
{
    /// <summary>Grapa de borde a borde con los dos ganchos hacia el mismo lado.</summary>
    GrapaC,

    /// <summary>La misma grapa, con un gancho hacia cada lado (Luciano, 20-oct).</summary>
    GrapaS,

    /// <summary>Estribo cerrado (caja): rango en su eje, de extremo a extremo en el otro.</summary>
    Cerrado,
}

/// <summary>
/// Un estribo menor puesto a mano en el panel de estribos menores --
/// docs/planes/2026-10-20-diseno-estribos-menores-visual-y-64.md, sección 1.
/// Reemplaza a las casillas de <see cref="ConfigLongitudinal.GrapasFila"/>,
/// <see cref="ConfigLongitudinal.GrapasColumna"/> y <see cref="ConfigLongitudinal.Cajas"/>.
///
/// Mismo sistema de ejes que <see cref="CajaMenor"/>: <see cref="EjeCaja.Fila"/>
/// cruza la sección en X a la altura de esa fila; <see cref="EjeCaja.Columna"/>,
/// en Y. En una grapa <see cref="Desde"/> y <see cref="Hasta"/> son iguales,
/// porque siempre va de borde a borde (Luciano, 20-oct).
/// </summary>
public sealed class EstriboMenor
{
    public FormaMenor Forma { get; set; }

    public EjeCaja Eje { get; set; }

    /// <summary>Índice (0-based) de la fila o columna donde arranca, inclusive.</summary>
    public int Desde { get; set; }

    /// <summary>Índice (0-based) donde termina, inclusive. Igual a <see cref="Desde"/> en una grapa.</summary>
    public int Hasta { get; set; }

    /// <summary>Ganchos hacia el otro lado (botón "Voltear"). No aplica al cerrado.</summary>
    public bool Volteado { get; set; }

    /// <summary>Barra de esta pieza; null = "Igual al estribo" del tramo.</summary>
    public string? TipoBarraNombre { get; set; }

    /// <summary>
    /// El ancho mínimo de un estribo cerrado hecho con esa barra, medido entre
    /// los ejes de SU barra (no de las que abraza: el cerrado va por fuera de
    /// ellas, #132): en el lado corto entran dos dobleces de 90°, y cada uno
    /// ocupa medio diámetro de doblado más medio diámetro de barra. Más
    /// angosto, Revit no lo puede doblar (10-sep: una caja de 3,7 cm por fuera
    /// con #3 no se pudo ajustar; 20-oct: una de 3,8 cm salió con "An internal
    /// error has occurred").
    /// </summary>
    public static double AnchoMinimoCerradoMm(TipoBarra barra) => barra.DobladoEstriboMm + barra.DiametroMm;

    /// <summary>
    /// Las medidas por FUERA de un estribo cerrado que abraza esas piezas de
    /// barra (Luciano, 20-oct: "debería rodear/abrazar la varilla"): cada lado
    /// toca por fuera la pieza que más sale para ese lado. Con las piezas REALES
    /// a la altura del cerrado -- las dos de un traslapo, si cae en uno --, no
    /// con la posición teórica: Revit corre las varillas unos milímetros y el
    /// cerrado atravesaba alguna (#130, medido el 21-oct). Mismas unidades.
    ///
    /// Las esquinas del cerrado son curvas (<paramref name="radioDoblezInterior"/>,
    /// medido por dentro): una barra que toca los dos lados rectos queda
    /// atravesada por la curva (~4 mm con #6 y cerrado #3, 21-oct). Los cuatro
    /// lados se abren lo justo para que la barra de cada esquina se apoye en la
    /// curva, como las esquinas del estribo principal.
    /// </summary>
    public static (double X1, double X2, double Y1, double Y2) CajaQueAbraza(
        IEnumerable<GanchoGrapa.Pieza> piezas, double diametroCerrado, double radioDoblezInterior)
    {
        List<GanchoGrapa.Pieza> todas = piezas.ToList();
        if (todas.Count == 0) throw new ArgumentException("no hay ninguna pieza que abrazar", nameof(piezas));

        double x1 = todas.Min(p => p.X - p.Radio) - diametroCerrado, x2 = todas.Max(p => p.X + p.Radio) + diametroCerrado;
        double y1 = todas.Min(p => p.Y - p.Radio) - diametroCerrado, y2 = todas.Max(p => p.Y + p.Radio) + diametroCerrado;

        // Cuánto hay que abrir para que en cada esquina las piezas queden
        // adentro de la curva: con los dos lados de la esquina corridos t hacia
        // afuera, el centro de la curva se corre t·(sx, sy), y la pieza tiene que
        // quedar a radioDoblezInterior − radio de él (la menor t que lo cumple).
        double abrir = 0;
        foreach ((int sx, int sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            double cx = (sx < 0 ? x1 + diametroCerrado : x2 - diametroCerrado) - sx * radioDoblezInterior;
            double cy = (sy < 0 ? y1 + diametroCerrado : y2 - diametroCerrado) - sy * radioDoblezInterior;
            foreach (GanchoGrapa.Pieza p in todas)
            {
                double wx = p.X - cx, wy = p.Y - cy;
                if (wx * sx <= 0 || wy * sy <= 0) continue; // no está en la esquina: la tocan los lados rectos
                double libre = radioDoblezInterior - p.Radio;
                if (libre <= 0 || wx * wx + wy * wy <= libre * libre) continue;

                // |w − s·t|² = libre²  →  2t² − 2(w·s)t + |w|² − libre² = 0
                double ws = wx * sx + wy * sy;
                double discriminante = ws * ws - 2 * (wx * wx + wy * wy - libre * libre);
                if (discriminante < 0) continue;
                abrir = Math.Max(abrir, (ws - Math.Sqrt(discriminante)) / 2);
            }
        }

        return (x1 - abrir, x2 + abrir, y1 - abrir, y2 + abrir);
    }

    public EstriboMenor Copiar() => new()
    {
        Forma = Forma, Eje = Eje, Desde = Desde, Hasta = Hasta, Volteado = Volteado, TipoBarraNombre = TipoBarraNombre,
    };
}
