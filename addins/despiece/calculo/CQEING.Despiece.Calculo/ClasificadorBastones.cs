namespace CQEING.Despiece.Calculo;

/// <summary>
/// Reconoce un bastón por su altura: va más adentro que la capa exterior de su lado de la viga. La cantidad no sirve,
/// porque hay vigas con las barras corridas de una en una (plan del 31-oct).
/// </summary>
public static class ClasificadorBastones
{
    public static bool EsBaston(double altura, IReadOnlyCollection<double> alturas, double tolerancia)
    {
        double abajo = alturas.Min(), arriba = alturas.Max();
        double exterior = altura > (abajo + arriba) / 2 ? arriba : abajo;
        return Math.Abs(exterior - altura) > tolerancia;
    }
}
