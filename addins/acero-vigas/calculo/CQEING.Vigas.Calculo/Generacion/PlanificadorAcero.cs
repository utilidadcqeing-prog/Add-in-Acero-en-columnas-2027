using CQEING.Vigas.Calculo.Estribos;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Seccion;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>
/// Convierte el cálculo del pórtico (<see cref="CalculoPortico"/>, el mismo del tablero) en lo que se crea en Revit (#85).
/// Decisiones del plan docs/planes/2026-10-19-generar-acero-vigas-85.md:
/// <list type="bullet">
/// <item>1: cada barra calculada sale una vez por cada barra de su capa en el corte, en todas las filas;</item>
/// <item>2: el traslapo va al lado: la segunda, cuarta… barra de cada capa se corre un diámetro hacia el centro, o hacia
/// afuera o abajo si al lado no deja la distancia libre (<see cref="AlLado"/>);</item>
/// <item>3: el host es la viga del punto medio de la parte recta, o la más cercana.</item>
/// </list>
/// </summary>
public static class PlanificadorAcero
{
    /// <summary>Null si el pórtico no se puede generar (como dice el tablero) o no se sabe dónde está en el modelo.</summary>
    public static PlanAcero? Planificar(string idPortico, PorticoLeido p, OpcionesTablero o, Reglas r)
    {
        if (p.Ubicacion is not { } u || CalculoPortico.Calcular(p, o, r) is not { SePuedeGenerar: true } c)
            return null;

        var barras = new List<PiezaLongitudinal>();
        foreach (SolucionCapa capa in new[] { c.Vigas.Superior!, c.Vigas.Inferior! })
        {
            CaraSeccion cara = capa.Capa == Posicion.Superior ? c.Estribos.Seccion.Superior : c.Estribos.Seccion.Inferior;
            List<BarraEnSeccion> corte = cara.Filas.SelectMany(f => f).ToList();
            List<BarraEnSeccion> alLado = AlLado(corte, c.Seccion, r);
            List<Barra> enOrden = capa.Barras.OrderBy(b => b.Desde).ToList();
            for (int i = 0; i < enOrden.Count; i++)
            {
                Barra b = enOrden[i];
                List<BarraEnSeccion> puestos = i % 2 == 1 ? alLado : corte;
                barras.AddRange(puestos.Select(s => new PiezaLongitudinal(b.Id, b.Capa, s.Diametro,
                    s.X, s.Y, b.Desde, b.Hasta, b.TipoInicio, b.TipoFin, Host(u, b.Desde, b.Hasta), b.Largo)));
            }
        }

        var bastones = new List<PiezaLongitudinal>();
        var sinLugar = new List<string>();
        foreach (Baston b in c.Vigas.Bastones)
        {
            UbicacionBastones enCorte = UbicadorBastones.Ubicar(c.Seccion, b.Capa, [b.Grupo]);
            if (enCorte.Error != ErrorCara.Ninguno || enCorte.Barras.Count == 0)
            {
                sinLugar.Add(b.Id);
                continue;
            }
            bastones.AddRange(enCorte.Barras.Select(s => new PiezaLongitudinal(b.Id, b.Capa, s.Barra.Diametro, s.Barra.X, s.Barra.Y,
                b.Desde, b.Hasta, b.TipoInicio, b.TipoFin, Host(u, b.Desde, b.Hasta), b.Largo)));
        }

        List<PiezaLongitudinal> anidadas = AnidarGanchos(barras.Concat(bastones).ToList(), r);
        barras = anidadas.Take(barras.Count).ToList();
        bastones = anidadas.Skip(barras.Count).ToList();

        List<GrupoEstribos> estribos = Estribos(c, p.Portico, o.Estribos, r, u);
        List<int> diametros = barras.Concat(bastones).Select(x => x.Diametro).Concat(estribos.Select(e => e.Diametro)).Distinct().Order().ToList();
        return new PlanAcero(idPortico, barras, bastones, sinLugar, estribos, diametros);
    }

