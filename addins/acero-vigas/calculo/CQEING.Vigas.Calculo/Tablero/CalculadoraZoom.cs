namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// El zoom de la página del tablero (#83, decisión 6 del plan): la página mide <see cref="Ancho"/> × <see cref="Alto"/>
/// como la maqueta y, si el área disponible es más chica, se achica entera para que nada se corte. Nunca se agranda.
/// </summary>
public static class CalculadoraZoom
{
    public const double Ancho = 1440;
    public const double Alto = 900;

    /// <summary>
    /// El <c>ZoomFactor</c> de WebView2 para el área disponible, en unidades WPF (DIP), que ya traen la escala de Windows.
    /// </summary>
    public static double Factor(double anchoDisponible, double altoDisponible) =>
        Math.Min(1, Math.Min(anchoDisponible / Ancho, altoDisponible / Alto));
}
