using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>De qué es un <see cref="Trazo"/>: la grapa es el estribo abierto.</summary>
public enum TipoTrazo
{
    Barra,
    Baston,
    Estribo,
    Grapa,
}

/// <summary>
/// Una barra de la vista previa 3D: su eje, en mm del modelo, con los dobleces ya partidos en tramos rectos.
/// <paramref name="Numero"/> es el diámetro en octavos (para el color) y <paramref name="Diametro"/>, el real en mm.
/// <paramref name="Pieza"/> es el id del tablero (<c>S1</c>, <c>BS1</c>, <c>E</c>, <c>E1</c>) y <paramref name="Tramo"/>,
/// el del estribo (null en las piezas longitudinales): con ellos se dice qué toca a qué en los choques.
/// </summary>
public sealed record Trazo(IReadOnlyList<Punto3> Puntos, int Numero, double Diametro, string Pieza, TipoTrazo Tipo, string? Tramo = null);

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
/// <item>el abierto es una rama al lado de sus barras con un gancho que abraza cada una (hallazgo E13b; desde el 21-oct
/// el doblez toca la barra en vez de centrarse en ella).</item>
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
        var trazos = new List<Trazo>();
        foreach (PiezaLongitudinal p in plan.Barras)
            trazos.Add(Longitudinal(p, TipoTrazo.Barra, u, r, Doblado(r, p.Diametro, doblados)));
        foreach (PiezaLongitudinal p in plan.Bastones)
            trazos.Add(Longitudinal(p, TipoTrazo.Baston, u, r, Doblado(r, p.Diametro, doblados)));
        foreach (GrupoEstribos g in plan.Estribos)
        {
            DobladoBarra d = Doblado(r, g.Diametro, doblados);
            if (EnElCorte(g, u.Ancho, r, d) is not { } corte)
                continue;
            TipoTrazo tipo = g.Tipo == TipoEstribo.Cerrado ? TipoTrazo.Estribo : TipoTrazo.Grapa;
            for (int i = 0; i < g.Corrida.Cantidad; i++)
            {
                double x = g.Corrida.Primero + i * g.Corrida.Separacion;
                trazos.Add(new Trazo(corte.Select(c => MarcoPortico.AlModelo(u, x, c.X, c.Y)).ToList(), g.Diametro, d.Diametro, g.Pieza, tipo, g.Tramo));
            }
        }
        return trazos;
    }

    /// <summary>
    /// El estribo en el corte, por su eje y con sus ganchos, en mm (x desde la cara izquierda, y desde la de abajo): el
    /// mismo trazo que la vista previa 3D y que el corte 1-1 del tablero. Null si es un abierto sin sus barras.
    /// </summary>
    /// <param name="d">El doblado del modelo; sin él, el diámetro de las reglas y doblado de 4 d.</param>
    public static List<(double X, double Y)>? EnElCorte(GrupoEstribos g, double anchoViga, Reglas r, DobladoBarra? d = null)
    {
        d ??= Doblado(r, g.Diametro, null);
        (int grados, int? e) = GanchoPropio.DeEstribo(g.Gancho, r.PorDiametro[g.Diametro]);
        double cola = e ?? 6 * d.Diametro;
        return g.Tipo == TipoEstribo.Cerrado ? Cerrado(g, d, grados, cola) : Abierto(g, anchoViga, d, grados, cola);
    }

    private static DobladoBarra Doblado(Reglas r, int n, IReadOnlyDictionary<int, DobladoBarra>? doblados) =>
        doblados?.GetValueOrDefault(n) is { } d ? d
            : new DobladoBarra(r.PorDiametro[n].Diametro, 4 * r.PorDiametro[n].Diametro, 4 * r.PorDiametro[n].Diametro);

    /// <summary>La parte recta en (XCorte, YCorte), con un gancho en cada punta que lo lleve, doblando hacia adentro.</summary>
    private static Trazo Longitudinal(PiezaLongitudinal p, TipoTrazo tipo, UbicacionPortico u, Reglas r, DobladoBarra d)
    {
        // En el plano (x a lo largo, y del corte). s: hacia dónde dobla el gancho (abajo en la capa superior).
        double s = p.Capa == Posicion.Superior ? -1 : 1;
        var puntos = new List<(double X, double Y)>();
        puntos.AddRange(Gancho(p.GanchoInicio, p.Desde, -1).AsEnumerable().Reverse());
        puntos.Add((p.Desde, p.YCorte));
        puntos.Add((p.Hasta, p.YCorte));
        puntos.AddRange(Gancho(p.GanchoFin, p.Hasta, 1));
        return new Trazo(puntos.Select(q => MarcoPortico.AlModelo(u, q.X, p.XCorte, q.Y)).ToList(), p.Diametro, d.Diametro, p.Pieza, tipo);

        // afuera: −1 en el inicio (la barra sigue hacia −x), +1 en el fin. Del doblez a la punta de la cola.
        List<(double X, double Y)> Gancho(TipoGancho tipo, double punta, double afuera)
        {
            int grados = tipo switch { TipoGancho.De90 => 90, TipoGancho.De135 => 135, TipoGancho.De180 => 180, _ => 0 };
            if (grados == 0)
                return [];
            double radio = d.Gancho / 2 + d.Diametro / 2;
            double cola = GanchoPropio.Extension(r.LargoGanchoRedondeado(r.PorDiametro[p.Diametro], tipo), d.Diametro, d.Gancho, grados);
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
    /// La rama al lado de sus dos barras, con un gancho que abraza cada barra (la toca por fuera) y la cola hacia el núcleo.
    /// En C la rama va del lado del centro de la viga en las dos barras. En S, abajo pasa al otro lado de la barra (hallazgo
    /// H6 del plan de 22-oct): la rama queda un poco en diagonal y cada gancho dobla hacia un lado. Null si el plan no
    /// trae sus barras.
    /// </summary>
    private static List<(double X, double Y)>? Abierto(GrupoEstribos g, double anchoViga, DobladoBarra d, int grados, double cola)
    {
        if (g.Rama is not { } rama)
            return null;
        // Se dobla como estribo (H8 del plan de 22-oct: con el doblado de gancho, 6 d, el de 180° no cabía entre barras).
        double r = d.Estribo / 2 + d.Diametro / 2;
        (int haciaAbajo, int hacia) = rama.Lados(anchoViga, g.Forma);
        // El doblez abraza la barra: su cara interior toca la barra por encima (abajo, por debajo), así que su centro va
        // medio doblado menos medio diámetro de barra hacia adentro (centrado en la barra, el gancho cruzaba el cerrado).
        double alCentro = d.Estribo / 2 - rama.Diametro / 2;
        double yArriba = rama.YArriba - alCentro, yAbajo = rama.YAbajo + alCentro;
        // Ángulos medidos desde la rama: 180° (o 0°) es el punto de la rama a la altura del centro del doblez. El giro
        // pasa por encima de la barra de arriba (por debajo de la de abajo), hacia el lado de la barra.
        (double desde, double giro) Lado(int h) => h > 0 ? (0, 1) : (180, -1);
        (double desdeAbajo, double giroAbajo) = Lado(haciaAbajo);
        (double desdeArriba, double giroArriba) = Lado(hacia);
        List<(double X, double Y)> abajo = Arco((rama.X, yAbajo), r, desdeAbajo - giroAbajo * grados, desdeAbajo);
        List<(double X, double Y)> arriba = Arco((rama.X, yArriba), r, desdeArriba, desdeArriba + giroArriba * grados);
        var lazo = new List<(double X, double Y)>
        {
            Mas(abajo[0], Tangente(desdeAbajo - giroAbajo * grados), -giroAbajo * cola),
        };
        lazo.AddRange(abajo);
        lazo.AddRange(arriba);
        lazo.Add(Mas(arriba[^1], Tangente(desdeArriba + giroArriba * grados), giroArriba * cola));
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
