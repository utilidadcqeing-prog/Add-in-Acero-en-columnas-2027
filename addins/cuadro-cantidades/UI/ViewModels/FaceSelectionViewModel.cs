using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEING_CuadroCantidades.Core.Application.Services;

namespace CQEING_CuadroCantidades.UI.ViewModels;

/// <summary>
/// Panel "Caras para el área" de la ventana de detalles: muestra las caras de las columnas, vigas o zapatas
/// seleccionadas en la tabla y cambia cuáles suman a la cantidad en M2.
/// </summary>
public sealed partial class FaceSelectionViewModel : ObservableObject
{
    private readonly Func<Func<FaceAreaData, IReadOnlySet<ElementFace>>, Task> _applyChange;
    private IReadOnlyList<FaceAreaData> _selected = Array.Empty<FaceAreaData>();

    /// <summary>
    /// Inicializa el panel.
    /// </summary>
    /// <param name="applyChange">Aplica a cada elemento seleccionado un cambio de sus caras elegidas, calculado a
    /// partir de sus datos de caras, y lo guarda.</param>
    public FaceSelectionViewModel(Func<Func<FaceAreaData, IReadOnlySet<ElementFace>>, Task> applyChange)
    {
        _applyChange = applyChange ?? throw new ArgumentNullException(nameof(applyChange));

        X1 = new FaceOptionViewModel(ElementFace.X1, "−X", this);
        X2 = new FaceOptionViewModel(ElementFace.X2, "+X", this);
        Y1 = new FaceOptionViewModel(ElementFace.Y1, "−Y", this);
        Y2 = new FaceOptionViewModel(ElementFace.Y2, "+Y", this);
        Z1 = new FaceOptionViewModel(ElementFace.Z1, "Fondo · −Z", this);
        Z2 = new FaceOptionViewModel(ElementFace.Z2, "Superior · +Z", this);

        RemoveExteriorCommand = new AsyncRelayCommand(() => ApplyAsync(WithoutExterior), () => CanEditExterior);
        OnlyExteriorCommand = new AsyncRelayCommand(() => ApplyAsync(OnlyExterior), () => CanEditExterior);
    }

    public FaceOptionViewModel X1 { get; }
    public FaceOptionViewModel X2 { get; }
    public FaceOptionViewModel Y1 { get; }
    public FaceOptionViewModel Y2 { get; }
    public FaceOptionViewModel Z1 { get; }
    public FaceOptionViewModel Z2 { get; }

    private IEnumerable<FaceOptionViewModel> Options => new[] { X1, X2, Y1, Y2, Z1, Z2 };

    /// <summary>
    /// Selecciones rápidas para el tipo de elemento seleccionado.
    /// </summary>
    public ObservableCollection<FacePresetViewModel> Presets { get; } = new();

    /// <summary>
    /// Cuántos elementos y de qué tipo están seleccionados.
    /// </summary>
    [ObservableProperty]
    private string _selectionText = NoElementsText;

    private const string NoElementsText = "Ninguna columna, viga ni zapata";

    /// <summary>
    /// Indica que no hay columnas, vigas ni zapatas a las que aplicar caras.
    /// </summary>
    [ObservableProperty]
    private bool _hasNoSelection = true;

    /// <summary>
    /// Indica que no hay filas seleccionadas y los cambios van a toda la actividad.
    /// </summary>
    [ObservableProperty]
    private bool _appliesToWholeActivity;

    [ObservableProperty]
    private bool _hasMixedSelection;

    [ObservableProperty]
    private bool _showColumnPlan;

    [ObservableProperty]
    private bool _showBeamPlan;

    [ObservableProperty]
    private bool _showFootingPlan;

    /// <summary>
    /// Área (m²) de las caras elegidas en los elementos seleccionados.
    /// </summary>
    [ObservableProperty]
    private string _selectionAreaText = "0.00";

    /// <summary>
    /// Columnas o vigas seleccionadas: se muestran las opciones de caras exteriores.
    /// </summary>
    public bool ShowExteriorOptions => ShowColumnPlan || ShowBeamPlan;

    /// <summary>
    /// Algún elemento seleccionado tiene sus caras exteriores clasificadas por la línea de propiedad.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditExterior))]
    private bool _hasBoundary;

    /// <summary>
    /// Cuántas caras exteriores hay en la selección, o cómo activarlas.
    /// </summary>
    [ObservableProperty]
    private string _boundaryText = string.Empty;

    /// <summary>
    /// Hay elementos atravesados por la línea de propiedad o fuera del lote.
    /// </summary>
    [ObservableProperty]
    private bool _hasBoundaryIssue;

    [ObservableProperty]
    private string _boundaryIssueText = string.Empty;

    /// <summary>
    /// Se pueden quitar o dejar solas las caras exteriores.
    /// </summary>
    public bool CanEditExterior => CanEdit && HasBoundary && ShowExteriorOptions;

    /// <summary>
    /// Quita de las caras elegidas de cada elemento las que dan hacia afuera de la línea de propiedad.
    /// </summary>
    public IAsyncRelayCommand RemoveExteriorCommand { get; }

