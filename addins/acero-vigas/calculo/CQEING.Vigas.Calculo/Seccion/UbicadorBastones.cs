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
    public static UbicacionBastones Ubicar(EntradaSeccion e, Posicion cara, IReadOnlyList<GrupoBarras> bastones)
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
            if (!EnLaFila1(e, filas[0], numero) && !EnOtraFila(e, filas, puestosLlenos, numero))
                return new UbicacionBastones([], ErrorCara.NoCaben);

        return new UbicacionBastones(ConAltura(e, cara, filas), ErrorCara.Ninguno);
    }

    private static bool EnLaFila1(EntradaSeccion e, List<Puesto> fila, int numero)
    {
        double d = Diametro(e, numero), libre = e.Reglas.DistanciaLibre;
        List<Puesto> orden = fila.OrderBy(p => p.X).ToList();
        foreach (int i in DeLosExtremosHaciaAdentro(orden.Count - 1))
        {
            Puesto a = orden[i], b = orden[i + 1];
            double necesario = Diametro(e, a.Diametro) / 2 + libre + d + libre + Diametro(e, b.Diametro) / 2;
            if (b.X - a.X >= necesario - 1e-9)
            {
                fila.Add(new Puesto((a.X + b.X) / 2, numero, true));
                return true;
            }
        }
        return false;
    }

    private static bool EnOtraFila(EntradaSeccion e, List<List<Puesto>> filas, double[] puestos, int numero)
    {
        for (int k = 1; k < e.MaximoFilas; k++)
        {
            if (k == filas.Count)
                filas.Add([]);
            foreach (int i in DeLosExtremosHaciaAdentro(puestos.Length))
            {
                if (filas[k].Any(p => Math.Abs(p.X - puestos[i]) < 1e-6))
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
            borde += filas[k].Max(p => Diametro(e, p.Diametro)) + e.SeparacionFilas;
        }
        return resultado;
    }

    private static double Diametro(EntradaSeccion e, int numero) => RepartidorFilas.Diametro(e, numero);
}
