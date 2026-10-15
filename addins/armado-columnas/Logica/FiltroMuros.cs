namespace practicacq.Logica;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

/// <summary>Restringe la selección en pantalla a muros.</summary>
public class FiltroMuros : ISelectionFilter
{
    public bool AllowElement(Element elem) => elem is Wall;

    public bool AllowReference(Reference reference, XYZ position) => false;
}
