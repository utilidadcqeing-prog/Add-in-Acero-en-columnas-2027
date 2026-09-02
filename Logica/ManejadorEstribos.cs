namespace practicacq.Logica;

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using practicacq.Modelo;

/// <summary>
/// Ejecuta la generación de estribos dentro de una transacción, en contexto de API válido.
/// </summary>
public class ManejadorEstribos : IExternalEventHandler
{
    public OpcionesEstribos? Opciones { get; set; }
    public Action<ResultadoEstribos>? AlTerminar { get; set; }
    public Action<string>? AlFallar { get; set; }

    public void Execute(UIApplication app)
    {
        if (Opciones is null) return;

        Document doc = app.ActiveUIDocument?.Document!;
        if (doc is null) return;

        try
        {
            using var t = new Transaction(doc, "Generar estribos de columna");
            t.Start();

            ResultadoEstribos res = GeneradorEstribos.Generar(doc, Opciones);

            t.Commit();
            AlTerminar?.Invoke(res);
        }
        catch (Exception ex)
        {
            AlFallar?.Invoke(ex.Message);
        }
    }

    public string GetName() => "Generación de estribos de columna";
}
