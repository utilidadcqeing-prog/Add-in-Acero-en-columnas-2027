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
/// <see cref="ManejadorSeleccion"/> pero al revés: acá quien manda qué
/// seleccionar es la ventana, no el usuario.
/// </summary>
public sealed class ManejadorSeleccionElemento : IExternalEventHandler
{
    /// <summary>
    /// Los elementos a seleccionar. Se fija antes de llamar a <c>Raise()</c>.
    /// Una lista vacía (no <c>null</c>) es válida y significa "deseleccionar
    /// todo" -- pasa cuando se destildan todos los tramos de la lista.
    /// </summary>
    public IReadOnlyList<ElementId>? Objetivos { get; set; }

    /// <summary>
    /// Si además de seleccionar hay que mover la cámara a los elementos.
    /// Pedido explícito: tildar en la lista solo debe reflejarse como
    /// seleccionado, sin saltarle la vista al usuario -- eso queda reservado
    /// para activar una fila (clic o flechas).
    /// </summary>
    public bool CentrarVista { get; set; } = true;

    /// <summary>
    /// True mientras este handler está cambiando la selección de Revit por
    /// pedido del addin (no del usuario). <c>UIApplication.SelectionChanged</c>
    /// se dispara igual sea cual sea el origen del cambio -- sin esto, la
    /// sincronización Revit→addin (ver <c>ComandoEstribos</c>) tomaría esta
    /// selección programática como si el usuario hubiera clickeado en Revit y
    /// reactivaría la misma fila de nuevo (inofensivo, pero trabajo de más en
    /// cada clic hecho desde la ventana). Estático porque hay dos instancias
    /// de este handler (activar / marcar) y cualquiera de las dos puede estar
    /// tocando la selección en un momento dado.
    /// </summary>
    public static bool SincronizacionEnCurso { get; private set; }

    public void Execute(UIApplication app)
    {
        UIDocument? uidoc = app.ActiveUIDocument;
        if (uidoc is null || Objetivos is null) return;

        // Elementos borrados o de otro documento entretanto: se filtran en vez
        // de reventar la ventana con una excepción de Revit.
        List<ElementId> existentes = Objetivos
            .Where(id => uidoc.Document.GetElement(id) is not null)
            .ToList();

        SincronizacionEnCurso = true;
        try
        {
            uidoc.Selection.SetElementIds(existentes);

            if (CentrarVista && existentes.Count > 0)
                uidoc.ShowElements(existentes);
        }
        finally
        {
            SincronizacionEnCurso = false;
        }
    }

    public string GetName() => "Seleccionar elemento desde el addin";
}
