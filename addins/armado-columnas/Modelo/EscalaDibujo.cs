namespace practicacq.Modelo;

using System;
using System.Globalization;

/// <summary>
/// La escala de los dibujos de la columna (desde arriba y de costado), sin
/// WPF: una sola escala para los dos ejes, así una 25×30 se ve más alta que
/// ancha y nunca cuadrada (reunión del 21-10, #133: "no puede haber nada
/// figurativo"). docs/planes/2026-10-21-secciones-a-escala-133.md.
/// </summary>
public static class EscalaDibujo
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-CO");

    /// <summary>
    /// Píxeles por metro para que un rectángulo de <paramref name="anchoReal"/>
    /// × <paramref name="altoReal"/> metros entre en la caja disponible sin
    /// deformarse: manda el lado que primero llega al borde.
    /// </summary>
    public static double Ajustar(double anchoReal, double altoReal, double anchoDisponible, double altoDisponible) =>
        Math.Min(Math.Max(0, anchoDisponible) / Math.Max(1e-6, anchoReal), Math.Max(0, altoDisponible) / Math.Max(1e-6, altoReal));

    /// <summary>Una medida en metros como texto para una cota: "25 cm", "22,5 cm".</summary>
    public static string Cm(double metros) => string.Format(Es, "{0:0.#} cm", metros * 100);

    /// <summary>Una altura en metros como texto para una cota: "3,00 m".</summary>
    public static string M(double metros) => string.Format(Es, "{0:0.00} m", metros);
}
