using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    private const string Titulo = "Gestor de add-ins CQEING";
    private const string EnlaceDescarga = "https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/releases";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        string? gestor = LanzadorGestor.Ruta();
        if (gestor is null)
        {
            TaskDialog.Show(Titulo,
                "El gestor no está instalado en este equipo.\n\n" +
                "Descargue «CQEING.Gestor-win-Setup.exe» de la última versión del gestor en:\n" + EnlaceDescarga);
            return Result.Succeeded;
        }

        try
        {
            Process.Start(new ProcessStartInfo(gestor) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(gestor)! });
        }
        catch (Exception e) when (e is Win32Exception or IOException)
        {
            TaskDialog.Show(Titulo, $"No se pudo abrir el gestor:\n{e.Message}");
        }
        return Result.Succeeded;
    }
}
