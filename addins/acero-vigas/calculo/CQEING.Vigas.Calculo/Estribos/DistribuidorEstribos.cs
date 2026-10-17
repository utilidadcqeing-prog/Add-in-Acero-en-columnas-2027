using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Vigas;

namespace CQEING.Vigas.Calculo.Estribos;

/// <summary>
/// Reparte los estribos a lo largo del pórtico (#90, decisiones 2 a 4 del plan
/// docs/planes/2026-10-16-estribos-vigas-90.md):
/// <list type="bullet">
/// <item>el primero va a 5 cm de la cara del apoyo, medidos hasta la cara del estribo;</item>
/// <item>una zona junto a cada columna a la separación exacta y el resto repartido a la misma distancia, sin pasar de
/// la separación del centro. Las zonas "son las que están pegadas a las columnas" (Carlos, min 1:21:21): la luz lleva
/// dos y el voladizo una, junto a su columna;</item>
/// <item>si el tramo no alcanza para sus zonas (la luz corta, min 1:22:43), todo va como zona.</item>
/// </list>
/// </summary>
internal static class DistribuidorEstribos
{
    /// <summary>De la cara del apoyo (o de la punta del voladizo) a la cara del primer estribo, en mm.</summary>
    public const double DistanciaACara = 50;

    private const double Tolerancia = 1e-9;

    /// <summary>Los voladizos (<c>"Voladizo A"</c>, <c>"Voladizo D"</c>) y las luces (<c>"A-B"</c>), de izquierda a derecha.</summary>
    internal static IReadOnlyList<Tramo> Tramos(Portico p)
    {
        Apoyo primero = p.Apoyos[0], ultimo = p.Apoyos[^1];
        var tramos = new List<Tramo>();
        if (p.VoladizoInicio > 0)
            tramos.Add(new Tramo($"Voladizo {primero.Eje}", primero.CaraIzquierda - p.VoladizoInicio, primero.CaraIzquierda,
                TipoTramo.VoladizoInicial));
        tramos.AddRange(new GeometriaPortico(p, 0).Luces.Select(l => new Tramo(l.Nombre, l.CaraIzquierda, l.CaraDerecha, TipoTramo.Luz)));
        if (p.VoladizoFinal > 0)
            tramos.Add(new Tramo($"Voladizo {ultimo.Eje}", ultimo.CaraDerecha, ultimo.CaraDerecha + p.VoladizoFinal,
                TipoTramo.VoladizoFinal));
        return tramos;
    }

    /// <summary>El centro de cada estribo de <paramref name="t"/>, en mm desde el eje A y de izquierda a derecha.</summary>
    /// <param name="diametroEstribo">Diámetro real del estribo, en mm.</param>
    internal static IReadOnlyList<double> Posiciones(Tramo t, ConfigEstribos c, double diametroEstribo)
    {
        // Donde dos tramos se tocan (el último de una zona y el primero del centro) el estribo es uno solo.
        var posiciones = new List<double>();
        foreach (Segmento s in Segmentos(t, c, diametroEstribo))
            foreach (double x in EnSegmento(s))
                if (posiciones.Count == 0 || x - posiciones[^1] > Tolerancia)
                    posiciones.Add(x);
        return posiciones;
    }

    /// <summary>
    /// Las zonas de <paramref name="t"/> en mm, de izquierda a derecha, para el alzado del tablero (#83): las de
    /// confinamiento junto a las columnas (<see cref="ZonaTramo.Junta"/>) y el centro.
    /// </summary>
    internal static IReadOnlyList<ZonaTramo> Zonas(Tramo t, ConfigEstribos c, double diametroEstribo) =>
        Segmentos(t, c, diametroEstribo).Select(s => new ZonaTramo(s.Desde, s.Hasta, s.Junta)).ToList();

