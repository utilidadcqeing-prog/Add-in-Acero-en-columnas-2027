using System.Text.Json;
using System.Text.Json.Nodes;
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
    private const string Titulo = "Tablero de vigas";

    private const string FaltaRuntime =
        "Falta Microsoft Edge WebView2 Runtime. Instálelo desde https://go.microsoft.com/fwlink/p/?LinkId=2124703 y vuelva a abrir el tablero.";

    private static VentanaTablero? abierta;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (abierta is not null)
        {
            if (abierta.WindowState == WindowState.Minimized)
                abierta.WindowState = WindowState.Normal;
            abierta.Activate();
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

        var ventana = new VentanaTablero(commandData.Application.MainWindowHandle);
        ventana.MensajeRecibido += json => Responder(ventana, json);
        ventana.Closed += (_, _) => abierta = null;
        abierta = ventana;
        ventana.Show();
        return Result.Succeeded;
    }

    /// <summary>Temporal (tarea 1 del plan): devuelve el eco y, al cargar, el diagnóstico de WebView2.</summary>
    private static void Responder(VentanaTablero ventana, string json)
    {
        JsonNode? mensaje = JsonNode.Parse(json);
        string? tipo = mensaje?["tipo"]?.GetValue<string>();
        if (tipo == "eco")
            ventana.Enviar(json);
        else if (tipo == "listo")
            ventana.Enviar(JsonSerializer.Serialize(new { tipo = "diagnostico", texto = ventana.Diagnostico }));
    }
}
