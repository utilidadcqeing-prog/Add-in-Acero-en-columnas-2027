using Autodesk.Revit.UI;
using CQEING.Compartido;

namespace CQEING.Vigas;

/// <summary>Registra el panel «Vigas» en la pestaña CQEING. Épica #78.</summary>
public class AppVigas : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        RibbonPanel panel = CintaCqeing.ObtenerPanel(app, "Vigas");

        panel.AddItem(CintaCqeing.CrearBoton(
            "ComandoTablero",
            "Tablero",
            "CQEING.Vigas.Tablero.ComandoTablero",
            "Abre el tablero de acero en vigas: pórticos, barras, traslapos, bastones, estribos y kilos.",
            "iconoCQ"));

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
}
