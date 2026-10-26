using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Seccion;

/// <summary>
/// Busca puesto en el corte a los bastones de una cara (#89, decisión 1 del plan
/// docs/planes/2026-10-16-bastones-vigas-89.md), sobre el reparto de las barras corridas de <see cref="RepartidorFilas"/>:
/// <list type="bullet">
/// <item>en la fila 1, el primer hueco entre dos barras donde quepa con la distancia libre a las dos, centrado en él;
/// los huecos se miran de los extremos hacia adentro, primero el izquierdo;</item>
/// <item>si no cabe, en la fila siguiente, en el primer puesto libre de la fila llena, de los extremos hacia adentro
/// (Carlos: "en el extremo, izquierda o derecha, según la disponibilidad").</item>
/// </list>
/// Los bastones ocupan puestos y cuentan para el máximo de filas.
/// </summary>
public static class UbicadorBastones
{
    private sealed record Puesto(double X, int Diametro, bool EsBaston);

    /// <param name="bastones">Los bastones que pasan por el corte en esa cara (cuáles son lo decide #91).</param>
    /// <param name="ocupadas">
    /// Barras que no son del reparto pero pasan por ahí: las que traslapan al lado o abajo de su puesto. Un bastón que se
    /// metería en una deja ese puesto prohibido y se vuelve a ubicar (Santiago, 24-oct: el bastón quedaba en el mismo
    /// punto de una barra). Si así ya no caben, quedan donde van sin ellas (mejor montado que sin crear): ver
    /// <see cref="SeMeten"/>.
    /// </param>
    public static UbicacionBastones Ubicar(EntradaSeccion e, Posicion cara, IReadOnlyList<GrupoBarras> bastones,
        IReadOnlyList<BarraEnSeccion>? ocupadas = null)
    {
        var prohibidos = new List<(int Fila, double X)>();
        UbicacionBastones primera = Ubicar(e, cara, bastones, prohibidos), u = primera;
        while (true)
        {
            if (u.Error != ErrorCara.Ninguno)
                return primera;
            List<BastonEnSeccion> metidos = u.Barras.Where(b => SeMeten(e, b.Barra, ocupadas ?? [])).ToList();
            if (metidos.Count == 0)
                return u;
            // Cada vuelta prohíbe al menos un puesto que antes no lo estaba: termina, con lugar o sin él.
            prohibidos.AddRange(metidos.Select(b => (b.Fila, b.Barra.X)));
            u = Ubicar(e, cara, bastones, prohibidos);
        }
    }

    /// <summary>Si la barra <paramref name="b"/> del corte se mete en alguna de las <paramref name="otras"/>.</summary>
    public static bool SeMeten(EntradaSeccion e, BarraEnSeccion b, IReadOnlyList<BarraEnSeccion> otras) =>
        otras.Any(o => Libre(e, b, o) < -Tolerancia);

    private const double Tolerancia = 1e-6;

