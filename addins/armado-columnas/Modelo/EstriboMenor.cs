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
    /// El ancho mínimo, entre ejes de las dos barras que abraza, de un estribo
    /// cerrado hecho con esa barra: en el lado corto entran dos dobleces de
    /// 90°, y cada uno ocupa medio diámetro de doblado más medio diámetro de
    /// barra. Más angosto, Revit no lo puede doblar (20-oct: una caja de 3,8 cm
    /// con #3 salió con "An internal error has occurred").
    /// </summary>
    public static double AnchoMinimoCerradoMm(TipoBarra barra) => barra.DobladoEstriboMm + barra.DiametroMm;

    /// <summary>
    /// A qué distancia de costado del centro del gancho de 180° pasa el arco
    /// por encima de una barra que está <paramref name="profundidad"/> por
    /// dentro de la punta del gancho (las dos cosas medidas al eje). Es donde
    /// tiene que quedar la barra para que el gancho la monte (Luciano, 20-oct:
    /// "el gancho tiene que pasar sobre las varillas"). Mismas unidades en todo.
    /// </summary>
    public static double CorrimientoSobreBarra(double radio, double profundidad)
    {
        if (profundidad <= 0) return 0;
        if (profundidad >= radio) return radio;
        double resto = radio - profundidad;
        return Math.Sqrt(radio * radio - resto * resto);
    }

    /// <summary>
    /// Dónde van, por FUERA, los dos lados de un estribo cerrado que abraza las
    /// barras de los ejes <paramref name="ejeDesde"/> y <paramref name="ejeHasta"/>
    /// (Luciano, 20-oct: "debería rodear/abrazar la varilla"). En un traslapo la
    /// pieza de arriba queda un diámetro hacia menos (medido el 20-oct), así
    /// que el lado de abajo deja lugar a las dos posiciones. Mismas unidades.
    /// </summary>
    public static (double Desde, double Hasta) LadosQueAbrazan(
        double ejeDesde, double ejeHasta, double diametroBarra, double diametroCerrado) =>
        (ejeDesde - diametroBarra - diametroBarra / 2 - diametroCerrado,
         ejeHasta + diametroBarra / 2 + diametroCerrado);

    public EstriboMenor Copiar() => new()
    {
        Forma = Forma, Eje = Eje, Desde = Desde, Hasta = Hasta, Volteado = Volteado, TipoBarraNombre = TipoBarraNombre,
    };
}
