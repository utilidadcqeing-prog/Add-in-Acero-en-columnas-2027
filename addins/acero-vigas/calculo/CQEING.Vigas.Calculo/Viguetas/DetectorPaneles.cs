using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Viguetas;

/// <summary>Una viga que cierra paneles: su eje en planta y su ancho, en mm.</summary>
public sealed record BordePanel(string Id, Punto Desde, Punto Hasta, double Ancho);

/// <summary>
/// Un panel de losa: el espacio cerrado por vigas. <paramref name="Eje"/> es el contorno por los ejes de las vigas y
/// <paramref name="Libre"/>, el contorno entre caras (medio ancho de viga hacia adentro). Los dos en sentido
/// antihorario, en mm.
/// </summary>
public sealed record Panel(string Id, IReadOnlyList<Punto> Eje, IReadOnlyList<Punto> Libre)
{
    public double AreaLibre => Geometria.Area(Libre);
}

/// <summary>
/// Arma los paneles de un piso con los ejes de sus vigas (#949): parte cada eje donde se cruza con otro, descarta los
/// tramos sueltos (voladizos) y recorre las caras del dibujo que quedan. Cada cara cerrada es un panel.
/// </summary>
public static class DetectorPaneles
{
    /// <summary>Distancia (mm) a la que dos puntos se toman como el mismo nudo.</summary>
    public const double Tolerancia = 20;

    /// <summary>
    /// Cuánto (mm) puede quedarse corta una viga antes de cruzar a otra y aun así tomarse como unida: en los modelos las
    /// vigas a veces terminan en la cara de la columna y no en el eje.
    /// </summary>
    public const double Holgura = 300;

    /// <summary>Paneles más chicos que esto (mm², 0,25 m²) no son paneles: quedan entre vigas casi pegadas.</summary>
    public const double AreaMinima = 250_000;

    public static List<Panel> Detectar(IEnumerable<BordePanel> bordes)
    {
        List<BordePanel> lista = bordes.Where(b => Geometria.Distancia(b.Desde, b.Hasta) > Tolerancia).ToList();
        var nudos = new List<Punto>();
        var aristas = new Dictionary<(int, int), double>();

        int Nudo(Punto p)
        {
            for (int i = 0; i < nudos.Count; i++)
                if (Geometria.Distancia(nudos[i], p) <= Tolerancia)
                    return i;
            nudos.Add(p);
            return nudos.Count - 1;
        }

        for (int i = 0; i < lista.Count; i++)
        {
            BordePanel b = lista[i];
            double largo = Geometria.Distancia(b.Desde, b.Hasta);
            var cortes = new List<(double S, Punto P)> { (0, b.Desde), (largo, b.Hasta) };
            for (int j = 0; j < lista.Count; j++)
                if (j != i && Cruce(b, lista[j]) is (double s, Punto p))
                    cortes.Add((s, p));
            // De punta a punta, con lo que la viga se estira para llegar a otra (Holgura).
            List<int> puntos = cortes.OrderBy(c => c.S).Select(c => Nudo(c.P)).ToList();
            for (int k = 0; k + 1 < puntos.Count; k++)
            {
                (int a, int c) = (Math.Min(puntos[k], puntos[k + 1]), Math.Max(puntos[k], puntos[k + 1]));
                if (a != c)
                    aristas[(a, c)] = Math.Max(aristas.GetValueOrDefault((a, c)), b.Ancho);
            }
        }

        QuitarSueltas(aristas);
        return Caras(nudos, aristas);
    }

    /// <summary>Dónde cruza <paramref name="b"/> a <paramref name="otra"/>: distancia a lo largo de b y el punto.</summary>
    private static (double, Punto)? Cruce(BordePanel b, BordePanel otra)
    {
        (double dx, double dy) = (b.Hasta.X - b.Desde.X, b.Hasta.Y - b.Desde.Y);
        (double ex, double ey) = (otra.Hasta.X - otra.Desde.X, otra.Hasta.Y - otra.Desde.Y);
        double det = dx * ey - dy * ex;
        double la = Math.Sqrt(dx * dx + dy * dy), lb = Math.Sqrt(ex * ex + ey * ey);
        if (Math.Abs(det) < 1e-6 * la * lb)
            return null;
        (double fx, double fy) = (otra.Desde.X - b.Desde.X, otra.Desde.Y - b.Desde.Y);
        double t = (fx * ey - fy * ex) / det, u = (fx * dy - fy * dx) / det;
        double s = t * la, r = u * lb;
        if (s < -Holgura || s > la + Holgura || r < -Holgura || r > lb + Holgura)
            return null;
        return (s, new Punto(b.Desde.X + t * dx, b.Desde.Y + t * dy));
    }

