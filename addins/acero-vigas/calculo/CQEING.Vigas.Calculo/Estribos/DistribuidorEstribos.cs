using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Vigas;

namespace CQEING.Vigas.Calculo.Estribos;

/// <summary>
/// Reparte los estribos a lo largo del pórtico (#90, decisiones 2 a 4 del plan
/// docs/planes/2026-10-16-estribos-vigas-90.md):
/// <list type="bullet">
/// <item>el primero va a 5 cm de la cara del apoyo, medidos hasta la cara del estribo;</item>
/// <item>una zona junto a cada columna a la separación exacta y el resto también a su separación exacta: lo que sobra
/// queda en un último espacio más corto, nunca más largo (Santiago, 30-oct, min 34:32: pedido cada 20, salía cada 19).
/// Las zonas "son las que están pegadas a las columnas" (Carlos, min 1:21:21): la luz lleva
/// dos; el voladizo va todo como zona (Carlos, 21-oct, min 15:14);</item>
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
    /// confinamiento junto a las columnas (<see cref="ZonaTramo.Junta"/>) y el centro, con lo que dice su rótulo
    /// (#100): cuántos estribos, cada cuánto y hasta dónde llega la zona desde la cara.
    /// </summary>
    internal static IReadOnlyList<ZonaTramo> Zonas(Tramo t, ConfigEstribos c, double diametroEstribo)
    {
        IReadOnlyList<Segmento> segmentos = Segmentos(t, c, diametroEstribo);
        IReadOnlyList<double> posiciones = Posiciones(t, c, diametroEstribo);
        bool Dentro(double x, Segmento s) => x >= s.Desde - ToleranciaMm && x <= s.Hasta + ToleranciaMm;
        // El estribo que comparten una zona y el centro es de la zona: se cuentan primero las zonas.
        var libres = posiciones.ToList();
        var cantidades = new int[segmentos.Count];
        foreach (int i in Enumerable.Range(0, segmentos.Count).OrderByDescending(i => segmentos[i].Junta))
        {
            List<double> suyos = libres.Where(x => Dentro(x, segmentos[i])).ToList();
            suyos.ForEach(x => libres.Remove(x));
            cantidades[i] = suyos.Count;
        }
        return segmentos.Select((s, i) =>
        {
            double separacion = s.Exacta || posiciones.Count(x => Dentro(x, s)) > 1 ? s.Separacion : 0;
            double? largo = s.Junta && s.Exacta ? (s.DesdeLaDerecha ? t.CaraDerecha - s.Desde : s.Hasta - t.CaraIzquierda) : null;
            return new ZonaTramo(s.Desde, s.Hasta, s.Junta, cantidades[i], separacion, largo);
        }).ToList();
    }

    /// <summary>Para comparar posiciones en mm que salen de sumas con decimales.</summary>
    private const double ToleranciaMm = 1e-6;

    /// <summary>
    /// Un trozo del tramo: una zona va a la separación exacta desde su columna (<paramref name="DesdeLaDerecha"/> si
    /// la columna está a la derecha); los demás van a la separación y terminan en el espacio que sobra.
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
        // En el voladizo inicial la columna está a la derecha: lo que sobra queda hacia la punta.
        var todoComoZona = new Segmento(desde, hasta, c.SeparacionZona, true, false, DesdeLaDerecha: t.Tipo == TipoTramo.VoladizoInicial);
        // En voladizo "no existe el confinamiento y el no confinamiento… siempre va a existir el confinamiento"
        // (Carlos, 21-oct, min 15:14).
        if (t.Tipo != TipoTramo.Luz || t.CaraDerecha - t.CaraIzquierda < 2 * c.LargoZona)
            return [todoComoZona];

        int enZona = c.LargoZona < alPrimero ? 0 : (int)Math.Floor((c.LargoZona - alPrimero) / c.SeparacionZona + Tolerancia) + 1;
        if (enZona == 0)
            return [new Segmento(desde, hasta, c.SeparacionCentro, false, false)];

        double finZonaIzquierda = desde + (enZona - 1) * c.SeparacionZona;
        double finZonaDerecha = hasta - (enZona - 1) * c.SeparacionZona;
        // El centro reparte entre el último de cada zona; si las dos zonas se tocan, no hay centro.
        var segmentos = new List<Segmento> { new(desde, finZonaIzquierda, c.SeparacionZona, true, true) };
        if (finZonaDerecha - finZonaIzquierda > Tolerancia)
            segmentos.Add(new Segmento(finZonaIzquierda, finZonaDerecha, c.SeparacionCentro, false, false));
        segmentos.Add(new Segmento(finZonaDerecha, hasta, c.SeparacionZona, true, true, DesdeLaDerecha: true));
        return segmentos;
    }

    private static IEnumerable<double> EnSegmento(Segmento s)
    {
        if (!s.Exacta)
            return Repartir(s.Desde, s.Hasta, s.Separacion, s.DesdeLaDerecha);
        int n = (int)Math.Round((s.Hasta - s.Desde) / s.Separacion) + 1;
        return Enumerable.Range(0, n).Select(i => s.DesdeLaDerecha ? s.Hasta - (n - 1 - i) * s.Separacion : s.Desde + i * s.Separacion);
    }

    /// <summary>
    /// De <paramref name="desde"/> a <paramref name="hasta"/> cada <paramref name="separacion"/>, con lo que sobra en el
    /// último espacio. Si sobra menos de <see cref="EspacioMinimo"/>, los dos últimos espacios se igualan para que no
    /// queden dos estribos casi juntos: ninguno pasa de la separación.
    /// </summary>
    private static IReadOnlyList<double> Repartir(double desde, double hasta, double separacion, bool desdeLaDerecha)
    {
        double largo = hasta - desde;
        if (largo <= Tolerancia)
            return [desde];
        int espacios = (int)Math.Ceiling(largo / separacion - Tolerancia);
        List<double> distancias = Enumerable.Range(0, espacios).Select(i => i * separacion).ToList();
        double sobra = largo - (espacios - 1) * separacion;
        if (espacios > 1 && sobra < EspacioMinimo)
            distancias[^1] = largo - (separacion + sobra) / 2;
        distancias.Add(largo);
        return desdeLaDerecha ? distancias.Select(x => hasta - x).Reverse().ToList() : distancias.Select(x => desde + x).ToList();
    }

    /// <summary>El espacio más corto que se deja entre dos estribos del reparto, en mm.</summary>
    private const double EspacioMinimo = 50;
}
