namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// Las formas y los ganchos PROPIOS del add-in para estribos, cajas y grapas
/// (#64), para no depender de lo que traiga cargado cada modelo -- diseño en
/// docs/planes/2026-10-09-diseno-formas-ganchos-propios-64.md, revisado en
/// docs/planes/2026-10-20-diseno-estribos-menores-visual-y-64.md (sección 3).
/// Todo lo de acá se probó contra Revit 2027 real: el 8-oct
/// (2026-10-08-diagnostico-formas-ganchos-propios-64.md) y la grapa en S el
/// 20-oct (2026-10-20-diagnostico-grapa-s.md).
///
/// Mismo molde que <see cref="TiposEmpalme"/>: una instancia por corrida, que
/// busca por nombre, crea lo que falta y cachea. Debe usarse dentro de una
/// transacción abierta. Nada de esto deja de generar: si una pieza no se pudo,
/// vuelve sin tipo y con el motivo, y quien llama cae a lo del modelo.
/// </summary>
public sealed class TiposPropios
{
    /// <summary>Un tipo propio, o null con el motivo de por qué no se pudo.</summary>
    public sealed record Pieza<T>(T? Tipo, string? Aviso) where T : class;

    public const string NombreGanchoEstribo = "CQ Estribo 135°";
    public const string NombreGanchoGrapa = "CQ Grapa 180°";
    public const string NombreFormaEstribo = "CQ Estribo";
    public const string NombreFormaGrapa = "CQ Grapa";
    public const string NombreFormaGrapaS = "CQ Grapa S";

    // Grapas dobladas como el estribo (Luciano, 20-oct): estilo StirrupTie,
    // así Revit dobla el gancho con StirrupTieBendDiameter (4 d) y no con el
    // StandardHookBendDiameter (6 d) del gancho de anclaje. Nombres aparte de
    // los Standard de arriba, que quedan de respaldo.
    public const string NombreGanchoGrapaEstribo = "CQ Grapa 180° estribo";
    public const string NombreFormaGrapaEstriboC = "CQ Grapa C";
    public const string NombreFormaGrapaEstriboS = "CQ Grapa en S";

    /// <summary>Multiplicadores medidos el 8-oct: con 6, el 135° da la E del cuadro en #2 a #5; con 4, el 180° da la de #6 a #8.</summary>
    private const double MultiplicadorEstribo = 6;
    private const double MultiplicadorGrapa = 4;

    /// <summary>El cuadro está redondeado al milímetro.</summary>
    private const double ToleranciaExtensionMetros = 0.001;

    /// <summary>
    /// Las cotas de los lados (CQ A a CQ D) y las de los ganchos (#176): estas
    /// dos últimas van atadas por fórmula al largo de cada gancho, como A y G
    /// de la «T1» de la plantilla, y son las que el detalle de plegado acota en
    /// los ganchos. Sin ellas el detalle solo mide los lados.
    /// </summary>
    private static readonly string[] NombresParametros = { "CQ A", "CQ B", "CQ C", "CQ D", "CQ Gancho inicio", "CQ Gancho fin" };
    private const int ParametroGanchoInicio = 4;
    private const int ParametroGanchoFin = 5;

    private readonly Document _doc;
    private readonly Dictionary<string, object> _cache = new();
    private List<ElementId>? _parametros;
    private string? _avisoParametros;

    public TiposPropios(Document doc) => _doc = doc;

    public Pieza<RebarHookType> GanchoEstribo() =>
        Cacheado(NombreGanchoEstribo, () => Gancho(NombreGanchoEstribo, 135, MultiplicadorEstribo, RebarStyle.StirrupTie));

    public Pieza<RebarHookType> GanchoGrapa() =>
        Cacheado(NombreGanchoGrapa, () => Gancho(NombreGanchoGrapa, 180, MultiplicadorGrapa, RebarStyle.Standard));

    /// <summary>El 180° de las grapas con estilo StirrupTie: se dobla con el diámetro de estribo.</summary>
    public Pieza<RebarHookType> GanchoGrapaEstribo() =>
        Cacheado(NombreGanchoGrapaEstribo, () => Gancho(NombreGanchoGrapaEstribo, 180, MultiplicadorGrapa, RebarStyle.StirrupTie));

