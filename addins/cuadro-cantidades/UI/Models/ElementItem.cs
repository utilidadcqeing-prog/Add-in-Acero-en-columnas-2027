using CommunityToolkit.Mvvm.ComponentModel;

namespace CQEING_CuadroCantidades.UI.Models;

/// <summary>
/// Representa un elemento individual en la interfaz de usuario para su selección y visualización.
/// </summary>
public sealed partial class ElementItem : ObservableObject
{
    /// <summary>
    /// Obtiene o establece un valor que indica si el elemento está seleccionado.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Obtiene la categoría del elemento.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Obtiene el nombre del elemento.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Obtiene el identificador numérico del elemento (ElementId de Revit).
    /// </summary>
    public string ElementId { get; init; } = string.Empty;

    /// <summary>
    /// Obtiene el identificador único (UniqueId) del elemento de Revit.
    /// </summary>
    public string UniqueId { get; init; } = string.Empty;

    /// <summary>
    /// Obtiene la representación de datos del elemento en formato JSON.
    /// </summary>
    public string ElementDataJson { get; init; } = "{}";
}
