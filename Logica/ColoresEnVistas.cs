namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using practicacq.Modelo;

/// <summary>
/// Pinta las barras por diámetro con filtros de vista, con los colores de
/// <see cref="ColoresVarilla"/> (los mismos de la vista previa). Medido contra
/// Revit real en <c>docs/planes/2026-10-20-diagnostico-filtros-color.md</c>:
///
/// - Filtros propios "CQEING #3".."#11" (por diámetro) y "CQEING otro" (toda
///   la armadura, en gris, agregado al final: en Revit gana el filtro de más
///   arriba). Se crean una vez y se reutilizan.
/// - Van a cada vista donde se ve alguna de las columnas generadas. Si la
///   vista tiene una plantilla que controla los filtros, van a la plantilla
///   (sino Revit no deja cambiarlos en la vista).
/// - Los filtros "ACERO #N" que ya trae el proyecto quedan en la vista por
///   encima de los nuestros y la API no permite reordenarlos: Luciano eligió
///   repintarlos con la tabla (20-oct), en TODAS las vistas y plantillas que
///   los usan. Solo se cambian los colores; el resto de sus ajustes queda.
/// </summary>
public static class ColoresEnVistas
{
    private const string Prefijo = "CQEING ";

    /// <summary>Devuelve en cuántas vistas o plantillas quedaron los filtros "CQEING".</summary>
    public static int Aplicar(Document doc, IReadOnlyCollection<ElementId> columnas, List<string> avisos)
    {
        if (columnas.Count == 0) return 0;

        ElementId solido = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .FirstOrDefault(f => f.GetFillPattern().IsSolidFill)?.Id ?? ElementId.InvalidElementId;

        List<(ParameterFilterElement Filtro, ColoresVarilla.ColorRgb Color)> propios;
        try
        {
            propios = FiltrosPropios(doc);
        }
        catch (Exception ex)
        {
            avisos.Add($"Colores por diámetro: no se pudieron crear los filtros de vista ({ex.Message}).");
            return 0;
        }

        List<View> vistas = new FilteredElementCollector(doc)
            .OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && AdmiteFiltros(v.ViewType))
            .ToList();

        var destinos = new Dictionary<ElementId, View>();
        var idsColumnas = new List<ElementId>(columnas);
        foreach (View vista in vistas)
        {
            try
            {
                if (!vista.AreGraphicsOverridesAllowed()) continue;
                bool seVe = new FilteredElementCollector(doc, vista.Id)
                    .WherePasses(new ElementIdSetFilter(idsColumnas))
                    .Any();
                if (!seVe) continue;

                View destino = PlantillaQueControlaFiltros(doc, vista) ?? vista;
                destinos[destino.Id] = destino;
            }
            catch (Exception ex)
            {
                avisos.Add($"Colores por diámetro: no se pudo revisar la vista «{vista.Name}» ({ex.Message}).");
            }
        }

        int pintadas = 0;
        foreach (View destino in destinos.Values)
        {
            try
            {
                foreach ((ParameterFilterElement filtro, ColoresVarilla.ColorRgb color) in propios)
                {
                    OverrideGraphicSettings ogs = destino.IsFilterApplied(filtro.Id)
                        ? destino.GetFilterOverrides(filtro.Id)
                        : new OverrideGraphicSettings();
                    destino.SetFilterOverrides(filtro.Id, ConColor(ogs, color, solido));
                }
                pintadas++;
            }
            catch (Exception ex)
            {
                avisos.Add($"Colores por diámetro: no se pudieron poner en «{destino.Name}» ({ex.Message}).");
            }
        }

