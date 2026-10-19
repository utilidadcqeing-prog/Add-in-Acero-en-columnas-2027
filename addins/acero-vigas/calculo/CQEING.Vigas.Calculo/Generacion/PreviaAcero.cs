using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>
/// Una barra de la vista previa 3D: su eje, en mm del modelo, con los dobleces ya partidos en tramos rectos.
/// <paramref name="Numero"/> es el diámetro en octavos (para el color) y <paramref name="Diametro"/>, el real en mm.
/// </summary>
public sealed record Trazo(IReadOnlyList<Punto3> Puntos, int Numero, double Diametro);

/// <summary>
/// El diámetro de una barra del modelo y los de doblado de su gancho y de su estribo, en mm (de su tipo en Revit).
/// </summary>
public sealed record DobladoBarra(double Diametro, double Gancho, double Estribo);

/// <summary>
/// La vista previa 3D del acero (el botón "3D"): el mismo plan que crea "Generar" (<see cref="PlanAcero"/>), dibujado como
/// lo deja Revit, sin crear nada en el modelo. Las medidas salen de las pruebas en Revit del plan
/// docs/planes/2026-10-19-generar-acero-vigas-85.md:
/// <list type="bullet">
/// <item>el gancho de una barra arranca en la punta de su parte recta y dobla hacia adentro de la viga, con el radio del
/// doblado del tipo más medio diámetro y la cola de <see cref="GanchoPropio.Extension"/>;</item>
/// <item>el cerrado tiene los dos ganchos en la esquina de arriba a la izquierda, dando la vuelta a la barra de esquina, y
/// las colas entran al núcleo (hallazgo E2);</item>
/// <item>el abierto es una rama al lado de sus barras con un gancho centrado en cada una (hallazgo E13b).</item>
/// </list>
/// Es un dibujo: no reproduce el milímetro que Revit corre la barra en la esquina de los ganchos.
/// </summary>
public static class PreviaAcero
{
    /// <summary>Grados de arco por tramo recto al partir un doblez.</summary>
    private const double PasoArco = 15;

    /// <param name="doblados">El de cada diámetro del modelo; uno que falte usa el diámetro de las reglas y doblado de 4d.</param>
    public static IReadOnlyList<Trazo> Trazos(PlanAcero plan, UbicacionPortico u, Reglas r, IReadOnlyDictionary<int, DobladoBarra>? doblados = null)
    {
        DobladoBarra Doblado(int n) => doblados?.GetValueOrDefault(n) is { } d ? d
            : new DobladoBarra(r.PorDiametro[n].Diametro, 4 * r.PorDiametro[n].Diametro, 4 * r.PorDiametro[n].Diametro);

        var trazos = new List<Trazo>();
        foreach (PiezaLongitudinal p in plan.Barras.Concat(plan.Bastones))
            trazos.Add(Longitudinal(p, u, r.PorDiametro[p.Diametro], Doblado(p.Diametro)));
        foreach (GrupoEstribos g in plan.Estribos)
        {
            DobladoBarra d = Doblado(g.Diametro);
            int grados = g.Gancho == GanchoEstribo.De135 ? 135 : 90;
            double cola = grados == 135 && r.PorDiametro[g.Diametro].Estribo135.E is int e ? e : 6 * d.Diametro;
            List<(double X, double Y)>? corte = g.Tipo == TipoEstribo.Cerrado ? Cerrado(g, d, grados, cola) : Abierto(g, u, d, grados, cola);
            if (corte is null)
                continue;
            for (int i = 0; i < g.Corrida.Cantidad; i++)
            {
                double x = g.Corrida.Primero + i * g.Corrida.Separacion;
                trazos.Add(new Trazo(corte.Select(c => MarcoPortico.AlModelo(u, x, c.X, c.Y)).ToList(), g.Diametro, d.Diametro));
            }
        }
        return trazos;
    }

    /// <summary>La parte recta en (XCorte, YCorte), con un gancho en cada punta que lo lleve, doblando hacia adentro.</summary>
    private static Trazo Longitudinal(PiezaLongitudinal p, UbicacionPortico u, ReglaDiametro regla, DobladoBarra d)
    {
        // En el plano (x a lo largo, y del corte). s: hacia dónde dobla el gancho (abajo en la capa superior).
        double s = p.Capa == Posicion.Superior ? -1 : 1;
        var puntos = new List<(double X, double Y)>();
        puntos.AddRange(Gancho(p.GanchoInicio, p.Desde, -1).AsEnumerable().Reverse());
        puntos.Add((p.Desde, p.YCorte));
        puntos.Add((p.Hasta, p.YCorte));
        puntos.AddRange(Gancho(p.GanchoFin, p.Hasta, 1));
        return new Trazo(puntos.Select(q => MarcoPortico.AlModelo(u, q.X, p.XCorte, q.Y)).ToList(), p.Diametro, d.Diametro);

        // afuera: −1 en el inicio (la barra sigue hacia −x), +1 en el fin. Del doblez a la punta de la cola.
        List<(double X, double Y)> Gancho(TipoGancho tipo, double punta, double afuera)
        {
            int grados = tipo switch { TipoGancho.De90 => 90, TipoGancho.De135 => 135, TipoGancho.De180 => 180, _ => 0 };
            if (grados == 0)
                return [];
            double radio = d.Gancho / 2 + d.Diametro / 2;
            double cola = GanchoPropio.Extension(Reglas.LargoGancho(regla, tipo), d.Diametro, d.Gancho, grados);
            var gancho = new List<(double, double)>();
            foreach (double phi in Angulos(grados).Skip(1))
                gancho.Add((punta + afuera * radio * Math.Sin(phi), p.YCorte + s * radio * (1 - Math.Cos(phi))));
            double fin = grados * Math.PI / 180;
            (double ux, double uy) = (afuera * Math.Cos(fin), s * Math.Sin(fin));
            (double X, double Y) ultimo = gancho[^1];
            gancho.Add((ultimo.X + ux * cola, ultimo.Y + uy * cola));
            return gancho;
        }
    }

