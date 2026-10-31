using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Web.WebView2.Core;

namespace CQEING.Vigas.Tablero;

/// <summary>Abre el tablero de vigas (#83), una sola ventana sin modo; si ya está abierta, la trae al frente.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoTablero : IExternalCommand
{
    private const string Titulo = "Acero en vigas";

    private const string FaltaRuntime =
        "Falta Microsoft Edge WebView2 Runtime. Instálelo desde https://go.microsoft.com/fwlink/p/?LinkId=2124703 y vuelva a abrir la ventana.";

    private static VentanaTablero? abierta;

    /// <summary>Si falta WebView2 Runtime, lo avisa con <paramref name="titulo"/>. También lo usa «Crear viguetas» (#947).</summary>
    internal static bool HayWebView2(string titulo)
    {
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
            return true;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            TaskDialog.Show(titulo, FaltaRuntime);
            return false;
        }
    }

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (abierta is not null)
        {
            abierta.Volver();
            return Result.Succeeded;
        }

        if (!HayWebView2(Titulo))
            return Result.Cancelled;

        UIApplication uiapp = commandData.Application;
        VentanaTablero ventana;
        try
        {
            ventana = new VentanaTablero(uiapp.MainWindowHandle);
        }
        catch (Exception e)
        {
            // Revit muestra solo el mensaje de afuera ("Set connectionId threw an exception"); la causa va adentro.
            message = e.ToString();
            return Result.Failed;
        }
        // El puente se crea aquí, con la API válida: su ExternalEvent no se puede crear desde la ventana.
        _ = new PuenteTablero(ventana, uiapp);
        ventana.Closed += (_, _) => abierta = null;
        abierta = ventana;
        ventana.Show();
        return Result.Succeeded;
    }
}
