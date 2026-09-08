namespace practicacq;

using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using practicacq.Logica;
using practicacq.Vistas;

[Transaction(TransactionMode.Manual)]
public class ComandoEstribos : IExternalCommand
{
    private static VentanaConfiguradorEstribos? _ventana;

    // Issue #24 (sincronizar selección): viven a nivel de comando, no de
    // ventana, porque VentanaConfiguradorEstribos no puede referenciar
    // Autodesk.Revit -- es la misma regla de L1 que la mantiene corriendo
    // sin Revit abierto. Dos pares separados y no uno compartido: activar
    // una fila mueve la cámara, tildar en la lista no debe hacerlo, y son
    // dos eventos de la ventana que pueden dispararse en cualquier orden.
    private static readonly ManejadorSeleccionElemento _manejadorActivar = new() { CentrarVista = true };
    private static readonly ExternalEvent _eventoActivar = ExternalEvent.Create(_manejadorActivar);

    private static readonly ManejadorSeleccionElemento _manejadorMarcado = new() { CentrarVista = false };
    private static readonly ExternalEvent _eventoMarcado = ExternalEvent.Create(_manejadorMarcado);

    // J3 (#5): generar el armado real necesita transacción, por eso pasa por
    // el mismo patrón ExternalEvent que la selección de arriba.
    private static readonly ManejadorGeneracionArmado _manejadorGenerar = new();
    private static readonly ExternalEvent _eventoGenerar = ExternalEvent.Create(_manejadorGenerar);

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

        _ventana.ElementosActivados += ids =>
        {
            List<ElementId> objetivos = ParsearIds(ids);
            if (objetivos.Count == 0) return;

            _manejadorActivar.Objetivos = objetivos;
            _eventoActivar.Raise();
        };

        // Tildar puede vaciar el conjunto (destildar todo) -- a diferencia de
        // ElementosActivados, acá sí hay que avisarle a Revit para que
        // deseleccione, no cortar antes.
        _ventana.SeleccionMarcadaCambio += ids =>
        {
            _manejadorMarcado.Objetivos = ParsearIds(ids);
            _eventoMarcado.Raise();
        };

        // La ventana que dispara esto puede haberse cerrado (y reabierto) para
        // cuando Revit termina de procesar el evento -- por eso los callbacks
        // van contra la ventana viva del momento (_ventana), no contra una
        // referencia capturada acá.
        _manejadorGenerar.AlTerminar = res => _ventana?.MostrarResultadoGeneracion(res);
        _manejadorGenerar.AlFallar = msg => _ventana?.MostrarErrorGeneracion(msg);

        _ventana.GenerarSolicitado += opciones =>
        {
            _manejadorGenerar.Opciones = opciones;
            _eventoGenerar.Raise();
        };

        // Reunión 9-sep: sincronización en el otro sentido -- si el usuario
        // selecciona una columna directamente en el modelo (no en la
        // ventana), la ventana debe reflejarlo también. UIApplication ya
        // avisa cambios de selección nativos desde 2023, así que no hace
        // falta un ExternalEvent propio para ESCUCHAR (a diferencia de
        // ManejadorSeleccionElemento, que sí necesita uno para actuar/mandar
        // la selección de vuelta a Revit) -- este handler corre ya en
        // contexto de API válido, solo lee.
        void AlCambiarSeleccionEnRevit(object? sender, SelectionChangedEventArgs e)
        {
            if (ManejadorSeleccionElemento.SincronizacionEnCurso) return;
            if (_ventana is null || e.GetDocument().Equals(doc) == false) return;

            List<string> ids = e.GetSelectedElements().Select(id => IdTexto(id)).ToList();
            _ventana.SincronizarDesdeRevit(ids);
        }

        uiapp.SelectionChanged += AlCambiarSeleccionEnRevit;

        new WindowInteropHelper(_ventana).Owner = uiapp.MainWindowHandle;
        _ventana.Closed += (_, _) =>
        {
            uiapp.SelectionChanged -= AlCambiarSeleccionEnRevit;
            _ventana = null;
        };
        _ventana.Show();

        return Result.Succeeded;
    }

    // Mismo formato que LectorContexto.IdTexto -- ElementId.Value (long), no
    // .IntegerValue (deprecado). Los dos lados de la sincronización tienen
    // que hablar el mismo formato de id para que la búsqueda en _nodos
    // encuentre algo.
    private static string IdTexto(ElementId id) => id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // long? (no ElementId?) a propósito: ElementId ya tiene su propio .Value
    // (el long interno, ver LectorContexto), y mezclar los dos Nullable en la
    // misma expresión es una trampa segura para confundirse. Acá se resuelve
    // el nullable antes de construir el ElementId, no después.
    private static List<ElementId> ParsearIds(IReadOnlyList<string> ids) => ids
        .Select(id => long.TryParse(id, out long valor) ? valor : (long?)null)
        .Where(valor => valor is not null)
        .Select(valor => new ElementId(valor!.Value))
        .ToList();
}
