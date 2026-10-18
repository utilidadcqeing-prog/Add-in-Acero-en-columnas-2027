namespace CQEING.Vigas.Calculo.Nucleo;

/// <summary>
/// Mínimo (y dónde está) de cualquier tramo de un arreglo, en tiempo constante después de prepararlo (tabla
/// dispersa). Ante empates gana el índice menor. <see cref="long.MaxValue"/> cuenta como "no se puede".
/// </summary>
internal sealed class MinimoPorRango
{
    private readonly long[] _valores;
    private readonly int _desde;
    private readonly List<int[]> _niveles = [];

    /// <summary>Prepara los índices [<paramref name="desde"/>, <paramref name="hasta"/>] de <paramref name="valores"/>.</summary>
    public MinimoPorRango(long[] valores, int desde, int hasta)
    {
        _valores = valores;
        _desde = desde;
        int largo = hasta - desde + 1;
        _niveles.Add(Enumerable.Range(desde, largo).ToArray());
        for (int ancho = 2; ancho <= largo; ancho *= 2)
        {
            int[] previo = _niveles[^1];
            var nivel = new int[largo - ancho + 1];
            for (int i = 0; i < nivel.Length; i++)
                nivel[i] = Menor(previo[i], previo[i + ancho / 2]);
            _niveles.Add(nivel);
        }
    }

    /// <summary>Mínimo de [<paramref name="i"/>, <paramref name="j"/>]; (MaxValue, -1) si está vacío o no hay ninguno.</summary>
    public (long Valor, int Indice) Minimo(int i, int j)
    {
        if (i > j)
            return (long.MaxValue, -1);
        int nivel = 31 - int.LeadingZeroCount(j - i + 1);
        int indice = Menor(_niveles[nivel][i - _desde], _niveles[nivel][j - _desde - (1 << nivel) + 1]);
        return _valores[indice] == long.MaxValue ? (long.MaxValue, -1) : (_valores[indice], indice);
    }

    private int Menor(int a, int b) => _valores[b] < _valores[a] ? b : a;
}
