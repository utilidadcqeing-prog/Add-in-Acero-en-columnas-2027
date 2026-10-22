using System.Globalization;
using CQEING.Vigas.Calculo.Estribos;
using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// Un arreglo con la forma en que la página lo aplica a su estado:
/// <list type="bullet">
/// <item><c>"forma"</c>: el adicional <paramref name="Indice"/> de <paramref name="Donde"/> pasa a la forma <paramref name="Valor"/> (<c>"C"</c> o <c>"S"</c>);</item>
/// <item><c>"gancho"</c>: el gancho de los estribos de <paramref name="Donde"/> pasa a <paramref name="Valor"/> (<c>"135°"</c>…);</item>
/// <item><c>"quitar"</c>: se quita el adicional <paramref name="Indice"/> de <paramref name="Donde"/>;</item>
/// <item><c>"salto"</c>: los <paramref name="Saltos"/>, los mismos cambios que deja el arrastre (#84).</item>
/// </list>
/// <paramref name="Donde"/> es <c>"est"</c> (los estribos del pórtico) o <c>"estMarc"</c> (los de los tramos marcados).
/// </summary>
public sealed record CambioArreglo(string Tipo, string Donde = "", int Indice = -1, string Valor = "", IReadOnlyList<CambioArrastre>? Saltos = null);

/// <summary>Un botón del globo del choque: lo que hace, dicho como queda ("Acortar BS1: 2.40 → 2.10 m"), y el cambio.</summary>
public sealed record ArregloVista(string Texto, CambioArreglo Cambio);

/// <summary>La respuesta a <see cref="PedirArreglos"/>: <paramref name="N"/> vuelve para descartar las viejas.</summary>
public sealed record ArreglosVista(int N, int Choque, IReadOnlyList<ArregloVista> Arreglos);

/// <summary>
/// Los arreglos de un choque (decisión 7 del plan docs/planes/2026-10-22-choques-entre-barras-vigas.md). Cada candidato
/// se prueba antes de ofrecerlo, con el mismo cálculo de siempre: se aplica a las opciones, se vuelve a planificar y a
/// detectar, y solo se ofrece si ese choque desaparece, no quedan más choques de los que había y el tablero deja
/// generar. Se piden al abrir el punto: probar todos los de todos los puntos en cada cálculo tardaría segundos.
/// </summary>
public static class ArreglosChoque
{
    /// <summary>Cuántos arreglos ofrece cada punto, como mucho.</summary>
    public const int Maximo = 3;

    /// <summary>
    /// Un choque de las mismas piezas a menos de esto (mm) del original es el mismo choque, que no se arregló: un bastón
    /// acortado que sigue tocando parte de la corrida deja su punto un poco corrido.
    /// </summary>
    private const double MismoLugar = 1500;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <param name="indice">El del choque en <see cref="VistaPortico.Choques"/>, calculado con estas mismas opciones.</param>
    public static IReadOnlyList<ArregloVista> Buscar(string id, PorticoLeido p, OpcionesTablero o, Reglas r,
        IReadOnlyDictionary<int, DobladoBarra>? doblados, int indice)
    {
        if (p.Ubicacion is not { } u || PlanificadorAcero.Planificar(id, p, o, r) is not { } plan)
            return [];
        IReadOnlyList<Choque> antes = ArmadorChoques.Detectar(plan, u, r, doblados);
        if (indice < 0 || indice >= antes.Count)
            return [];
        EntradaVigas e = CalculoPortico.EntradaDe(p, o, r);
        var prueba = new Prueba(id, p, o, r, doblados, u, antes[indice], antes.Sum(c => c.Veces), MotorVigas.Calcular(e));
        return Candidatos(prueba, e).Take(Maximo).ToList();
    }

