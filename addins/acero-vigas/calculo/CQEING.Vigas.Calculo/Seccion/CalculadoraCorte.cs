using CQEING.Vigas.Calculo.Estribos;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Seccion;

/// <summary>
/// Dónde va el corte 1-1 y qué se ve en él (#91, decisiones 2 a 5 del plan docs/planes/2026-10-17-corte-vigas-91.md):
/// <list type="bullet">
/// <item>solo en los tramos, fuera de los apoyos y de los traslapos de cualquier capa (Carlos: "que no se deje colocar
/// en una zona de traslapo"), con <see cref="MargenMm"/> de margen en cada punta;</item>
/// <item>fuera de ahí salta al punto permitido más cercano ("estamos más cercanos acá, se pone para acá");</item>
/// <item>va de estribo en estribo, no de forma continua (Juan, 17-oct: "el arrastre debe ser no lineal sino por
/// estribo"; decisión 9): solo en los centros de los estribos cerrados que caen en lo permitido;</item>
/// <item>por defecto, en la sección que más largo ocupa: la vista tipo;</item>
/// <item>la proyección son los bastones que no pasan por el corte pero tocan la franja de la profundidad.</item>
/// </list>
/// </summary>
public static class CalculadoraCorte
{
    /// <summary>Para que el corte no caiga justo en la punta de una barra o en la cara de un apoyo.</summary>
    public const int MargenMm = 10;

    private const double Tolerancia = 1e-6;

    /// <summary>Los tramos donde se puede poner el corte, de izquierda a derecha y sin solaparse.</summary>
    public static IReadOnlyList<Intervalo> Permitidos(Portico p, ResultadoVigas rv)
    {
        List<(int Desde, int Hasta)> traslapos = rv.ZonasTraslapo.OrderBy(z => z.Desde).ToList();
        var permitidos = new List<Intervalo>();
        foreach (Tramo t in DistribuidorEstribos.Tramos(p))
        {
            double desde = t.CaraIzquierda;
            foreach ((int Desde, int Hasta) z in traslapos.Where(z => z.Hasta > t.CaraIzquierda && z.Desde < t.CaraDerecha))
            {
                Agregar(permitidos, desde, Math.Min(z.Desde, t.CaraDerecha));
                desde = Math.Max(desde, z.Hasta);
            }
            Agregar(permitidos, desde, t.CaraDerecha);
        }
        return permitidos;
    }

    /// <summary>
    /// <paramref name="x"/> si está permitido; si no, el punto permitido más cercano (en un empate, el de la derecha).
    /// Sin puntos permitidos, <paramref name="x"/> tal cual.
    /// </summary>
    public static double Ajustar(double x, IReadOnlyList<Intervalo> permitidos)
    {
        if (permitidos.Count == 0 || permitidos.Any(i => i.Desde - Tolerancia <= x && x <= i.Hasta + Tolerancia))
            return x;
        double mejor = x, distancia = double.MaxValue;
        foreach (double borde in permitidos.SelectMany(i => new[] { i.Desde, i.Hasta }))
        {
            double d = Math.Abs(borde - x);
            if (d < distancia - Tolerancia || (Math.Abs(d - distancia) <= Tolerancia && borde > mejor))
                (mejor, distancia) = (borde, d);
        }
        return mejor;
    }

    /// <summary>Los centros de los estribos cerrados que caen en lo permitido, de izquierda a derecha.</summary>
    public static IReadOnlyList<double> Puntos(ResultadoEstribos re, IReadOnlyList<Intervalo> permitidos) =>
        re.Estribos.Where(e => e.Id == MotorEstribos.IdCerrado).SelectMany(e => e.Posiciones)
            .Where(x => permitidos.Any(i => i.Desde - Tolerancia <= x && x <= i.Hasta + Tolerancia))
            .Distinct().Order().ToList();

    /// <summary>El punto más cercano a <paramref name="x"/> (en un empate, el de la derecha); sin puntos, <paramref name="x"/>.</summary>
    public static double AlPunto(double x, IReadOnlyList<double> puntos)
    {
        double mejor = x, distancia = double.MaxValue;
        foreach (double punto in puntos)
        {
            double d = Math.Abs(punto - x);
            if (d < distancia - Tolerancia || (Math.Abs(d - distancia) <= Tolerancia && punto > mejor))
                (mejor, distancia) = (punto, d);
        }
        return mejor;
    }