    /// <summary>
    /// El gancho de grapa de ese ángulo, doblado como el estribo (StirrupTie).
    /// El 180° es el de siempre; el 135° y el 90° son propios, con el
    /// multiplicador del estribo (6 d): la cola del cuadro la fija después
    /// <see cref="AjustarExtension"/>.
    /// </summary>
    public Pieza<RebarHookType> GanchoGrapaEstribo(AnguloGancho angulo)
    {
        if (angulo == AnguloGancho.Grados180) return GanchoGrapaEstribo();
        int grados = (int)angulo;
        string nombre = $"CQ Grapa {grados}° estribo";
        return Cacheado(nombre, () => Gancho(nombre, grados, MultiplicadorEstribo, RebarStyle.StirrupTie));
    }

    /// <summary>La grapa de un tramo con estilo StirrupTie (ver <see cref="GanchoGrapaEstribo()"/>).</summary>
    public Pieza<RebarShape> FormaGrapaEstribo(FormaMenor forma) =>
        FormaGrapaEstribo(forma, AnguloGancho.Grados180, AnguloGancho.Grados180);

    /// <summary>
    /// La grapa StirrupTie con el gancho de cada punta: con 180/180 es la
    /// forma de siempre (mismo nombre); con otros ángulos, una forma aparte
    /// por combinación ("CQ Grapa C 135-90"), porque Revit pide que los
    /// ganchos de la barra coincidan con los de la forma (RevitAPI.xml 2027,
    /// CreateFromCurvesAndShape).
    /// </summary>
    public Pieza<RebarShape> FormaGrapaEstribo(FormaMenor forma, AnguloGancho inicio, AnguloGancho fin)
    {
        bool s = forma == FormaMenor.GrapaS;
        string nombre = s ? NombreFormaGrapaEstriboS : NombreFormaGrapaEstriboC;
        if (inicio != AnguloGancho.Grados180 || fin != AnguloGancho.Grados180) nombre += $" {(int)inicio}-{(int)fin}";
        return Cacheado(nombre, () => Forma(nombre, RebarStyle.StirrupTie, 1,
            () => CrearFormaGrapa(nombre, s, RebarStyle.StirrupTie, (int)inicio, (int)fin)));
    }

    /// <summary>Rectángulo cerrado de cuatro lados antihorario, cotas por fuera del doblez, 135° en las dos puntas: estribo principal y cajas.</summary>
    public Pieza<RebarShape> FormaEstribo() =>
        Cacheado(NombreFormaEstribo, () => Forma(NombreFormaEstribo, RebarStyle.StirrupTie, 4, CrearFormaEstribo));

    /// <summary>
    /// Un tramo recto con 180° en las dos puntas. La C y la S son formas
    /// aparte para que en las planillas figuren por separado, pero hacia qué
    /// lado mira cada gancho lo decide la barra al crearse
    /// (<see cref="OrientacionesGrapa"/>), no la forma: medido el 20-oct.
    /// </summary>
    public Pieza<RebarShape> FormaGrapa(FormaMenor forma)
    {
        bool s = forma == FormaMenor.GrapaS;
        string nombre = s ? NombreFormaGrapaS : NombreFormaGrapa;
        return Cacheado(nombre, () => Forma(nombre, RebarStyle.Standard, 1, () => CrearFormaGrapa(nombre, s, RebarStyle.Standard)));
    }

    /// <summary>
    /// Hacia dónde mira cada gancho de una grapa (diagnóstico del 20-oct): la
    /// C con los dos hacia el mismo lado, la S con uno para cada lado; voltear
    /// invierte los dos.
    /// </summary>
    public static (RebarTerminationOrientation Inicio, RebarTerminationOrientation Fin) OrientacionesGrapa(FormaMenor forma, bool volteado)
    {
        RebarTerminationOrientation inicio = volteado ? RebarTerminationOrientation.Right : RebarTerminationOrientation.Left;
        RebarTerminationOrientation otro = inicio == RebarTerminationOrientation.Left ? RebarTerminationOrientation.Right : RebarTerminationOrientation.Left;
        return (inicio, forma == FormaMenor.GrapaS ? otro : inicio);
    }