    /// <summary>Los arreglos que sirven, en el orden de la tabla de la decisión 7: primero el estribo, luego las piezas longitudinales.</summary>
    private static IEnumerable<ArregloVista> Candidatos(Prueba prueba, EntradaVigas e)
    {
        Choque c = prueba.Choque;
        if (Choque.EsEstribo(c.A.Tipo) && Estribo(prueba.Opciones, prueba.Portico, c.A) is { } est)
        {
            string id = c.A.Pieza;
            if (c.A.Tipo == TipoTrazo.Grapa && est.Adicional is { } grapa)
            {
                FormaGrapa otra = grapa.Forma == FormaGrapa.C ? FormaGrapa.S : FormaGrapa.C;
                var forma = new CambioArreglo("forma", est.Donde, est.Indice, otra.ToString());
                if (prueba.Sirve(forma))
                    yield return new ArregloVista($"Grapa {id} en {otra}", forma);
            }
            string deQuien = est.Donde == "estMarc" ? "de los tramos marcados" : "del pórtico";
            foreach (GanchoEstribo g in new[] { GanchoEstribo.De135, GanchoEstribo.De180, GanchoEstribo.De90 }.Where(g => g != est.Config.Gancho))
            {
                var gancho = new CambioArreglo("gancho", est.Donde, Valor: Grados(g));
                if (prueba.Sirve(gancho))
                    yield return new ArregloVista($"Gancho de {Grados(g)} en los estribos {deQuien}", gancho);
            }
            if (est.Adicional is not null)
            {
                var quitar = new CambioArreglo("quitar", est.Donde, est.Indice);
                if (prueba.Sirve(quitar))
                    yield return new ArregloVista(c.A.Tipo == TipoTrazo.Grapa ? $"Quitar la grapa {id}" : $"Quitar el estribo {id}", quitar);
            }
        }

        double x = MarcoPortico.EnPortico(prueba.Ubicacion, c.Punto);
        foreach (string id in new[] { c.A, c.B }.Where(z => z.Tipo == TipoTrazo.Baston).Select(z => z.Pieza).Distinct())
            foreach (ArregloVista a in DeBaston(prueba, e, id, x))
                yield return a;
        foreach (string id in new[] { c.A, c.B }.Where(z => z.Tipo == TipoTrazo.Barra).Select(z => z.Pieza).Distinct())
            foreach (ArregloVista a in DeBarra(prueba, e, id, x))
                yield return a;
    }

    /// <summary>
    /// Acortarlo por la punta del lado del choque hasta despejarlo, y correrlo entero alejándolo del choque: en los dos,
    /// lo menos que lo despeje, entre los saltos del arrastre.
    /// </summary>
    private static IEnumerable<ArregloVista> DeBaston(Prueba prueba, EntradaVigas e, string id, double x)
    {
        if (prueba.Vigas.Bastones.FirstOrDefault(b => b.Id == id) is not { } b)
            yield break;
        bool izquierda = x < (b.Desde + b.Hasta) / 2.0;

        List<CambioArreglo> acortes = Puntas(EditorArrastre.Saltos(e, prueba.Vigas, id, izquierda ? PuntoArrastre.Izquierda : PuntoArrastre.Derecha))
            .Where(s => izquierda ? s.Desde > b.Desde : s.Hasta < b.Hasta)
            .OrderBy(s => izquierda ? s.Desde : -s.Hasta)
            .Select(s => new CambioArreglo("salto", Saltos: [s])).ToList();
        if (prueba.ElPrimeroQueSirve(acortes) is { } acorte)
            yield return new ArregloVista($"Acortar {id}: {F2(b.LargoRecto)} → {F2(Recto(acorte))} m", acorte);

        List<CambioArreglo> corridas = Puntas(EditorArrastre.Saltos(e, prueba.Vigas, id, PuntoArrastre.Centro))
            .Where(s => izquierda ? s.Desde > b.Desde : s.Desde < b.Desde)
            .OrderBy(s => izquierda ? s.Desde : -s.Desde)
            .Select(s => new CambioArreglo("salto", Saltos: [s])).ToList();
        if (prueba.ElPrimeroQueSirve(corridas) is { } corrida)
            yield return new ArregloVista($"Correr {id} a {F2(corrida.Saltos![0].Desde)} – {F2(corrida.Saltos[0].Hasta)} m", corrida);

        static IEnumerable<CambioArrastre> Puntas(IReadOnlyList<Salto> saltos) =>
            saltos.Where(s => !s.EnVoladizo && s.Cambios.Count == 1).Select(s => s.Cambios[0]);
        static int Recto(CambioArreglo a) => a.Saltos![0].Hasta - a.Saltos[0].Desde;
    }

