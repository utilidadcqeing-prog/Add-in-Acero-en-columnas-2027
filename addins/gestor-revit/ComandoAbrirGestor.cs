using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CQEING.GestorRevit;

/// <summary>
/// Botón «Gestor de add-ins»: abre el gestor instalado en este equipo. No necesita un proyecto abierto.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ComandoAbrirGestor : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (LanzadorGestor.Abrir() is { } problema)
            TaskDialog.Show(LanzadorGestor.Titulo, problema);
        return Result.Succeeded;
    }
}
