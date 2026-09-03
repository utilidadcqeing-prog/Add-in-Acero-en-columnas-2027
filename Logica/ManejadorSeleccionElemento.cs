namespace practicacq.Logica;

using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

/// <summary>
/// Issue #24: selecciona en Revit el elemento que se activó desde la
/// ventana -- lo mismo que darle clic vos mismo en el modelo. Nada más:
/// nunca abre la familia (eso fue lo que le puso lento el equipo a William
/// en la reunión del 3-sep con el doble clic nativo).
///
/// Va por <see cref="IExternalEventHandler"/> porque la ventana es modeless
/// y <c>Selection.SetElementIds</c> necesita contexto de API, igual que
/// <see cref="ManejadorSeleccionColumnas"/> pero al revés: acá quien manda
/// qué seleccionar es la ventana, no el usuario.
/// </summary>
public sealed class ManejadorSeleccionElemento : IExternalEventHandler
{
    /// <summary>El elemento a seleccionar. Se fija antes de llamar a <c>Raise()</c>.</summary>
    public ElementId? Objetivo { get; set; }

    public void Execute(UIApplication app)
    {
        UIDocument? uidoc = app.ActiveUIDocument;
        if (uidoc is null || Objetivo is null) return;

        // Elemento borrado o de otro documento entretanto: no hacer nada en
        // vez de reventar la ventana con una excepción de Revit.
        if (uidoc.Document.GetElement(Objetivo) is null) return;

        uidoc.Selection.SetElementIds(new List<ElementId> { Objetivo });
        uidoc.ShowElements(Objetivo);
    }

    public string GetName() => "Seleccionar elemento desde el addin";
}
