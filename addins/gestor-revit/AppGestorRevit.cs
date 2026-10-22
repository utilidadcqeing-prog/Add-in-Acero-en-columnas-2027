using Autodesk.Revit.UI;
using CQEING.Compartido;

namespace CQEING.GestorRevit;

/// <summary>
/// Registra el panel «Gestor» en la pestaña CQEING. Es un panel propio, aparte de los de los add-ins, para que el
/// botón del gestor nunca se confunda con una herramienta de trabajo. Issue #159.
/// </summary>
public class AppGestorRevit : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        RibbonPanel panel = CintaCqeing.ObtenerPanel(app, "Gestor");

        panel.AddItem(CintaCqeing.CrearBoton(
            "ComandoAbrirGestor",
            "Gestor de\nadd-ins",
            "CQEING.GestorRevit.ComandoAbrirGestor",
            "Abre el Gestor de add-ins CQEING: instala y actualiza los add-ins de CQEING. Las versiones nuevas se ven al reiniciar Revit.",
            "iconoGestor"));

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
}
