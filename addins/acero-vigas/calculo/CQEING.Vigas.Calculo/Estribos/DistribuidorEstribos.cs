using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Vigas;

namespace CQEING.Vigas.Calculo.Estribos;

/// <summary>
/// Reparte los estribos a lo largo del pórtico (#90, decisiones 2 a 4 del plan
/// docs/planes/2026-10-16-estribos-vigas-90.md):
/// <list type="bullet">
/// <item>el primero va a 5 cm de la cara del apoyo, medidos hasta la cara del estribo;</item>
/// <item>en cada luz, una zona junto a cada columna a la separación exacta y el centro repartido a la misma distancia,
/// sin pasar de la separación del centro;</item>
/// <item>la luz corta (menor que dos zonas) y el voladizo van todos con la separación de la zona.</item>
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
            tramos.Add(new Tramo($"Voladizo {primero.Eje}", primero.CaraIzquierda - p.VoladizoInicio, primero.CaraIzquierda, true));
        tramos.AddRange(new GeometriaPortico(p, 0).Luces.Select(l => new Tramo(l.Nombre, l.CaraIzquierda, l.CaraDerecha, false)));
        if (p.VoladizoFinal > 0)
            tramos.Add(new Tramo($"Voladizo {ultimo.Eje}", ultimo.CaraDerecha, ultimo.CaraDerecha + p.VoladizoFinal, true));
        return tramos;
    }

    /// <summary>El centro de cada estribo de <paramref name="t"/>, en mm desde el eje A y de izquierda a derecha.</summary>
    /// <param name="diametroEstribo">Diámetro real del estribo, en mm.</param>
    internal static IReadOnlyList<double> Posiciones(Tramo t, ConfigEstribos c, double diametroEstribo)
    {
        double alPrimero = DistanciaACara + diametroEstribo / 2;
        double desde = t.CaraIzquierda + alPrimero, hasta = t.CaraDerecha - alPrimero;
        if (hasta - desde <= Tolerancia)
            return [(t.CaraIzquierda + t.CaraDerecha) / 2];
        if (t.EsVoladizo || t.CaraDerecha - t.CaraIzquierda < 2 * c.LargoZona)
            return Repartir(desde, hasta, c.SeparacionZona);

        int enZona = c.LargoZona < alPrimero ? 0 : (int)Math.Floor((c.LargoZona - alPrimero) / c.SeparacionZona + Tolerancia) + 1;
        if (enZona == 0)
            return Repartir(desde, hasta, c.SeparacionCentro);

        double finZonaIzquierda = desde + (enZona - 1) * c.SeparacionZona;
        double finZonaDerecha = hasta - (enZona - 1) * c.SeparacionZona;
        List<double> posiciones = Enumerable.Range(0, enZona).Select(i => desde + i * c.SeparacionZona).ToList();
        IEnumerable<double> zonaDerecha = Enumerable.Range(0, enZona).Reverse().Select(i => hasta - i * c.SeparacionZona);
        // El centro reparte entre el último de cada zona; si las dos zonas se tocan, ese estribo es uno solo.
        if (finZonaDerecha - finZonaIzquierda > Tolerancia)
            posiciones.AddRange(Repartir(finZonaIzquierda, finZonaDerecha, c.SeparacionCentro).Skip(1).SkipLast(1));
        else
            zonaDerecha = zonaDerecha.Skip(1);
        posiciones.AddRange(zonaDerecha);
        return posiciones;
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
