using Autodesk.Revit.UI;
using CQEING.Compartido;

namespace CQEING.Despiece;

/// <summary>Registra el panel «Despiece» en la pestaña CQEING. Épica #217.</summary>
public class AppDespiece : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        // Sin licencia vigente no se crea el botón; el aviso lo da el componente del gestor (issue #189).
        if (!Licencia.Vigente(out _)) return Result.Succeeded;
        // Con una versión nueva publicada, tampoco: hay que actualizar desde el gestor (actualización obligatoria).
        if (!BloqueoPorVersion.AlDia(app)) return Result.Succeeded;

        RibbonPanel panel = CintaCqeing.ObtenerPanel(app, "Despiece");

        panel.AddItem(CintaCqeing.CrearBoton(
            "ComandoDespiece",
            "Despiece",
            "CQEING.Despiece.Interfaz.ComandoDespiece",
            "Abre el despiece: elige un elemento, revísalo y crea sus vistas.",
            "iconoCQ"));

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
}
