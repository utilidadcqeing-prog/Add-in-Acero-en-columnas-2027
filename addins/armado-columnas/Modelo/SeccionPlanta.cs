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

    /// <summary>Hasta qué distancia (m) se busca el doblez de una esquina, como el generador.</summary>
    public const double AlcanceDoblez = 0.04;

    /// <summary>
    /// Del borde de la sección al eje de la barra: el recubrimiento llega a la
    /// cara del estribo, y la longitudinal va pegada por dentro (atraviesa el
    /// estribo entero más medio diámetro propio). Mismas unidades en todo.
    /// </summary>
    public static double Inset(double recubrimiento, double diametroEstribo, double diametroBarra) =>
        recubrimiento + diametroEstribo + diametroBarra / 2;

    /// <summary>
    /// Dónde va una barra de esquina de radio <paramref name="radio"/> pedida en
    /// (<paramref name="x"/>, <paramref name="y"/>) para quedar dentro del
    /// doblez más cercano del estribo, tocándolo por dentro: sobre la línea
    /// del centro del doblez a la barra, a (radio interior − radio) del centro.
    /// Si no hay un doblez a menos de <paramref name="alcance"/>, o la barra no
    /// entra en él (radio interior menor que el de la barra: ahí la tocan los
    /// dos lados rectos), queda donde se pidió. Mismas unidades en todo; lo usan
    /// el generador (con los dobleces medidos en Revit) y la vista previa.
    /// </summary>
    public static (double X, double Y) EnElDoblez(
        IEnumerable<(double X, double Y, double RadioInterior)> dobleces, double x, double y, double radio, double alcance)
    {
        (double X, double Y, double RadioInterior)? cercano = null;
        double mejor = alcance;
        foreach (var d in dobleces)
        {
            double distancia = Math.Sqrt((d.X - x) * (d.X - x) + (d.Y - y) * (d.Y - y));
            if (distancia < mejor) (cercano, mejor) = (d, distancia);
        }

        if (cercano is not { } c || c.RadioInterior <= radio || mejor < 1e-9) return (x, y);
        double t = (c.RadioInterior - radio) / mejor;
        return (c.X + (x - c.X) * t, c.Y + (y - c.Y) * t);
    }

    /// <summary>
    /// Los cuatro dobleces del estribo principal (centro y radio interior, en
    /// metros), como lo deja Revit: cara exterior al recubrimiento, esquinas con
    /// el diámetro de doblado de la barra.
    /// </summary>
    public static IEnumerable<(double X, double Y, double RadioInterior)> DoblecesEstribo(
        double ladoX, double ladoY, double recubrimiento, double diametroEstribo, double doblezEstribo)
    {
        double a = recubrimiento + diametroEstribo + doblezEstribo / 2;
        foreach ((double x, double y) in new[] { (a, a), (ladoX - a, a), (ladoX - a, ladoY - a), (a, ladoY - a) })
            yield return (x, y, doblezEstribo / 2);
    }

    /// <summary>La intermedia número <paramref name="indice"/> de <paramref name="n"/>, repartidas parejas entre dos esquinas.</summary>
    public static double PosicionIntermedia(double desde, double hasta, int n, int indice) =>
        desde + (indice + 1) * (hasta - desde) / (n + 1);

    /// <summary>
    /// Las barras en el orden de <see cref="ConfigLongitudinal.Anillo"/>. Las
    /// esquinas "de referencia" (entre las que se reparten las intermedias)
    /// usan la barra más gruesa, como el generador; cada barra se separa de su
    /// cara según su propio diámetro.
    /// </summary>
    /// <param name="doblezEstriboMm">
    /// Diámetro de doblado del estribo: si se da, las esquinas se corren adentro
    /// de su curva como en el generador (<see cref="EnElDoblez"/>). 0 = sin correr.
    /// </param>
    public static IReadOnlyList<BarraEnPlanta> Calcular(
        double ladoX, double ladoY, double recubrimiento, double diametroEstriboMm,
        ConfigLongitudinal cfg, Func<PosicionBarra, double> diametroMm, double doblezEstriboMm = 0)
    {
        IReadOnlyList<PosicionBarra> anillo = cfg.Anillo();
        double estribo = diametroEstriboMm / 1000;
        List<(double X, double Y, double RadioInterior)> dobleces = doblezEstriboMm > 0
            ? DoblecesEstribo(ladoX, ladoY, recubrimiento, estribo, doblezEstriboMm / 1000).ToList()
            : new();
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
            (double x, double y) = EnElDoblez(dobleces, izquierda ? inset : ladoX - inset, abajo ? inset : ladoY - inset,
                diametroMm(PosicionBarra.Esquina) / 2000, AlcanceDoblez);
            barras.Add(Nueva(x, y));
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
