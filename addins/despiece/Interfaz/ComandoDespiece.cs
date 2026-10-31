using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Web.WebView2.Core;

namespace CQEING.Despiece.Interfaz;

/// <summary>Abre el despiece (#220), una sola ventana sin modo; si ya está abierta, la trae al frente.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoDespiece : IExternalCommand
{
    private const string Titulo = "Despiece";

    private const string FaltaRuntime =
        "Falta Microsoft Edge WebView2 Runtime. Instálelo desde https://go.microsoft.com/fwlink/p/?LinkId=2124703 y vuelva a abrir el despiece.";

    private static VentanaDespiece? abierta;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (abierta is not null)
        {
            abierta.Volver();
            return Result.Succeeded;
        }

        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            TaskDialog.Show(Titulo, FaltaRuntime);
            return Result.Cancelled;
        }

        VentanaDespiece ventana;
        try
        {
            ventana = new VentanaDespiece(commandData.Application.MainWindowHandle);
        }
        catch (Exception e)
        {
            // Revit muestra solo el mensaje de afuera ("Set connectionId threw an exception"); la causa va adentro.
            message = e.ToString();
            return Result.Failed;
        }
        // El puente se crea aquí, con la API válida: su ExternalEvent no se puede crear desde la ventana.
        _ = new PuenteDespiece(ventana, commandData.Application);
        // Una excepción sin atender en el hilo de Revit lo cierra entero ("error irrecuperable"). Mientras la ventana
        // esté abierta se anota y no se deja escapar; al cerrarla se suelta, para no tapar errores de otros add-ins.
        void SinAtender(object? sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Registro.Paso("EXCEPCIÓN SIN ATENDER: " + e.Exception);
            e.Handled = true;
        }
        ventana.Dispatcher.UnhandledException += SinAtender;
        ventana.Closed += (_, _) =>
        {
            ventana.Dispatcher.UnhandledException -= SinAtender;
            abierta = null;
        };
        abierta = ventana;
        ventana.Show();
        return Result.Succeeded;
    }
}
