namespace practicacq.Logica;

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using practicacq.Modelo;

/// <summary>
/// Ejecuta la generación dentro de una transacción, en contexto de API válido.
/// </summary>
public class ManejadorGeneracion : IExternalEventHandler
{
    public OpcionesGeneracion? Opciones { get; set; }
    public Action<ResultadoGeneracion>? AlTerminar { get; set; }
    public Action<string>? AlFallar { get; set; }

    public void Execute(UIApplication app)
    {
        if (Opciones is null) return;

        Document doc = app.ActiveUIDocument?.Document!;
        if (doc is null) return;

        try
        {
            using var t = new Transaction(doc, "Generar columnetas y viguetas");
            t.Start();

            ResultadoGeneracion res = GeneradorConfinamiento.Generar(doc, Opciones);

            t.Commit();
            AlTerminar?.Invoke(res);
        }
        catch (Exception ex)
        {
            AlFallar?.Invoke(ex.Message);
        }
    }

    public string GetName() => "Generación de columnetas y viguetas";
}