    /// <summary>
    /// Si la extensión recta que da Revit para este gancho en esta barra
    /// difiere de la E del cuadro en más de 1 mm, apaga el cálculo automático
    /// de ESE gancho en esa barra y fija el largo. No toca los demás ganchos
    /// de la barra ni su doblado. Hoy pasa con el 180° de la #2 a la #5 (el
    /// cuadro pide 6,5 cm y el multiplicador da 4 d). Devuelve un aviso si no
    /// pudo, o null.
    /// </summary>
    public string? AjustarExtension(RebarBarType barra, RebarHookType gancho, CuadroVarillas.Gancho cuadro)
    {
        string clave = $"ext:{barra.Id.Value}:{gancho.Id.Value}";
        if (_cache.ContainsKey(clave)) return null;
        _cache[clave] = true;

        try
        {
            double objetivo = EstandarCQ.APies(cuadro.Extension);
            double actual = gancho.GetHookExtensionLength(barra);
            if (Math.Abs(actual - objetivo) <= EstandarCQ.APies(ToleranciaExtensionMetros)) return null;

            double largo = barra.GetHookLength(gancho.Id);
            barra.SetAutoCalcHookLengths(gancho.Id, false);
            barra.SetHookLength(gancho.Id, largo + (objetivo - actual));
            return null;
        }
        catch (Exception ex)
        {
            return $"no se pudo ajustar el gancho «{gancho.Name}» en «{barra.Name}» a la extensión del cuadro " +
                   $"({cuadro.Extension * 100:0.0} cm): {ex.Message}";
        }
    }

    // ------------------------------------------------------------------ ganchos

    private Pieza<RebarHookType> Gancho(string nombre, double grados, double multiplicador, RebarStyle estilo)
    {
        try
        {
            RebarHookType gancho = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                                       .FirstOrDefault(g => g.Name == nombre)
                                   ?? CrearGancho(nombre, grados);

            // El nombre promete las medidas del cuadro: si alguien lo cambió a
            // mano, se vuelve a fijar en cada corrida (mismo criterio que TiposEmpalme).
            double angulo = grados * Math.PI / 180;
            if (Math.Abs(gancho.HookAngle - angulo) > 1e-6) gancho.HookAngle = angulo;
            if (gancho.Style != estilo) gancho.Style = estilo;
            if (Math.Abs(gancho.StraightLineMultiplier - multiplicador) > 1e-6) gancho.StraightLineMultiplier = multiplicador;
            return new Pieza<RebarHookType>(gancho, null);
        }
        catch (Exception ex)
        {
            return new Pieza<RebarHookType>(null, $"no se pudo crear el gancho propio «{nombre}»: {ex.Message}");
        }
    }

    private RebarHookType CrearGancho(string nombre, double grados)
    {
        // RebarHookType.Create lo entrega con estilo Standard; el estilo se
        // fija después (medido el 8-oct).
        RebarHookType gancho = RebarHookType.Create(_doc, grados * Math.PI / 180, MultiplicadorGrapa);
        gancho.Name = nombre;
        return gancho;
    }

    // ------------------------------------------------------------------- formas

