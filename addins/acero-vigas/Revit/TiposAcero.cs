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
/// <item>el gancho se busca por ángulo y estilo, no por nombre. Si falta, se crea uno propio (<c>CQEING …</c>).
/// <b>Nunca se cambia un tipo que ya tiene el proyecto</b> (Juan, 2026-10-19).</item>
/// </list>
/// </summary>
internal sealed class TiposAcero
{
    private const double ToleranciaAngulo = 0.01; // rad

    private readonly Document doc;
    private readonly Dictionary<int, RebarBarType> barras;
    private readonly Dictionary<(RebarStyle, int), RebarHookType> ganchos = [];

    private TiposAcero(Document doc, Dictionary<int, RebarBarType> barras) => (this.doc, this.barras) = (doc, barras);

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
        return new TiposAcero(doc, barras);
    }

    /// <summary>Null si el modelo no tiene un tipo para ese diámetro (la sesión ya salta esos pórticos).</summary>
    public RebarBarType? Barra(int diametro) => barras.GetValueOrDefault(diametro);

    /// <summary>El gancho de una punta de barra o bastón; null = sin gancho. Puede crear un tipo: dentro de una transacción.</summary>
    public RebarHookType? Gancho(TipoGancho t) => t switch
    {
        TipoGancho.De90 => Buscar(RebarStyle.Standard, 90),
        TipoGancho.De135 => Buscar(RebarStyle.Standard, 135),
        TipoGancho.De180 => Buscar(RebarStyle.Standard, 180),
        _ => null,
    };

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