    /// <summary>Saca, una y otra vez, las aristas con una punta suelta: voladizos y vigas que no cierran nada.</summary>
    private static void QuitarSueltas(Dictionary<(int, int), double> aristas)
    {
        while (true)
        {
            var grado = new Dictionary<int, int>();
            foreach ((int a, int b) in aristas.Keys)
            {
                grado[a] = grado.GetValueOrDefault(a) + 1;
                grado[b] = grado.GetValueOrDefault(b) + 1;
            }
            List<(int, int)> sueltas = aristas.Keys.Where(k => grado[k.Item1] == 1 || grado[k.Item2] == 1).ToList();
            if (sueltas.Count == 0)
                return;
            foreach ((int, int) k in sueltas)
                aristas.Remove(k);
        }
    }

    /// <summary>
    /// Recorre cada arista en los dos sentidos girando siempre lo menos posible a la derecha: así cada vuelta encierra
    /// una cara con el interior a la izquierda. Las caras antihorarias son paneles; la horaria es el contorno de afuera.
    /// </summary>
    private static List<Panel> Caras(List<Punto> nudos, Dictionary<(int, int), double> aristas)
    {
        var vecinos = new Dictionary<int, List<int>>();
        foreach ((int a, int b) in aristas.Keys)
        {
            (vecinos.TryGetValue(a, out var la) ? la : vecinos[a] = []).Add(b);
            (vecinos.TryGetValue(b, out var lb) ? lb : vecinos[b] = []).Add(a);
        }
        foreach ((int n, List<int> l) in vecinos)
            l.Sort((p, q) => Angulo(nudos[n], nudos[p]).CompareTo(Angulo(nudos[n], nudos[q])));

        double Ancho(int a, int b) => aristas[(Math.Min(a, b), Math.Max(a, b))];
        var usadas = new HashSet<(int, int)>();
        var paneles = new List<(List<Punto> Eje, List<Punto> Libre)>();
        foreach ((int a, int b) in aristas.Keys.SelectMany(k => new[] { k, (k.Item2, k.Item1) }))
        {
            if (usadas.Contains((a, b)))
                continue;
            var cara = new List<int>();
            (int u, int v) = (a, b);
            for (int pasos = 0; pasos <= aristas.Count * 2 && usadas.Add((u, v)); pasos++)
            {
                cara.Add(u);
                List<int> l = vecinos[v];
                int w = l[(l.IndexOf(u) - 1 + l.Count) % l.Count];
                (u, v) = (v, w);
            }
            List<Punto> eje = cara.Select(i => nudos[i]).ToList();
            if (cara.Count < 3 || Geometria.Area(eje) <= 0)
                continue;
            List<double> anchos = cara.Select((n, i) => Ancho(n, cara[(i + 1) % cara.Count])).ToList();
            List<Punto> libre = Geometria.HaciaAdentro(eje, anchos.Select(x => x / 2).ToList());
            if (Geometria.Area(libre) >= AreaMinima)
                paneles.Add((Geometria.SinColineales(eje), libre));
        }

        // Numerados como se leen en la planta: de arriba hacia abajo y de izquierda a derecha.
        return paneles
            .OrderByDescending(p => Math.Round(Geometria.Centro(p.Libre).Y / 500))
            .ThenBy(p => Geometria.Centro(p.Libre).X)
            .Select((p, i) => new Panel($"P{i + 1}", p.Eje, p.Libre))
            .ToList();
    }

    private static double Angulo(Punto desde, Punto hacia) => Math.Atan2(hacia.Y - desde.Y, hacia.X - desde.X);
}
