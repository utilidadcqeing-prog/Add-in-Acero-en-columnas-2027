namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>Qué choca con qué, para la pista de cómo arreglarlo (decisión 4 del plan de choques de 22-oct).</summary>
public enum TipoChoque
{
    /// <summary>Un estribo o una grapa contra una barra o un bastón.</summary>
    EstriboConBarra,

    /// <summary>Un bastón contra una barra o contra otro bastón.</summary>
    BastonConBarra,

    EntreBarras,
    EntreEstribos,
}

/// <summary>Una de las dos piezas de un choque, como la dice su <see cref="Trazo"/>: el id del tablero, de qué es, el tramo del
/// estribo y el diámetro en octavos.</summary>
public sealed record PiezaChocada(string Pieza, TipoTrazo Tipo, string? Tramo, int Numero);

/// <summary>
/// Dos piezas que se meten una en la otra. <paramref name="A"/> es la que se mete (el estribo o la grapa antes que el
/// bastón y este antes que la barra) y <paramref name="B"/>, en la que se mete. <paramref name="Punto"/> es el medio del
/// tramo donde se meten, entre los dos ejes, en mm del modelo; <paramref name="SeMeteMm"/>, lo que más se meten; y
/// <paramref name="Veces"/>, cuántos estribos de la corrida chocan igual (1 en las piezas longitudinales).
/// </summary>
public sealed record Choque(Punto3 Punto, PiezaChocada A, PiezaChocada B, double SeMeteMm, int Veces)
{
    public TipoChoque Tipo => (EsEstribo(A.Tipo), EsEstribo(B.Tipo), A.Tipo) switch
    {
        (true, true, _) => TipoChoque.EntreEstribos,
        (true, false, _) => TipoChoque.EstriboConBarra,
        (_, _, TipoTrazo.Baston) => TipoChoque.BastonConBarra,
        _ => TipoChoque.EntreBarras,
    };

    internal static bool EsEstribo(TipoTrazo t) => t is TipoTrazo.Estribo or TipoTrazo.Grapa;
}

/// <summary>
/// Los puntos donde las barras se tocan (plan docs/planes/2026-10-22-choques-entre-barras-vigas.md), sobre los mismos
/// trazos de la vista previa 3D (<see cref="PreviaAcero.Trazos"/>), así que el tablero y el 3D marcan lo mismo.
/// Dos trazos chocan si sus ejes quedan a menos de la suma de sus radios menos <see cref="Holgura"/> (decisión 1): lo que
/// se toca a propósito, como el doblez que abraza su barra o el traslapo lado a lado, queda tangente y no cuenta.
/// </summary>
public static class DetectorChoques
{
    /// <summary>Lo que se pueden meter dos barras sin que cuente como choque, en mm: deja pasar los dobleces partidos en
    /// tramos rectos de <see cref="PreviaAcero"/>, cuya cuerda se mete unas décimas en la barra que abrazan.</summary>
    public const double Holgura = 0.5;

    /// <summary>Dos tramos donde se meten, a menos de esto a lo largo de la pieza (mm), son el mismo lugar.</summary>
    private const double MismoLugar = 1;

    public static IReadOnlyList<Choque> Detectar(IReadOnlyList<Trazo> trazos)
    {
        List<(Punto3 Min, Punto3 Max)> cajas = trazos.Select(Caja).ToList();
        var choques = new List<(Choque Choque, Trazo A)>();
        for (int i = 0; i < trazos.Count; i++)
            for (int j = i + 1; j < trazos.Count; j++)
            {
                if (!SeCruzan(cajas[i], cajas[j], 0))
                    continue;
                (Trazo a, Trazo b) = trazos[i].Tipo >= trazos[j].Tipo ? (trazos[i], trazos[j]) : (trazos[j], trazos[i]);
                choques.AddRange(Entre(a, b).Select(c => (c, a)));
            }
        return Juntar(choques);
    }

    /// <summary>Un choque por cada lugar donde <paramref name="a"/> se mete en <paramref name="b"/>.</summary>
    private static IEnumerable<Choque> Entre(Trazo a, Trazo b)
    {
        double limite = (a.Diametro + b.Diametro) / 2 - Holgura;
        // Los tramos de a donde se meten, por su largo desde el inicio de a, con la distancia mínima entre ejes.
        var tramos = new List<(double Desde, double Hasta, double Minima)>();
        double s = 0;
        for (int i = 0; i + 1 < a.Puntos.Count; i++)
        {
            (Punto3 p, Punto3 q) = (a.Puntos[i], a.Puntos[i + 1]);
            double largo = Distancia(p, q);
            for (int k = 0; k + 1 < b.Puntos.Count; k++)
            {
                (Punto3 c, Punto3 d) = (b.Puntos[k], b.Puntos[k + 1]);
                if (!SeCruzan(CajaDe(p, q), CajaDe(c, d), limite) || DistanciaEntreSegmentos(p, q, c, d) >= limite
                    || Dentro(p, q, c, d, limite) is not { } t)
                    continue;
                tramos.Add((s + t.Desde * largo, s + t.Hasta * largo, t.Minima));
            }
            s += largo;
        }

        foreach ((double desde, double hasta, double minima) in Lugares(tramos))
        {
            Punto3 enA = PuntoEn(a, (desde + hasta) / 2);
            Punto3 enB = Cercano(b, enA);
            var medio = new Punto3((enA.X + enB.X) / 2, (enA.Y + enB.Y) / 2, (enA.Z + enB.Z) / 2);
            yield return new Choque(medio, Pieza(a), Pieza(b), limite + Holgura - minima, 1);
        }
    }

