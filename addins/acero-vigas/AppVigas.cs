using Autodesk.Revit.UI;
using CQEING.Compartido;

namespace CQEING.Vigas;

/// <summary>Registra el panel «Vigas» en la pestaña CQEING. Épica #78.</summary>
public class AppVigas : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        RibbonPanel panel = CintaCqeing.ObtenerPanel(app, "Vigas");

        // Temporal (#81): se quita cuando el tablero (#83) tenga "Seleccionar en Revit".
        panel.AddItem(CintaCqeing.CrearBoton(
            "ComandoDiagnosticoPorticos",
            "Pórticos\n(diagnóstico)",
            "CQEING.Vigas.Revit.ComandoDiagnosticoPorticos",
            "Lee vigas, ejes, apoyos y niveles del modelo y escribe un informe para verificar la lectura de pórticos.",
            "iconoCQ"));

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
}
