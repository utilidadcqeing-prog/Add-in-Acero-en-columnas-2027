using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Seccion;

namespace CQEING.Vigas.Calculo.Estribos;

/// <summary>
/// Acomoda los estribos adicionales en el corte y mueve las barras que deben quedar alineadas (#90, decisión 7 del plan
/// docs/planes/2026-10-16-estribos-vigas-90.md):
/// <list type="bullet">
/// <item>cada vertical es una barra de arriba con una de abajo, emparejadas de izquierda a derecha; en cada una manda la
/// que se eligió primero y la otra se mueve a su x;</item>
/// <item>las elegidas, las esquinas de la fila 1 y las ya alineadas por otro adicional quedan fijas; las sueltas entre
/// dos fijas se quedan si cumplen la distancia libre y, si no, se reparten parejas entre esas dos;</item>
/// <item>si así tampoco caben, el adicional no se pone y nada se mueve.</item>
/// </list>
/// Parte del reparto de <see cref="RepartidorFilas"/> sin cambiarlo y devuelve otro con las x movidas.
/// </summary>
internal static class AlineadorEstribos
{
    private const double Tolerancia = 1e-9;

    /// <summary>Una fila del corte, de izquierda a derecha: la x y el diámetro real de cada barra, y cuáles ya alineó otro adicional.</summary>
    private sealed record Fila(double[] X, double[] Diametros, bool[] Alineada)
    {
        public Fila Copia() => new((double[])X.Clone(), Diametros, (bool[])Alineada.Clone());
    }

    /// <summary>Las filas de cada cara; la fila 1 es la primera.</summary>
    private sealed class Corte(Dictionary<Posicion, List<Fila>> filas)
    {
        public List<Fila> De(Posicion cara) => filas[cara];

        public Fila De(PuntoBarra b) => filas[b.Cara][b.Fila - 1];

        public double X(PuntoBarra b) => De(b).X[b.Numero - 1];

        public double Diametro(PuntoBarra b) => De(b).Diametros[b.Numero - 1];

        public bool Existe(PuntoBarra b) =>
            b.Fila >= 1 && b.Fila <= filas[b.Cara].Count && b.Numero >= 1 && b.Numero <= De(b).X.Length;

        /// <summary>Las barras de esquina de la fila 1, las que abraza el cerrado obligatorio.</summary>
        public bool EsEsquina(PuntoBarra b) => b.Fila == 1 && (b.Numero == 1 || b.Numero == De(b).X.Length);

        public Corte Copia() => new(filas.ToDictionary(par => par.Key, par => par.Value.Select(f => f.Copia()).ToList()));
    }

    internal static (ResultadoSeccion Seccion, IReadOnlyList<EstriboEnSeccion> Figuras) Alinear(
        EntradaSeccion e, IReadOnlyList<(string Id, EstriboAdicional Adicional)> adicionales, List<Aviso> avisos)
    {
        ResultadoSeccion seccion = RepartidorFilas.Repartir(e);
        if (seccion.Superior.Error != ErrorCara.Ninguno || seccion.Inferior.Error != ErrorCara.Ninguno)
        {
            avisos.AddRange(adicionales.Select(a => new Aviso(TipoAviso.NoSePuedeAlinear, a.Id)));
            return (seccion, []);
        }

        var corte = new Corte(new()
        {
            [Posicion.Superior] = Filas(e, seccion.Superior),
            [Posicion.Inferior] = Filas(e, seccion.Inferior),
        });
        var figuras = new List<EstriboEnSeccion>();
        foreach ((string id, EstriboAdicional adicional) in adicionales)
        {
            // Se prueba sobre una copia: si el adicional no se puede poner, nada se mueve.
            Corte prueba = corte.Copia();
            if (Poner(e, prueba, id, adicional, out EstriboEnSeccion? figura) is { } problema)
            {
                avisos.Add(new Aviso(problema, id));
                continue;
            }
            corte = prueba;
            figuras.Add(figura!);
        }
        return (new ResultadoSeccion(ConX(seccion.Superior, corte.De(Posicion.Superior)), ConX(seccion.Inferior, corte.De(Posicion.Inferior))), figuras);
    }

    /// <summary>Pone <paramref name="adicional"/> en <paramref name="corte"/>; devuelve el problema si no se puede.</summary>
    private static TipoAviso? Poner(EntradaSeccion e, Corte corte, string id, EstriboAdicional adicional, out EstriboEnSeccion? figura)
    {
        figura = null;
        List<PuntoBarra> elegidas = adicional.Barras.Distinct().ToList();
        if (!elegidas.All(corte.Existe))
            return TipoAviso.BarraNoExiste;

        int pide = adicional.Tipo == TipoEstribo.Cerrado ? 2 : 1;
        List<PuntoBarra> DeLaCara(Posicion cara) =>
            elegidas.Where(b => b.Cara == cara).OrderBy(corte.X).ThenBy(b => b.Fila).ToList();
        List<PuntoBarra> arriba = DeLaCara(Posicion.Superior), abajo = DeLaCara(Posicion.Inferior);
        if (arriba.Count != pide || abajo.Count != pide)
            return TipoAviso.EstriboIncompleto;

        var verticales = arriba.Zip(abajo, (s, i) => elegidas.IndexOf(s) < elegidas.IndexOf(i) ? (Manda: s, Otra: i) : (Manda: i, Otra: s)).ToList();
        bool EsRamaDelCerrado(PuntoBarra a, PuntoBarra b) =>
            corte.EsEsquina(a) && corte.EsEsquina(b) && (a.Numero == 1) == (b.Numero == 1);
        if (verticales.All(v => EsRamaDelCerrado(v.Manda, v.Otra)))
            return TipoAviso.IgualAlCerrado;

        // Todas las x de destino salen del corte antes de mover nada: la que manda en una vertical no se mueve por la otra.
        var destinos = elegidas.ToDictionary(b => b, corte.X);
        foreach ((PuntoBarra manda, PuntoBarra otra) in verticales)
        {
            double x = corte.X(manda);
            if (Math.Abs(corte.X(otra) - x) > Tolerancia)
            {
                if (corte.EsEsquina(otra))
                    return TipoAviso.MueveUnaEsquina;
                if (corte.De(otra).Alineada[otra.Numero - 1])
                    return TipoAviso.NoSePuedeAlinear;
            }
            destinos[otra] = x;
        }

        foreach (var enFila in destinos.GroupBy(d => (d.Key.Cara, d.Key.Fila)))
        {
            Fila fila1 = corte.De(enFila.Key.Cara)[0];
            Dictionary<int, double> fijas = enFila.ToDictionary(d => d.Key.Numero - 1, d => d.Value);
            if (!Acomodar(e, corte.De(enFila.First().Key), enFila.Key.Fila == 1, fijas, fila1.X[0], fila1.X[^1]))
                return TipoAviso.NoSePuedeAlinear;
        }
        foreach (PuntoBarra b in elegidas)
            corte.De(b).Alineada[b.Numero - 1] = true;

        figura = Figura(e, corte, id, adicional.Tipo, verticales.Select(v => v.Manda).ToList(), elegidas);
        return null;
    }

