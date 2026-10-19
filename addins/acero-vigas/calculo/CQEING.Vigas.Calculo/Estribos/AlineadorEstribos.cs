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
/// Hace lo mismo que la maqueta (corte 1-1). Parte del reparto de <see cref="RepartidorFilas"/> sin cambiarlo y devuelve
/// otro con las x movidas; cada fila conserva el orden del reparto, así que <see cref="PuntoBarra.Numero"/> sigue
/// nombrando la misma barra aunque se mueva.
/// </summary>
internal static class AlineadorEstribos
{
    private const double Tolerancia = 1e-9;

    /// <summary>
    /// Una fila del corte, en el orden del reparto: la x, la y y el diámetro real de cada barra, y cuáles ya alineó otro
    /// adicional.
    /// </summary>
    private sealed record Fila(double[] X, double[] Y, double[] Diametros, bool[] Alineada)
    {
        public Fila Copia() => new((double[])X.Clone(), Y, Diametros, (bool[])Alineada.Clone());
    }

    /// <summary>Las filas de cada cara; la fila 1 es la primera.</summary>
    private sealed class Corte(Dictionary<Posicion, List<Fila>> filas)
    {
        public List<Fila> De(Posicion cara) => filas[cara];

        public Fila De(PuntoBarra b) => filas[b.Cara][b.Fila - 1];

        public double X(PuntoBarra b) => De(b).X[b.Numero - 1];

        public double Y(PuntoBarra b) => De(b).Y[b.Numero - 1];

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
        // Las ramas (barra de arriba, barra de abajo) de los adicionales ya puestos (hallazgo E14).
        var ramas = new HashSet<(PuntoBarra Arriba, PuntoBarra Abajo)>();
        foreach ((string id, EstriboAdicional adicional) in adicionales)
        {
            // Se prueba sobre una copia: si el adicional no se puede poner, nada se mueve.
            Corte prueba = corte.Copia();
            if (Poner(e, prueba, id, adicional, ramas, out EstriboEnSeccion? figura, out var suyas) is { } problema)
            {
                avisos.Add(new Aviso(problema, id));
                continue;
            }
            corte = prueba;
            figuras.Add(figura!);
            ramas.UnionWith(suyas);
        }
        return (new ResultadoSeccion(ConX(seccion.Superior, corte.De(Posicion.Superior)), ConX(seccion.Inferior, corte.De(Posicion.Inferior))), figuras);
    }

    /// <summary>
    /// Pone <paramref name="adicional"/> en <paramref name="corte"/>; devuelve el problema si no se puede.
    /// <paramref name="ocupadas"/> son las ramas de los adicionales ya puestos; <paramref name="suyas"/>, las de este.
    /// </summary>
    private static TipoAviso? Poner(EntradaSeccion e, Corte corte, string id, EstriboAdicional adicional,
        IReadOnlySet<(PuntoBarra Arriba, PuntoBarra Abajo)> ocupadas, out EstriboEnSeccion? figura, out List<(PuntoBarra Arriba, PuntoBarra Abajo)> suyas)
    {
        figura = null;
        suyas = [];
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
        // Un abierto en la rama de otro adicional, o un cerrado con sus dos ramas ya puestas, abraza barras que ya están
        // abrazadas (hallazgo E14).
        List<(PuntoBarra Arriba, PuntoBarra Abajo)> propias = arriba.Zip(abajo).ToList();
        if (propias.All(ocupadas.Contains))
            return TipoAviso.RepiteOtroAdicional;

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
            Dictionary<int, double> fijas = enFila.ToDictionary(d => d.Key.Numero - 1, d => d.Value);
            if (!Acomodar(e, corte.De(enFila.First().Key), enFila.Key.Fila == 1, fijas))
                return TipoAviso.NoSePuedeAlinear;
        }
        foreach (PuntoBarra b in elegidas)
            corte.De(b).Alineada[b.Numero - 1] = true;

