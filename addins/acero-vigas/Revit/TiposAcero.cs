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
/// <item>los estribos, un gancho propio de estribo por ángulo y diámetro, con la extensión E del cuadro (tarea 8);</item>
/// <item><b>Nunca se cambia un tipo que ya tiene el proyecto</b> (Juan, 2026-10-19).</item>
/// </list>
/// </summary>
internal sealed class TiposAcero
{
    private readonly Document doc;
    private readonly Reglas reglas;
    private readonly Dictionary<int, RebarBarType> barras;
    private readonly Dictionary<string, RebarHookType> propios = [];

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

        double d = LectorModelo.Mm(barra.BarNominalDiameter);
        double extension = GanchoPropio.Extension(Reglas.LargoGancho(reglas.PorDiametro[numero], t), d,
            LectorModelo.Mm(barra.StandardHookBendDiameter), grados);
        return Propio(GanchoPropio.Nombre(grados, numero), RebarStyle.Standard, grados, extension / d);
    }

    /// <summary>
    /// El gancho propio de los estribos del #<paramref name="numero"/> (<c>"CQEING Estribo 135° #3"</c>, estilo de estribo):
    /// extensión recta E del cuadro de Carlos (57 mm en el 135° del #3, 65 mm en el 180°), o 6 diámetros si el cuadro no
    /// la trae (#6 a #8 en 135°, y el de 90°). Crea o ajusta el tipo: dentro de una transacción.
    /// </summary>
    public RebarHookType GanchoEstribo(GanchoEstribo g, int numero)
    {
        (int grados, int? e) = GanchoPropio.DeEstribo(g, reglas.PorDiametro[numero]);
        double d = Barra(numero) is { } barra ? LectorModelo.Mm(barra.BarNominalDiameter) : reglas.PorDiametro[numero].Diametro;
        return Propio(GanchoPropio.Nombre(grados, numero, estribo: true), RebarStyle.StirrupTie, grados, e is int cola ? cola / d : 6);
    }

    /// <summary>
    /// El gancho del estribo abierto (<c>"CQEING Grapa 135° #3 estribo"</c>), con la cola de estribo: la extensión E del
    /// cuadro (57 mm en el 135° del #3, 65 mm en el 180°) o 6 diámetros. Con la cola de barra (B completo, 84 mm) la punta
    /// quedaba a 25 mm de la cara de la viga (sexta prueba). Revit, sin este tipo, deja la cola de 180° en 4 d (diagnóstico
    /// de la grapa en S, 20-oct). Estilo de estribo, como la grapa (H8 del plan de 22-oct). El nombre es otro que el de
    /// los tipos de estilo de barra que dejaron las versiones anteriores, para no cambiarles el estilo a las grapas que ya
    /// los usan.
    /// </summary>
    public RebarHookType? GanchoGrapa(GanchoEstribo g, int numero)
    {
        if (Barra(numero) is not { } barra)
            return null;
        (int grados, int? e) = GanchoPropio.DeEstribo(g, reglas.PorDiametro[numero]);
        double d = LectorModelo.Mm(barra.BarNominalDiameter);
        return Propio($"CQEING Grapa {grados}° #{numero} estribo", RebarStyle.StirrupTie, grados, e is int cola ? cola / d : 6);
    }

    /// <summary>
    /// El tipo del add-in con ese nombre, creado o puesto al día (si cambió el tipo de barra o las reglas): es del add-in.
    /// Los del proyecto nunca se tocan. Se resuelve una vez por generación.
    /// </summary>
    private RebarHookType Propio(string nombre, RebarStyle estilo, int grados, double multiplicador)
    {
        if (propios.TryGetValue(nombre, out RebarHookType? ya))
            return ya;
        RebarHookType gancho = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .FirstOrDefault(h => h.Name == nombre) ?? RebarHookType.Create(doc, grados * Math.PI / 180, multiplicador);
        gancho.Name = nombre;
        gancho.Style = estilo;
        if (Math.Abs(gancho.StraightLineMultiplier - multiplicador) > 1e-6)
            gancho.StraightLineMultiplier = multiplicador;
        propios[nombre] = gancho;
        return gancho;
    }
}
