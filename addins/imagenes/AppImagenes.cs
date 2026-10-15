using Autodesk.Revit.UI;
using CQEING.Compartido;

namespace practicacq.Imagenes;

/// <summary>Registra el panel «Imágenes» en la pestaña CQEING. Issue utilidadcqeing-prog/Add-in-Cuadro-de-cantidades-V0#183.</summary>
public class AppImagenes : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        RibbonPanel panel = CintaCqeing.ObtenerPanel(app, "Imágenes");

        panel.AddItem(CintaCqeing.CrearBoton(
            "ComandoImagenesActividad",
            "Imágenes\npor actividad",
            "practicacq.Imagenes.ComandoImagenesActividad",
            "Crea imágenes de los elementos de cada actividad del presupuesto: selecciona en Revit, elige colores y guarda con el código de la actividad.",
            "iconoCQ"));

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
}