        figura = Figura(e, corte, id, adicional.Tipo, verticales.Select(v => v.Manda).ToList(), elegidas);
        suyas = propias;
        return null;
    }

    /// <summary>
    /// Pone las barras de <paramref name="fijas"/> en su x y acomoda las sueltas: entre dos fijas seguidas (las elegidas,
    /// las ya alineadas y las esquinas de la fila 1) se quedan si cumplen la distancia libre y, si no, se reparten parejas
    /// entre esas dos. Las de la punta de las filas 2 y siguientes, que no tienen esquinas fijas, se quedan donde están.
    /// Devuelve false si así no queda la distancia libre entre todas, de izquierda a derecha.
    /// </summary>
    private static bool Acomodar(EntradaSeccion e, Fila fila, bool esFila1, Dictionary<int, double> fijas)
    {
        double[] x = (double[])fila.X.Clone();
        int n = x.Length;
        foreach ((int i, double destino) in fijas)
            x[i] = destino;
        List<int> anclas = Enumerable.Range(0, n)
            .Where(i => fijas.ContainsKey(i) || fila.Alineada[i] || esFila1 && (i == 0 || i == n - 1))
            .ToList();

        bool Libre(int a, int b) => x[b] - x[a] >= (fila.Diametros[a] + fila.Diametros[b]) / 2 + e.Reglas.DistanciaLibre - Tolerancia;
        foreach ((int a, int b) in anclas.Zip(anclas.Skip(1)))
        {
            if (b - a < 2 || Enumerable.Range(a, b - a).All(i => Libre(i, i + 1)))
                continue;
            for (int i = a + 1; i < b; i++)
                x[i] = x[a] + (x[b] - x[a]) * (i - a) / (b - a);
        }

        int[] deIzquierdaADerecha = Enumerable.Range(0, n).OrderBy(i => x[i]).ToArray();
        if (!deIzquierdaADerecha.Zip(deIzquierdaADerecha.Skip(1)).All(par => Libre(par.First, par.Second)))
            return false;
        Array.Copy(x, fila.X, n);
        return true;
    }

    /// <summary>
    /// El estribo por sus caras exteriores. El cerrado abraza por fuera las barras de sus dos verticales, con el diámetro
    /// mayor de las cuatro; el abierto es la rama en la x de su vertical. Como en la maqueta, de arriba abajo van pegados
    /// a las barras que abrazan (en la fila 1, de recubrimiento a recubrimiento).
    /// </summary>
    private static EstriboEnSeccion Figura(EntradaSeccion e, Corte corte, string id, TipoEstribo tipo, List<PuntoBarra> mandan, List<PuntoBarra> elegidas)
    {
        double estribo = RepartidorFilas.Diametro(e, e.DiametroEstribo);
        double arriba = elegidas.Where(b => b.Cara == Posicion.Superior).Max(b => corte.Y(b) + corte.Diametro(b) / 2) + estribo;
        double abajo = elegidas.Where(b => b.Cara == Posicion.Inferior).Min(b => corte.Y(b) - corte.Diametro(b) / 2) - estribo;
        if (tipo == TipoEstribo.Abierto)
            return new EstriboEnSeccion(id, tipo, corte.X(mandan[0]), corte.X(mandan[0]), abajo, arriba);
        double afuera = elegidas.Max(corte.Diametro) / 2 + estribo;
        return new EstriboEnSeccion(id, tipo, corte.X(mandan[0]) - afuera, corte.X(mandan[1]) + afuera, abajo, arriba);
    }

    private static List<Fila> Filas(EntradaSeccion e, CaraSeccion c) =>
        c.Filas.Select(f => f.OrderBy(b => b.X).ToList())
            .Select(f => new Fila(f.Select(b => b.X).ToArray(), f.Select(b => b.Y).ToArray(),
                f.Select(b => RepartidorFilas.Diametro(e, b.Diametro)).ToArray(), new bool[f.Count]))
            .ToList();

    private static CaraSeccion ConX(CaraSeccion c, List<Fila> filas) => c with
    {
        Filas = c.Filas.Select((f, k) => (IReadOnlyList<BarraEnSeccion>)f.OrderBy(b => b.X).Select((b, i) => b with { X = filas[k].X[i] }).ToList()).ToList(),
    };
}
