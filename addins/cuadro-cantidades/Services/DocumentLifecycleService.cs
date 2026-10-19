using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Argumentos de evento para cuando cambia la selección de elementos en la interfaz de Revit.
/// </summary>
public class SelectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// Obtiene la colección de identificadores (<see cref="ElementId"/>) de los elementos actualmente seleccionados.
    /// </summary>
    public IReadOnlyCollection<ElementId> SelectedIds { get; }

    /// <summary>
    /// Obtiene el documento de Revit activo al momento de realizar la selección.
    /// </summary>
    public Document Document { get; }

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="SelectionChangedEventArgs"/>.
    /// </summary>
    /// <param name="document">El documento de Revit.</param>
    /// <param name="selectedIds">La colección de elementos seleccionados.</param>
    public SelectionChangedEventArgs(Document document, IReadOnlyCollection<ElementId> selectedIds)
    {
        Document = document;
        SelectedIds = selectedIds;
    }
}

/// <summary>
/// Servicio estático interno que gestiona el ciclo de vida del documento de Revit y monitorea eventos globales,
/// como el cierre del documento, modificaciones en la base de datos del modelo, y cambios en la selección de elementos en tiempo de inactividad (Idling).
/// </summary>
internal static class DocumentLifecycleService
{
    private static bool _isInitialized;
    private static ICollection<ElementId> _lastSelection = new List<ElementId>();

    /// <summary>
    /// Evento que se desencadena cuando cambia la selección de elementos en el documento activo de Revit.
    /// </summary>
    public static event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    /// <summary>
    /// Evento que se desencadena cuando se producen cambios (adiciones, modificaciones, eliminaciones) en el documento de Revit.
    /// </summary>
    public static event EventHandler<DocumentChangedEventArgs>? DocumentChanged;

    /// <summary>
    /// Inicializa las suscripciones del servicio a los eventos del ciclo de vida de la aplicación Revit.
    /// </summary>
    /// <param name="application">La aplicación controlada de la interfaz de usuario de Revit.</param>
    public static void Initialize(UIControlledApplication application)
    {
        if (_isInitialized)
        {
            return;
        }

        // Suscribirse a los eventos del ciclo de vida
        application.ControlledApplication.DocumentClosing += OnDocumentClosing;
        application.ControlledApplication.DocumentChanged += OnDocumentChanged;
        application.Idling += OnIdling;
        _isInitialized = true;
    }

    /// <summary>
    /// Cancela las suscripciones y detiene el monitoreo de eventos de ciclo de vida del documento.
    /// </summary>
    /// <param name="application">La aplicación controlada de la interfaz de usuario de Revit.</param>
    public static void Shutdown(UIControlledApplication application)
    {
        if (!_isInitialized)
        {
            return;
        }

        // Desuscribirse de los eventos para evitar fugas de memoria
        application.ControlledApplication.DocumentClosing -= OnDocumentClosing;
        application.ControlledApplication.DocumentChanged -= OnDocumentChanged;
        application.Idling -= OnIdling;
        _isInitialized = false;
    }

    /// <summary>
    /// Manejador de eventos para el cierre de un documento en Revit.
    /// </summary>
    private static void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
    {
        // Cerrar la ventana del complemento si el documento que se está cerrando es el activo
        ModelessWindowService.CloseIfDocument(e.Document);
    }

    /// <summary>
    /// Manejador de eventos que se dispara cuando cambia la base de datos de un documento de Revit.
    /// </summary>
    private static void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        DocumentChanged?.Invoke(sender, e);
    }

    /// <summary>
    /// Manejador de eventos para el estado de inactividad de Revit. Se utiliza para detectar cambios en la selección de forma no intrusiva.
    /// </summary>
    private static void OnIdling(object? sender, IdlingEventArgs e)
    {
        try
        {
            if (sender is not UIApplication uiApp || uiApp.ActiveUIDocument is null)
            {
                return;
            }

            // Obtener los ElementIds seleccionados actualmente
            var currentSelection = GetSelectedElementIds(uiApp.ActiveUIDocument);

            // Comparar con la última selección registrada para determinar si hubo cambios
            if (!AreCollectionsEqual(_lastSelection, currentSelection))
            {
                _lastSelection = currentSelection;
                SelectionChanged?.Invoke(
                    sender,
                    new SelectionChangedEventArgs(
                        uiApp.ActiveUIDocument.Document,
                        currentSelection.ToList()
                    )
                );
            }
        }
        catch
        {
            // Silenciar cualquier error de la API de Revit durante Idling
            // (ej. cuando la vista no está disponible o un diálogo modal bloquea la selección).
        }
    }

    /// <summary>
    /// Elementos seleccionados en Revit, incluidos los dueños de caras seleccionadas: GetElementIds no los
    /// devuelve cuando se seleccionan caras (por ejemplo, las que marca la ventana de detalles).
    /// </summary>
    /// <param name="uiDocument">Documento activo de Revit.</param>
    public static ICollection<ElementId> GetSelectedElementIds(UIDocument uiDocument)
    {
        var selection = uiDocument.Selection;
        var ids = new HashSet<ElementId>(selection.GetElementIds());
        foreach (var reference in selection.GetReferences())
        {
            if (reference.ElementId != ElementId.InvalidElementId)
                ids.Add(reference.ElementId);
        }

        return ids.ToList();
    }

    /// <summary>
    /// Compara dos colecciones de <see cref="ElementId"/> para determinar si son idénticas en contenido.
    /// </summary>
    /// <param name="list1">Primera colección de elementos.</param>
    /// <param name="list2">Segunda colección de elementos.</param>
    /// <returns>True si ambas colecciones contienen los mismos elementos; de lo contrario, False.</returns>
    private static bool AreCollectionsEqual(
        ICollection<ElementId> list1,
        ICollection<ElementId> list2
    )
    {
        if (list1.Count != list2.Count)
            return false;
        if (list1.Count == 0)
            return true;

        // Comprobar que todos los elementos de la primera lista existan en la segunda
        return list1.All(list2.Contains);
    }
}
