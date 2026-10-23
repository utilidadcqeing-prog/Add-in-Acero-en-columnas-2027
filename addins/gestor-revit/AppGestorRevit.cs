using Autodesk.Revit.UI;
using CQEING.Compartido;

namespace CQEING.GestorRevit;

/// <summary>
/// Registra el panel «Gestor» en la pestaña CQEING. Es un panel propio, aparte de los de los add-ins, para que el
/// botón del gestor nunca se confunda con una herramienta de trabajo. Al abrir Revit consulta, en segundo plano, si hay
/// versiones nuevas de los add-ins instalados y avisa (<see cref="AvisoNovedades"/>). Issue #159.
/// </summary>
public class AppGestorRevit : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        RibbonPanel panel = CintaCqeing.ObtenerPanel(app, "Gestor");

        var boton = (PushButton)panel.AddItem(CintaCqeing.CrearBoton(
            "ComandoAbrirGestor",
            AvisoNovedades.TextoBoton,
            "CQEING.GestorRevit.ComandoAbrirGestor",
            AvisoNovedades.AyudaBoton,
            "iconoGestor"));

        AvisoNovedades.Empezar(app.ControlledApplication.VersionNumber, boton);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
}