    /// <summary>
    /// Pone las barras de <paramref name="fijas"/> en su x y acomoda las sueltas: entre dos fijas (las elegidas, las ya
    /// alineadas y las esquinas de la fila 1) se quedan si cumplen la distancia libre y, si no, se reparten parejas.
    /// Fuera de la primera o la última fija (en las filas 2 y siguientes), el límite es la x de las esquinas de la fila 1.
    /// Devuelve false si así no queda la distancia libre entre todas.
    /// </summary>
    private static bool Acomodar(EntradaSeccion e, Fila fila, bool esFila1, Dictionary<int, double> fijas, double minimo, double maximo)
    {
        double[] x = (double[])fila.X.Clone();
        int n = x.Length;
        foreach ((int i, double destino) in fijas)
            x[i] = destino;
        List<int> anclas = Enumerable.Range(0, n)
            .Where(i => fijas.ContainsKey(i) || fila.Alineada[i] || esFila1 && (i == 0 || i == n - 1))
            .ToList();

        double Necesaria(int i) => (fila.Diametros[i] + fila.Diametros[i + 1]) / 2 + e.Reglas.DistanciaLibre;
        bool Cumple(int desde, int hasta) =>
            x[desde] >= minimo - Tolerancia && x[hasta] <= maximo + Tolerancia
            && Enumerable.Range(desde, hasta - desde).All(i => x[i + 1] - x[i] >= Necesaria(i) - Tolerancia);

        // Tramos entre anclas seguidas; -1 y n son los límites de la fila.
        List<int> cortes = [-1, .. anclas, n];
        for (int k = 0; k + 1 < cortes.Count; k++)
        {
            int a = cortes[k], b = cortes[k + 1], sueltas = b - a - 1;
            if (sueltas == 0 || Cumple(Math.Max(a, 0), Math.Min(b, n - 1)))
                continue;
            double izquierda = a < 0 ? minimo : x[a], derecha = b >= n ? maximo : x[b];
            // Con dos fijas, las sueltas quedan entre ellas; contra un límite, la del extremo va en el límite.
            int espacios = a >= 0 && b < n ? sueltas + 1 : sueltas;
            int primera = a < 0 ? 0 : 1;
            for (int t = 0; t < sueltas; t++)
                x[a + 1 + t] = izquierda + (primera + t) * (derecha - izquierda) / espacios;
        }
        if (!Cumple(0, n - 1))
            return false;
        Array.Copy(x, fila.X, n);
        return true;
    }

    /// <summary>
    /// El estribo por sus caras exteriores. El cerrado abraza por fuera las barras de sus dos verticales, con el diámetro
    /// mayor de las cuatro; el abierto es la rama en la x de su vertical. Los dos van de recubrimiento a recubrimiento.
    /// </summary>
    private static EstriboEnSeccion Figura(EntradaSeccion e, Corte corte, string id, TipoEstribo tipo, List<PuntoBarra> mandan, List<PuntoBarra> elegidas)
    {
        double abajo = e.Recubrimiento.Abajo, arriba = e.Alto - e.Recubrimiento.Arriba;
        if (tipo == TipoEstribo.Abierto)
            return new EstriboEnSeccion(id, tipo, corte.X(mandan[0]), corte.X(mandan[0]), abajo, arriba);
        double afuera = elegidas.Max(corte.Diametro) / 2 + RepartidorFilas.Diametro(e, e.DiametroEstribo);
        return new EstriboEnSeccion(id, tipo, corte.X(mandan[0]) - afuera, corte.X(mandan[1]) + afuera, abajo, arriba);
    }

    private static List<Fila> Filas(EntradaSeccion e, CaraSeccion c) =>
        c.Filas.Select(f => f.OrderBy(b => b.X).ToList())
            .Select(f => new Fila(f.Select(b => b.X).ToArray(), f.Select(b => RepartidorFilas.Diametro(e, b.Diametro)).ToArray(), new bool[f.Count]))
            .ToList();

    private static CaraSeccion ConX(CaraSeccion c, List<Fila> filas) => c with
    {
        Filas = c.Filas.Select((f, k) => (IReadOnlyList<BarraEnSeccion>)f.OrderBy(b => b.X).Select((b, i) => b with { X = filas[k].X[i] }).ToList()).ToList(),
    };
}
