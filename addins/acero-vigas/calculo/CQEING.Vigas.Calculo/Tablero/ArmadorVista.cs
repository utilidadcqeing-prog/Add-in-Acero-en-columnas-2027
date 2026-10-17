using System.Globalization;
using CQEING.Vigas.Calculo.Estribos;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Seccion;
using CQEING.Vigas.Calculo.Vigas;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// Arma lo que pinta el tablero (#83) con el motor: <see cref="MotorVigas"/>, <see cref="MotorEstribos"/> y el reparto
/// de la sección. Pasa de los mm del motor a las unidades de la maqueta (m en el alzado, cm en la sección).
/// </summary>
public static class ArmadorVista
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Avisos que impiden generar el acero en Revit.</summary>
    private static readonly TipoAviso[] Bloquean = [TipoAviso.SinTraslapoMinimo, TipoAviso.SinSolucion, TipoAviso.DiametroSinReglas];

    public static PorticoEnLista EnLista(PorticoLeido p)
    {
        IReadOnlyList<Apoyo> apoyos = p.Portico.Apoyos;
        string tipo = NombreTipo(p.Tipo);
        string sub = p.Tipo == TipoViga.Cimentacion
            ? $"Cimentación · eje {p.Eje ?? "?"}"
            : $"{(p.Eje is null ? "Sin eje" : "Eje " + p.Eje)} · {apoyos[0].Eje} a {apoyos[^1].Eje}{Voladizos(p.Portico)}";
        var tramos = DistribuidorEstribos.Tramos(p.Portico).Select(t => new TramoEnLista(
            t.Nombre, t.EsVoladizo ? t.Nombre : "Viga " + t.Nombre, M(DesdeEje(p.Portico, t)), M(HastaEje(p.Portico, t)))).ToList();
        return new PorticoEnLista(p.Nombre, p.Nombre, sub, tipo, tramos);
    }

    public static VistaPortico Armar(PorticoLeido p, OpcionesTablero o, Reglas r)
    {
        TipoViga tipo = o.Tipo ?? p.Tipo;
        Portico portico = p.Portico;
        var avisos = new List<Aviso>();
        var apoyos = portico.Apoyos.Select(a => new ApoyoVista(a.Eje, M(a.PosicionEje), M(a.CaraIzquierda), M(a.CaraDerecha))).ToList();
        double inicio = M(portico.Apoyos[0].CaraIzquierda - portico.VoladizoInicio);
        double fin = M(portico.Apoyos[^1].CaraDerecha + portico.VoladizoFinal);

        // Un diámetro que no está en las reglas haría reventar el reparto de la sección: se avisa y no se calcula.
        int[] faltan = Diametros(o).Where(d => !r.PorDiametro.ContainsKey(d)).Distinct().Order().ToArray();
        if (faltan.Length > 0)
        {
            avisos.AddRange(faltan.Select(d => new Aviso(TipoAviso.DiametroSinReglas, $"#{d}")));
            var vacia = new CaraVista(0, [], nameof(ErrorCara.Ninguno), 0);
            return new VistaPortico(p.Nombre, NombreTipo(tipo), inicio, fin, apoyos, [], [], [], [], vacia, vacia,
                new OpcionCaraVista(0, false), new OpcionCaraVista(0, false), "izq", "izq",
                new SeccionVista(Cm(Ancho(p)), Cm(Alto(p)), [], []), [], Textos(avisos, null), [], 0, 0, false);
        }

        var entrada = new EntradaVigas(portico, tipo, o.Superior, o.Inferior, r, o.Cara, Cambios: o.Cambios, Bastones: o.Bastones);
        ResultadoVigas rv = MotorVigas.Calcular(entrada);
        EntradaSeccion seccion = Seccion(p, o, r);
        IReadOnlyDictionary<string, ConfigEstribos>? porTramo = o.EstribosMarcados is null
            ? null
            : o.TramosMarcados.Distinct().ToDictionary(t => t, _ => o.EstribosMarcados);
        ResultadoEstribos re = MotorEstribos.Calcular(new EntradaEstribos(portico, seccion, o.Estribos, porTramo));
        avisos.AddRange(rv.Avisos);
        avisos.AddRange(re.Avisos);

        var barras = rv.Barras.Select(b => new BarraVista(b.Id, Capa(b.Capa), M(b.Desde), M(b.Hasta), b.GanchoInicio > 0, b.GanchoFin > 0,
            M(b.Largo), Recta(b), b.Manual, b.LlegaAlVoladizo, Cantidad(b.Grupos), b.Kg)).ToList();
        var traslapos = rv.Traslapos.Select(t => new TraslapoVista(t.Id, t.BarraIzquierda, t.BarraDerecha, Capa(t.Capa), M(t.Desde), M(t.Hasta),
            M(t.Minimo), $"#{t.Diametro}", t.Manual)).ToList();
        var bastones = rv.Bastones.Select(b => new BastonVista(b.Id, M(b.Eje), M(b.Desde), M(b.Hasta), b.GanchoInicio > 0, b.GanchoFin > 0,
            M(b.Largo), Donde(p, b), Cantidad([b.Grupo]), b.Kg,
            b.LargoRecto < CalculadorBastones.LargoDe(o.Bastones, b.Id))).ToList();

        bool seccionSinError = re.Seccion.Superior.Error == ErrorCara.Ninguno && re.Seccion.Inferior.Error == ErrorCara.Ninguno;
        bool sePuede = rv.Superior is not null && rv.Inferior is not null && seccionSinError && !avisos.Any(a => Bloquean.Contains(a.Tipo));
        OpcionesCorte oc = o.Corte ?? new OpcionesCorte();
        CorteEnPunto corte = CalculadoraCorte.En(portico, rv, re, oc);
        (SeccionVista enCorte, IReadOnlyList<BarraSeccion> proyeccion, string? avisoBastones) = SeccionDe(p, r, re, seccion, corte);
        return new VistaPortico(p.Nombre, NombreTipo(tipo), inicio, fin, apoyos, barras, traslapos, bastones,
            Zonas(portico, o, porTramo, r, re), Cara(re.Seccion.Superior), Cara(re.Seccion.Inferior),
            Opcion(rv.CaraInicio), Opcion(rv.CaraFinal), Lado(rv.CaraUsada), Lado(rv.CaraMasEconomica),
            enCorte, ListaEstribos(p, o, re), Textos(avisos, rv),
            rv.ZonasTraslapo.Select(z => new Rango(M(z.Desde), M(z.Hasta))).ToList(), rv.Kg + re.Kg, re.Cantidad, sePuede,
            CorteDe(corte, oc, proyeccion, corte.SinLugar ? "No hay un punto sin traslapo para el corte." : avisoBastones),
            re.Estribos.Where(e => e.Id == MotorEstribos.IdCerrado).SelectMany(e => e.Posiciones).Order().Select(M).ToList());
    }

    /// <summary>La pantalla Reglas: los valores en m y kg/m con los decimales de <c>RULES0</c> de la maqueta.</summary>
    public static VistaReglas Reglas(ReglasCargadas c)
    {
        Reglas r = c.Reglas;
        var filas = Modelo.Reglas.DiametrosEnPantalla.Where(r.PorDiametro.ContainsKey).Select(d =>
        {
            ReglaDiametro x = r.PorDiametro[d];
            return new ReglaVista($"#{d}", x.Traslapo is int t ? F(t / 1000.0, "0.00") : "", F(x.Gancho90.B / 1000.0, "0.000"),
                F(x.Estribo135.B / 1000.0, "0.000"), F(x.KgPorMetro, "0.000"));
        }).ToList();
        return new VistaReglas(filas, F(r.Modulo / 1000.0, "0.00"), F(r.DistanciaLibre / 10, "0.##"), r.Varilla == 12000, c.Problema);
    }

    /// <summary>
    /// Pasa los textos de la pantalla Reglas a <see cref="Modelo.Reglas"/> en mm, encima de <paramref name="base"/> (lo
    /// que la pantalla no muestra, como el #2, queda igual). Un texto que no es número pasa a 0 para que
    /// <see cref="Modelo.Reglas.Validar"/> lo rechace con su mensaje; un traslapo vacío queda sin traslapo.
    /// </summary>
    public static Reglas ReglasDe(VistaReglas v, Reglas @base)
    {
        var porDiametro = new Dictionary<int, ReglaDiametro>(@base.PorDiametro);
        foreach (ReglaVista fila in v.PorDiametro)
        {
            if (!int.TryParse(fila.D.TrimStart('#'), out int d) || !porDiametro.TryGetValue(d, out ReglaDiametro? x))
                continue;
            porDiametro[d] = x with
            {
                Traslapo = string.IsNullOrWhiteSpace(fila.T) ? null : Mm(fila.T),
                Gancho90 = x.Gancho90 with { B = Mm(fila.G) },
                Estribo135 = x.Estribo135 with { B = Mm(fila.E) },
                KgPorMetro = Numero(fila.K) ?? 0,
            };
        }
        return @base with
        {
            PorDiametro = porDiametro,
            Modulo = Mm(v.Multiplo),
            DistanciaLibre = (Numero(v.DistanciaLibreCm) ?? 0) * 10,
            Varilla = v.Varilla12 ? 12000 : 6000,
        };
    }

    /// <summary>El texto del aviso para la pantalla. Usa <paramref name="r"/> para saber qué barra toca la frontera.</summary>
    internal static string TextoAviso(Aviso a, ResultadoVigas? r) => a.Tipo switch
    {
        TipoAviso.TraslapoLargo => TextoTraslapoLargo(a, r),
        TipoAviso.SinTraslapoMinimo => $"Falta el traslapo mínimo de {a.Elemento} en las reglas.",
        TipoAviso.SinSolucion => $"Las barras de {(a.Elemento == nameof(Posicion.Superior) ? "arriba" : "abajo")} no tienen solución, ni con varilla de 12 m.",
        TipoAviso.VarillaLarga => $"{a.Elemento} mide {F(a.Largo / 1000.0, "0.00")} m: no sale de la varilla normal y usa la de 12 m.",
        TipoAviso.CambioNoAplicado => $"El cambio de {a.Elemento} ya no se puede aplicar y se ignora.",
        TipoAviso.DiametroSinReglas => $"El {a.Elemento} no está en las reglas.",
        TipoAviso.BastonLargoNoValido => "El largo de los bastones debe ser mayor que 0.",
        TipoAviso.EstriboValorNoValido => $"Los estribos de {a.Elemento} tienen una separación o una zona no válida y quedan sin estribos.",
        TipoAviso.EstriboIncompleto => $"El estribo {a.Elemento} no tiene las barras que pide.",
        TipoAviso.BarraNoExiste => $"El estribo {a.Elemento} elige una barra que no está en el corte.",
        TipoAviso.MueveUnaEsquina => $"El estribo {a.Elemento} movería una barra de esquina, que abraza el cerrado obligatorio.",
        TipoAviso.NoSePuedeAlinear => $"El estribo {a.Elemento} no cabe: las barras no quedan con la distancia libre.",
        TipoAviso.IgualAlCerrado => $"El estribo {a.Elemento} ya es el cerrado de las esquinas.",
        _ => a.Elemento,
    };

    /// <summary>Como la tarjeta de aviso de la maqueta: el largo, por qué quedó largo y si la otra opción lo evita.</summary>
    private static string TextoTraslapoLargo(Aviso a, ResultadoVigas? r)
    {
        string texto = $"El traslapo {a.Elemento} queda en {F(a.Largo / 1000.0, "0.00")}, {F(a.Exceso / 1000.0, "0.00")} por encima del mínimo.";
        Traslapo? t = r?.Traslapos.FirstOrDefault(x => x.Id == a.Elemento);
        if (a.Causa == CausaAviso.VecinaDeFrontera && t is not null)
        {
            List<Traslapo> capa = r!.Traslapos.Where(x => x.Capa == t.Capa).ToList();
            string idBarra = capa[^1] == t ? t.BarraDerecha : t.BarraIzquierda;
            Barra? barra = r.Barras.FirstOrDefault(b => b.Id == idBarra);
            texto += barra?.LlegaAlVoladizo == true
                ? $" {idBarra} termina en el voladizo y solo puede crecer."
                : $" {idBarra} llega al apoyo de borde y solo puede crecer.";
        }
        if (a.Alternativa is { LoEvita: true } alt)
        {
            long kg = (long)Math.Round(Math.Abs(alt.KgAhorro), MidpointRounding.AwayFromZero);
            texto += $" Si el traslapo va al {(alt.Cara == Modelo.Cara.Inicio ? "inicio" : "final")} de la luz no pasa"
                + (kg == 0 ? "." : alt.KgAhorro > 0 ? $", y además se ahorran {kg} kg." : $", aunque cuesta {kg} kg más.");
        }
        return texto;
    }

    private static List<AvisoVista> Textos(IEnumerable<Aviso> avisos, ResultadoVigas? r) =>
        avisos.Select(a => new AvisoVista(a.Tipo.ToString(), a.Elemento, TextoAviso(a, r), a.Alternativa is { LoEvita: true })).ToList();

    private static EntradaSeccion Seccion(PorticoLeido p, OpcionesTablero o, Reglas r)
    {
        var rec = new Recubrimientos((o.RecArribaCm ?? o.RecubrimientoCm) * 10, (o.RecAbajoCm ?? o.RecubrimientoCm) * 10,
            (o.RecLadosCm ?? o.RecubrimientoCm) * 10);
        return new EntradaSeccion(Ancho(p), Alto(p), rec, o.Estribos.Diametro, o.Superior, o.Inferior, r,
            (o.SeparacionFilasCm ?? 0) * 10, o.MaximoFilas);
    }

    private static IReadOnlyList<ZonaEstribos> Zonas(Portico portico, OpcionesTablero o, IReadOnlyDictionary<string, ConfigEstribos>? porTramo,
        Reglas r, ResultadoEstribos re)
    {
        var zonas = new List<ZonaEstribos>();
        foreach (Tramo t in DistribuidorEstribos.Tramos(portico))
        {
            // Solo los tramos que llevan estribos (los de una configuración no válida quedan sin ellos).
            if (!re.Estribos.Any(e => e.Id == MotorEstribos.IdCerrado && e.Tramo == t.Nombre))
                continue;
            ConfigEstribos c = porTramo?.GetValueOrDefault(t.Nombre) ?? o.Estribos;
            zonas.AddRange(DistribuidorEstribos.Zonas(t, c, r.PorDiametro[c.Diametro].Diametro)
                .Select(z => new ZonaEstribos(M(z.Desde), M(z.Hasta), z.Junta)));
        }
        return zonas;
    }

    /// <summary>
    /// La sección en el corte (#91): las barras corridas, los bastones que pasan por ahí y los estribos del tramo del
    /// corte. Aparte, los bastones de la proyección y el aviso si los bastones del corte no caben (decisión 6 del plan
    /// docs/planes/2026-10-17-corte-vigas-91.md).
    /// </summary>
    private static (SeccionVista Seccion, IReadOnlyList<BarraSeccion> Proyeccion, string? Aviso) SeccionDe(PorticoLeido p, Reglas r,
        ResultadoEstribos re, EntradaSeccion e, CorteEnPunto corte)
    {
        double alto = Alto(p);
        BarraSeccion Barra(string id, string nombre, bool arriba, int fila, BarraEnSeccion b, bool esquina, bool baston) =>
            new(id, nombre, arriba, fila, Cm(b.X), Cm(alto - b.Y), Cm(r.PorDiametro[b.Diametro].Diametro), esquina, baston);
        IEnumerable<BarraSeccion> Cara(CaraSeccion c, bool arriba)
        {
            string letra = arriba ? "a" : "b", nombre = arriba ? "arriba" : "abajo";
            int n = 0;
            for (int fila = 0; fila < c.Filas.Count; fila++)
            {
                List<BarraEnSeccion> orden = c.Filas[fila].OrderBy(b => b.X).ToList();
                for (int i = 0; i < orden.Count; i++)
                {
                    n++;
                    yield return Barra($"{letra}{n}", $"{nombre} {n}", arriba, fila, orden[i], fila == 0 && (i == 0 || i == orden.Count - 1), false);
                }
            }
        }

        var bastones = new List<BarraSeccion>();
        var proyeccion = new List<BarraSeccion>();
        string? aviso = null;
        foreach (Posicion cara in new[] { Posicion.Superior, Posicion.Inferior })
        {
            // Si las barras corridas de la cara ya no caben, el error lo muestra el paso 1.
            if ((cara == Posicion.Superior ? re.Seccion.Superior : re.Seccion.Inferior).Error != ErrorCara.Ninguno)
                continue;
            List<GrupoBarras> cortados = corte.Cortados.Where(b => b.Capa == cara).Select(b => b.Grupo).ToList();
            List<GrupoBarras> proyectados = corte.Proyectados.Where(b => b.Capa == cara).Select(b => b.Grupo).ToList();
            if (cortados.Count + proyectados.Count == 0)
                continue;
            bool arriba = cara == Posicion.Superior;
            string letra = arriba ? "bs" : "bi", nombre = arriba ? "bastón arriba" : "bastón abajo";
            UbicacionBastones enElCorte = UbicadorBastones.Ubicar(e, cara, cortados);
            if (enElCorte.Error != ErrorCara.Ninguno)
            {
                aviso ??= TextoBastones(arriba, enElCorte.Error);
                continue;
            }
            bastones.AddRange(enElCorte.Barras.Select((b, i) => Barra($"{letra}{i + 1}", $"{nombre} {i + 1}", arriba, b.Fila - 1, b.Barra, false, true)));
            // Ubicar es determinista: con los proyectados al final, los cortados quedan donde estaban (hallazgo 5).
            UbicacionBastones conProyeccion = UbicadorBastones.Ubicar(e, cara, [.. cortados, .. proyectados]);
            if (conProyeccion.Error == ErrorCara.Ninguno)
                proyeccion.AddRange(conProyeccion.Barras.Where(b => !enElCorte.Barras.Contains(b))
                    .Select((b, i) => Barra($"p{letra}{i + 1}", $"{nombre} {i + 1} (detrás)", arriba, b.Fila - 1, b.Barra, false, true)));
        }

        HashSet<string> delTramo = re.Estribos.Where(x => x.Tramo == corte.Tramo).Select(x => x.Id).ToHashSet();
        var figuras = re.EnSeccion.Where(f => delTramo.Contains(f.Id)).Select(f => new FiguraSeccion(f.Id, f.Tipo.ToString(),
            Cm(f.Izquierda), Cm(f.Derecha), Cm(alto - f.Arriba), Cm(alto - f.Abajo))).ToList();
        var barras = Cara(re.Seccion.Superior, true).Concat(Cara(re.Seccion.Inferior, false)).Concat(bastones).ToList();
        return (new SeccionVista(Cm(Ancho(p)), Cm(alto), barras, figuras), proyeccion, aviso);
    }

    private static string TextoBastones(bool arriba, ErrorCara error) => error == ErrorCara.NoCabenEnElAlto
        ? $"Los bastones de {(arriba ? "arriba" : "abajo")} chocan con las barras de {(arriba ? "abajo" : "arriba")}."
        : $"Los bastones de {(arriba ? "arriba" : "abajo")} no caben en el corte.";

    private static CorteVista CorteDe(CorteEnPunto c, OpcionesCorte o, IReadOnlyList<BarraSeccion> proyeccion, string? aviso) =>
        new(M(c.X), c.EsTipo, c.Ajustado, M(Math.Max(0, o.Profundidad)), o.Mirando == MiraCorte.Derecha ? "derecha" : "izquierda",
            c.Tramo, c.Permitidos.Select(i => new Rango(M(i.Desde), M(i.Hasta))).ToList(), proyeccion, aviso,
            c.Puntos.Select(M).ToList());

    /// <summary>Una fila por estribo de la lista: los del mismo id, diámetro y largo se suman en todo el pórtico.</summary>
    private static IReadOnlyList<FilaEstribos> ListaEstribos(PorticoLeido p, OpcionesTablero o, ResultadoEstribos re)
    {
        int tramos = DistribuidorEstribos.Tramos(p.Portico).Count;
        return re.Estribos
            .GroupBy(e => (e.Id, e.Tipo, e.Diametro, Largo: Math.Round(e.Largo, 3)))
            .Select(g =>
            {
                bool cerrado = g.Key.Id == MotorEstribos.IdCerrado;
                string detalle = cerrado
                    ? $"zonas {F(o.Estribos.LargoZona / 1000.0, "0.00")} cada {F(o.Estribos.SeparacionZona / 1000.0, "0.00")} · centro cada {F(o.Estribos.SeparacionCentro / 1000.0, "0.00")}"
                    : g.Count() == tramos ? "todo el pórtico" : string.Join(", ", g.Select(e => e.Tramo.StartsWith("Voladizo") ? e.Tramo : "Viga " + e.Tramo));
                string tipo = cerrado ? "Estribo cerrado" : g.Key.Tipo == TipoEstribo.Cerrado ? "Adicional cerrado" : "Adicional abierto";
                return new FilaEstribos(g.Key.Id, tipo, g.Sum(e => e.Posiciones.Count), $"#{g.Key.Diametro}", M(g.First().Largo), detalle, g.Sum(e => e.Kg));
            })
            .ToList();
    }

    private static IEnumerable<int> Diametros(OpcionesTablero o) =>
        o.Superior.Concat(o.Inferior).Select(g => g.Diametro)
            .Concat(new[] { o.Bastones.Superior, o.Bastones.Inferior }.OfType<GrupoBarras>().Select(g => g.Diametro))
            .Append(o.Estribos.Diametro)
            .Concat(o.EstribosMarcados is null ? [] : [o.EstribosMarcados.Diametro]);

    private static CaraVista Cara(CaraSeccion c) => new(c.CabenPorFila, c.Filas.Select(f => f.Count).ToList(), c.Error.ToString(), c.Maximo);

    private static OpcionCaraVista Opcion(SolucionCapa? s) => s is null ? new(0, false) : new(s.Kg, true);

    private static string Lado(Modelo.Cara c) => c == Modelo.Cara.Inicio ? "izq" : "der";

    private static string Capa(Posicion p) => p == Posicion.Superior ? "arriba" : "abajo";

    private static string NombreTipo(TipoViga t) => t == TipoViga.Aerea ? "Aérea" : "Cimentación";

    private static string Cantidad(IEnumerable<GrupoBarras> grupos) => string.Join(" + ", grupos.Select(g => $"{g.Cantidad} #{g.Diametro}"));

    private static string Recta(Barra b)
    {
        int ganchos = (b.GanchoInicio > 0 ? 1 : 0) + (b.GanchoFin > 0 ? 1 : 0);
        return ganchos switch
        {
            0 => "recta",
            1 => $"{F(M(b.LargoRecto), "0.00")} + gancho",
            _ => $"{F(M(b.LargoRecto), "0.00")} + 2 ganchos",
        };
    }

    private static string Donde(PorticoLeido p, Baston b)
    {
        if (!b.EnApoyo)
            return $"Centro de la luz {b.Donde}";
        int i = p.Portico.Apoyos.ToList().FindIndex(a => a.Eje == b.Donde);
        string apoyo = i >= 0 && i < p.TiposDeApoyo.Count ? p.TiposDeApoyo[i] switch
        {
            TipoApoyo.Muro => "el muro",
            TipoApoyo.Viga => "la viga",
            _ => "la columna",
        } : "la columna";
        return $"Centrado en {apoyo} {b.Donde}";
    }

    private static string Voladizos(Portico p) => (p.VoladizoInicio > 0, p.VoladizoFinal > 0) switch
    {
        (true, true) => " + voladizos",
        (false, false) => "",
        _ => " + voladizo",
    };

    /// <summary>Para "Ver: tramo": una luz va de eje a eje; un voladizo, de su eje a la punta.</summary>
    private static double DesdeEje(Portico p, Tramo t) =>
        t.Tipo == TipoTramo.VoladizoInicial ? t.CaraIzquierda : EjeJunto(p, t.CaraIzquierda);

    private static double HastaEje(Portico p, Tramo t) =>
        t.Tipo == TipoTramo.VoladizoFinal ? t.CaraDerecha : EjeJunto(p, t.CaraDerecha);

    /// <summary>El eje del apoyo cuya cara está en <paramref name="cara"/>.</summary>
    private static double EjeJunto(Portico p, double cara) =>
        p.Apoyos.First(a => a.CaraIzquierda == cara || a.CaraDerecha == cara).PosicionEje;

    private static double Ancho(PorticoLeido p) => p.Tramos[0].Ancho;

    private static double Alto(PorticoLeido p) => p.Tramos[0].Alto;

    private static double M(double mm) => mm / 1000;

    private static double Cm(double mm) => mm / 10;

    private static string F(double v, string formato) => v.ToString(formato, Inv);

    private static double? Numero(string? texto) =>
        double.TryParse(texto?.Trim().Replace(',', '.'), NumberStyles.Float, Inv, out double v) ? v : null;

    private static int Mm(string texto) => (int)Math.Round((Numero(texto) ?? 0) * 1000);
}
