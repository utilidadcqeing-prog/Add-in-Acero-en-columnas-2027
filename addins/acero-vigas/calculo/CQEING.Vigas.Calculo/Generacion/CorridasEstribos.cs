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
