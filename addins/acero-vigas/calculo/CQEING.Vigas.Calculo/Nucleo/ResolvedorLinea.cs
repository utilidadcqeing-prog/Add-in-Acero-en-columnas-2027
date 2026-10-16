namespace CQEING.Vigas.Calculo.Nucleo;

/// <summary>
/// Busca, entre todas las formas válidas de partir una línea en barras, la mejor según la decisión 2 del plan
/// (docs/planes/2026-10-16-motor-calculo-vigas-80.md): menos barras de 12 m, menos acero y traslapos más cerca
/// de su punto ideal. Válida = cada barra múltiplo del módulo y dentro de su varilla, cada traslapo con al menos
/// el mínimo, dentro de su ranura y sin alejarse de su punto ideal más de lo que permite la ranura.
/// </summary>
/// <remarks>
/// Cómo busca (todo en mm):
/// <list type="bullet">
/// <item>El acero total es la distancia entre puntas (ganchos incluidos) más la suma de los traslapos, así que el
/// acero mínimo posible sale de redondear hacia arriba al módulo esa distancia más un traslapo mínimo por ranura.
/// Se prueba ese total y, si no hay solución, uno o más módulos por encima.</item>
/// <item>Para un total dado, lo que sobra por encima de los mínimos (la "holgura") se reparte entre los traslapos.
/// Una programación dinámica recorre los traslapos de izquierda a derecha; su estado es cuántos módulos suman las
/// barras ya puestas (una fila por valor) y cuánta holgura se gastó (una columna por milímetro). Con eso quedan
/// fijas las puntas de cada traslapo: fin = base − holgura previa, inicio = base − mínimo − holgura nueva.</item>
/// <item>Cada fila se procesa entera de una vez (mínimos acumulados o por rango), sin probar cada par de holguras.</item>
/// </list>
/// Las ranuras tienen que estar en orden y no pisarse entre sí; el adaptador de vigas pone a lo sumo una por luz.
/// </remarks>
public static class ResolvedorLinea
{
    /// <summary>Cuántos módulos por traslapo se prueban por encima del acero mínimo antes de dar por imposible.</summary>
    private const int ModulosExtraPorTraslapo = 2;

    /// <summary>Peso de una barra de 12 m en el costo: más que cualquier desvío posible, para que mande primero.</summary>
    private const long CostoBarraDe12 = 1_000_000_000_000L;

    /// <summary>Vueltas máximas de <see cref="Repartir"/>; en la práctica se estabiliza en dos o tres.</summary>
    private const int VueltasDeReparto = 50;

    private const long Inalcanzable = long.MaxValue;

    public static SolucionLinea? Resolver(ProblemaLinea p)
    {
        int distancia = (p.Fin + p.GanchoFin) - (p.Inicio - p.GanchoInicio);
        int traslapos = p.Ranuras.Count;
        if (traslapos == 0)
            return BarraDerecha(p, distancia);

        int totalMinimo = DividirHaciaArriba(distancia + traslapos * p.TraslapoMinimo, p.Modulo);
        SolucionLinea? mejor = null;
        for (int total = totalMinimo; total <= totalMinimo + traslapos * ModulosExtraPorTraslapo; total++)
        {
            SolucionLinea? s = ResolverConTotal(p, total, distancia);
            if (s is not null && (mejor is null || s.BarrasDe12 < mejor.BarrasDe12))
                mejor = s;
            if (mejor is { BarrasDe12: 0 })
                break;
        }
        return mejor;
    }

    private static SolucionLinea? BarraDerecha(ProblemaLinea p, int largo)
    {
        if (largo > p.VarillaMaxima)
            return null;
        if (p.LargosFijos is not null && p.LargosFijos.TryGetValue(0, out int fijo) && fijo != largo)
            return null;
        var barra = new BarraLinea(p.Inicio, p.Fin, largo);
        return new SolucionLinea([barra], [], largo > p.VarillaBase ? 1 : 0);
    }

    /// <summary>
    /// Mejores caminos que llegan a "barras que suman K módulos" (la fila), por holgura gastada (la columna), y de
    /// dónde viene cada uno.
    /// </summary>
    private sealed class Fila
    {
        public Fila(int columnas)
        {
            Costo = new long[columnas];
            Array.Fill(Costo, Inalcanzable);
            ModulosPrevios = new int[columnas];
            HolguraPrevia = new int[columnas];
        }

