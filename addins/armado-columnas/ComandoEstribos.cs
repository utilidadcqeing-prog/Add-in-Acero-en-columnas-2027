namespace practicacq;

using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI.Events;
using practicacq.Logica;
using practicacq.Vistas;

[Transaction(TransactionMode.Manual)]
public class ComandoEstribos : IExternalCommand
{
    private static VentanaConfiguradorEstribos? _ventana;

    /// <summary>El proyecto sobre el que trabaja <see cref="_ventana"/> (#168).</summary>
    private static Document? _documentoVentana;

    // Issue #24 (sincronizar selección): vive a nivel de comando, no de
    // ventana, porque VentanaConfiguradorEstribos no puede referenciar
    // Autodesk.Revit -- es la misma regla de L1 que la mantiene corriendo
    // sin Revit abierto.
    //
    // Antes había un segundo par (sin centrar la vista) para reflejar en Revit
    // lo que estaba TILDADO en la lista. El tilde desapareció con el issue #56
    // y no se recableó a lo "aceptado" a propósito: cada Aceptar le mandaría
    // una selección nueva a Revit y movería lo que el usuario esté mirando.
    private static readonly ManejadorSeleccionElemento _manejadorActivar = new() { CentrarVista = true };
    private static readonly ExternalEvent _eventoActivar = ExternalEvent.Create(_manejadorActivar);

    // J3 (#5): generar el armado real necesita transacción, por eso pasa por
    // el mismo patrón ExternalEvent que la selección de arriba.
    private static readonly ManejadorGeneracionArmado _manejadorGenerar = new();
    private static readonly ExternalEvent _eventoGenerar = ExternalEvent.Create(_manejadorGenerar);

    // Configuraciones guardadas (10-sep): escribir en el documento tambien
    // necesita transaccion, mismo patron.
    private static readonly ManejadorPersistirConfiguraciones _manejadorConfiguraciones = new();
    private static readonly ExternalEvent _eventoConfiguraciones = ExternalEvent.Create(_manejadorConfiguraciones);

    // Vista previa 3D (#123): simular el armado y pintarlo necesita contexto
    // de API, mismo patrón. Un Raise pendiente se atiende una sola vez, con
    // el último pedido de la ventana.
    private static readonly ManejadorVistaPrevia3D _manejador3D = new();
    private static readonly ExternalEvent _evento3D = ExternalEvent.Create(_manejador3D);

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiapp = commandData.Application;

        if (uiapp.ActiveUIDocument?.Document is null)
        {
            message = "Abre un proyecto antes de ejecutar el comando.";
            return Result.Failed;
        }

        // J2: proveedores reales sobre el documento activo. La ventana no cambió
        // una línea -- ver L1, este era exactamente el punto de la interfaz.
        Document doc = uiapp.ActiveUIDocument.Document;

        if (_ventana is not null && _ventana.IsLoaded)
        {
            // #168: la ventana abierta trabaja sobre el proyecto en el que se
            // abrió. Si ahora el activo es otro, se cierra (pregunta si hay
            // tramos sin generar) y se abre una para el proyecto nuevo.
            if (_documentoVentana is not null && !(_documentoVentana.IsValidObject && _documentoVentana.Equals(doc)))
                _ventana.Close();

            if (_ventana is not null)
            {
                _ventana.Activate();
                return Result.Succeeded;
            }
        }

        _documentoVentana = doc;

        OfrecerVarillasPropias(doc);

        // Las configuraciones con nombre viven DENTRO del modelo (Extensible
        // Storage), asi que viajan con el proyecto.
        var almacen = new AlmacenConfiguracionesRevit(doc);
        _manejadorConfiguraciones.Almacen = almacen;
        _manejadorConfiguraciones.AlFallar = motivo =>
            TaskDialog.Show("CQEING", $"No se pudieron guardar las configuraciones en el modelo: {motivo}");
        almacen.CambiosPendientes += () => _eventoConfiguraciones.Raise();

        _ventana = new VentanaConfiguradorEstribos(
            new LectorContexto(doc),
            new ProveedorTiposReal(doc),
            modoSimulado: false,
            almacen,
            new RegistroGeneradosRevit(doc));