    /// <summary>
    /// Busca la forma por nombre o la crea. Una con el nombre propio pero
    /// alterada (otro estilo u otra cantidad de lados) no se usa: el nombre
    /// promete una geometría que ya no tiene.
    /// </summary>
    private Pieza<RebarShape> Forma(string nombre, RebarStyle estilo, int lados, Func<RebarShape> crear)
    {
        try
        {
            RebarShape? existente = new FilteredElementCollector(_doc).OfClass(typeof(RebarShape)).Cast<RebarShape>()
                .FirstOrDefault(f => f.Name == nombre);
            if (existente is not null)
            {
                int segmentos = existente.GetRebarShapeDefinition() is RebarShapeDefinitionBySegments s ? s.NumberOfSegments : 0;
                if (existente.RebarStyle != estilo || segmentos != lados)
                    return new Pieza<RebarShape>(null,
                        $"la forma «{nombre}» del modelo fue modificada (estilo {existente.RebarStyle}, {segmentos} lado(s)); no se usa.");

                // #176: las formas creadas antes no tienen las cotas de los
                // ganchos, y una forma ya creada no se puede cambiar. Se le
                // cambia el nombre (las barras que ya la usan la conservan
                // hasta que se regeneren) y se crea la nueva con el nombre de
                // siempre. Sin parámetros se sigue con la vieja, como antes.
                if (Parametros() is not { } p
                    || existente.GetRebarShapeDefinition().HasParameter(p[ParametroGanchoInicio])
                    || existente.GetRebarShapeDefinition().HasParameter(p[ParametroGanchoFin]))
                    return new Pieza<RebarShape>(existente, null);
                existente.Name = NombreLibre($"{nombre} (anterior)");
            }

            if (Parametros() is null)
                return new Pieza<RebarShape>(null, $"no se pudo crear la forma «{nombre}»: {_avisoParametros}");

            return new Pieza<RebarShape>(crear(), null);
        }
        catch (Exception ex)
        {
            return new Pieza<RebarShape>(null, $"no se pudo crear la forma propia «{nombre}»: {ex.Message}");
        }
    }

    /// <summary>El nombre pedido, o con " 2", " 3"… si ya hay una forma que se llama así.</summary>
    private string NombreLibre(string nombre)
    {
        var usados = new HashSet<string>(new FilteredElementCollector(_doc).OfClass(typeof(RebarShape)).Select(f => f.Name));
        string libre = nombre;
        for (int i = 2; usados.Contains(libre); i++) libre = $"{nombre} {i}";
        return libre;
    }

    /// <summary>
    /// Las dos cotas de los ganchos (#176), con la misma fórmula que A y G de la
    /// «T1» de la plantilla (leída el 28-oct: «Start Hook Length» / «End Hook
    /// Length»). El nombre de la fórmula sale del idioma de Revit en uso, para
    /// que también funcione con Revit en español.
    /// </summary>
    private void AgregarCotasGanchos(RebarShapeDefinition definicion, bool inicio = true, bool fin = true)
    {
        List<ElementId> p = _parametros!;
        if (inicio)
            definicion.AddFormulaParameter(p[ParametroGanchoInicio], LabelUtils.GetLabelForBuiltInParameter(ParameterTypeId.RebarShapeStartHookLength));
        if (fin)
            definicion.AddFormulaParameter(p[ParametroGanchoFin], LabelUtils.GetLabelForBuiltInParameter(ParameterTypeId.RebarShapeEndHookLength));
    }

    private RebarShape CrearFormaEstribo()
    {
        List<ElementId> p = _parametros!;
        var definicion = new RebarShapeDefinitionBySegments(_doc, 4);
        double[] porDefecto = { EstandarCQ.APies(0.30), EstandarCQ.APies(0.40), EstandarCQ.APies(0.30), EstandarCQ.APies(0.40) };
        (double X, double Y)[] direcciones = { (1, 0), (0, 1), (-1, 0), (0, -1) };

        for (int i = 0; i < 4; i++) definicion.AddParameter(p[i], porDefecto[i]);
        for (int i = 0; i < 4; i++)
        {
            definicion.SetSegmentFixedDirection(i, direcciones[i].X, direcciones[i].Y);
            // Cotas por fuera del doblez: lo que funcionó el 8-oct con ScaleToBox.
            definicion.AddConstraintParallelToSegment(i, p[i], true, true);
        }

        for (int v = 1; v <= 3; v++)
            definicion.AddBendDefaultRadius(v, RebarShapeVertexTurn.Left, RebarShapeBendAngle.Right);
        AgregarCotasGanchos(definicion);

        using var terminaciones = new RebarShapeTerminationsData(_doc)
        {
            HookAngleAtStart = 135,
            HookAngleAtEnd = 135,
            TerminationOrientationAtStart = RebarTerminationOrientation.Left,
            TerminationOrientationAtEnd = RebarTerminationOrientation.Left,
        };
        RebarShape forma = RebarShape.Create(_doc, definicion, null, RebarStyle.StirrupTie, StirrupTieAttachmentType.InteriorFace, 0, terminaciones);
        forma.Name = NombreFormaEstribo;
        return forma;
    }

