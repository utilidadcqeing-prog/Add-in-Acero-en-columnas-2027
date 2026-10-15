using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using practicacq.Imagenes.Revit;
using practicacq.Imagenes.Vistas;

namespace practicacq.Imagenes;

/// <summary>
/// Botón "Imágenes por actividad" de la pestaña CQEING: abre la ventana no modal (una sola a la vez).
/// Issue utilidadcqeing-prog/Add-in-Cuadro-de-cantidades-V0#183.
///
/// UBICACIÓN PROVISIONAL (decisión del 2026-10-13): vive en este repo (armado de columnas, rama
/// <c>addin-de-imagenes</c>) aunque la issue está en el repo del add-in de cantidades. Todo lo del
/// add-in de imágenes está en la carpeta <c>Imagenes/</c> y no depende del armado de columnas, así que
/// si el equipo decide moverlo a su propio proyecto o al de cantidades, basta con copiar esa carpeta
/// y el botón de <c>App.cs</c>.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ComandoImagenesActividad : IExternalCommand
{
    private static VentanaImagenesActividad? _ventana;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiapp = commandData.Application;
        Document? doc = uiapp.ActiveUIDocument?.Document;
        if (doc is null)
        {
            message = "Abre un proyecto antes de ejecutar el comando.";
            return Result.Failed;
        }

        if (_ventana is not null && _ventana.IsLoaded)
        {
            _ventana.Activate();
            return Result.Succeeded;
        }

        // El ExternalEvent se tiene que crear en contexto de API: aquí, en el comando.
        EjecutorRevit.Inicializar();

        var puente = new PuenteRevit(uiapp, IdentificadorModelo.Obtener(doc), doc.Title);
        _ventana = new VentanaImagenesActividad(puente, uiapp.MainWindowHandle);
        new WindowInteropHelper(_ventana).Owner = uiapp.MainWindowHandle;
        _ventana.Closed += (_, _) => _ventana = null;
        _ventana.Show();

        return Result.Succeeded;
    }
}
