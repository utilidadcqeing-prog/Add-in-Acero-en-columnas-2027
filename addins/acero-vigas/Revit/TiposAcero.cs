using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Revit;

/// <summary>
/// Los tipos de barra y de gancho del documento para generar el acero (#85, decisiones 7 y 8 del plan
/// docs/planes/2026-10-19-generar-acero-vigas-85.md):
/// <list type="bullet">
/// <item>la barra de un diámetro es el tipo cuyo diámetro nominal está a menos de 0.5 mm del de la regla; entre varios,
/// el que se llama <c>#n</c>, luego el que lo contiene en el nombre, luego el más cercano;</item>
/// <item>las barras y los bastones usan ganchos propios por ángulo y diámetro (<see cref="GanchoPropio"/>, decisión 8b),
/// para que Revit mida lo mismo que el tablero;</item>
/// <item>los estribos, por ahora, el gancho del proyecto por ángulo y estilo; si falta, uno propio (tarea 8).</item>
/// <item><b>Nunca se cambia un tipo que ya tiene el proyecto</b> (Juan, 2026-10-19).</item>
/// </list>
/// </summary>
internal sealed class TiposAcero
{
    private const double ToleranciaAngulo = 0.01; // rad

    private readonly Document doc;
    private readonly Reglas reglas;
    private readonly Dictionary<int, RebarBarType> barras;
    private readonly Dictionary<(RebarStyle, int), RebarHookType> ganchos = [];
    private readonly Dictionary<(int Grados, int Numero), RebarHookType> propios = [];

    private TiposAcero(Document doc, Reglas reglas, Dictionary<int, RebarBarType> barras) => (this.doc, this.reglas, this.barras) = (doc, reglas, barras);

    public static TiposAcero Leer(Document doc, Reglas r)
    {
        List<RebarBarType> tipos = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().ToList();
        var barras = new Dictionary<int, RebarBarType>();
        foreach ((int numero, ReglaDiametro regla) in r.PorDiametro)
        {
            RebarBarType? elegido = tipos
                .Where(t => Math.Abs(LectorModelo.Mm(t.BarNominalDiameter) - regla.Diametro) <= DiametrosDisponibles.Tolerancia)
                .OrderBy(t => t.Name.Trim() == $"#{numero}" ? 0 : t.Name.Contains($"#{numero}") ? 1 : 2)
                .ThenBy(t => Math.Abs(LectorModelo.Mm(t.BarNominalDiameter) - regla.Diametro))
                .FirstOrDefault();
            if (elegido is not null)
                barras[numero] = elegido;
        }
        return new TiposAcero(doc, r, barras);
    }

    /// <summary>Null si el modelo no tiene un tipo para ese diámetro (la sesión ya salta esos pórticos).</summary>
    public RebarBarType? Barra(int diametro) => barras.GetValueOrDefault(diametro);

    /// <summary>
    /// El gancho propio de una punta de barra o bastón del #<paramref name="numero"/> (decisión 8b): <c>"CQEING 90° #5"</c>,
    /// con la extensión que da el B del cuadro en el tipo de barra del modelo. Null = sin gancho. Crea o ajusta el tipo:
    /// dentro de una transacción.
    /// </summary>
    public RebarHookType? Gancho(TipoGancho t, int numero)
    {
        int grados = t switch { TipoGancho.De90 => 90, TipoGancho.De135 => 135, TipoGancho.De180 => 180, _ => 0 };
        if (grados == 0 || Barra(numero) is not { } barra)
            return null;
        if (propios.TryGetValue((grados, numero), out RebarHookType? ya))
            return ya;

        double d = LectorModelo.Mm(barra.BarNominalDiameter);
        double extension = GanchoPropio.Extension(Reglas.LargoGancho(reglas.PorDiametro[numero], t), d,
            LectorModelo.Mm(barra.StandardHookBendDiameter), grados);
        string nombre = GanchoPropio.Nombre(grados, numero);
        RebarHookType gancho = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .FirstOrDefault(h => h.Name == nombre) ?? RebarHookType.Create(doc, grados * Math.PI / 180, extension / d);
        // Es del add-in: si cambió el tipo de barra o las reglas, se pone al día. Los del proyecto nunca se tocan.
        gancho.Name = nombre;
        gancho.Style = RebarStyle.Standard;
        if (Math.Abs(gancho.StraightLineMultiplier - extension / d) > 1e-6)
            gancho.StraightLineMultiplier = extension / d;
        propios[(grados, numero)] = gancho;
        return gancho;
    }

    /// <summary>El gancho de los estribos. Puede crear un tipo: dentro de una transacción.</summary>
    public RebarHookType GanchoEstribo(GanchoEstribo g) => Buscar(RebarStyle.StirrupTie, g == Calculo.Modelo.GanchoEstribo.De135 ? 135 : 90);

    private RebarHookType Buscar(RebarStyle estilo, int grados)
    {
        if (ganchos.TryGetValue((estilo, grados), out RebarHookType? ya))
            return ya;
        double angulo = grados * Math.PI / 180;
        RebarHookType gancho = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .Where(h => h.Style == estilo && Math.Abs(h.HookAngle - angulo) <= ToleranciaAngulo)
            .OrderBy(h => h.Name)
            .FirstOrDefault() ?? Crear(estilo, grados, angulo);
        ganchos[(estilo, grados)] = gancho;
        return gancho;
    }

    /// <summary>Un tipo nuevo y propio; no toca los del proyecto. Largo recto: 12 diámetros en barras y 6 en estribos.</summary>
    private RebarHookType Crear(RebarStyle estilo, int grados, double angulo)
    {
        RebarHookType nuevo = RebarHookType.Create(doc, angulo, estilo == RebarStyle.StirrupTie ? 6 : 12);
        nuevo.Style = estilo;
        nuevo.Name = estilo == RebarStyle.StirrupTie ? $"CQEING Estribo {grados}°" : $"CQEING {grados}°";
        return nuevo;
    }
}