    /// <summary>
    /// Un trozo del tramo: una zona va a la separación exacta desde su columna (<paramref name="DesdeLaDerecha"/> si
    /// la columna está a la derecha); el centro se reparte a la misma distancia sin pasar de la separación.
    /// </summary>
    private sealed record Segmento(double Desde, double Hasta, double Separacion, bool Junta, bool Exacta, bool DesdeLaDerecha = false);

    private static IReadOnlyList<Segmento> Segmentos(Tramo t, ConfigEstribos c, double diametroEstribo)
    {
        double alPrimero = DistanciaACara + diametroEstribo / 2;
        double desde = t.CaraIzquierda + alPrimero, hasta = t.CaraDerecha - alPrimero;
        if (hasta - desde <= Tolerancia)
        {
            double centro = (t.CaraIzquierda + t.CaraDerecha) / 2;
            return [new Segmento(centro, centro, c.SeparacionZona, true, false)];
        }
        // La zona va junto a cada columna: en la luz, a los dos lados; en el voladizo, solo del lado de su apoyo.
        bool zonaIzquierda = t.Tipo != TipoTramo.VoladizoInicial, zonaDerecha = t.Tipo != TipoTramo.VoladizoFinal;
        int zonas = (zonaIzquierda ? 1 : 0) + (zonaDerecha ? 1 : 0);
        var todoComoZona = new Segmento(desde, hasta, c.SeparacionZona, true, false);
        if (t.CaraDerecha - t.CaraIzquierda < zonas * c.LargoZona)
            return [todoComoZona];

        int enZona = c.LargoZona < alPrimero ? 0 : (int)Math.Floor((c.LargoZona - alPrimero) / c.SeparacionZona + Tolerancia) + 1;
        if (enZona == 0)
            return [new Segmento(desde, hasta, c.SeparacionCentro, false, false)];

        int izquierda = zonaIzquierda ? enZona : 1, derecha = zonaDerecha ? enZona : 1;
        double finZonaIzquierda = desde + (izquierda - 1) * c.SeparacionZona;
        double finZonaDerecha = hasta - (derecha - 1) * c.SeparacionZona;
        // Si la zona llega hasta el último estribo (un voladizo apenas del largo de la zona), todo va como zona.
        if (finZonaDerecha - finZonaIzquierda < -Tolerancia)
            return [todoComoZona];

        var segmentos = new List<Segmento>();
        if (zonaIzquierda)
            segmentos.Add(new Segmento(desde, finZonaIzquierda, c.SeparacionZona, true, true));
        // El centro reparte entre el último de cada zona; si las dos zonas se tocan, no hay centro.
        if (finZonaDerecha - finZonaIzquierda > Tolerancia)
            segmentos.Add(new Segmento(finZonaIzquierda, finZonaDerecha, c.SeparacionCentro, false, false));
        if (zonaDerecha)
            segmentos.Add(new Segmento(finZonaDerecha, hasta, c.SeparacionZona, true, true, DesdeLaDerecha: true));
        return segmentos;
    }

    private static IEnumerable<double> EnSegmento(Segmento s)
    {
        if (!s.Exacta)
            return Repartir(s.Desde, s.Hasta, s.Separacion);
        int n = (int)Math.Round((s.Hasta - s.Desde) / s.Separacion) + 1;
        return Enumerable.Range(0, n).Select(i => s.DesdeLaDerecha ? s.Hasta - (n - 1 - i) * s.Separacion : s.Desde + i * s.Separacion);
    }

    /// <summary>De <paramref name="desde"/> a <paramref name="hasta"/>, a la misma distancia y sin pasar de <paramref name="separacion"/>.</summary>
    private static IReadOnlyList<double> Repartir(double desde, double hasta, double separacion)
    {
        double largo = hasta - desde;
        if (largo <= Tolerancia)
            return [desde];
        int espacios = (int)Math.Ceiling(largo / separacion - Tolerancia);
        return Enumerable.Range(0, espacios + 1).Select(i => desde + i * largo / espacios).ToList();
    }
}
