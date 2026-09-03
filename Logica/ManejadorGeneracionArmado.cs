namespace practicacq.Logica;

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using practicacq.Modelo;

/// <summary>
/// Ejecuta la generación de armado (J3) dentro de una transacción, en
/// contexto de API válido -- la ventana es modeless y no puede abrir
/// transacciones por su cuenta. Mismo patrón que <see cref="ManejadorGeneracion"/>
/// (columnetas/viguetas) y <see cref="ManejadorSeleccionElemento"/> (#24).
/// </summary>
public class ManejadorGeneracionArmado : IExternalEventHandler
{
    public OpcionesArmado? Opciones { get; set; }
    public Action<ResultadoArmado>? AlTerminar { get; set; }
    public Action<string>? AlFallar { get; set; }

    public void Execute(UIApplication app)
    {
        if (Opciones is null) return;

        Document doc = app.ActiveUIDocument?.Document!;
        if (doc is null) return;

        try
        {
            using var t = new Transaction(doc, "Generar armado de columnas");
            t.Start();

            ResultadoArmado res = GeneradorArmado.Generar(doc, Opciones);

            t.Commit();
            AlTerminar?.Invoke(res);
        }
        catch (Exception ex)
        {
            AlFallar?.Invoke(ex.Message);
        }
    }

    public string GetName() => "Generación de armado de columnas";
}
