namespace CQEING.Despiece.Calculo;

/// <summary>Un tramo de la cadena de cotas: el ancho de una columna, o la luz entre dos.</summary>
public readonly record struct TramoViga(double Desde, double Hasta, bool EsColumna);

/// <summary>Reparte una viga en columnas y luces, medidas a lo largo de la viga.</summary>
public static class TramosViga
{
    private const double Minimo = 1e-6;

    /// <summary>
    /// Devuelve los tramos en orden y sin huecos. Las columnas que se tocan cuentan como una, y las que no alcanzan la
    /// viga no cuentan. Una columna de borde puede sobresalir de la viga: se mide entera.
    /// </summary>
    public static IReadOnlyList<TramoViga> Calcular(double inicio, double fin, IEnumerable<(double Desde, double Hasta)> columnas)
    {
        var unidas = new List<(double Desde, double Hasta)>();
        foreach (var c in columnas.Where(c => c.Hasta > inicio && c.Desde < fin).OrderBy(c => c.Desde))
        {
            if (unidas.Count > 0 && c.Desde <= unidas[^1].Hasta)
                unidas[^1] = (unidas[^1].Desde, Math.Max(unidas[^1].Hasta, c.Hasta));
            else
                unidas.Add(c);
        }

        var tramos = new List<TramoViga>();
        double punto = inicio;
        foreach (var c in unidas)
        {
            if (c.Desde - punto > Minimo)
                tramos.Add(new TramoViga(punto, c.Desde, false));
            tramos.Add(new TramoViga(c.Desde, c.Hasta, true));
            punto = c.Hasta;
        }
        if (fin - punto > Minimo)
            tramos.Add(new TramoViga(punto, fin, false));
        return tramos;
    }
}
