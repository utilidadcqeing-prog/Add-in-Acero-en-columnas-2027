using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CQEING.Vigas.Tablero;

namespace CQEING.Vigas.Viguetas;

/// <summary>Abre «Crear viguetas» (#947), una sola ventana sin modo; si ya está abierta, la trae al frente.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoViguetas : IExternalCommand
{
    private const string Titulo = "Crear viguetas";

    private static VentanaTablero? abierta;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (abierta is not null)
        {
            abierta.Volver();
            return Result.Succeeded;
        }
        UIApplication uiapp = commandData.Application;
        if (uiapp.ActiveUIDocument?.Document is not Document doc || doc.IsFamilyDocument)
        {
            TaskDialog.Show(Titulo, "Abra un modelo de proyecto para crear viguetas.");
            return Result.Cancelled;
        }
        if (!ComandoTablero.HayWebView2(Titulo))
            return Result.Cancelled;

        VentanaTablero ventana;
        try
        {
            ventana = new VentanaTablero(uiapp.MainWindowHandle, "viguetas.html", Titulo);
        }
        catch (Exception e)
        {
            message = e.ToString();
            return Result.Failed;
        }
        // Como en el tablero: el ExternalEvent del puente se crea aquí, con la API válida.
        _ = new PuenteViguetas(ventana, doc);
        ventana.Closed += (_, _) => abierta = null;
        abierta = ventana;
        ventana.Show();
        return Result.Succeeded;
    }
}