    /// <summary>Correr el traslapo de la punta más cercana al choque un múltiplo de corte hacia un lado o hacia el otro.</summary>
    private static IEnumerable<ArregloVista> DeBarra(Prueba prueba, EntradaVigas e, string id, double x)
    {
        if (prueba.Vigas.Barras.FirstOrDefault(b => b.Id == id) is not { } barra)
            yield break;
        PuntoArrastre punta = Math.Abs(x - barra.Desde) <= Math.Abs(x - barra.Hasta) ? PuntoArrastre.Izquierda : PuntoArrastre.Derecha;
        IReadOnlyList<Salto> saltos = EditorArrastre.Saltos(e, prueba.Vigas, id, punta);
        int aqui = saltos.ToList().FindIndex(s => s.Cambios.Count == 0);
        if (aqui < 0)
            yield break;
        foreach (int i in new[] { aqui - 1, aqui + 1 }.Where(i => i >= 0 && i < saltos.Count && !saltos[i].EnVoladizo))
        {
            var corrido = new CambioArreglo("salto", Saltos: saltos[i].Cambios);
            if (prueba.Sirve(corrido))
                yield return new ArregloVista(string.Join(" y ", saltos[i].Cambios.Select(m => $"Correr el traslapo {m.Id} a {F2(m.Desde)} m")), corrido);
        }
    }

    /// <summary>
    /// De qué configuración es el estribo del choque y, si es un adicional, cuál: la de los tramos marcados si su tramo
    /// está marcado, la del pórtico si no. Los adicionales se numeran como en <see cref="MotorEstribos"/>: primero los
    /// del pórtico (si algún tramo los usa) y después los de los tramos marcados. Null si no se encuentra.
    /// </summary>
    private static (string Donde, int Indice, ConfigEstribos Config, EstriboAdicional? Adicional)? Estribo(OpcionesTablero o, PorticoLeido p, PiezaChocada pieza)
    {
        bool Marcado(string? tramo) => o.EstribosMarcados is not null && tramo is not null && o.TramosMarcados.Contains(tramo);
        bool marcado = Marcado(pieza.Tramo);
        ConfigEstribos config = marcado ? o.EstribosMarcados! : o.Estribos;
        string donde = marcado ? "estMarc" : "est";
        if (pieza.Pieza == MotorEstribos.IdCerrado)
            return (donde, -1, config, null);
        if (!int.TryParse(pieza.Pieza.AsSpan(MotorEstribos.IdCerrado.Length), NumberStyles.None, Inv, out int numero))
            return null;
        int indice = numero - 1;
        if (marcado && DistribuidorEstribos.Tramos(p.Portico).Any(t => !Marcado(t.Nombre)))
            indice -= o.Estribos.Adicionales?.Count ?? 0;
        IReadOnlyList<EstriboAdicional> adicionales = config.Adicionales ?? [];
        return indice >= 0 && indice < adicionales.Count ? (donde, indice, config, adicionales[indice]) : null;
    }

    /// <summary>Las opciones con el arreglo aplicado, como lo aplica la página.</summary>
    internal static OpcionesTablero Aplicar(OpcionesTablero o, CambioArreglo c, ResultadoVigas vigas)
    {
        return c.Tipo switch
        {
            "forma" => ConConfig(cfg => cfg with
            {
                Adicionales = (cfg.Adicionales ?? []).Select((a, i) => i == c.Indice ? a with { Forma = Enum.Parse<FormaGrapa>(c.Valor) } : a).ToList(),
            }),
            "gancho" => ConConfig(cfg => cfg with { Gancho = DeGrados(c.Valor) }),
            "quitar" => ConConfig(cfg => cfg with { Adicionales = (cfg.Adicionales ?? []).Where((_, i) => i != c.Indice).ToList() }),
            "salto" => ConSaltos(c.Saltos ?? []),
            _ => o,
        };

        OpcionesTablero ConConfig(Func<ConfigEstribos, ConfigEstribos> cambio) => c.Donde == "estMarc" && o.EstribosMarcados is { } marcados
            ? o with { EstribosMarcados = cambio(marcados) }
            : o with { Estribos = cambio(o.Estribos) };

        // Como aplicarSalto en la página: un traslapo corrido va a los movidos; un bastón, a sus puntas, y la punta que
        // se movió pierde el gancho elegido.
        OpcionesTablero ConSaltos(IReadOnlyList<CambioArrastre> saltos)
        {
            var movidos = new Dictionary<string, TraslapoMovido>(o.Cambios?.Movidos ?? new Dictionary<string, TraslapoMovido>());
            var ganchos = new Dictionary<string, TipoGancho>(o.Cambios?.Ganchos ?? new Dictionary<string, TipoGancho>());
            var puntas = new Dictionary<string, PuntasBaston>(o.Bastones.Puntas ?? new Dictionary<string, PuntasBaston>());
            foreach (CambioArrastre s in saltos)
            {
                if (s.Tipo == "movido")
                {
                    movidos[s.Id] = new TraslapoMovido(s.Desde, s.EnVoladizo);
                    continue;
                }
                Baston? b = vigas.Bastones.FirstOrDefault(x => x.Id == s.Id);
                if (b is null || b.Desde != s.Desde)
                    ganchos.Remove(s.Id + ".inicio");
                if (b is null || b.Hasta != s.Hasta)
                    ganchos.Remove(s.Id + ".fin");
                puntas[s.Id] = new PuntasBaston(s.Desde, s.Hasta);
            }
            return o with
            {
                Cambios = (o.Cambios ?? new CambiosManuales()) with { Movidos = movidos, Ganchos = ganchos },
                Bastones = o.Bastones with { Puntas = puntas },
            };
        }
    }