    /// <summary>
    /// Carlos (21-oct, min 01:03:07): "son elementos sólidos que no se pueden traslapar". Una barra que va debajo de otra
    /// (arriba; encima, abajo) y llega a la misma punta que ese gancho lo atraviesa: termina antes, con todo su gancho por
    /// dentro del arranque del de afuera (un radio de doblado más un diámetro), y su largo baja lo mismo. Se recorren de la
    /// cara hacia adentro, así una tercera fila se mete dentro de la segunda ya corrida.
    /// </summary>
    private static List<PiezaLongitudinal> AnidarGanchos(List<PiezaLongitudinal> piezas, Reglas r)
    {
        const double tolerancia = 1e-6;
        PiezaLongitudinal[] p = piezas.ToArray();
        double D(PiezaLongitudinal x) => r.PorDiametro[x.Diametro].Diametro;
        // De la punta de la parte recta a la cara exterior del doblez (el gancho sale hacia la punta de la viga).
        double Vuelta(PiezaLongitudinal x, TipoGancho t) => t == TipoGancho.Sin ? 0 : DobladoDe(r.PorDiametro[x.Diametro], t) / 2 + D(x);

        foreach (bool inicio in new[] { true, false })
        {
            int sentido = inicio ? 1 : -1; // hacia adentro de la viga desde esa punta
            TipoGancho Gancho(PiezaLongitudinal x) => inicio ? x.GanchoInicio : x.GanchoFin;
            int Punta(PiezaLongitudinal x) => inicio ? x.Desde : x.Hasta;
            foreach (int[] capa in Enumerable.Range(0, p.Length).GroupBy(i => p[i].Capa).Select(g => g
                         .OrderByDescending(i => p[i].Capa == Posicion.Superior ? p[i].YCorte : -p[i].YCorte).ToArray()))
            {
                for (int k = 0; k < capa.Length; k++)
                {
                    PiezaLongitudinal adentro = p[capa[k]];
                    int punta = Punta(adentro);
                    foreach (PiezaLongitudinal afuera in capa.Take(k).Select(i => p[i]))
                    {
                        bool debajo = Math.Abs(afuera.XCorte - adentro.XCorte) < (D(afuera) + D(adentro)) / 2 - tolerancia
                            && Math.Abs(afuera.YCorte - adentro.YCorte) > tolerancia;
                        double vueltaAfuera = Vuelta(afuera, Gancho(afuera));
                        if (!debajo || vueltaAfuera == 0 || sentido * (punta - Punta(afuera)) >= vueltaAfuera + Vuelta(adentro, Gancho(adentro)))
                            continue;
                        int minima = Punta(afuera) + sentido * (int)Math.Ceiling(Vuelta(adentro, Gancho(adentro)) - tolerancia);
                        if (sentido * (minima - punta) > 0)
                            punta = minima;
                    }
                    int corrida = sentido * (punta - Punta(adentro));
                    if (corrida > 0)
                        p[capa[k]] = (inicio ? adentro with { Desde = punta } : adentro with { Hasta = punta }) with { Largo = adentro.Largo - corrida };
                }
            }
        }
        return p.ToList();
    }

    /// <summary>El diámetro de doblado del gancho del cuadro de Reglas; 6 diámetros si el cuadro no lo trae.</summary>
    private static double DobladoDe(ReglaDiametro regla, TipoGancho t) =>
        (t switch { TipoGancho.De180 => regla.Gancho180.D, TipoGancho.De135 => regla.Estribo135.D, _ => regla.Gancho90.D }) ?? 6 * regla.Diametro;

    /// <summary>
    /// Una corrida por cada zona de cada tramo y estribo (hallazgo E6: como el rótulo del tablero), con las caras del corte
    /// (las mismas en todo el pórtico). La configuración de cada tramo es la suya si está marcado, o la general.
    /// </summary>
    private static List<GrupoEstribos> Estribos(PorticoCalculado calculo, Portico portico, ConfigEstribos general, Reglas r, UbicacionPortico u)
    {
        ResultadoEstribos re = calculo.Estribos;
        Dictionary<string, Tramo> tramos = DistribuidorEstribos.Tramos(portico).ToDictionary(t => t.Nombre);
        var grupos = new List<GrupoEstribos>();
        foreach (EstriboEnPortico e in re.Estribos)
        {
            if (re.EnSeccion.FirstOrDefault(s => s.Id == e.Id) is not { } caras)
                continue;
            ConfigEstribos config = calculo.PorTramo?.GetValueOrDefault(e.Tramo) ?? general;
            IReadOnlyList<Corrida> corridas = tramos.TryGetValue(e.Tramo, out Tramo? tramo)
                ? CorridasEstribos.PorZonas(e.Posiciones, DistribuidorEstribos.Zonas(tramo, config, r.PorDiametro[config.Diametro].Diametro))
                : CorridasEstribos.Partir(e.Posiciones);
            Rama? rama = e.Tipo == TipoEstribo.Abierto ? RamaDe(re.Seccion, caras.Izquierda, r) : null;
            // Carlos (21-oct, min 01:03:07): "son elementos sólidos que no se pueden traslapar". En el plano del cerrado, los
            // ganchos del adicional lo cruzan: el adicional n va pegado al lado, n diámetros de estribo más allá.
            double desfase = e.Id == MotorEstribos.IdCerrado ? 0 : int.Parse(e.Id[MotorEstribos.IdCerrado.Length..]) * r.PorDiametro[e.Diametro].Diametro;
            corridas = corridas.Select(c => c with { Primero = c.Primero + desfase }).ToList();
            grupos.AddRange(corridas.Select(c => new GrupoEstribos(e.Id, e.Tramo, e.Tipo, e.Diametro, e.Gancho,
                caras.Izquierda, caras.Derecha, caras.Abajo, caras.Arriba, c,
                MarcoPortico.VigaEn(u, c.Primero + (c.Cantidad - 1) * c.Separacion / 2) ?? throw new InvalidOperationException("El pórtico no tiene vigas."),
                rama)));
        }
        return grupos;
    }