    /// <summary>Lo que queda libre entre dos barras del corte, en mm (negativo si se meten una en la otra).</summary>
    private static double Libre(EntradaSeccion e, BarraEnSeccion a, BarraEnSeccion b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)) - (Diametro(e, a.Diametro) + Diametro(e, b.Diametro)) / 2;

    private static bool Prohibido(List<(int Fila, double X)> prohibidos, int fila, double x) =>
        prohibidos.Any(p => p.Fila == fila && Math.Abs(p.X - x) < 1e-6);

    private static UbicacionBastones Ubicar(EntradaSeccion e, Posicion cara, IReadOnlyList<GrupoBarras> bastones, List<(int Fila, double X)> prohibidos)
    {
        ResultadoSeccion seccion = RepartidorFilas.Repartir(e);
        CaraSeccion c = cara == Posicion.Superior ? seccion.Superior : seccion.Inferior;
        if (c.Error != ErrorCara.Ninguno)
            return new UbicacionBastones([], c.Error);

        IReadOnlyList<GrupoBarras> corridas = cara == Posicion.Superior ? e.Superior : e.Inferior;
        double estribo = Diametro(e, e.DiametroEstribo);
        double izquierda = e.Recubrimiento.Lados + estribo + Diametro(e, corridas[0].Diametro) / 2;
        double[] puestosLlenos = RepartidorFilas.Puestos(izquierda, e.Ancho - izquierda, c.CabenPorFila, e.Ancho);

        List<List<Puesto>> filas = c.Filas.Select(f => f.Select(b => new Puesto(b.X, b.Diametro, false)).ToList()).ToList();
        foreach (int numero in bastones.SelectMany(g => Enumerable.Repeat(g.Diametro, g.Cantidad)))
            if (!EnLaFila1(e, filas[0], numero, prohibidos) && !EnOtraFila(e, filas, puestosLlenos, numero, prohibidos))
                return new UbicacionBastones([], ErrorCara.NoCaben);

        List<BastonEnSeccion> ubicados = ConAltura(e, cara, filas);
        CaraSeccion otra = cara == Posicion.Superior ? seccion.Inferior : seccion.Superior;
        return ChocaConLaOtraCara(e, cara, ubicados, otra)
            ? new UbicacionBastones([], ErrorCara.NoCabenEnElAlto)
            : new UbicacionBastones(ubicados, ErrorCara.Ninguno);
    }

    /// <summary>Como en <see cref="RepartidorFilas"/>: entre las barras de las dos caras debe quedar la distancia libre.</summary>
    private static bool ChocaConLaOtraCara(EntradaSeccion e, Posicion cara, List<BastonEnSeccion> ubicados, CaraSeccion otra)
    {
        List<BarraEnSeccion> deEnfrente = otra.Filas.SelectMany(f => f).ToList();
        if (ubicados.Count == 0 || deEnfrente.Count == 0)
            return false;
        double Borde(BarraEnSeccion b, int signo) => b.Y + signo * Diametro(e, b.Diametro) / 2;
        double libre = cara == Posicion.Inferior
            ? deEnfrente.Min(b => Borde(b, -1)) - ubicados.Max(b => Borde(b.Barra, 1))
            : ubicados.Min(b => Borde(b.Barra, -1)) - deEnfrente.Max(b => Borde(b, 1));
        return libre < e.Reglas.DistanciaLibre;
    }

    private static bool EnLaFila1(EntradaSeccion e, List<Puesto> fila, int numero, List<(int Fila, double X)> prohibidos)
    {
        double d = Diametro(e, numero), libre = e.Reglas.DistanciaLibre;
        List<Puesto> orden = fila.OrderBy(p => p.X).ToList();
        foreach (int i in DeLosExtremosHaciaAdentro(orden.Count - 1))
        {
            Puesto a = orden[i], b = orden[i + 1];
            double necesario = Diametro(e, a.Diametro) / 2 + libre + d + libre + Diametro(e, b.Diametro) / 2;
            if (b.X - a.X >= necesario - 1e-9 && !Prohibido(prohibidos, 1, (a.X + b.X) / 2))
            {
                fila.Add(new Puesto((a.X + b.X) / 2, numero, true));
                return true;
            }
        }
        return false;
    }

    private static bool EnOtraFila(EntradaSeccion e, List<List<Puesto>> filas, double[] puestos, int numero, List<(int Fila, double X)> prohibidos)
    {
        for (int k = 1; k < e.MaximoFilas; k++)
        {
            if (k == filas.Count)
                filas.Add([]);
            foreach (int i in DeLosExtremosHaciaAdentro(puestos.Length))
            {
                if (filas[k].Any(p => Math.Abs(p.X - puestos[i]) < 1e-6) || Prohibido(prohibidos, k + 1, puestos[i]))
                    continue;
                filas[k].Add(new Puesto(puestos[i], numero, true));
                return true;
            }
        }
        return false;
    }

    /// <summary>0, n−1, 1, n−2…</summary>
    private static IEnumerable<int> DeLosExtremosHaciaAdentro(int n)
    {
        for (int i = 0, j = n - 1; i <= j; i++, j--)
        {
            yield return i;
            if (j != i)
                yield return j;
        }
    }

    /// <summary>
    /// La altura de cada bastón, igual que en <see cref="RepartidorFilas"/>: cada fila apoya en la anterior (o en el
    /// estribo) y su borde sube con el diámetro mayor de la fila más la separación entre filas.
    /// </summary>
    private static List<BastonEnSeccion> ConAltura(EntradaSeccion e, Posicion cara, List<List<Puesto>> filas)
    {
        double recubrimiento = cara == Posicion.Superior ? e.Recubrimiento.Arriba : e.Recubrimiento.Abajo;
        double borde = recubrimiento + Diametro(e, e.DiametroEstribo);
        var resultado = new List<BastonEnSeccion>();
        for (int k = 0; k < filas.Count; k++)
        {
            foreach (Puesto p in filas[k].Where(p => p.EsBaston).OrderBy(p => p.X))
            {
                double distancia = borde + Diametro(e, p.Diametro) / 2;
                double y = cara == Posicion.Inferior ? distancia : e.Alto - distancia;
                resultado.Add(new BastonEnSeccion(k + 1, new BarraEnSeccion(p.Diametro, p.X, y)));
            }
            // Una fila vacía es la de un puesto prohibido: la ocupa la barra que traslapa ahí, de la corrida (vigueta de un
            // solo puesto, #941). Sube como la fila 1.
            List<Puesto> ocupan = filas[k].Count > 0 ? filas[k] : filas[0];
            borde += ocupan.Max(p => Diametro(e, p.Diametro)) + e.SeparacionFilas
                + (k == 0 ? RepartidorFilas.EnLaCurva(e, filas[0].Where(p => !p.EsBaston).MinBy(p => p.X)!.Diametro) : 0);
        }
        return resultado;
    }

    private static double Diametro(EntradaSeccion e, int numero) => RepartidorFilas.Diametro(e, numero);
}
