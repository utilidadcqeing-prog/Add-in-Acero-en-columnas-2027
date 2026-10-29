namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Los ganchos de 90° de las barras longitudinales vistos en planta (#181):
/// hacia dónde dobla cada uno y si la punta se sale de la sección de la
/// columna. Es la misma regla que usa el generador
/// (<c>GeneradorLongitudinal.OrientacionesHaciaAdentro</c> y
/// <c>LadoLejosDelLindero</c>), escrita sin Revit para la vista previa:
///
/// - El gancho dobla en el plano de la barra, perpendicular a su normal: las
///   esquinas de abajo y las barras de las caras Y doblan a lo largo de X;
///   las esquinas de arriba y las de las caras X, a lo largo de Y.
/// - Mira hacia el centro de la sección. Una barra justo en el eje dobla
///   hacia el lado "positivo" de Revit (+Y si dobla en Y, −X si dobla en X).
/// - Si cruzaría la línea de propiedad, se gira; si cruza para los dos
///   lados, se deja como está.
///
/// Las medidas salen del cuadro (#177): la pata, por fuera del doblez, mide
/// el <see cref="CuadroVarillas.Varilla.Gancho90"/>.
/// </summary>
public static class GanchosLongitudinales
{
    /// <param name="Dx">Hacia dónde dobla en planta (−1, 0 o 1).</param>
    /// <param name="Recto">Del eje de la barra a la punta, en metros (B − medio diámetro).</param>
    /// <param name="Radio">Radio del doblez, al eje de la barra, en metros.</param>
    /// <param name="SeSale">La punta queda fuera de la sección de la columna.</param>
    /// <param name="SinSalida">Cruza la línea de propiedad hacia los dos lados.</param>
    public sealed record Gancho(
        SeccionPlanta.BarraEnPlanta Barra, int Dx, int Dy, double Recto, double Radio, bool SeSale, bool Girado, bool SinSalida);

    /// <param name="barras">En la zona común, con (0, 0) en su esquina de abajo a la izquierda.</param>
    /// <param name="origenEnPlanta">Dónde cae esa esquina en el plano del lindero, en metros.</param>
    public static IReadOnlyList<Gancho> Calcular(
        IReadOnlyList<SeccionPlanta.BarraEnPlanta> barras, double ladoX, double ladoY,
        IReadOnlyList<TramoLindero> lindero, (double X, double Y) origenEnPlanta)
    {
        var ganchos = new List<Gancho>();
        foreach (SeccionPlanta.BarraEnPlanta b in barras)
        {
            if (CuadroVarillas.PorDiametro(b.DiametroMm) is not { } varilla) continue;

            double diametro = b.DiametroMm / 1000;
            double recto = varilla.Gancho90 - diametro / 2;
            double radio = varilla.Gancho90Cuadro.DiametroDoblado / 2 + diametro / 2;

            // Normal Y (esquinas de abajo, caras Y): dobla en X, el positivo de Revit es −X.
            // Normal X (esquinas de arriba, caras X): dobla en Y, el positivo es +Y.
            bool enX = b.Posicion == PosicionBarra.CaraY || (b.Posicion == PosicionBarra.Esquina && b.Y < ladoY / 2);
            int positivo = enX ? -1 : 1;
            double haciaElCentro = enX ? ladoX / 2 - b.X : ladoY / 2 - b.Y;
            int signo = Math.Abs(haciaElCentro) < 1e-9 ? positivo : Math.Sign(haciaElCentro);

            bool girado = false, sinSalida = false;
            if (lindero.Count > 0)
            {
                double alcance = Lindero.AlcanceGancho90(b.DiametroMm);
                double gx = origenEnPlanta.X + b.X, gy = origenEnPlanta.Y + b.Y;
                bool Cruza(int s) => Lindero.Cruza(lindero, gx, gy, enX ? s : 0, enX ? 0 : s, alcance);
                if (Cruza(signo))
                {
                    if (Cruza(-signo)) sinSalida = true;
                    else { signo = -signo; girado = true; }
                }
            }

            int dx = enX ? signo : 0, dy = enX ? 0 : signo;
            double puntaX = b.X + dx * recto, puntaY = b.Y + dy * recto;
            bool seSale = puntaX < -1e-6 || puntaX > ladoX + 1e-6 || puntaY < -1e-6 || puntaY > ladoY + 1e-6;

            ganchos.Add(new Gancho(b, dx, dy, recto, radio, seSale, girado, sinSalida));
        }

        return ganchos;
    }
}