        public long[] Costo { get; }
        public int[] ModulosPrevios { get; }
        public int[] HolguraPrevia { get; }
        public bool Vacia => Costo.All(c => c == Inalcanzable);

        public void Relajar(int holgura, long costo, int modulosPrevios, int holguraPrevia)
        {
            if (costo >= Costo[holgura])
                return;
            Costo[holgura] = costo;
            ModulosPrevios[holgura] = modulosPrevios;
            HolguraPrevia[holgura] = holguraPrevia;
        }
    }

    /// <summary>Mejor solución con exactamente <paramref name="total"/> módulos de acero, o null.</summary>
    private static SolucionLinea? ResolverConTotal(ProblemaLinea p, int total, int distancia)
    {
        int n = p.Ranuras.Count;
        int m = p.Modulo;
        int min = p.TraslapoMinimo;
        int holgura = total * m - distancia - n * min;
        if (holgura < 0)
            return null;
        int origen = p.Inicio - p.GanchoInicio;

        var filas = new SortedDictionary<int, Fila>[n];
        var inicial = new Fila(holgura + 1);
        inicial.Costo[0] = 0;
        var previas = new SortedDictionary<int, Fila> { [0] = inicial };
        for (int i = 0; i < n; i++)
        {
            Ranura r = p.Ranuras[i];
            var actuales = new SortedDictionary<int, Fila>();
            foreach (var (modulosPrevios, previa) in previas)
            {
                foreach (int k in ModulosPosibles(p, i))
                {
                    int modulos = modulosPrevios + k;
                    // Fin del traslapo i = baseFin − holgura previa; inicio = baseFin − mínimo − holgura nueva.
                    int baseFin = origen + modulos * m - i * min;
                    int desde = Math.Max(0, baseFin - r.ZonaHasta);
                    int hasta = Math.Min(holgura, baseFin - min - r.ZonaDesde);
                    if (desde > holgura)
                        break;
                    if (hasta < desde)
                        continue;
                    if (!actuales.TryGetValue(modulos, out Fila? destino))
                        actuales[modulos] = destino = new Fila(holgura + 1);
                    Transicion(previa.Costo, destino, modulosPrevios, desde, hasta, r, baseFin, min, CostoBarra(p, k));
                    if (destino.Vacia)
                        actuales.Remove(modulos);
                }
            }
            filas[i] = actuales;
            previas = actuales;
        }

        // Cierre: la última barra completa el total y se gasta toda la holgura.
        int? final = null;
        long mejorCosto = Inalcanzable;
        foreach (var (modulos, fila) in previas)
        {
            int k = total - modulos;
            if (fila.Costo[holgura] == Inalcanzable || !ModulosPosibles(p, n).Contains(k))
                continue;
            long costo = fila.Costo[holgura] + CostoBarra(p, k);
            if (costo < mejorCosto)
                (final, mejorCosto) = (modulos, costo);
        }
        return final is null ? null : Reconstruir(p, filas, final.Value, holgura, total);
    }