    /// <summary>Junta los tramos que se tocan a lo largo de la pieza en un lugar, con la menor distancia de todos.</summary>
    private static IEnumerable<(double Desde, double Hasta, double Minima)> Lugares(List<(double Desde, double Hasta, double Minima)> tramos)
    {
        (double Desde, double Hasta, double Minima)? lugar = null;
        foreach (var t in tramos.OrderBy(t => t.Desde))
        {
            if (lugar is { } l && t.Desde <= l.Hasta + MismoLugar)
                lugar = (l.Desde, Math.Max(l.Hasta, t.Hasta), Math.Min(l.Minima, t.Minima));
            else
            {
                if (lugar is { } anterior)
                    yield return anterior;
                lugar = t;
            }
        }
        if (lugar is { } ultimo)
            yield return ultimo;
    }

    /// <summary>
    /// Los estribos de una corrida que chocan igual con la misma pieza se juntan en un punto, el del estribo del medio,
    /// con cuántos son (decisión 2). Chocan igual si se meten lo mismo y en el mismo lugar del estribo: los estribos de
    /// una corrida son el mismo lazo corrido, así que el choque queda a la misma distancia de su primer punto.
    /// </summary>
    private static List<Choque> Juntar(List<(Choque Choque, Trazo A)> choques) =>
        choques
            .GroupBy(x => Choque.EsEstribo(x.Choque.A.Tipo) && x.Choque.A.Tramo is not null
                ? (object)(x.Choque.A, x.Choque.B, Math.Round(x.Choque.SeMeteMm, 1), Desde(x.A.Puntos[0], x.Choque.Punto))
                : x)
            .Select(g => g.ElementAt(g.Count() / 2).Choque with { Veces = g.Count() })
            .ToList();

    private static (double, double, double) Desde(Punto3 origen, Punto3 p) =>
        (Math.Round(p.X - origen.X, 1), Math.Round(p.Y - origen.Y, 1), Math.Round(p.Z - origen.Z, 1));

    private static PiezaChocada Pieza(Trazo t) => new(t.Pieza, t.Tipo, t.Tramo, t.Numero);

    /// <summary>
    /// El tramo de pq (de 0 a 1) cuyos puntos quedan a menos de <paramref name="limite"/> del segmento cd, y la menor
    /// distancia. La distancia de un punto que corre en línea recta a un segmento es convexa: se busca su mínimo y desde
    /// él, hacia cada lado, dónde cruza el límite. Null si no se acercan tanto.
    /// </summary>
    private static (double Desde, double Hasta, double Minima)? Dentro(Punto3 p, Punto3 q, Punto3 c, Punto3 d, double limite)
    {
        double F(double t) => DistanciaAlSegmento(Mas(p, q, t), c, d);
        (double lo, double hi) = (0.0, 1.0);
        for (int i = 0; i < 60; i++)
        {
            double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
            if (F(m1) <= F(m2))
                hi = m2;
            else
                lo = m1;
        }
        double tMin = (lo + hi) / 2, minima = F(tMin);
        if (minima >= limite)
            return null;
        return (Borde(0), Borde(1), minima);

        // Dónde cruza el límite entre el mínimo y la punta (la punta misma si toda esa parte queda adentro).
        double Borde(double punta)
        {
            if (F(punta) < limite)
                return punta;
            (double adentro, double afuera) = (tMin, punta);
            for (int i = 0; i < 50; i++)
            {
                double m = (adentro + afuera) / 2;
                if (F(m) < limite)
                    adentro = m;
                else
                    afuera = m;
            }
            return (adentro + afuera) / 2;
        }
    }

    /// <summary>El punto a <paramref name="s"/> mm del inicio de la pieza, a lo largo de su eje.</summary>
    private static Punto3 PuntoEn(Trazo t, double s)
    {
        for (int i = 0; i + 1 < t.Puntos.Count; i++)
        {
            double largo = Distancia(t.Puntos[i], t.Puntos[i + 1]);
            if (s <= largo || i + 2 == t.Puntos.Count)
                return Mas(t.Puntos[i], t.Puntos[i + 1], largo > 0 ? Math.Clamp(s / largo, 0, 1) : 0);
            s -= largo;
        }
        return t.Puntos[0];
    }