    /// <summary>
    /// Deja elegidas solo las caras que dan hacia afuera de la línea de propiedad.
    /// </summary>
    public IAsyncRelayCommand OnlyExteriorCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isReadOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isBusy;

    /// <summary>
    /// Indica si se pueden cambiar las caras: hay columnas, vigas o zapatas (de un solo tipo) seleccionadas y no se
    /// está guardando.
    /// </summary>
    public bool CanEdit => (ShowColumnPlan || ShowBeamPlan || ShowFootingPlan) && !IsReadOnly && !IsBusy;

    partial void OnShowColumnPlanChanged(bool value) => NotifyEditability();

    partial void OnShowBeamPlanChanged(bool value) => NotifyEditability();

    partial void OnShowFootingPlanChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanEditExterior));
    }

    partial void OnIsReadOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditExterior));
        NotifyCommands();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditExterior));
        NotifyCommands();
    }

    private void NotifyEditability()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(ShowExteriorOptions));
        OnPropertyChanged(nameof(CanEditExterior));
    }

    /// <summary>
    /// Actualiza el panel con los datos de caras de los elementos a los que se aplican los cambios.
    /// </summary>
    /// <param name="selected">Columnas, vigas o zapatas seleccionadas, o todas las de la actividad.</param>
    /// <param name="appliesToWholeActivity">Indica que no hay filas seleccionadas y se trabaja con toda la actividad.</param>
    public void Refresh(IReadOnlyList<FaceAreaData> selected, bool appliesToWholeActivity)
    {
        _selected = selected ?? Array.Empty<FaceAreaData>();

        var kinds = _selected.Select(d => d.Kind).Distinct().ToList();
        var kind = kinds.Count == 1 ? kinds[0] : FaceMeasuredKind.None;

        HasNoSelection = _selected.Count == 0;
        HasMixedSelection = kinds.Count > 1;
        ShowColumnPlan = kind == FaceMeasuredKind.Column;
        ShowBeamPlan = kind == FaceMeasuredKind.Beam;
        ShowFootingPlan = kind == FaceMeasuredKind.Footing;
        SelectionText = BuildSelectionText(_selected.Count, kind, appliesToWholeActivity);
        AppliesToWholeActivity = appliesToWholeActivity && _selected.Count > 0;

        // Suma de las áreas ya redondeadas por elemento, igual que el TOTAL de la tabla
        double area = FaceAreaCalculator.RoundArea(_selected.Sum(d => d.SelectedArea ?? 0));
        SelectionAreaText = area.ToString("0.00", CultureInfo.InvariantCulture);

        foreach (var option in Options)
        {
            int count = _selected.Count(d => d.Selection.Contains(option.Face));
            option.IsSelected = count == 0 ? false : count == _selected.Count ? true : (bool?)null;
        }

        var classified = _selected.Where(d => d.ExteriorFaces != null).ToList();
        int crossed = _selected.Count(d => d.CrossesBoundary);
        HasBoundary = classified.Count > 0;
        BoundaryText = HasBoundary
            ? BuildBoundaryText(classified.Sum(d => d.ExteriorFaces!.Count), classified.Count(d => d.ExteriorFaces!.Count > 0))
            : crossed > 0 ? string.Empty : "Dibuja una línea de propiedad en Revit (Masa y emplazamiento) para distinguir las caras exteriores.";
        HasBoundaryIssue = crossed > 0;
        BoundaryIssueText = crossed == 1
            ? "1 elemento está fuera o atravesado por la línea de propiedad: no se le cambian las caras."
            : $"{crossed} elementos están fuera o atravesados por la línea de propiedad: no se les cambian las caras.";

        RebuildPresets(kind);
        NotifyCommands();
    }

    internal Task ToggleAsync(FaceOptionViewModel option)
    {
        bool include = option.IsSelected != true;
        return ApplyAsync(data =>
        {
            var next = data.Selection.ToHashSet();
            if (include)
                next.Add(option.Face);
            else
                next.Remove(option.Face);
            return next;
        });
    }

    internal async Task ApplyAsync(Func<FaceAreaData, IReadOnlySet<ElementFace>> change)
    {
        if (!CanEdit)
            return;

        IsBusy = true;
        try
        {
            await _applyChange(change);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildPresets(FaceMeasuredKind kind)
    {
        Presets.Clear();

        var presets = kind switch
        {
            FaceMeasuredKind.Column => new (string, ElementFace[])[]
            {
                ("Las 4 caras", new[] { ElementFace.X1, ElementFace.X2, ElementFace.Y1, ElementFace.Y2 }),
                ("Solo X1 + X2", new[] { ElementFace.X1, ElementFace.X2 }),
                ("Solo Y1 + Y2", new[] { ElementFace.Y1, ElementFace.Y2 }),
            },
            FaceMeasuredKind.Beam => new (string, ElementFace[])[]
            {
                ("Fondo + costados", new[] { ElementFace.X1, ElementFace.X2, ElementFace.Y1, ElementFace.Y2, ElementFace.Z1 }),
                ("Superior + costados", new[] { ElementFace.X1, ElementFace.X2, ElementFace.Y1, ElementFace.Y2, ElementFace.Z2 }),
                ("Solo costados", new[] { ElementFace.X1, ElementFace.X2, ElementFace.Y1, ElementFace.Y2 }),
            },
            FaceMeasuredKind.Footing => new (string, ElementFace[])[]
            {
                ("Todas las caras", FaceAreaCalculator.BeamFaces.ToArray()),
                ("Superior + costados", new[] { ElementFace.X1, ElementFace.X2, ElementFace.Y1, ElementFace.Y2, ElementFace.Z2 }),
                ("Fondo + costados", new[] { ElementFace.X1, ElementFace.X2, ElementFace.Y1, ElementFace.Y2, ElementFace.Z1 }),
                ("Solo superior", new[] { ElementFace.Z2 }),
            },
            _ => Array.Empty<(string, ElementFace[])>(),
        };

        foreach (var (label, faces) in presets)
        {
            IReadOnlySet<ElementFace> selection = faces.ToHashSet();
            Presets.Add(new FacePresetViewModel(label, _ => selection, this));
        }
    }

    private void NotifyCommands()
    {
        foreach (var option in Options)
        {
            option.ToggleCommand.NotifyCanExecuteChanged();
        }

        foreach (var preset in Presets)
        {
            preset.ApplyCommand.NotifyCanExecuteChanged();
        }

        RemoveExteriorCommand.NotifyCanExecuteChanged();
        OnlyExteriorCommand.NotifyCanExecuteChanged();
    }

    // Elementos sin clasificar (sin línea de propiedad, atravesados o traídos de la API) conservan sus caras
    private static IReadOnlySet<ElementFace> WithoutExterior(FaceAreaData data) =>
        data.ExteriorFaces == null ? data.Selection : data.Selection.Except(data.ExteriorFaces).ToHashSet();

    private static IReadOnlySet<ElementFace> OnlyExterior(FaceAreaData data) =>
        data.ExteriorFaces ?? data.Selection;

    private static string BuildBoundaryText(int faces, int elements) => (faces, elements) switch
    {
        (0, _) => "Ninguna cara da hacia afuera de la línea de propiedad.",
        (1, _) => "1 cara exterior según la línea de propiedad.",
        (_, 1) => $"{faces} caras exteriores en 1 elemento según la línea de propiedad.",
        _ => $"{faces} caras exteriores en {elements} elementos según la línea de propiedad.",
    };

    private static string BuildSelectionText(int count, FaceMeasuredKind kind, bool wholeActivity)
    {
        string elements = (count, kind) switch
        {
            (0, _) => NoElementsText,
            (1, FaceMeasuredKind.Column) => "1 columna",
            (_, FaceMeasuredKind.Column) => $"{count} columnas",
            (1, FaceMeasuredKind.Beam) => "1 viga",
            (_, FaceMeasuredKind.Beam) => $"{count} vigas",
            (1, FaceMeasuredKind.Footing) => "1 zapata",
            (_, FaceMeasuredKind.Footing) => $"{count} zapatas",
            _ => $"{count} elementos",
        };

        if (count == 0)
            return elements;
        if (wholeActivity)
            return $"{elements} · toda la actividad";
        if (kind == FaceMeasuredKind.None)
            return $"{elements} seleccionados";
        return count == 1 ? $"{elements} seleccionada" : $"{elements} seleccionadas";
    }
}

/// <summary>
/// Una cara del dibujo: si suma al área en los elementos seleccionados (true), en ninguno (false) o en algunos (null).
/// </summary>
public sealed partial class FaceOptionViewModel : ObservableObject
{
    private readonly FaceSelectionViewModel _owner;

    internal FaceOptionViewModel(ElementFace face, string direction, FaceSelectionViewModel owner)
    {
        Face = face;
        Direction = direction;
        _owner = owner;
        ToggleCommand = new AsyncRelayCommand(() => _owner.ToggleAsync(this), () => _owner.CanEdit);
    }

    public ElementFace Face { get; }

    /// <summary>
    /// Nombre de la cara ("X1").
    /// </summary>
    public string Name => Face.ToString();

    /// <summary>
    /// Hacia dónde mira la cara ("−X", "Fondo · −Z").
    /// </summary>
    public string Direction { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private bool? _isSelected;

    /// <summary>
    /// Texto para lectores de pantalla.
    /// </summary>
    public string AccessibleName => $"Cara {Name}, {Direction}: " + IsSelected switch
    {
        true => "suma al área",
        false => "no suma",
        null => "suma solo en algunos elementos",
    };

    public IAsyncRelayCommand ToggleCommand { get; }
}

/// <summary>
/// Selección rápida de caras ("Fondo + costados").
/// </summary>
public sealed class FacePresetViewModel
{
    internal FacePresetViewModel(string label, Func<FaceAreaData, IReadOnlySet<ElementFace>> change, FaceSelectionViewModel owner)
    {
        Label = label;
        ApplyCommand = new AsyncRelayCommand(() => owner.ApplyAsync(change), () => owner.CanEdit);
    }

    public string Label { get; }

    public IAsyncRelayCommand ApplyCommand { get; }
}
