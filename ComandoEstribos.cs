namespace practicacq;

using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using practicacq.Modelo;
using practicacq.Vistas;

[Transaction(TransactionMode.Manual)]
public class ComandoEstribos : IExternalCommand
{
    private static VentanaConfiguradorEstribos? _ventana;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiapp = commandData.Application;

        if (uiapp.ActiveUIDocument?.Document is null)
        {
            message = "Abre un proyecto antes de ejecutar el comando.";
            return Result.Failed;
        }

        if (_ventana is not null && _ventana.IsLoaded)
        {
            _ventana.Activate();
            return Result.Succeeded;
        }

        // TODO(J2): reemplazar por LectorContexto(uiapp.ActiveUIDocument.Document)
        // y un proveedor de tipos real (RebarBarType/RebarHookType del documento)
        // cuando J2 los tenga listos. La ventana no cambia una línea -- ver L1.
        _ventana = new VentanaConfiguradorEstribos(
            new ProveedorContextoFalso(),
            new ProveedorTiposFalso(),
            modoSimulado: true);

        new WindowInteropHelper(_ventana).Owner = uiapp.MainWindowHandle;
        _ventana.Closed += (_, _) => _ventana = null;
        _ventana.Show();

        return Result.Succeeded;
    }
}