    /// <summary>El gancho como lo escribe la página: "135°", "90°" o "180°".</summary>
    internal static string Grados(GanchoEstribo g) => g switch
    {
        GanchoEstribo.De90 => "90°",
        GanchoEstribo.De180 => "180°",
        _ => "135°",
    };

    private static GanchoEstribo DeGrados(string texto) => texto switch
    {
        "90°" => GanchoEstribo.De90,
        "180°" => GanchoEstribo.De180,
        _ => GanchoEstribo.De135,
    };

    private static string F2(double mm) => (mm / 1000).ToString("0.00", Inv);

    /// <summary>Prueba los arreglos de un choque sobre las opciones de la página; recuerda lo que ya probó.</summary>
    private sealed class Prueba(string id, PorticoLeido p, OpcionesTablero o, Reglas r, IReadOnlyDictionary<int, DobladoBarra>? doblados,
        UbicacionPortico u, Choque choque, int total, ResultadoVigas vigas)
    {
        private readonly Dictionary<CambioArreglo, (bool SePuede, bool Despeja, int Total)> probados = new(ReferenceEqualityComparer.Instance);

        public PorticoLeido Portico => p;
        public OpcionesTablero Opciones => o;
        public UbicacionPortico Ubicacion => u;
        public Choque Choque => choque;
        public ResultadoVigas Vigas => vigas;

        /// <summary>Si el arreglo despeja el choque, el tablero lo deja generar y no quedan más choques que antes.</summary>
        public bool Sirve(CambioArreglo c) => Probar(c) is { SePuede: true, Despeja: true } x && x.Total <= total;

        /// <summary>
        /// El primero de <paramref name="lista"/> que sirve, de menor a mayor cambio. Despejar es monótono (cuanto más se
        /// acorta o se corre, menos toca): se busca por mitades el primero que despeja y desde ahí, unos pocos más.
        /// </summary>
        public CambioArreglo? ElPrimeroQueSirve(IReadOnlyList<CambioArreglo> lista)
        {
            int desde = 0, hasta = lista.Count - 1, primero = -1;
            while (desde <= hasta)
            {
                int medio = (desde + hasta) / 2;
                if (Probar(lista[medio]).Despeja)
                    (primero, hasta) = (medio, medio - 1);
                else
                    desde = medio + 1;
            }
            return primero < 0 ? null : lista.Skip(primero).Take(3).FirstOrDefault(Sirve);
        }

        private (bool SePuede, bool Despeja, int Total) Probar(CambioArreglo c)
        {
            if (probados.TryGetValue(c, out var hecho))
                return hecho;
            OpcionesTablero nuevas = Aplicar(o, c, vigas);
            var resultado = PlanificadorAcero.Planificar(id, p, nuevas, r) is { } plan
                && ArmadorChoques.Detectar(plan, u, r, doblados) is var choques
                ? (true, !choques.Any(MismoChoque), choques.Sum(x => x.Veces))
                : (false, false, int.MaxValue);
            probados[c] = resultado;
            return resultado;
        }

        private bool MismoChoque(Choque x) =>
            x.A.Pieza == choque.A.Pieza && x.A.Tipo == choque.A.Tipo && x.A.Tramo == choque.A.Tramo
            && x.B.Pieza == choque.B.Pieza && x.B.Tipo == choque.B.Tipo
            && Math.Abs(MarcoPortico.EnPortico(u, x.Punto) - MarcoPortico.EnPortico(u, choque.Punto)) < MismoLugar;
    }
}
