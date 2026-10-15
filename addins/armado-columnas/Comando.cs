namespace practicacq;

using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using practicacq.Vistas;

[Transaction(TransactionMode.Manual)]
public class Comando : IExternalCommand
{
    private static VentanaColumnetas? _ventana;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiapp = commandData.Application;

        if (uiapp.ActiveUIDocument?.Document is null)
        {
            message = "Abre un proyecto antes de ejecutar el comando.";
            return Result.Failed;
        }

        // La ventana es modeless: si ya está abierta, la traemos al frente
        // en lugar de crear una segunda.
        if (_ventana is not null && _ventana.IsLoaded)
        {
            _ventana.Activate();
            return Result.Succeeded;
        }

        _ventana = new VentanaColumnetas(uiapp);
        new WindowInteropHelper(_ventana).Owner = uiapp.MainWindowHandle;
        _ventana.Closed += (_, _) => _ventana = null;
        _ventana.Show();

        return Result.Succeeded;
    }
}