        _ventana.ElementosActivados += ids =>
        {
            List<ElementId> objetivos = ParsearIds(ids);
            if (objetivos.Count == 0) return;

            _manejadorActivar.Objetivos = objetivos;
            _eventoActivar.Raise();
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

        // Vista previa 3D (#123): mismo criterio que arriba, contra la ventana viva.
        _manejador3D.Olvidar();
        _manejador3D.Documento = doc;
        _manejador3D.AlTerminar = resumen => _ventana?.MostrarResumen3D(resumen);
        _ventana.VistaPrevia3DSolicitada += (plan, filtro, abrirVista) =>
        {
            _manejador3D.Plan = plan;
            _manejador3D.Filtro = filtro;
            _manejador3D.AbrirVista |= abrirVista;
            _evento3D.Raise();
        };

        // Si se cierra el documento con la ventana abierta, el dibujo se va con él.
        void AlCerrarDocumento(object? sender, DocumentClosingEventArgs e)
        {
            // Lo llama Revit desde su lado nativo: nunca dejar escapar una excepción.
            try
            {
                if (e.Document.Equals(doc)) _manejador3D.Olvidar();
            }
            catch { /* ver comentario de AlCambiarSeleccionEnRevit */ }
        }

        uiapp.Application.DocumentClosing += AlCerrarDocumento;

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
            // Este handler lo invoca Revit directamente (no pasa por un
            // ExternalEvent, a diferencia de ManejadorSeleccionElemento) --
            // una excepción sin atrapar acá se escapa por el lado nativo del
            // dispatch de eventos de Revit y puede tirar abajo el proceso
            // entero, no solo mostrar un error de .NET. Probado en la
            // práctica: cerrar la ventana crasheaba Revit -- no hay forma de
            // garantizar que _ventana/_columnaActiva estén en un estado
            // consistente en todo momento (ver reentrancia en
            // VentanaConfiguradorEstribos.SincronizarDesdeRevit), así que
            // cualquier excepción acá se traga en vez de dejarla propagar.
            try
            {
                if (ManejadorSeleccionElemento.SincronizacionEnCurso) return;
                if (_ventana is null || e.GetDocument().Equals(doc) == false) return;

                List<string> ids = e.GetSelectedElements().Select(id => IdTexto(id)).ToList();
                _ventana.SincronizarDesdeRevit(ids);
            }
            catch { /* ver comentario de arriba -- nunca dejar escapar esto */ }
        }

        uiapp.SelectionChanged += AlCambiarSeleccionEnRevit;

        new WindowInteropHelper(_ventana).Owner = uiapp.MainWindowHandle;
        _ventana.Closed += (_, _) =>
        {
            // Mismo motivo que en AlCambiarSeleccionEnRevit: esto corre
            // colgado del cierre nativo de una ventana parentada al HWND de
            // Revit (WindowInteropHelper.Owner arriba) -- si uiapp ya no es
            // válido en ese momento (Revit cerrando, documento cerrándose),
            // "-=" puede tirar una excepción que no tiene por dónde
            // desenrollarse limpio del lado de Revit.
            try { uiapp.SelectionChanged -= AlCambiarSeleccionEnRevit; }
            catch { /* ver comentario de arriba */ }

            // La vista previa 3D vive mientras la ventana esté abierta (#123).
            try
            {
                uiapp.Application.DocumentClosing -= AlCerrarDocumento;
                _manejador3D.Olvidar();
                uiapp.ActiveUIDocument?.RefreshActiveView();
            }
            catch { /* ver comentario de arriba */ }

            _ventana = null;
        };
        _ventana.Show();

        return Result.Succeeded;
    }

    /// <summary>
    /// Si al modelo le falta alguna de las varillas "#N CQEING", pregunta si
    /// agregarlas. La respuesta no se guarda a propósito: si dice que no, se
    /// vuelve a preguntar cada vez que se abre el configurador hasta que diga
    /// que sí (pedido de Luciano, 19-oct). Corre acá y no desde la ventana
    /// porque crear tipos necesita transacción, y en Execute ya estamos en
    /// contexto de API válido.
    /// </summary>
    internal static void OfrecerVarillasPropias(Document doc)
    {
        List<practicacq.Modelo.CuadroVarillas.Varilla> faltantes = VarillasPropias.Faltantes(doc);
        if (faltantes.Count == 0) return;

        string lista = string.Join(", ", faltantes.Select(v => practicacq.Modelo.CuadroVarillas.NombrePropio(v.Numero)));
        var pregunta = new TaskDialog("CQEING")
        {
            MainInstruction = "Este modelo no tiene las varillas CQEING. ¿Agregarlas?",
            MainContent = $"Faltan: {lista}.\n\nSe agregan con los diámetros del cuadro de la empresa. " +
                "Las barras que ya tiene el modelo no se tocan.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.Yes,
        };
        if (pregunta.Show() != TaskDialogResult.Yes) return;

        if (VarillasPropias.Crear(doc, faltantes) is string motivo)
            TaskDialog.Show("CQEING", $"No se pudieron agregar las varillas CQEING: {motivo}");
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