    /// <summary>
    /// Pasa de una fila a <paramref name="destino"/> poniendo una barra más. x es la holgura previa (fija el fin del
    /// traslapo) y u la nueva (fija el inicio); las dos van en [<paramref name="desde"/>, <paramref name="hasta"/>]
    /// y x ≤ u, porque el traslapo no baja del mínimo. Los costos son el doble del desvío.
    /// </summary>
    private static void Transicion(long[] costo, Fila destino, int modulosPrevios, int desde, int hasta, Ranura r,
        int baseFin, int min, long costoBarra)
    {
        long maximo = r.DesvioMaximo == int.MaxValue ? Inalcanzable : 2L * r.DesvioMaximo;
        switch (r.Referencia)
        {
            case Referencia.Inicio:
            {
                // El desvío depende solo del inicio, o sea de u.
                long mejor = Inalcanzable;
                int arg = -1;
                for (int u = desde; u <= hasta; u++)
                {
                    if (costo[u] < mejor)
                        (mejor, arg) = (costo[u], u);
                    long desvio = 2L * Math.Abs(baseFin - min - u - (long)r.PuntoIdeal);
                    if (arg >= 0 && desvio <= maximo)
                        destino.Relajar(u, mejor + desvio + costoBarra, modulosPrevios, arg);
                }
                break;
            }
            case Referencia.Fin:
            {
                // El desvío depende solo del fin, o sea de x.
                long mejor = Inalcanzable;
                int arg = -1;
                for (int u = desde; u <= hasta; u++)
                {
                    long desvio = 2L * Math.Abs(baseFin - u - (long)r.PuntoIdeal);
                    if (costo[u] != Inalcanzable && desvio <= maximo && costo[u] + desvio < mejor)
                        (mejor, arg) = (costo[u] + desvio, u);
                    if (arg >= 0)
                        destino.Relajar(u, mejor + costoBarra, modulosPrevios, arg);
                }
                break;
            }
            default:
                TransicionCentro(costo, destino, modulosPrevios, desde, hasta,
                    2L * baseFin - min - 2L * r.PuntoIdeal, maximo, costoBarra);
                break;
        }
    }

    /// <summary>
    /// Capa del centro: el doble del desvío es |w − x| con w = a − u. Se parte en x ≤ w (costo[x] − x + w) y x ≥ w
    /// (costo[x] + x − w), y cada parte se resuelve con un mínimo por rango, recortado al desvío máximo.
    /// </summary>
    private static void TransicionCentro(long[] costo, Fila destino, int modulosPrevios, int desde, int hasta, long a,
        long maximo, long costoBarra)
    {
        var menosX = new long[costo.Length];
        var masX = new long[costo.Length];
        for (int x = desde; x <= hasta; x++)
        {
            menosX[x] = costo[x] == Inalcanzable ? Inalcanzable : costo[x] - x;
            masX[x] = costo[x] == Inalcanzable ? Inalcanzable : costo[x] + x;
        }
        var izquierda = new MinimoPorRango(menosX, desde, hasta);
        var derecha = new MinimoPorRango(masX, desde, hasta);

        for (int u = desde; u <= hasta; u++)
        {
            long w = a - u;
            long candidato = Inalcanzable;
            int arg = -1;

            var (valor, indice) = izquierda.Minimo(Acotar(Math.Max(desde, w - maximo)), Acotar(Math.Min(u, w)));
            if (indice >= 0)
                (candidato, arg) = (valor + w, indice);

            (valor, indice) = derecha.Minimo(Acotar(Math.Max(desde, w)), Acotar(Math.Min(u, w + maximo)));
            if (indice >= 0 && valor - w < candidato)
                (candidato, arg) = (valor - w, indice);

            if (arg >= 0)
                destino.Relajar(u, candidato + costoBarra, modulosPrevios, arg);
        }
    }

    private static int Acotar(long valor) => (int)Math.Clamp(valor, int.MinValue / 2, int.MaxValue / 2);

    private static SolucionLinea Reconstruir(ProblemaLinea p, SortedDictionary<int, Fila>[] filas, int modulosFinales,
        int holguraFinal, int total)
    {
        int n = filas.Length;
        int m = p.Modulo;
        int min = p.TraslapoMinimo;
        int origen = p.Inicio - p.GanchoInicio;

        var modulosAcumulados = new int[n];
        var holguras = new int[n];
        var modulosBarra = new int[n + 1];
        modulosBarra[n] = total - modulosFinales;
        (int modulos, int holgura) = (modulosFinales, holguraFinal);
        for (int i = n - 1; i >= 0; i--)
        {
            Fila fila = filas[i][modulos];
            (modulosAcumulados[i], holguras[i]) = (modulos, holgura);
            int modulosPrevios = fila.ModulosPrevios[holgura];
            modulosBarra[i] = modulos - modulosPrevios;
            (modulos, holgura) = (modulosPrevios, fila.HolguraPrevia[holgura]);
        }

        var inicios = new int[n];
        var fines = new int[n];
        for (int i = 0; i < n; i++)
        {
            int baseFin = origen + modulosAcumulados[i] * m - i * min;
            fines[i] = baseFin - (i == 0 ? 0 : holguras[i - 1]);
            inicios[i] = baseFin - min - holguras[i];
        }
        Repartir(p, inicios, fines);

        var traslapos = new List<TraslapoLinea>(n);
        for (int i = 0; i < n; i++)
            traslapos.Add(new TraslapoLinea(inicios[i], fines[i], DesvioDoble(p.Ranuras[i], inicios[i], fines[i]) / 2.0));

        var barras = new List<BarraLinea>(n + 1);
        for (int b = 0; b <= n; b++)
        {
            int desde = b == 0 ? p.Inicio : traslapos[b - 1].Desde;
            int hasta = b == n ? p.Fin : traslapos[b].Hasta;
            barras.Add(new BarraLinea(desde, hasta, modulosBarra[b] * m));
        }
        return new SolucionLinea(barras, traslapos, barras.Count(b => b.Largo > p.VarillaBase));
    }