        RepintarFiltrosEmpresa(doc, solido, avisos);
        return pintadas;
    }

    /// <summary>Los "CQEING #N" en el orden de la tabla y "CQEING otro" al final.</summary>
    private static List<(ParameterFilterElement, ColoresVarilla.ColorRgb)> FiltrosPropios(Document doc)
    {
        var categorias = new List<ElementId> { new(BuiltInCategory.OST_Rebar) };
        var idDiametro = new ElementId(BuiltInParameter.REBAR_BAR_DIAMETER);
        Dictionary<string, ParameterFilterElement> existentes = new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
            .Where(f => f.Name.StartsWith(Prefijo, StringComparison.Ordinal))
            .GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());

        ParameterFilterElement Obtener(string nombre, ElementFilter? regla)
        {
            if (existentes.TryGetValue(nombre, out ParameterFilterElement? filtro))
            {
                if (regla is not null) filtro.SetElementFilter(regla);
                return filtro;
            }
            return regla is null
                ? ParameterFilterElement.Create(doc, nombre, categorias)
                : ParameterFilterElement.Create(doc, nombre, categorias, regla);
        }

        var lista = new List<(ParameterFilterElement, ColoresVarilla.ColorRgb)>();
        foreach ((int numero, double diametroMm, ColoresVarilla.ColorRgb color) in ColoresVarilla.Tabla)
        {
            var regla = new ElementParameterFilter(new List<FilterRule>
            {
                ParameterFilterRuleFactory.CreateGreaterOrEqualRule(idDiametro, Pies(diametroMm - CuadroVarillas.ToleranciaMm), 1e-9),
                ParameterFilterRuleFactory.CreateLessRule(idDiametro, Pies(diametroMm + CuadroVarillas.ToleranciaMm), 1e-9),
            });
            lista.Add((Obtener($"{Prefijo}#{numero}", regla), color));
        }
        lista.Add((Obtener($"{Prefijo}otro", null), ColoresVarilla.Gris));
        return lista;
    }

    /// <summary>Los "ACERO #N" del proyecto, en todas las vistas y plantillas que los usan.</summary>
    private static void RepintarFiltrosEmpresa(Document doc, ElementId solido, List<string> avisos)
    {
        List<(ElementId Id, ColoresVarilla.ColorRgb Color)> empresa = new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
            .Select(f => (f.Id, Color: ColoresVarilla.ColorDeFiltroEmpresa(f.Name)))
            .Where(p => p.Color is not null)
            .Select(p => (p.Id, p.Color!.Value))
            .ToList();
        if (empresa.Count == 0) return;

        foreach (View vista in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
        {
            if (!AdmiteFiltros(vista.ViewType)) continue;
            try
            {
                if (!vista.AreGraphicsOverridesAllowed()) continue;
                foreach ((ElementId id, ColoresVarilla.ColorRgb color) in empresa)
                {
                    if (!vista.IsFilterApplied(id)) continue;
                    vista.SetFilterOverrides(id, ConColor(vista.GetFilterOverrides(id), color, solido));
                }
            }
            catch (Exception ex)
            {
                avisos.Add($"Colores por diámetro: no se pudieron repintar los filtros ACERO en «{vista.Name}» ({ex.Message}).");
            }
        }
    }

    /// <summary>
    /// La plantilla de la vista, si controla los filtros; si no la tiene o la
    /// plantilla deja los filtros libres, null (se pinta la vista misma).
    /// </summary>
    private static View? PlantillaQueControlaFiltros(Document doc, View vista)
    {
        if (vista.ViewTemplateId == ElementId.InvalidElementId) return null;
        if (doc.GetElement(vista.ViewTemplateId) is not View plantilla) return null;

        var idFiltros = new ElementId(BuiltInParameter.VIS_GRAPHICS_FILTERS);
        bool libre = plantilla.GetNonControlledTemplateParameterIds().Contains(idFiltros);
        return libre ? null : plantilla;
    }

    /// <summary>Solo cambia los colores (líneas y relleno sólido); lo demás queda como estaba.</summary>
    private static OverrideGraphicSettings ConColor(OverrideGraphicSettings actual, ColoresVarilla.ColorRgb rgb, ElementId solido)
    {
        var color = new Color(rgb.R, rgb.G, rgb.B);
        var ogs = new OverrideGraphicSettings(actual)
            .SetProjectionLineColor(color)
            .SetCutLineColor(color);
        if (solido != ElementId.InvalidElementId)
            ogs.SetSurfaceForegroundPatternId(solido).SetSurfaceForegroundPatternColor(color)
               .SetCutForegroundPatternId(solido).SetCutForegroundPatternColor(color);
        return ogs;
    }

    /// <summary>Vistas gráficas que pueden tener filtros: fuera tablas, planos, leyendas y navegadores.</summary>
    private static bool AdmiteFiltros(ViewType tipo) => tipo switch
    {
        ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan or ViewType.AreaPlan
            or ViewType.Section or ViewType.Elevation or ViewType.Detail or ViewType.ThreeD => true,
        _ => false,
    };

    private static double Pies(double mm) => mm / 304.8;
}