    private RebarShape CrearFormaGrapa(string nombre, bool s, RebarStyle estilo, int gradosInicio = 180, int gradosFin = 180)
    {
        List<ElementId> p = _parametros!;
        var definicion = new RebarShapeDefinitionBySegments(_doc, 1);
        definicion.AddParameter(p[0], EstandarCQ.APies(0.30));
        definicion.SetSegmentFixedDirection(0, 1, 0);
        definicion.AddConstraintParallelToSegment(0, p[0], false, false);
        AgregarCotasGanchos(definicion, gradosInicio > 0, gradosFin > 0);

        using var terminaciones = new RebarShapeTerminationsData(_doc)
        {
            HookAngleAtStart = gradosInicio,
            HookAngleAtEnd = gradosFin,
            TerminationOrientationAtStart = RebarTerminationOrientation.Left,
            TerminationOrientationAtEnd = s ? RebarTerminationOrientation.Right : RebarTerminationOrientation.Left,
        };
        RebarShape forma = RebarShape.Create(_doc, definicion, null, estilo, StirrupTieAttachmentType.InteriorFace, 0, terminaciones);
        forma.Name = nombre;
        return forma;
    }

    /// <summary>
    /// Los cuatro parámetros compartidos de las cotas de las formas ("CQ A" a
    /// "CQ D"): los que ya estén en el documento, o creados desde un archivo
    /// temporal. El archivo de parámetros compartidos que tenga configurado el
    /// usuario se restaura siempre, pase lo que pase.
    /// </summary>
    private List<ElementId>? Parametros()
    {
        if (_parametros is not null || _avisoParametros is not null) return _parametros;

        var porNombre = new Dictionary<string, ElementId>();
        foreach (ElementId id in RebarShapeParameters.GetAllRebarShapeParameters(_doc))
            if (_doc.GetElement(id)?.Name is string n) porNombre.TryAdd(n, id);

        if (NombresParametros.All(porNombre.ContainsKey))
            return _parametros = NombresParametros.Select(n => porNombre[n]).ToList();

        var app = _doc.Application;
        string anterior = app.SharedParametersFilename ?? "";
        string ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cqeing_parametros_{Guid.NewGuid():N}.txt");
        try
        {
            System.IO.File.WriteAllText(ruta, "");
            app.SharedParametersFilename = ruta;
            DefinitionGroup grupo = app.OpenSharedParameterFile().Groups.Create("CQEING");
            var ids = new List<ElementId>();
            foreach (string nombre in NombresParametros)
            {
                if (porNombre.TryGetValue(nombre, out ElementId? id))
                {
                    ids.Add(id);
                    continue;
                }

                var definicion = (ExternalDefinition)grupo.Definitions.Create(
                    new ExternalDefinitionCreationOptions(nombre, SpecTypeId.ReinforcementLength));
                ids.Add(RebarShapeParameters.GetOrCreateElementIdForExternalDefinition(_doc, definicion));
            }

            return _parametros = ids;
        }
        catch (Exception ex)
        {
            _avisoParametros = $"no se pudieron crear los parámetros de forma {string.Join(", ", NombresParametros)} ({ex.Message})";
            return null;
        }
        finally
        {
            try { app.SharedParametersFilename = anterior; } catch { /* lo que se pueda: ver el aviso del retest */ }
            try { System.IO.File.Delete(ruta); } catch { /* best-effort */ }
        }
    }

    private Pieza<T> Cacheado<T>(string clave, Func<Pieza<T>> resolver) where T : class
    {
        if (_cache.TryGetValue(clave, out object? previo)) return (Pieza<T>)previo;
        Pieza<T> pieza = resolver();
        _cache[clave] = pieza;
        return pieza;
    }
}
