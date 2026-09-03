namespace practicacq.Logica;

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

/// <summary>
/// Issue #24: selecciona en Revit los elementos que se activaron desde la
/// ventana -- lo mismo que darle clic vos mismo en el modelo. Nada más:
/// nunca abre la familia (eso fue lo que le puso lento el equipo a William
/// en la reunión del 3-sep con el doble clic nativo).
///
/// Son varios elementos y no uno solo porque una columna «lógica» puede ser
/// varios <c>FamilyInstance</c> apilados (cimentación, piso, nudo de viga --
/// ver #28): seleccionar solo el de más abajo dejaba la zapata sin resaltar.
///
/// Va por <see cref="IExternalEventHandler"/> porque la ventana es modeless
/// y <c>Selection.SetElementIds</c> necesita contexto de API, igual que
/// <see cref="ManejadorSeleccionColumnas"/> pero al revés: acá quien manda
/// qué seleccionar es la ventana, no el usuario.
/// </summary>
public sealed class ManejadorSeleccionElemento : IExternalEventHandler
{
    /// <summary>Los elementos a seleccionar. Se fija antes de llamar a <c>Raise()</c>.</summary>
    public IReadOnlyList<ElementId>? Objetivos { get; set; }

    public void Execute(UIApplication app)
    {
        UIDocument? uidoc = app.ActiveUIDocument;
        if (uidoc is null || Objetivos is null || Objetivos.Count == 0) return;

        // Elementos borrados o de otro documento entretanto: se filtran en vez
        // de reventar la ventana con una excepción de Revit.
        List<ElementId> existentes = Objetivos
            .Where(id => uidoc.Document.GetElement(id) is not null)
            .ToList();

        if (existentes.Count == 0) return;

        uidoc.Selection.SetElementIds(existentes);
        uidoc.ShowElements(existentes);
    }

    public string GetName() => "Seleccionar elemento desde el addin";
}
