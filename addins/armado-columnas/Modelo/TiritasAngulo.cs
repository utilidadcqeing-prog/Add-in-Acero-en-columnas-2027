namespace practicacq.Modelo;

using System;

/// <summary>
/// Dónde van, en píxeles del lienzo de «Editar estribos menores», las dos
/// tiritas de 90° · 135° · 180° de la grapa elegida, una junto a cada punta.
/// Sin WPF, para poder probarlo.
/// </summary>
public static class TiritasAngulo
{
    /// <param name="fila">Grapa de fila (horizontal); si no, de columna (vertical).</param>
    /// <param name="inicio">La barra de la punta de inicio (izquierda o abajo), en píxeles.</param>
    /// <param name="fin">La barra de la otra punta.</param>
    /// <returns>La esquina de arriba a la izquierda de cada tirita.</returns>
    public static ((double Left, double Top) Inicio, (double Left, double Top) Fin) Ubicar(
        bool fila, (double X, double Y) inicio, (double X, double Y) fin,
        double ancho, double alto, double anchoLienzo, double altoLienzo)
    {
        (double, double) Dentro(double left, double top) =>
            (Math.Max(2, Math.Min(anchoLienzo - ancho - 2, left)), Math.Max(2, Math.Min(altoLienzo - alto - 2, top)));

        // Fila: la del inicio debajo de su barra y la del fin encima, cada una
        // hacia adentro. Las dos a la misma altura se pisaban (~150 px cada una
        // contra ~220 px entre barras) y un clic cambiaba la punta equivocada
        // (revisión final del 26-oct).
        if (fila)
            return (Dentro(inicio.X - 6, inicio.Y + 16), Dentro(fin.X - ancho + 6, fin.Y - alto - 16));

        // Columna: a la derecha, hacia adentro (arriba de la barra de abajo, debajo de la de arriba).
        return (Dentro(inicio.X + 14, inicio.Y - alto - 10), Dentro(fin.X + 14, fin.Y + 10));
    }
}
