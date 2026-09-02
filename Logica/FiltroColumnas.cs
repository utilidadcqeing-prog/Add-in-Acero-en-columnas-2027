namespace practicacq.Logica;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

public class FiltroColumnas : ISelectionFilter
{
    public bool AllowElement(Element e) =>
        e is FamilyInstance &&
        e.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;

    public bool AllowReference(Reference r, XYZ p) => false;
}
