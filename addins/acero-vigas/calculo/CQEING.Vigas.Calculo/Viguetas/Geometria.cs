using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Viguetas;

/// <summary>Cuentas de polígonos en planta para los paneles (#949), en mm.</summary>
public static class Geometria
{
    public static double Distancia(Punto a, Punto b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Área con signo: positiva si el polígono va en sentido antihorario.</summary>
    public static double Area(IReadOnlyList<Punto> p)
    {
        double suma = 0;
        for (int i = 0; i < p.Count; i++)
        {
            Punto a = p[i], b = p[(i + 1) % p.Count];
            suma += a.X * b.Y - b.X * a.Y;
        }
        return suma / 2;
    }

    public static Punto Centro(IReadOnlyList<Punto> p) => new(p.Average(q => q.X), p.Average(q => q.Y));

    /// <summary>Si <paramref name="q"/> cae dentro del polígono (regla par-impar).</summary>
    public static bool Dentro(IReadOnlyList<Punto> p, Punto q)
    {
        bool dentro = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            if ((p[i].Y > q.Y) != (p[j].Y > q.Y) && q.X < (p[j].X - p[i].X) * (q.Y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X)
                dentro = !dentro;
        return dentro;
    }

    /// <summary>Quita los vértices que están en línea recta entre sus vecinos (los cruces en T de un lado).</summary>
    public static List<Punto> SinColineales(IReadOnlyList<Punto> p)
    {
        var r = new List<Punto>();
        for (int i = 0; i < p.Count; i++)
        {
            Punto a = p[(i - 1 + p.Count) % p.Count], b = p[i], c = p[(i + 1) % p.Count];
            if (Math.Abs(Cruz(b.X - a.X, b.Y - a.Y, c.X - b.X, c.Y - b.Y)) > 1e-6 * Distancia(a, b) * Distancia(b, c))
                r.Add(b);
        }
        return r;
    }

    /// <summary>
    /// Corre cada lado de un polígono antihorario hacia adentro (a su izquierda) lo que dice <paramref name="corrimiento"/>
    /// para ese lado, y une los lados corridos. Dos lados en línea con el mismo corrimiento no dejan vértice; con
    /// corrimientos distintos dejan un escalón.
    /// </summary>
    public static List<Punto> HaciaAdentro(IReadOnlyList<Punto> p, IReadOnlyList<double> corrimiento)
    {
        int n = p.Count;
        var r = new List<Punto>();
        for (int i = 0; i < n; i++)
        {
            int antes = (i - 1 + n) % n;
            (Punto a0, Punto a1) = Corrido(p[antes], p[i], corrimiento[antes]);
            (Punto b0, Punto b1) = Corrido(p[i], p[(i + 1) % n], corrimiento[i]);
            (double dx, double dy) = (a1.X - a0.X, a1.Y - a0.Y);
            (double ex, double ey) = (b1.X - b0.X, b1.Y - b0.Y);
            double det = Cruz(dx, dy, ex, ey);
            if (Math.Abs(det) > 1e-6 * Math.Sqrt((dx * dx + dy * dy) * (ex * ex + ey * ey)))
            {
                double t = Cruz(b0.X - a0.X, b0.Y - a0.Y, ex, ey) / det;
                r.Add(new Punto(a0.X + t * dx, a0.Y + t * dy));
            }
            else if (Math.Abs(corrimiento[antes] - corrimiento[i]) > 1)
            {
                r.Add(a1);
                r.Add(b0);
            }
        }
        return r;
    }

    /// <summary>
    /// Dónde corta la recta «coordenada <paramref name="enY"/> = <paramref name="cota"/>» al polígono: tramos [desde,
    /// hasta] sobre la otra coordenada, ordenados.
    /// </summary>
    public static List<(double Desde, double Hasta)> Cortes(IReadOnlyList<Punto> p, bool enY, double cota)
    {
        var w = new List<double>();
        for (int i = 0; i < p.Count; i++)
        {
            Punto a = p[i], b = p[(i + 1) % p.Count];
            (double ua, double ub) = enY ? (a.Y, b.Y) : (a.X, b.X);
            (double wa, double wb) = enY ? (a.X, b.X) : (a.Y, b.Y);
            if ((ua <= cota && cota < ub) || (ub <= cota && cota < ua))
                w.Add(wa + (cota - ua) / (ub - ua) * (wb - wa));
        }
        w.Sort();
        var tramos = new List<(double, double)>();
        for (int i = 0; i + 1 < w.Count; i += 2)
            tramos.Add((w[i], w[i + 1]));
        return tramos;
    }

    private static (Punto, Punto) Corrido(Punto a, Punto b, double d)
    {
        double largo = Distancia(a, b);
        (double nx, double ny) = (-(b.Y - a.Y) / largo * d, (b.X - a.X) / largo * d);
        return (new Punto(a.X + nx, a.Y + ny), new Punto(b.X + nx, b.Y + ny));
    }

    private static double Cruz(double ax, double ay, double bx, double by) => ax * by - ay * bx;
}
