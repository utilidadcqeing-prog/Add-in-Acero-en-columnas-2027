namespace practicacq.Logica;

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

/// <summary>
/// Escribe en el documento las configuraciones guardadas
/// (<see cref="AlmacenConfiguracionesRevit"/>). Va por
/// <c>ExternalEvent</c>, mismo patrón que la selección y la generación: la
/// ventana es NO modal, así que no puede abrir una transacción cuando quiere --
/// Revit solo acepta modificar el documento desde un contexto de API válido.
/// </summary>
public sealed class ManejadorPersistirConfiguraciones : IExternalEventHandler
{
    public AlmacenConfiguracionesRevit? Almacen { get; set; }

    /// <summary>Se llama con el motivo si no se pudo guardar.</summary>
    public Action<string>? AlFallar { get; set; }

    public void Execute(UIApplication app)
    {
        if (Almacen is null) return;
        if (app.ActiveUIDocument?.Document is not Document doc) return;

        using var transaccion = new Transaction(doc, "CQEING - guardar configuraciones");
        try
        {
            transaccion.Start();
            Bitacora3D.Anotar("guardar configuraciones: empieza");

            string? motivo = Almacen.Persistir();
            if (motivo is null)
            {
                transaccion.Commit();
                Bitacora3D.Anotar("guardar configuraciones: listo");
                return;
            }

            transaccion.RollBack();
            AlFallar?.Invoke(motivo);
        }
        catch (Exception ex)
        {
            if (transaccion.HasStarted() && !transaccion.HasEnded()) transaccion.RollBack();
            AlFallar?.Invoke(ex.Message);
        }
    }

    public string GetName() => "CQEING - guardar configuraciones de armado";
}
