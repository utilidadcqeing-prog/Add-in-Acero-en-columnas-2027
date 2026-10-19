using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>
/// Estribos iguales a separación constante, en mm desde el eje A: <paramref name="Primero"/> es el centro del primero y
/// <paramref name="Separacion"/>, 0 cuando hay uno solo.
/// </summary>
public sealed record Corrida(double Primero, int Cantidad, double Separacion);

/// <summary>
/// Parte las posiciones de los estribos de un tramo en corridas de separación constante: cada corrida es un conjunto de
/// Revit (decisión 10 del plan docs/planes/2026-10-19-generar-acero-vigas-85.md). En un tramo normal salen la zona
/// junto a cada columna y el centro.
/// </summary>
public static class CorridasEstribos
{
    /// <summary>
    /// Una corrida por zona del motor (<c>DistribuidorEstribos.Zonas</c>), como el rótulo del tablero: el estribo que
    /// comparten una zona y el centro es de la zona (hallazgo E6). Se reparten primero las zonas junto a las columnas.
    /// Si alguna posición queda fuera de las zonas, se vuelve a <see cref="Partir"/>, que no pierde ninguna.
    /// </summary>
    public static IReadOnlyList<Corrida> PorZonas(IReadOnlyList<double> posiciones, IReadOnlyList<ZonaTramo> zonas, double tolerancia = 1)
    {
        var libres = posiciones.ToList();
        var suyas = new List<double>[zonas.Count];
        foreach (int i in Enumerable.Range(0, zonas.Count).OrderByDescending(i => zonas[i].Junta))
        {
            suyas[i] = libres.Where(x => x >= zonas[i].Desde - tolerancia && x <= zonas[i].Hasta + tolerancia).ToList();
            suyas[i].ForEach(x => libres.Remove(x));
        }
        if (libres.Count > 0)
            return Partir(posiciones, tolerancia);
        return suyas.Where(s => s.Count > 0)
            .Select(s => new Corrida(s[0], s.Count, s.Count > 1 ? (s[^1] - s[0]) / (s.Count - 1) : 0))
            .ToList();
    }

    /// <param name="posiciones">De menor a mayor, como las da el motor de estribos.</param>
    /// <param name="tolerancia">Cuánto puede variar una separación dentro de la misma corrida, en mm.</param>
    public static IReadOnlyList<Corrida> Partir(IReadOnlyList<double> posiciones, double tolerancia = 1)
    {
        var corridas = new List<Corrida>();
        int i = 0;
        while (i < posiciones.Count)
        {
            int fin = i;
            if (i + 1 < posiciones.Count)
            {
                double separacion = posiciones[i + 1] - posiciones[i];
                fin = i + 1;
                while (fin + 1 < posiciones.Count && Math.Abs(posiciones[fin + 1] - posiciones[fin] - separacion) <= tolerancia)
                    fin++;
            }
            int cantidad = fin - i + 1;
            // La separación promedio: el ruido de decimales no se acumula a lo largo de la corrida.
            corridas.Add(new Corrida(posiciones[i], cantidad, cantidad > 1 ? (posiciones[fin] - posiciones[i]) / (cantidad - 1) : 0));
            i = fin + 1;
        }
        return corridas;
    }
}