    /// <summary>
    /// Regla fija de empates (decisión 14 del plan): si correr una barra del medio no cambia el desvío total, se la
    /// corre hasta repartir el desvío entre sus dos traslapos (menor suma de cuadrados). Los largos no cambian.
    /// </summary>
    private static void Repartir(ProblemaLinea p, int[] inicios, int[] fines)
    {
        int min = p.TraslapoMinimo;
        bool hubo = true;
        for (int vuelta = 0; hubo && vuelta < VueltasDeReparto; vuelta++)
        {
            hubo = false;
            // La barra b del medio va del inicio del traslapo b − 1 al fin del traslapo b.
            for (int b = 1; b < inicios.Length; b++)
            {
                Ranura izquierda = p.Ranuras[b - 1], derecha = p.Ranuras[b];
                int desde = Math.Max(min - (fines[b] - inicios[b]), izquierda.ZonaDesde - inicios[b - 1]);
                int hasta = Math.Min(fines[b - 1] - inicios[b - 1] - min, derecha.ZonaHasta - fines[b]);
                (long, long)? Costo(int d)
                {
                    long a = DesvioDoble(izquierda, inicios[b - 1] + d, fines[b - 1]);
                    long c = DesvioDoble(derecha, inicios[b], fines[b] + d);
                    if (a > 2L * izquierda.DesvioMaximo || c > 2L * derecha.DesvioMaximo)
                        return null;
                    return (a + c, a * a + c * c);
                }
                var mejor = Costo(0)!.Value;
                int corrimiento = 0;
                for (int d = desde; d <= hasta; d++)
                {
                    if (Costo(d) is not { } costo)
                        continue;
                    if (costo.CompareTo(mejor) < 0 || (costo == mejor && Math.Abs(d) < Math.Abs(corrimiento)))
                        (mejor, corrimiento) = (costo, d);
                }
                if (corrimiento == 0)
                    continue;
                inicios[b - 1] += corrimiento;
                fines[b] += corrimiento;
                hubo = true;
            }
        }
    }

    /// <summary>Largos posibles de la barra <paramref name="indice"/>, en módulos y de menor a mayor.</summary>
    private static IEnumerable<int> ModulosPosibles(ProblemaLinea p, int indice)
    {
        if (p.LargosFijos is not null && p.LargosFijos.TryGetValue(indice, out int fijo))
        {
            if (fijo % p.Modulo == 0 && fijo <= p.VarillaMaxima)
                yield return fijo / p.Modulo;
            yield break;
        }
        for (int k = 1; k * p.Modulo <= p.VarillaMaxima; k++)
            yield return k;
    }

    private static long CostoBarra(ProblemaLinea p, int modulos) =>
        modulos * p.Modulo > p.VarillaBase ? CostoBarraDe12 : 0;

    /// <summary>El doble del desvío, para no trabajar con medios milímetros.</summary>
    private static long DesvioDoble(Ranura r, int inicio, int fin) => r.Referencia switch
    {
        Referencia.Inicio => 2L * Math.Abs(inicio - r.PuntoIdeal),
        Referencia.Fin => 2L * Math.Abs(fin - r.PuntoIdeal),
        _ => Math.Abs((long)inicio + fin - 2L * r.PuntoIdeal),
    };

    private static int DividirHaciaArriba(int a, int b) => (a + b - 1) / b;
}
