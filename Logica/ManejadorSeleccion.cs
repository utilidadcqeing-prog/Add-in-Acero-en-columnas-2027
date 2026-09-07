namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

/// <summary>
/// Pide al usuario que seleccione muros en el modelo. Va por ExternalEvent
/// porque la ventana es modeless y PickObjects necesita contexto de API.
/// </summary>
public class ManejadorSeleccion : IExternalEventHandler
{
    public Action<List<Wall>>? AlTerminar { get; set; }

    public void Execute(UIApplication app)
    {
        UIDocument uidoc = app.ActiveUIDocument;
        if (uidoc is null) return;

        var muros = new List<Wall>();

        try
        {
            IList<Reference> refs = uidoc.Selection.PickObjects(
                ObjectType.Element,
                new FiltroMuros(),
                "Selecciona los muros y termina con Finalizar");

            foreach (Reference r in refs)
            {
                if (uidoc.Document.GetElement(r) is Wall muro)
                    muros.Add(muro);
            }
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            // El usuario pulsó Escape: dejamos la lista como estaba.
            return;
        }

        AlTerminar?.Invoke(muros);
    }

    public string GetName() => "Selección de muros";
}
