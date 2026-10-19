using Autodesk.Revit.DB;
using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Revit;

/// <summary>
/// Los colores por diámetro en Revit (#85, decisión 9 del plan docs/planes/2026-10-19-generar-acero-vigas-85.md): un
/// filtro de vista por diámetro, <c>"CQEING Acero #5"</c>, sobre la armadura estructural con la regla "diámetro de barra =
/// el del tipo del modelo", con el color de <see cref="ColoresDiametro"/> en líneas y relleno. Los filtros quedan en el
/// proyecto y se pueden meter en las plantillas de vista; pintar elemento por elemento se pierde en cada vista nueva.
/// </summary>
internal static class FiltrosColor
{
    /// <summary>Tolerancia de la regla, en pies (0.5 mm, la misma con que se elige el tipo de barra).</summary>
    private static readonly double Tolerancia = CreadorBarras.Pies(DiametrosDisponibles.Tolerancia);

    /// <summary>
    /// Crea o pone al día el filtro de cada diámetro y lo aplica a las vistas. Dentro de una transacción. Devuelve un aviso
    /// por cada vista que no los acepta (su plantilla controla los filtros).
    /// </summary>
    public static List<string> Aplicar(Document doc, IEnumerable<View> vistas, IEnumerable<int> diametros, TiposAcero tipos)
    {
        ElementId? solido = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(f => f.GetFillPattern().IsSolidFill)?.Id;
        var avisos = new List<string>();
        var filtros = new List<(ElementId Filtro, OverrideGraphicSettings Color)>();
        foreach (int n in diametros.Distinct().Order())
        {
            if (tipos.Barra(n) is not { } barra)
                continue;
            if (Filtro(doc, n, barra.BarNominalDiameter) is { } filtro)
                filtros.Add((filtro.Id, Color(ColoresDiametro.Rgb(n), solido)));
            else
                avisos.Add($"Revit no aceptó el filtro de color del #{n}.");
        }

        // Una hoja o una tabla no tienen sobrescrituras: se saltan sin aviso.
        foreach (View vista in vistas.DistinctBy(v => v.Id).Where(v => v.AreGraphicsOverridesAllowed()))
        {
            try
            {
                foreach ((ElementId filtro, OverrideGraphicSettings color) in filtros)
                {
                    if (!vista.IsFilterApplied(filtro))
                        vista.AddFilter(filtro);
                    vista.SetFilterOverrides(filtro, color);
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                avisos.Add($"La vista \"{vista.Name}\" no acepta filtros (los controla su plantilla): no se le pusieron los colores por diámetro.");
            }
        }
        return avisos;
    }

    /// <summary>
    /// El filtro del #<paramref name="numero"/>; si ya existe se le pone la regla al día, no se crea otro. Null si Revit no
    /// acepta la regla en la armadura.
    /// </summary>
    private static ParameterFilterElement? Filtro(Document doc, int numero, double diametro)
    {
        string nombre = $"CQEING Acero #{numero}";
        var categorias = new List<ElementId> { new(BuiltInCategory.OST_Rebar) };
        var regla = new ElementParameterFilter(ParameterFilterRuleFactory.CreateEqualsRule(
            new ElementId(BuiltInParameter.REBAR_BAR_DIAMETER), diametro, Tolerancia));
        if (!ParameterFilterElement.ElementFilterIsAcceptableForParameterFilterElement(doc, categorias.ToHashSet(), regla))
            return null;
        if (new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
                .FirstOrDefault(f => f.Name == nombre) is { } ya)
        {
            ya.SetElementFilter(regla);
            return ya;
        }
        return ParameterFilterElement.Create(doc, nombre, categorias, regla);
    }

    private static OverrideGraphicSettings Color((byte R, byte G, byte B) rgb, ElementId? solido)
    {
        var color = new Color(rgb.R, rgb.G, rgb.B);
        var o = new OverrideGraphicSettings()
            .SetProjectionLineColor(color)
            .SetCutLineColor(color)
            .SetSurfaceForegroundPatternColor(color)
            .SetCutForegroundPatternColor(color);
        if (solido is not null)
            o.SetSurfaceForegroundPatternId(solido).SetCutForegroundPatternId(solido);
        return o;
    }
}
