using Autodesk.Revit.UI;
using CQEING.Compartido;

namespace CQEING.Vigas;

/// <summary>Registra el panel «Vigas» en la pestaña CQEING. Épica #78.</summary>
public class AppVigas : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        // Sin licencia vigente no se crea el botón; el aviso lo da el componente del gestor (issue #189).
        if (!Licencia.Vigente(out _)) return Result.Succeeded;
        // Con una versión nueva publicada, tampoco: hay que actualizar desde el gestor (actualización obligatoria).
        if (!BloqueoPorVersion.AlDia(app)) return Result.Succeeded;

        RibbonPanel panel = CintaCqeing.ObtenerPanel(app, "Vigas");

        panel.AddItem(CintaCqeing.CrearBoton(
            "ComandoTablero",
            "Acero en vigas",
            "CQEING.Vigas.Tablero.ComandoTablero",
            "Abre el tablero de acero en vigas: pórticos, barras, traslapos, bastones, estribos y kilos.",
            "vigas"));

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
}
