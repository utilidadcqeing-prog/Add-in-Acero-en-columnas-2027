namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

/// <summary>
/// Pide al usuario que seleccione columnas estructurales. Va por ExternalEvent
/// porque la ventana es modeless y PickObjects necesita contexto de API.
/// </summary>
public class ManejadorSeleccionColumnas : IExternalEventHandler
{
    public Action<List<FamilyInstance>>? AlTerminar { get; set; }

    public void Execute(UIApplication app)
    {
        UIDocument uidoc = app.ActiveUIDocument;
        if (uidoc is null) return;

        var columnas = new List<FamilyInstance>();

        try
        {
            IList<Reference> refs = uidoc.Selection.PickObjects(
                ObjectType.Element,
                new FiltroColumnas(),
                "Selecciona las columnas y termina con Finalizar");

            foreach (Reference r in refs)
            {
                if (uidoc.Document.GetElement(r) is FamilyInstance columna)
                    columnas.Add(columna);
            }
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            // El usuario pulsó Escape: dejamos la lista como estaba.
            return;
        }

        AlTerminar?.Invoke(columnas);
    }

    public string GetName() => "Selección de columnas";
}
