namespace practicacq.Modelo;

using System;
using System.Collections.Generic;

/// <summary>
/// Un tramo recto de la línea de propiedad, en planta y en METROS (la altura a
/// la que se dibujó en Revit no importa). Lo lee <c>LectorContexto</c> de
/// <c>PropertyLine.GetBoundary()</c>.
/// </summary>
public readonly record struct TramoLindero(double X1, double Y1, double X2, double Y2);

/// <summary>
/// En el borde del lote los ganchos no pueden terminar del lado del vecino
/// (issue #139). La regla (Luciano, 22-oct): el gancho sigue mirando hacia el
/// centro de la sección y solo se gira el que cruzaría la línea de propiedad.
/// Ver docs/planes/2026-10-22-ganchos-zapata-y-lindero-138-139.md.
/// </summary>
public static class Lindero
{
    /// <summary>
    /// Hasta dónde llega en planta la punta de un gancho de 90°, medido desde
    /// el eje de la barra: el largo del cuadro más 2,5 diámetros por el
    /// doblez y el grosor (medido el 22-oct: #4 llega a 25,6 cm con 22,2 del
    /// cuadro; #7 a 44,4 cm con 38,8). Sin dato del cuadro, 16 diámetros.
    /// </summary>
    public static double AlcanceGancho90(double diametroMm)
    {
        double diametro = diametroMm / 1000;
        return CuadroVarillas.PorDiametro(diametroMm) is { } varilla
            ? varilla.Gancho90 + 2.5 * diametro
            : 16 * diametro;
    }

    /// <summary>
    /// Si un gancho que sale de (<paramref name="x"/>, <paramref name="y"/>)
    /// en la dirección (<paramref name="dx"/>, <paramref name="dy"/>) y llega
    /// hasta <paramref name="alcance"/> metros corta o toca algún tramo del
    /// lindero.
    /// </summary>
    public static bool Cruza(IReadOnlyList<TramoLindero> lindero, double x, double y, double dx, double dy, double alcance)
    {
        double largo = Math.Sqrt(dx * dx + dy * dy);
        if (largo < 1e-12) return false;

        double px = x + dx / largo * alcance, py = y + dy / largo * alcance;
        foreach (TramoLindero t in lindero)
            if (SeCortan(x, y, px, py, t.X1, t.Y1, t.X2, t.Y2)) return true;

        return false;
    }

    private static bool SeCortan(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        double o1 = Giro(ax, ay, bx, by, cx, cy), o2 = Giro(ax, ay, bx, by, dx, dy);
        double o3 = Giro(cx, cy, dx, dy, ax, ay), o4 = Giro(cx, cy, dx, dy, bx, by);

        if (o1 * o2 < 0 && o3 * o4 < 0) return true;

        // Toques y casos alineados: un extremo apoyado sobre el otro tramo.
        return (o1 == 0 && Sobre(ax, ay, bx, by, cx, cy)) || (o2 == 0 && Sobre(ax, ay, bx, by, dx, dy)) ||
               (o3 == 0 && Sobre(cx, cy, dx, dy, ax, ay)) || (o4 == 0 && Sobre(cx, cy, dx, dy, bx, by));
    }

    /// <summary>Producto cruz redondeado a 0 por debajo de 1e-12 m² (una décima de micra por metro).</summary>
    private static double Giro(double ax, double ay, double bx, double by, double px, double py)
    {
        double v = (bx - ax) * (py - ay) - (by - ay) * (px - ax);
        return Math.Abs(v) < 1e-12 ? 0 : v;
    }

    private static bool Sobre(double ax, double ay, double bx, double by, double px, double py) =>
        px >= Math.Min(ax, bx) - 1e-9 && px <= Math.Max(ax, bx) + 1e-9 &&
        py >= Math.Min(ay, by) - 1e-9 && py <= Math.Max(ay, by) + 1e-9;
}
