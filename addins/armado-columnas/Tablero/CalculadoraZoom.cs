namespace practicacq.Tablero;

using System;

/// <summary>Un rectángulo de pantalla en unidades WPF (DIP): esquina de arriba a la izquierda y tamaño.</summary>
public readonly record struct Caja(double X, double Y, double Ancho, double Alto);

/// <summary>
/// El zoom de la página de columnas, igual que el tablero de vigas: la página
/// mide <see cref="Ancho"/> × <see cref="Alto"/> como la maqueta y, si el área
/// disponible es más chica, se achica entera para que nada se corte. Nunca se
/// agranda. Copia de <c>CQEING.Vigas.Calculo.Tablero.CalculadoraZoom</c>: los
/// add-ins no se referencian entre sí.
/// </summary>
public static class CalculadoraZoom
{
    public const double Ancho = 1440;
    public const double Alto = 900;

    /// <summary>El zoom más chico: WebView2 no acepta 0, y más chico la página ya no se lee.</summary>
    public const double Minimo = 0.25;

    /// <summary>El <c>ZoomFactor</c> de WebView2 para el área disponible, en unidades WPF (DIP).</summary>
    public static double Factor(double anchoDisponible, double altoDisponible) =>
        Math.Max(Minimo, Math.Min(1, Math.Min(anchoDisponible / Ancho, altoDisponible / Alto)));

    /// <summary>La ventana recortada al <paramref name="area"/> de trabajo de su monitor y movida lo justo para quedar dentro.</summary>
    public static Caja Encajar(Caja ventana, Caja area)
    {
        double ancho = Math.Min(ventana.Ancho, area.Ancho), alto = Math.Min(ventana.Alto, area.Alto);
        return new Caja(
            Math.Max(area.X, Math.Min(ventana.X, area.X + area.Ancho - ancho)),
            Math.Max(area.Y, Math.Min(ventana.Y, area.Y + area.Alto - alto)),
            ancho,
            alto);
    }
}
