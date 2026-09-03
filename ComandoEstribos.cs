namespace practicacq;

using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using practicacq.Logica;
using practicacq.Vistas;

[Transaction(TransactionMode.Manual)]
public class ComandoEstribos : IExternalCommand
{
    private static VentanaConfiguradorEstribos? _ventana;

    // Issue #24 (sincronizar selección): vive a nivel de comando, no de
    // ventana, porque VentanaConfiguradorEstribos no puede referenciar
    // Autodesk.Revit -- es la misma regla de L1 que la mantiene corriendo
    // sin Revit abierto.
    private static readonly ManejadorSeleccionElemento _manejadorSeleccion = new();
    private static readonly ExternalEvent _eventoSeleccion = ExternalEvent.Create(_manejadorSeleccion);

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

        // J2: proveedores reales sobre el documento activo. La ventana no cambió
        // una línea -- ver L1, este era exactamente el punto de la interfaz.
        Document doc = uiapp.ActiveUIDocument.Document;

        _ventana = new VentanaConfiguradorEstribos(
            new LectorContexto(doc),
            new ProveedorTiposReal(doc),
            modoSimulado: false);

        _ventana.ElementosActivados += idsColumna =>
        {
            // long? (no ElementId?) a propósito: ElementId ya tiene su propio
            // .Value (el long interno, ver LectorContexto), y mezclar los dos
            // Nullable en la misma expresión es una trampa segura para
            // confundirse. Acá se resuelve el nullable antes de construir el
            // ElementId, no después.
            List<ElementId> ids = idsColumna
                .Select(id => long.TryParse(id, out long valor) ? valor : (long?)null)
                .Where(valor => valor is not null)
                .Select(valor => new ElementId(valor!.Value))
                .ToList();

            if (ids.Count == 0) return;

            _manejadorSeleccion.Objetivos = ids;
            _eventoSeleccion.Raise();
        };

        new WindowInteropHelper(_ventana).Owner = uiapp.MainWindowHandle;
        _ventana.Closed += (_, _) => _ventana = null;
        _ventana.Show();

        return Result.Succeeded;
    }
}
