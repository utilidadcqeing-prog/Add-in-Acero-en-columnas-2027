namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Dónde va cada barra longitudinal en la sección, sin Revit: la misma cuenta
/// que <c>GeneradorLongitudinal</c> (que usa <see cref="Inset"/> y
/// <see cref="PosicionIntermedia"/> de acá). La usa la vista previa del panel.
/// </summary>
public static class SeccionPlanta
{
    /// <param name="X">Desde la cara izquierda de la sección, en metros.</param>
    /// <param name="Y">Desde la cara de abajo de la sección, en metros.</param>
    public sealed record BarraEnPlanta(PosicionBarra Posicion, double X, double Y, double DiametroMm);

    /// <summary>
    /// Del borde de la sección al eje de la barra: el recubrimiento llega a la
    /// cara del estribo, y la longitudinal va pegada por dentro (atraviesa el
    /// estribo entero más medio diámetro propio). Mismas unidades en todo.
    /// </summary>
    public static double Inset(double recubrimiento, double diametroEstribo, double diametroBarra) =>
        recubrimiento + diametroEstribo + diametroBarra / 2;

    /// <summary>La intermedia número <paramref name="indice"/> de <paramref name="n"/>, repartidas parejas entre dos esquinas.</summary>
    public static double PosicionIntermedia(double desde, double hasta, int n, int indice) =>
        desde + (indice + 1) * (hasta - desde) / (n + 1);

    /// <summary>
    /// Las barras en el orden de <see cref="ConfigLongitudinal.Anillo"/>. Las
    /// esquinas "de referencia" (entre las que se reparten las intermedias)
    /// usan la barra más gruesa, como el generador; cada barra se separa de su
    /// cara según su propio diámetro.
    /// </summary>
    public static IReadOnlyList<BarraEnPlanta> Calcular(
        double ladoX, double ladoY, double recubrimiento, double diametroEstriboMm,
        ConfigLongitudinal cfg, Func<PosicionBarra, double> diametroMm)
    {
        IReadOnlyList<PosicionBarra> anillo = cfg.Anillo();
        double estribo = diametroEstriboMm / 1000;
        double InsetDe(PosicionBarra p) => Inset(recubrimiento, estribo, diametroMm(p) / 1000);

        double insetEsquinas = anillo.Distinct().Max(InsetDe);
        double xEsqIzq = insetEsquinas, xEsqDer = ladoX - insetEsquinas;
        double yEsqAbajo = insetEsquinas, yEsqArriba = ladoY - insetEsquinas;

        int columnas = Math.Max(0, cfg.BarrasPorCaraX / 2);
        int filas = Math.Max(0, cfg.BarrasPorCaraY / 2);

        var barras = new List<BarraEnPlanta>();
        BarraEnPlanta Nueva(double x, double y)
        {
            PosicionBarra p = anillo[barras.Count];
            return new BarraEnPlanta(p, x, y, diametroMm(p));
        }

        void Esquina(bool izquierda, bool abajo)
        {
            double inset = InsetDe(PosicionBarra.Esquina);
            barras.Add(Nueva(izquierda ? inset : ladoX - inset, abajo ? inset : ladoY - inset));
        }

        void DeCaraX(int c, bool abajo)
        {
            double inset = InsetDe(PosicionBarra.CaraX);
            barras.Add(Nueva(PosicionIntermedia(xEsqIzq, xEsqDer, columnas, c), abajo ? inset : ladoY - inset));
        }

        void DeCaraY(int f, bool izquierda)
        {
            double inset = InsetDe(PosicionBarra.CaraY);
            barras.Add(Nueva(izquierda ? inset : ladoX - inset, PosicionIntermedia(yEsqAbajo, yEsqArriba, filas, f)));
        }

        // Mismo recorrido que el anillo de GeneradorLongitudinal.
        Esquina(izquierda: true, abajo: true);
        for (int c = 0; c < columnas; c++) DeCaraX(c, abajo: true);
        Esquina(izquierda: false, abajo: true);
        for (int f = 0; f < filas; f++) DeCaraY(f, izquierda: false);
        Esquina(izquierda: false, abajo: false);
        for (int c = columnas - 1; c >= 0; c--) DeCaraX(c, abajo: false);
        Esquina(izquierda: true, abajo: false);
        for (int f = filas - 1; f >= 0; f--) DeCaraY(f, izquierda: true);

        return barras;
    }
}
