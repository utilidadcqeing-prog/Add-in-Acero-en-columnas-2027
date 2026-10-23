namespace practicacq.Logica;

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using practicacq.Modelo;

/// <summary>
/// La vista "CQEING Columnas 3D" de la vista previa (#123): la columna con su
/// zapata y las vigas que llegan a ella, transparente y sombreada, con caja de
/// sección y la armadura del modelo oculta, para que se vea solo la vista
/// previa (docs/planes/2026-10-21-vista-previa-3d-columnas-123.md, decisión 3).
/// Mismo criterio que <c>AccionesRevit.Abrir3d</c> del add-in de vigas, sin
/// compartir código (decisión 2).
/// </summary>
internal static class Vista3DColumna
{
    /// <summary>Transparencia del hormigón, en %: la misma que la 3D de vigas.</summary>
    private const int Transparencia = 90;

    /// <summary>Margen de la caja de sección alrededor de la columna, en pies (0.5 m).</summary>
    private const double Margen = 0.5 / 0.3048;

    /// <summary>
    /// Crea la vista la primera vez (después la reutiliza), la ajusta a la
    /// columna y la deja activa. Necesita contexto de API válido y ninguna
    /// transacción abierta.
    /// </summary>
    public static void Abrir(UIDocument uidoc, ColumnaContexto columna)
    {
        Document doc = uidoc.Document;
        List<ElementId> propios = columna.Tramos.Select(t => t.IdElementoHost)
            .Append(columna.IdZapata)
            .Select(id => long.TryParse(id, out long valor) ? new ElementId(valor) : null)
            .OfType<ElementId>()
            .Distinct()
            .Where(id => doc.GetElement(id) is not null)
            .ToList();

        BoundingBoxXYZ? caja = Caja(doc, propios);
        List<ElementId> ids = caja is null ? propios : propios.Union(Vigas(doc, caja)).ToList();

        View3D? vista = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate && v.Name == Tubos3D.NombreVista);
        var transparente = new OverrideGraphicSettings();
        transparente.SetSurfaceTransparency(Transparencia);

        using (var t = new Transaction(doc, "Armado de columnas: vista 3D"))
        {
            t.Start();
            if (vista is null)
            {
                ElementId tipo = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .First(x => x.ViewFamily == ViewFamily.ThreeDimensional).Id;
                vista = View3D.CreateIsometric(doc, tipo);
                vista.Name = Tubos3D.NombreVista;
            }

            // Sombreado: en alambre Revit no pinta superficies, y la vista
            // previa se vería sin color (mismo motivo que en vigas).
            if (vista.DisplayStyle != DisplayStyle.Shading) vista.DisplayStyle = DisplayStyle.Shading;
            if (vista.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                vista.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);

            // La armadura del modelo, oculta: la que ya se generó (o la que se
            // genere con la vista abierta) se mezclaría con la vista previa.
            var armadura = new ElementId(BuiltInCategory.OST_Rebar);
            if (vista.CanCategoryBeHidden(armadura)) vista.SetCategoryHidden(armadura, true);

            Aislar(doc, vista, ids);
            foreach (ElementId id in ids) vista.SetElementOverrides(id, transparente);
            if (caja is not null)
            {
                vista.SetSectionBox(caja);
                vista.IsSectionBoxActive = true;
            }

            t.Commit();
        }

        uidoc.ActiveView = vista;
        if (caja is not null && uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == vista.Id) is UIView abierta)
            abierta.ZoomAndCenterRectangle(caja.Min, caja.Max);
    }

    /// <summary>
    /// Deja visibles en <paramref name="vista"/> solo <paramref name="ids"/>.
    /// Oculta de verdad, no con el aislamiento temporal (en vigas seguían
    /// saliendo elementos que cruzan la caja). La vista es del add-in, así que
    /// primero se muestra lo que ocultó la vez anterior.
    /// </summary>
    private static void Aislar(Document doc, View3D vista, List<ElementId> ids)
    {
        List<ElementId> ocultos = new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .Where(e => e.IsHidden(vista)).Select(e => e.Id).ToList();
        if (ocultos.Count > 0) vista.UnhideElements(ocultos);

        var quedan = ids.ToHashSet();
        List<ElementId> sobran = new FilteredElementCollector(doc, vista.Id).WhereElementIsNotElementType()
            .Where(e => !quedan.Contains(e.Id) && e.CanBeHidden(vista)).Select(e => e.Id).ToList();
        if (sobran.Count > 0) vista.HideElements(sobran);
    }

    /// <summary>Las vigas que tocan la caja: la caja las corta, así que se ven como tramos cortos que llegan a la columna.</summary>
    private static IEnumerable<ElementId> Vigas(Document doc, BoundingBoxXYZ caja) =>
        new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
            .WhereElementIsNotElementType()
            .WherePasses(new BoundingBoxIntersectsFilter(new Outline(caja.Min, caja.Max)))
            .ToElementIds();

    /// <summary>La caja que encierra los elementos, con <see cref="Margen"/> a cada lado.</summary>
    private static BoundingBoxXYZ? Caja(Document doc, IEnumerable<ElementId> ids)
    {
        List<BoundingBoxXYZ> cajas = ids.Select(id => doc.GetElement(id)?.get_BoundingBox(null)).OfType<BoundingBoxXYZ>().ToList();
        if (cajas.Count == 0) return null;

        return new BoundingBoxXYZ
        {
            Min = new XYZ(cajas.Min(c => c.Min.X) - Margen, cajas.Min(c => c.Min.Y) - Margen, cajas.Min(c => c.Min.Z) - Margen),
            Max = new XYZ(cajas.Max(c => c.Max.X) + Margen, cajas.Max(c => c.Max.Y) + Margen, cajas.Max(c => c.Max.Z) + Margen),
        };
    }
}
