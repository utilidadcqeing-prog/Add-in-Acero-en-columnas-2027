using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>
/// Junta en un <see cref="ConjuntoBarras"/> las piezas que Revit puede crear como un solo conjunto: la misma pieza del
/// tablero, en la misma capa y fila, del mismo diámetro, con las mismas puntas y los mismos ganchos. Revit reparte el
/// conjunto a lo ancho del corte, así que dos piezas en la misma x van en conjuntos distintos.
/// </summary>
public static class AgrupadorBarras
{
    /// <summary>Separación mínima en x, en mm, entre dos barras de un conjunto.</summary>
    private const double SeparacionMinima = 1;

    /// <summary>Los conjuntos salen en el orden de su primera pieza en <paramref name="piezas"/>.</summary>
    public static List<ConjuntoBarras> Agrupar(IEnumerable<PiezaLongitudinal> piezas)
    {
        var conjuntos = new List<ConjuntoBarras>();
        foreach (IGrouping<object, PiezaLongitudinal> iguales in piezas.GroupBy(p =>
                     (object)(p.Pieza, p.Capa, p.Fila, p.Diametro, p.Desde, p.Hasta, p.GanchoInicio, p.GanchoFin)))
        {
            var repartidos = new List<List<PiezaLongitudinal>>();
            foreach (PiezaLongitudinal p in iguales.OrderBy(p => p.XCorte))
            {
                List<PiezaLongitudinal>? donde = repartidos.FirstOrDefault(r => p.XCorte - r[^1].XCorte >= SeparacionMinima);
                if (donde is null)
                    repartidos.Add([p]);
                else
                    donde.Add(p);
            }
            conjuntos.AddRange(repartidos.Select(r => new ConjuntoBarras(r)));
        }
        return conjuntos;
    }
}