    /// <summary>El punto del eje de <paramref name="t"/> más cerca de <paramref name="p"/>.</summary>
    private static Punto3 Cercano(Trazo t, Punto3 p) =>
        Enumerable.Range(0, Math.Max(1, t.Puntos.Count - 1))
            .Select(i => CercanoEnSegmento(p, t.Puntos[i], t.Puntos[Math.Min(i + 1, t.Puntos.Count - 1)]))
            .MinBy(c => Distancia(p, c));

    private static Punto3 CercanoEnSegmento(Punto3 p, Punto3 c, Punto3 d)
    {
        (double dx, double dy, double dz) = (d.X - c.X, d.Y - c.Y, d.Z - c.Z);
        double largo2 = dx * dx + dy * dy + dz * dz;
        double t = largo2 > 0 ? Math.Clamp(((p.X - c.X) * dx + (p.Y - c.Y) * dy + (p.Z - c.Z) * dz) / largo2, 0, 1) : 0;
        return Mas(c, d, t);
    }

    private static double DistanciaAlSegmento(Punto3 p, Punto3 c, Punto3 d) => Distancia(p, CercanoEnSegmento(p, c, d));

    /// <summary>
    /// La menor distancia entre los segmentos pq y cd, en forma cerrada (Ericson, <i>Real-Time Collision Detection</i>,
    /// 5.1.9): descarta rápido los pares que no se acercan antes de buscar el tramo con <see cref="Dentro"/>.
    /// </summary>
    private static double DistanciaEntreSegmentos(Punto3 p, Punto3 q, Punto3 c, Punto3 d)
    {
        (double X, double Y, double Z) d1 = (q.X - p.X, q.Y - p.Y, q.Z - p.Z), d2 = (d.X - c.X, d.Y - c.Y, d.Z - c.Z), r = (p.X - c.X, p.Y - c.Y, p.Z - c.Z);
        static double Punto((double X, double Y, double Z) u, (double X, double Y, double Z) v) => u.X * v.X + u.Y * v.Y + u.Z * v.Z;
        double a = Punto(d1, d1), e = Punto(d2, d2), f = Punto(d2, r);
        double s, t;
        if (a < 1e-12 && e < 1e-12)
            return Distancia(p, c);
        if (a < 1e-12)
            (s, t) = (0, Math.Clamp(f / e, 0, 1));
        else
        {
            double cc = Punto(d1, r);
            if (e < 1e-12)
                (s, t) = (Math.Clamp(-cc / a, 0, 1), 0);
            else
            {
                double b = Punto(d1, d2), den = a * e - b * b;
                s = den > 1e-12 ? Math.Clamp((b * f - cc * e) / den, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0)
                    (s, t) = (Math.Clamp(-cc / a, 0, 1), 0);
                else if (t > 1)
                    (s, t) = (Math.Clamp((b - cc) / a, 0, 1), 1);
            }
        }
        return Distancia(Mas(p, q, s), Mas(c, d, t));
    }

    private static Punto3 Mas(Punto3 p, Punto3 q, double t) => new(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t, p.Z + (q.Z - p.Z) * t);

    private static double Distancia(Punto3 a, Punto3 b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>La caja del eje ensanchada por el radio de la barra.</summary>
    private static (Punto3 Min, Punto3 Max) Caja(Trazo t)
    {
        double r = t.Diametro / 2;
        return (new Punto3(t.Puntos.Min(p => p.X) - r, t.Puntos.Min(p => p.Y) - r, t.Puntos.Min(p => p.Z) - r),
            new Punto3(t.Puntos.Max(p => p.X) + r, t.Puntos.Max(p => p.Y) + r, t.Puntos.Max(p => p.Z) + r));
    }

    private static (Punto3 Min, Punto3 Max) CajaDe(Punto3 p, Punto3 q) =>
        (new Punto3(Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), Math.Min(p.Z, q.Z)), new Punto3(Math.Max(p.X, q.X), Math.Max(p.Y, q.Y), Math.Max(p.Z, q.Z)));

    /// <summary>Si las cajas se cruzan, con una ensanchada en <paramref name="margen"/>.</summary>
    private static bool SeCruzan((Punto3 Min, Punto3 Max) a, (Punto3 Min, Punto3 Max) b, double margen) =>
        a.Min.X <= b.Max.X + margen && b.Min.X <= a.Max.X + margen
        && a.Min.Y <= b.Max.Y + margen && b.Min.Y <= a.Max.Y + margen
        && a.Min.Z <= b.Max.Z + margen && b.Min.Z <= a.Max.Z + margen;
}
