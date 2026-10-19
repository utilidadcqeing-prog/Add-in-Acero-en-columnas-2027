namespace CQEING_CuadroCantidades.UI.Models;

/// <summary>
/// Representa una vista de Revit disponible para su visualización y captura.
/// </summary>
public sealed class RevitViewItem
{
    /// <summary>
    /// Nombre descriptivo de la vista de Revit.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Identificador único (UniqueId) de la vista de Revit. Es nulo para la vista por defecto.
    /// </summary>
    public string? UniqueId { get; set; }
}
