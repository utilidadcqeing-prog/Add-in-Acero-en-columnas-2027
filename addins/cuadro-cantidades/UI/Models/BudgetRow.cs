using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CQEING_CuadroCantidades.UI.Models;

/// <summary>
/// Representa una fila en la tabla de presupuesto dentro de la interfaz de usuario,
/// proporcionando propiedades observables para enlazar con la vista.
/// </summary>
public partial class BudgetRow : ObservableObject
{
    /// <summary>
    /// Obtiene o establece el número de fila.
    /// </summary>
    [ObservableProperty]
    private int _rowNumber;

    /// <summary>
    /// Obtiene o establece el esquema, jerarquía o nivel del ítem (ej. "1.1.2").
    /// </summary>
    [ObservableProperty]
    private string _outline = string.Empty;

    /// <summary>
    /// Obtiene o establece el tipo de fila o ítem.
    /// </summary>
    [ObservableProperty]
    private string _type = string.Empty;

    /// <summary>
    /// Obtiene o establece el código identificador del ítem de presupuesto.
    /// </summary>
    [ObservableProperty]
    private string _code = string.Empty;

    /// <summary>
    /// Obtiene o establece la descripción detallada del ítem de presupuesto.
    /// </summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>
    /// Obtiene o establece la unidad de medida (ej. "m3", "m2", "Und").
    /// </summary>
    [ObservableProperty]
    private string _unit = string.Empty;

    /// <summary>
    /// Obtiene o establece la cantidad calculada en formato de texto.
    /// </summary>
    [ObservableProperty]
    private string _quantity = string.Empty;

    /// <summary>
    /// Obtiene o establece el nivel jerárquico o de indentación de la fila.
    /// </summary>
    [ObservableProperty]
    private int _level;

    /// <summary>
    /// Obtiene o establece un valor que indica si la fila puede ser editada directamente.
    /// </summary>
    [ObservableProperty]
    private bool _canEdit;

    /// <summary>
    /// Obtiene o establece si la fila es visible en la UI (para sistema de accordion expand/collapse).
    /// </summary>
    [ObservableProperty]
    private bool _isRowVisible = true;

    /// <summary>
    /// Obtiene o establece si la fila está expandida (aplica a capítulos y subcapítulos).
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>
    /// Obtiene o establece la ruta local de la imagen de previsualización asociada.
    /// </summary>
    [ObservableProperty]
    private string? _previewImagePath;

    /// <summary>
    /// Obtiene o establece la fecha y hora de la última actualización de la previsualización.
    /// </summary>
    [ObservableProperty]
    private DateTime? _lastPreviewUpdate;

    /// <summary>
    /// Obtiene o establece el registro o instantánea de los identificadores de elementos vinculados.
    /// </summary>
    [ObservableProperty]
    private string? _elementIdsSnapshot;

    /// <summary>
    /// Obtiene o establece la representación en formato JSON de la lista de imágenes de previsualización.
    /// </summary>
    [ObservableProperty]
    private string? _previewImagesJson;

    /// <summary>
    /// Colección de elementos asignados a esta actividad para previsualización en la UI.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<ElementItem> AssignedElementsPreview { get; } = new();

    /// <summary>
    /// Tabla dinámica de previsualización que contiene los parámetros calculados y totales.
    /// </summary>
    [ObservableProperty]
    private System.Data.DataTable? _assignedElementsTable;

    partial void OnPreviewImagesJsonChanged(string? value)
    {
        NotifyCollageProperties();
    }

    partial void OnPreviewImagePathChanged(string? value)
    {
        NotifyCollageProperties();
    }

    private void NotifyCollageProperties()
    {
        OnPropertyChanged(nameof(CollageImagePaths));
        OnPropertyChanged(nameof(HasMultipleCollageImages));
        OnPropertyChanged(nameof(HasSingleCollageImage));
        OnPropertyChanged(nameof(CollageImage1));
        OnPropertyChanged(nameof(CollageImage2));
        OnPropertyChanged(nameof(CollageImage3));
        OnPropertyChanged(nameof(CollageImage4));
        OnPropertyChanged(nameof(HasMoreThan4CollageImages));
        OnPropertyChanged(nameof(MoreImagesText));
        OnPropertyChanged(nameof(Has2CollageImages));
        OnPropertyChanged(nameof(Has3CollageImages));
        OnPropertyChanged(nameof(Has4OrMoreCollageImages));
        OnPropertyChanged(nameof(HasAnyCollageImages));
    }

    public List<string> CollageImagePaths 
    {
        get 
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(PreviewImagesJson))
            {
                try {
                    var snapshots = System.Text.Json.JsonSerializer.Deserialize<List<CQEING_CuadroCantidades.Core.Domain.Entities.ActivityImageSnapshot>>(PreviewImagesJson);
                    if (snapshots != null && snapshots.Count > 0) {
                        snapshots.Sort((a, b) => b.CapturedAt.CompareTo(a.CapturedAt));

                        // 1. Tomar seleccionadas (máximo 3)
                        var selected = snapshots.Where(s => s.IsSelectedForPublication).Take(3).ToList();
                        
                        // 2. Si no hay seleccionadas explícitamente, tomar solo la última generada
                        var targetSnapshots = selected.Count > 0 ? selected : snapshots.Take(1).ToList();

                        foreach(var snap in targetSnapshots) {
                            if (!string.IsNullOrWhiteSpace(snap.ImagePath)) list.Add(snap.ImagePath);
                            else if (!string.IsNullOrWhiteSpace(snap.DebugImagePath)) list.Add(snap.DebugImagePath);
                        }
                    }
                } catch {}
            }
            if (list.Count == 0 && !string.IsNullOrWhiteSpace(PreviewImagePath))
            {
                // Soporte a imágenes viejas (sin JSON pero con un URL individual, o subidas con el string comma-separated)
                var paths = PreviewImagePath.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in paths)
                {
                    list.Add(p.Trim());
                    if (list.Count >= 3) break;
                }
            }
            return list;
        }
    }

    public bool HasAnyCollageImages => CollageImagePaths.Count > 0;
    public bool HasMultipleCollageImages => CollageImagePaths.Count > 1;
    public bool HasSingleCollageImage => CollageImagePaths.Count == 1;
    public bool Has2CollageImages => CollageImagePaths.Count == 2;
    public bool Has3CollageImages => CollageImagePaths.Count == 3;
    public bool Has4OrMoreCollageImages => CollageImagePaths.Count >= 4;

    public string? CollageImage1 => CollageImagePaths.Count > 0 ? CollageImagePaths[0] : null;
    public string? CollageImage2 => CollageImagePaths.Count > 1 ? CollageImagePaths[1] : null;
    public string? CollageImage3 => CollageImagePaths.Count > 2 ? CollageImagePaths[2] : null;
    public string? CollageImage4 => CollageImagePaths.Count > 3 ? CollageImagePaths[3] : null;

    public bool HasMoreThan4CollageImages => CollageImagePaths.Count > 4;
    public string MoreImagesText => $"+{CollageImagePaths.Count - 4}";
}