    /// <summary>
    /// La vista tipo (Carlos: "en una viga el 80 % del largo tiene la sección… el add-in elige solo esta sección"). Los
    /// tramos permitidos se parten en las puntas de los bastones y en los límites de los tramos; gana la firma (bastones
    /// de arriba, de abajo y estribos del tramo) que más largo suma, y en un empate la que aparece primero. El corte va
    /// al estribo de esa firma más cercano al centro de su pedazo más largo; sin estribos, a ese centro redondeado a 10 mm.
    /// </summary>
    public static double VistaTipo(Portico p, ResultadoVigas rv, ResultadoEstribos re, IReadOnlyList<Intervalo> permitidos)
    {
        IReadOnlyList<Tramo> tramos = DistribuidorEstribos.Tramos(p);
        List<double> puntas = rv.Bastones.SelectMany(b => new double[] { b.Desde, b.Hasta })
            .Concat(tramos.SelectMany(t => new[] { t.CaraIzquierda, t.CaraDerecha }))
            .Distinct().Order().ToList();

        var pedazos = new List<(Intervalo Pedazo, string Firma)>();
        foreach (Intervalo i in permitidos)
        {
            double desde = i.Desde;
            foreach (double hasta in puntas.Where(c => c > i.Desde && c < i.Hasta).Append(i.Hasta))
            {
                pedazos.Add((new Intervalo(desde, hasta), Firma((desde + hasta) / 2, tramos, rv, re)));
                desde = hasta;
            }
        }
        if (pedazos.Count == 0)
            return Centro(p);

        string ganadora = pedazos.GroupBy(x => x.Firma).OrderByDescending(g => g.Sum(x => Largo(x.Pedazo))).First().Key;
        Intervalo elegido = pedazos.Where(x => x.Firma == ganadora).MaxBy(x => Largo(x.Pedazo)).Pedazo;
        double centro = (elegido.Desde + elegido.Hasta) / 2;
        IReadOnlyList<double> puntos = Puntos(re, permitidos);
        if (puntos.Count > 0)
        {
            List<double> deLaFirma = puntos.Where(x => pedazos.Any(q => q.Firma == ganadora
                && q.Pedazo.Desde - Tolerancia <= x && x <= q.Pedazo.Hasta + Tolerancia)).ToList();
            return AlPunto(centro, deLaFirma.Count > 0 ? deLaFirma : puntos);
        }
        double minimo = Math.Ceiling(elegido.Desde / 10) * 10, maximo = Math.Floor(elegido.Hasta / 10) * 10;
        return minimo <= maximo ? Math.Clamp(Math.Round(centro / 10) * 10, minimo, maximo) : centro;
    }

    /// <summary>El corte que pidió el usuario, o la vista tipo, con lo que corta y lo que proyecta.</summary>
    public static CorteEnPunto En(Portico p, ResultadoVigas rv, ResultadoEstribos re, OpcionesCorte o)
    {
        IReadOnlyList<Intervalo> permitidos = Permitidos(p, rv);
        IReadOnlyList<double> puntos = Puntos(re, permitidos);
        bool sinLugar = permitidos.Count == 0;
        double x = sinLugar ? o.X ?? Centro(p)
            : o.X is int pedido ? (puntos.Count > 0 ? AlPunto(pedido, puntos) : Ajustar(pedido, permitidos))
            : VistaTipo(p, rv, re, permitidos);
        bool ajustado = !sinLugar && o.X is int pedidoX && Math.Abs(x - pedidoX) > 0.5;

        List<Baston> cortados = rv.Bastones.Where(b => Pasa(b, x)).ToList();
        double profundidad = o.Profundidad;
        List<Baston> proyectados = profundidad <= 0
            ? []
            : rv.Bastones.Where(b => !Pasa(b, x) && (o.Mirando == MiraCorte.Derecha
                ? b.Desde <= x + profundidad && b.Hasta > x
                : b.Hasta >= x - profundidad && b.Desde < x)).ToList();
        string tramo = TramoEn(x, DistribuidorEstribos.Tramos(p))?.Nombre ?? "";
        return new CorteEnPunto(x, o.X is null, ajustado, sinLugar, tramo, cortados, proyectados, permitidos, puntos);
    }

    private static void Agregar(List<Intervalo> permitidos, double desde, double hasta)
    {
        if (hasta - desde > 2 * MargenMm)
            permitidos.Add(new Intervalo(desde + MargenMm, hasta - MargenMm));
    }

    /// <summary>Lo que hace distinta una sección: los bastones que pasan por <paramref name="x"/> y los estribos de su tramo.</summary>
    private static string Firma(double x, IReadOnlyList<Tramo> tramos, ResultadoVigas rv, ResultadoEstribos re)
    {
        string Cara(Posicion cara) => string.Join(",", rv.Bastones.Where(b => b.Capa == cara && Pasa(b, x)).OrderBy(b => b.Desde)
            .Select(b => $"{b.Grupo.Cantidad}#{b.Grupo.Diametro}"));
        string tramo = TramoEn(x, tramos)?.Nombre ?? "";
        string estribos = string.Join(",", re.Estribos.Where(e => e.Tramo == tramo).Select(e => e.Id).Distinct().Order());
        return $"S:{Cara(Posicion.Superior)}|I:{Cara(Posicion.Inferior)}|E:{estribos}";
    }

    private static bool Pasa(Baston b, double x) => b.Desde <= x && x <= b.Hasta;

    private static Tramo? TramoEn(double x, IReadOnlyList<Tramo> tramos) =>
        tramos.FirstOrDefault(t => t.CaraIzquierda - Tolerancia <= x && x <= t.CaraDerecha + Tolerancia);

    private static double Largo(Intervalo i) => i.Hasta - i.Desde;

    private static double Centro(Portico p) =>
        (p.Apoyos[0].CaraIzquierda - p.VoladizoInicio + p.Apoyos[^1].CaraDerecha + p.VoladizoFinal) / 2.0;
}