    /// <summary>
    /// El lazo en el corte por el eje del estribo, en el sentido de las agujas: cola, gancho alrededor de la esquina de
    /// arriba a la izquierda, arriba, derecha, abajo, izquierda y el otro gancho alrededor de la misma esquina, con su cola.
    /// </summary>
    private static List<(double X, double Y)> Cerrado(GrupoEstribos g, DobladoBarra d, int grados, double cola)
    {
        double izq = g.Izquierda + d.Diametro / 2, der = g.Derecha - d.Diametro / 2;
        double abajo = g.Abajo + d.Diametro / 2, arriba = g.Arriba - d.Diametro / 2;
        double r = d.Estribo / 2 + d.Diametro / 2;
        (double X, double Y) esquina = (izq + r, arriba - r);

        var lazo = new List<(double X, double Y)>();
        List<(double X, double Y)> primero = Arco(esquina, r, 90 + grados, 90);
        // los arcos van en el sentido de las agujas (contra la tangente): la cola de entrada queda del lado de la tangente,
        // hacia el núcleo, y la de salida, del otro
        lazo.Add(Mas(primero[0], Tangente(90 + grados), cola));
        lazo.AddRange(primero);
        lazo.AddRange(Arco((der - r, arriba - r), r, 90, 0));
        lazo.AddRange(Arco((der - r, abajo + r), r, 0, -90));
        lazo.AddRange(Arco((izq + r, abajo + r), r, -90, -180));
        List<(double X, double Y)> segundo = Arco(esquina, r, 180, 180 - grados);
        lazo.AddRange(segundo);
        lazo.Add(Mas(segundo[^1], Tangente(180 - grados), -cola));
        return lazo;
    }

    /// <summary>
    /// La rama al lado de sus dos barras, del lado del centro de la viga, con un gancho alrededor de cada barra (centrado en
    /// ella) y la cola hacia el núcleo. Null si el plan no trae sus barras.
    /// </summary>
    private static List<(double X, double Y)>? Abierto(GrupoEstribos g, UbicacionPortico u, DobladoBarra d, int grados, double cola)
    {
        if (g.Rama is not { } rama)
            return null;
        double r = d.Gancho / 2 + d.Diametro / 2;
        double hacia = rama.X <= u.Ancho / 2 ? 1 : -1; // de la barra hacia el centro: ahí va la rama
        double xRama = rama.X + hacia * r;
        // Ángulos medidos desde la rama: 180° (o 0°) es el punto de la rama a la altura de la barra.
        double desde = hacia > 0 ? 0 : 180;
        double giro = hacia > 0 ? 1 : -1; // arriba gira pasando por encima de la barra, hacia el lado de la barra
        List<(double X, double Y)> abajo = Arco((rama.X, rama.YAbajo), r, desde - giro * grados, desde);
        List<(double X, double Y)> arriba = Arco((rama.X, rama.YArriba), r, desde, desde + giro * grados);
        var lazo = new List<(double X, double Y)>
        {
            Mas(abajo[0], Tangente(desde - giro * grados), -giro * cola),
        };
        lazo.AddRange(abajo);
        lazo.Add((xRama, rama.YArriba));
        lazo.AddRange(arriba.Skip(1));
        lazo.Add(Mas(arriba[^1], Tangente(desde + giro * grados), giro * cola));
        return lazo;
    }

    /// <summary>Puntos de un arco de <paramref name="de"/> a <paramref name="a"/> grados (en cualquier sentido), con sus puntas.</summary>
    private static List<(double X, double Y)> Arco((double X, double Y) c, double r, double de, double a)
    {
        int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(a - de) / PasoArco));
        return Enumerable.Range(0, n + 1)
            .Select(i => (de + (a - de) * i / n) * Math.PI / 180)
            .Select(t => (c.X + r * Math.Cos(t), c.Y + r * Math.Sin(t)))
            .ToList();
    }

    /// <summary>La tangente de un arco en <paramref name="grados"/>, en el sentido en que crece el ángulo.</summary>
    private static (double X, double Y) Tangente(double grados)
    {
        double t = grados * Math.PI / 180;
        return (-Math.Sin(t), Math.Cos(t));
    }

    private static (double X, double Y) Mas((double X, double Y) p, (double X, double Y) v, double largo) => (p.X + v.X * largo, p.Y + v.Y * largo);

    private static IEnumerable<double> Angulos(int grados)
    {
        int n = Math.Max(1, (int)Math.Ceiling(grados / PasoArco));
        return Enumerable.Range(0, n + 1).Select(i => grados * Math.PI / 180 * i / n);
    }
}