    /// <summary>
    /// Las barras que abraza un abierto en <paramref name="x"/>: la de arriba más alta y la de abajo más baja en esa x (las
    /// de la fila de cada cara), ya alineadas por el motor (hallazgo E13).
    /// </summary>
    private static Rama? RamaDe(ResultadoSeccion s, double x, Reglas r)
    {
        BarraEnSeccion? arriba = s.Superior.Filas.SelectMany(f => f).Where(b => Math.Abs(b.X - x) < 0.5).MaxBy(b => b.Y);
        BarraEnSeccion? abajo = s.Inferior.Filas.SelectMany(f => f).Where(b => Math.Abs(b.X - x) < 0.5).MinBy(b => b.Y);
        if (arriba is null || abajo is null)
            return null;
        return new Rama(x, arriba.Y, abajo.Y, Math.Max(r.PorDiametro[arriba.Diametro].Diametro, r.PorDiametro[abajo.Diametro].Diametro));
    }

    /// <summary>
    /// El puesto de cada barra del corte cuando es la que traslapa, en contacto con la suya (decisión 2). Prueba, en orden:
    /// un diámetro hacia el centro (la que está justo en el centro, hacia +X), hacia afuera y abajo, es decir hacia adentro
    /// de la viga (Carlos: "cuando la barra traslapada no cumple la distancia libre al lado, iría abajo"). Se queda con el
    /// primero que no se sale de las esquinas, no se monta en otra barra y deja la distancia libre con las otras que
    /// traslapan, que la acompañan en todo su largo. Si ninguno la deja (la viga es angosta), con el que más libre deja.
    /// Se ubican de las esquinas hacia adentro, porque las de las esquinas solo tienen un lado.
    /// </summary>
    private static List<BarraEnSeccion> AlLado(List<BarraEnSeccion> corte, EntradaSeccion e, Reglas r)
    {
        const double tolerancia = 1e-6;
        double D(BarraEnSeccion b) => r.PorDiametro[b.Diametro].Diametro;
        double Libre(BarraEnSeccion a, BarraEnSeccion b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)) - (D(a) + D(b)) / 2;
        double izquierda = corte.Min(b => b.X), derecha = corte.Max(b => b.X);

        var corridas = new BarraEnSeccion?[corte.Count];
        foreach (int i in Enumerable.Range(0, corte.Count).OrderByDescending(i => Math.Abs(corte[i].X - e.Ancho / 2)))
        {
            BarraEnSeccion s = corte[i];
            double alCentro = s.X > e.Ancho / 2 ? -D(s) : D(s), adentro = s.Y > e.Alto / 2 ? -D(s) : D(s);
            List<BarraEnSeccion> yaCorridas = corridas.OfType<BarraEnSeccion>().ToList();
            List<BarraEnSeccion> todas = corte.Where((_, k) => k != i).Concat(yaCorridas).ToList();
            List<(BarraEnSeccion Puesto, double Libre, double EntreCorridas)> candidatos =
                new[] { s with { X = s.X + alCentro }, s with { X = s.X - alCentro }, s with { Y = s.Y + adentro } }
                    .Where(p => p.X >= izquierda - tolerancia && p.X <= derecha + tolerancia)
                    .Select(p => (p, todas.Count == 0 ? double.MaxValue : todas.Min(o => Libre(p, o)),
                        yaCorridas.Count == 0 ? double.MaxValue : yaCorridas.Min(o => Libre(p, o))))
                    .ToList();
            corridas[i] = candidatos
                .FirstOrDefault(c => c.Libre >= -tolerancia && c.EntreCorridas >= r.DistanciaLibre - tolerancia).Puesto
                ?? candidatos.MaxBy(c => Math.Min(c.Libre, c.EntreCorridas)).Puesto;
        }
        return corridas.Select(b => b!).ToList();
    }

    private static string Host(UbicacionPortico u, int desde, int hasta) =>
        MarcoPortico.VigaEn(u, (desde + hasta) / 2.0) ?? throw new InvalidOperationException("El pórtico no tiene vigas.");
}
