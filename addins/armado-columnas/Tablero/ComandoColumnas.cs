namespace practicacq.Tablero;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Web.WebView2.Core;
using practicacq.Logica;
using practicacq.Modelo;

/// <summary>
/// Abre la ventana web de columnas (#205, #207): una sola ventana sin modo; si
/// ya está abierta, la trae al frente. Como <c>ComandoTablero</c> de vigas.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ComandoColumnas : IExternalCommand
{
    private const string Titulo = "Acero en columnas";

    private const string FaltaRuntime =
        "Falta Microsoft Edge WebView2 Runtime. Instálelo desde https://go.microsoft.com/fwlink/p/?LinkId=2124703 y vuelva a abrir la ventana de columnas.";

    private static VentanaColumnas? abierta;

    /// <summary>El proyecto sobre el que trabaja <see cref="abierta"/>.</summary>
    private static Document? documentoAbierta;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiapp = commandData.Application;
        if (uiapp.ActiveUIDocument?.Document is not Document documento || documento.IsFamilyDocument)
        {
            message = "Abre un proyecto antes de ejecutar el comando.";
            return Result.Failed;
        }

        if (abierta is not null)
        {
            // La ventana abierta trabaja sobre el proyecto en el que se abrió: si
            // ahora el activo es otro, se cierra y se abre una para el nuevo.
            if (documentoAbierta is not null && documentoAbierta.IsValidObject && documentoAbierta.Equals(documento))
            {
                abierta.Volver();
                return Result.Succeeded;
            }
            abierta.Close();
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

        OfrecerVarillasPropias(documento);

        VentanaColumnas ventana;
        try
        {
            ventana = new VentanaColumnas(uiapp.MainWindowHandle);
        }
        catch (Exception e)
        {
            // Revit muestra solo el mensaje de afuera ("Set connectionId threw an exception"); la causa va adentro.
            message = e.ToString();
            return Result.Failed;
        }
        // El puente se crea aquí, con la API válida: su ExternalEvent no se puede crear desde la ventana.
        _ = new PuenteColumnas(ventana, uiapp, documento);
        ventana.Closed += (_, _) =>
        {
            if (abierta == ventana)
            {
                abierta = null;
                documentoAbierta = null;
            }
        };
        abierta = ventana;
        documentoAbierta = documento;
        ventana.Show();
        return Result.Succeeded;
    }

    /// <summary>
    /// Si al modelo le falta alguna de las varillas "#N CQEING", pregunta si
    /// agregarlas. La respuesta no se guarda a propósito: si dice que no, se
    /// vuelve a preguntar cada vez que se abre la ventana hasta que diga que sí
    /// (pedido de Luciano, 19-oct). Corre acá y no desde la ventana porque crear
    /// tipos necesita transacción, y en Execute ya estamos en contexto de API
    /// válido. Venía de <c>ComandoEstribos</c>, que se borró con la ventana vieja (#212).
    /// </summary>
    private static void OfrecerVarillasPropias(Document doc)
    {
        List<CuadroVarillas.Varilla> faltantes = VarillasPropias.Faltantes(doc);
        if (faltantes.Count == 0) return;

        string lista = string.Join(", ", faltantes.Select(v => CuadroVarillas.NombrePropio(v.Numero)));
        var pregunta = new TaskDialog("CQEING")
        {
            MainInstruction = "Este modelo no tiene las varillas CQEING. ¿Agregarlas?",
            MainContent = $"Faltan: {lista}.\n\nSe agregan con los diámetros del cuadro de la empresa. " +
                "Las barras que ya tiene el modelo no se tocan.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.Yes,
        };
        if (pregunta.Show() != TaskDialogResult.Yes) return;

        if (VarillasPropias.Crear(doc, faltantes) is string motivo)
            TaskDialog.Show("CQEING", $"No se pudieron agregar las varillas CQEING: {motivo}");
    }
}
