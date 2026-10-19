using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEING_CuadroCantidades.Core;
using CQEING_CuadroCantidades.Core.Application.Events;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Application.Services;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;
using CQEING_CuadroCantidades.Services;
using CQEING_CuadroCantidades.UI.Models;
using CQEING_CuadroCantidades.UI.Utils;

namespace CQEING_CuadroCantidades.UI.ViewModels;

/// <summary>
/// Modelo de vista para la tabla dinámica de elementos, que gestiona los detalles, parámetros,
/// capturas de previsualización e historial de imágenes vinculados a una actividad específica del presupuesto.
/// </summary>
public sealed partial class DynamicTableViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Máximo de imágenes que se pueden marcar para publicar en la web.
    /// </summary>
    private const int MaxPublishedImages = 3;

    private readonly Guid _budgetId;
    private readonly BudgetRow _budgetRow;
    public BudgetRow ActivityBudgetRow => _budgetRow;
    private readonly List<LinkedElement> _elements;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRevitImageCaptureService _captureService;
    private readonly IPreviewDebugImageStorageService _debugImageStorageService;
    private readonly IAutoSaveService _autoSaveService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IElementAvailabilityService _elementAvailabilityService;
    private readonly IPreviewSessionContext _sessionContext;
    private readonly IManualPreviewService _manualPreviewService;
    private readonly IActivityPreviewUpdater _activityPreviewUpdater;
    private readonly IPreviewRefreshNotifier _previewRefreshNotifier;
    private readonly IPreviewRefreshService _previewRefreshService;
    private readonly IPreviewImageLoaderService _previewImageLoaderService;
    private readonly IUIThreadDispatcherService _uiDispatcherService;
    private readonly IApiClient _apiClient;

    // Código del presupuesto en la API (VersionName): con él se piden las imágenes del add-in de imágenes
    private readonly string _budgetCode;
    private bool _isGeneratingPreview;

    // Los guardados de esta ventana (caras, niveles y áreas resueltas) usan el mismo contexto de base de datos
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    /// <summary>
    /// Obtiene o establece el título detallado de la actividad.
    /// </summary>
    [ObservableProperty]
    private string _activityTitle = string.Empty;

    /// <summary>
    /// Obtiene o establece si la vista está en modo de solo lectura.
    /// </summary>
    [ObservableProperty]
    private bool _isReadOnly;

    /// <summary>
    /// Obtiene o establece la tabla de datos dinámicos generada a partir de los parámetros de los elementos de Revit.
    /// </summary>
    [ObservableProperty]
    private DataTable _dynamicData = new();

    /// <summary>
    /// Obtiene o establece la captura o snapshot de imagen de la actividad seleccionada actualmente.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSnapshotCommand))]
    private ActivityImageSnapshot? _selectedSnapshot;

    /// <summary>
    /// Obtiene o establece la imagen de previsualización procesada para mostrar en la interfaz de usuario.
    /// </summary>
    [ObservableProperty]
    private ImageSource? _selectedPreviewImage;

    /// <summary>
    /// Obtiene la colección observable de capturas históricas asociadas a la actividad.
    /// </summary>
    public ObservableCollection<ActivityImageSnapshot> Snapshots { get; } = new();

    /// <summary>
    /// Colección observable de vistas de Revit disponibles para capturar.
    /// </summary>
    public ObservableCollection<RevitViewItem> RevitViews { get; } = new();

    /// <summary>
    /// Obtiene o establece la vista de Revit seleccionada para la captura.
    /// </summary>
    [ObservableProperty]
    private RevitViewItem? _selectedRevitView;

    /// <summary>
    /// Obtiene los identificadores únicos (UniqueId) de los elementos seleccionados en la cuadrícula de datos.
    /// </summary>
    public List<string> SelectedElementUniqueIds { get; } = new();

    /// <summary>
    /// Panel "Caras para el área" de las columnas, vigas o zapatas seleccionadas.
    /// </summary>
    public FaceSelectionViewModel FaceSelection { get; }

    /// <summary>
    /// Indica si la actividad es en M2 y tiene columnas, vigas o zapatas, que se miden por caras.
    /// </summary>
    public bool IsFaceSelectionAvailable { get; }

    /// <summary>
    /// Pestaña activa del panel derecho (0 = caras, 1 = galería). Abre en caras si aplican.
    /// Con las caras a la vista, Revit selecciona las caras elegidas en lugar de los elementos.
    /// </summary>
    [ObservableProperty]
    private int _rightPanelTabIndex;

    private bool IsShowingFaces => IsFaceSelectionAvailable && RightPanelTabIndex == 0;

    // Evita tocar la selección de Revit mientras se arma la ventana
    private readonly bool _isInitialized;

    // Lo que esta ventana dejó seleccionado en Revit: su eco en el evento de selección se ignora
    private HashSet<ElementId>? _lastWrittenRevitSelection;

    /// <summary>
    /// Se dispara cuando el usuario selecciona en Revit elementos de la actividad, con sus UniqueId:
    /// la vista marca esas filas en la tabla.
    /// </summary>
    public event EventHandler<IReadOnlyCollection<string>>? RevitSelectionChanged;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="DynamicTableViewModel"/>.
    /// </summary>
    /// <param name="budgetId">Identificador único del presupuesto.</param>
    /// <param name="budgetRow">Fila del presupuesto asociada.</param>
    /// <param name="elements">Elementos de Revit vinculados a esta actividad.</param>
    /// <param name="unitOfWork">Unidad de trabajo para operaciones con la base de datos.</param>
    /// <param name="captureService">Servicio para la captura de pantalla del visor de Revit.</param>
    /// <param name="debugImageStorageService">Servicio de depuración y guardado local de imágenes.</param>
    /// <param name="autoSaveService">Servicio para gestionar el autoguardado.</param>
    /// <param name="eventAggregator">Agregador de eventos de la aplicación.</param>
    /// <param name="elementAvailabilityService">Servicio para el control de disponibilidad de elementos.</param>
    /// <param name="sessionContext">Contexto del estado de la sesión de previsualización.</param>
    /// <param name="manualPreviewService">Servicio de actualización manual de vistas.</param>
    /// <param name="activityPreviewUpdater">Actualizador de la vista en base a actividades.</param>
    /// <param name="previewRefreshNotifier">Notificador de actualizaciones de vistas de previsualización.</param>
    /// <param name="previewRefreshService">Servicio unificado de refresco de previsualización.</param>
    /// <param name="previewImageLoaderService">Servicio para la carga de imágenes.</param>
    /// <param name="uiDispatcherService">Despachador para interactuar de forma segura con el hilo de UI de WPF.</param>
    /// <param name="apiClient">Cliente de la API, para traer las imágenes del add-in de imágenes.</param>
    /// <param name="budgetCode">Código del presupuesto en la API.</param>
    public DynamicTableViewModel(
        Guid budgetId,
        BudgetRow budgetRow,
        IEnumerable<LinkedElement> elements,
        IUnitOfWork unitOfWork,
        IRevitImageCaptureService captureService,
        IPreviewDebugImageStorageService debugImageStorageService,
        IAutoSaveService autoSaveService,
        IEventAggregator eventAggregator,
        IElementAvailabilityService elementAvailabilityService,
        IPreviewSessionContext sessionContext,
        IManualPreviewService manualPreviewService,
        IActivityPreviewUpdater activityPreviewUpdater,
        IPreviewRefreshNotifier previewRefreshNotifier,
        IPreviewRefreshService previewRefreshService,
        IPreviewImageLoaderService previewImageLoaderService,
        IUIThreadDispatcherService uiDispatcherService,
        IApiClient apiClient,
        string budgetCode
    )
    {
        _budgetId = budgetId;
        _budgetRow = budgetRow ?? throw new ArgumentNullException(nameof(budgetRow));
        _elements = elements?.ToList() ?? new List<LinkedElement>();
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _debugImageStorageService = debugImageStorageService ?? throw new ArgumentNullException(nameof(debugImageStorageService));
        _autoSaveService = autoSaveService ?? throw new ArgumentNullException(nameof(autoSaveService));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _elementAvailabilityService = elementAvailabilityService ?? throw new ArgumentNullException(nameof(elementAvailabilityService));
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        _manualPreviewService = manualPreviewService ?? throw new ArgumentNullException(nameof(manualPreviewService));
        _activityPreviewUpdater = activityPreviewUpdater ?? throw new ArgumentNullException(nameof(activityPreviewUpdater));
        _previewRefreshNotifier = previewRefreshNotifier ?? throw new ArgumentNullException(nameof(previewRefreshNotifier));
        _previewRefreshService = previewRefreshService ?? throw new ArgumentNullException(nameof(previewRefreshService));
        _previewImageLoaderService = previewImageLoaderService ?? throw new ArgumentNullException(nameof(previewImageLoaderService));
        _uiDispatcherService = uiDispatcherService ?? throw new ArgumentNullException(nameof(uiDispatcherService));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _budgetCode = budgetCode ?? string.Empty;

        // Establecer el título detallado de la actividad
        ActivityTitle = $"Detalle: {budgetRow.Code} - {budgetRow.Description} ({_elements.Count} Elementos)";

        FaceSelection = new FaceSelectionViewModel(ApplyFaceSelectionAsync);
        IsFaceSelectionAvailable = QuantificationEngine.IsSquareMeters(budgetRow.Unit)
            && _elements.Any(el => FaceAreaCalculator.KindFromCategory(el.Category) != FaceMeasuredKind.None);
        RightPanelTabIndex = IsFaceSelectionAvailable ? 0 : 1;

        // Construir la estructura del DataTable dinámico
        BuildDynamicTable(_elements);

        // Sin filas seleccionadas, el panel de caras trabaja con toda la actividad
        RefreshFaceSelection();
        
        // Cargar el historial de capturas de la actividad
        LoadSnapshots();

        // Sumar a la galería las imágenes del add-in de imágenes (siempre frescas, al abrir la ventana)
        _ = LoadAddinImagesAsync();

        // Cargar las vistas disponibles en Revit
        LoadRevitViews();

        // Resolver de manera asíncrona niveles y áreas por cara faltantes de elementos ya cargados (retrocompatibilidad)
        _ = ResolveMissingRevitDataAsync();

        // Suscribirse a eventos de actualización de previsualización
        _eventAggregator.Subscribe<PreviewUpdatedEvent>(OnPreviewUpdated);

        // Lo que se selecciona en Revit se marca en la tabla
        DocumentLifecycleService.SelectionChanged += OnRevitSelectionChanged;

        _isInitialized = true;

        // La vista previa ya no se genera automáticamente al abrir la ventana de detalles.
        // Se deja para que el usuario elija manualmente qué vista capturar y anexar.
        /*
        if (ShouldRefreshPreviewForCurrentElements())
        {
            _isGeneratingPreview = true;
            _ = GeneratePreviewInternalAsync(
                _elements.Select(le => le.RevitUniqueId).ToList(),
                showMessages: false
            );
        }
        */
    }

    /// <summary>
    /// Deserializa y carga las capturas históricas almacenadas en formato JSON en el presupuesto.
    /// </summary>
    private void LoadSnapshots()
    {
        Snapshots.Clear();
        if (!string.IsNullOrEmpty(_budgetRow.PreviewImagesJson))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<ActivityImageSnapshot>>(
                    _budgetRow.PreviewImagesJson
                );
                if (list != null)
                {
                    foreach (var snap in list)
                    {
                        Snapshots.Add(snap);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DynamicTableViewModel] Error loading snapshots: {ex.Message}"
                );
            }
        }

        // Recuperar las imágenes de la API si no hay snapshots generados localmente.
        // Quedan marcadas para publicar: si no, una captura nueva sin marcar dejaría la actividad
        // sin imágenes seleccionadas y el siguiente guardado borraría en la web las ya publicadas.
        if (Snapshots.Count == 0 && !string.IsNullOrWhiteSpace(_budgetRow.PreviewImagePath))
        {
            var paths = _budgetRow.PreviewImagePath
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                bool isUrl = path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

                Snapshots.Add(new ActivityImageSnapshot
                {
                    ImagePath = path,
                    DebugImagePath = path,
                    CapturedAt = _budgetRow.LastPreviewUpdate ?? DateTime.Now,
                    ElementIds = _budgetRow.ElementIdsSnapshot ?? string.Empty,
                    IsSelectedForPublication = i < MaxPublishedImages,
                    PublishedUrl = isUrl ? path : null,
                });
            }
        }

        // Seleccionar la captura más reciente por defecto
        SelectedSnapshot = Snapshots.LastOrDefault();
    }

    /// <summary>
    /// Método parcial de control que se ejecuta cuando cambia el snapshot seleccionado en la UI.
    /// </summary>
    /// <param name="value">La nueva captura seleccionada.</param>
    partial void OnSelectedSnapshotChanged(ActivityImageSnapshot? value)
    {
        SelectedPreviewImage = value == null ? null : _previewImageLoaderService.LoadPreviewImage(value);
    }

    /// <summary>
    /// Consulta de forma asíncrona la lista de vistas disponibles en el documento de Revit activo
    /// y actualiza la colección observable para su selección en la interfaz de usuario.
    /// </summary>
    private void LoadRevitViews()
    {
        RevitEventExecutor.Execute(app =>
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                // Consulta de vistas en el documento
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && (
                        v.ViewType == ViewType.ThreeD ||
                        v.ViewType == ViewType.FloorPlan ||
                        v.ViewType == ViewType.CeilingPlan ||
                        v.ViewType == ViewType.Elevation ||
                        v.ViewType == ViewType.Section ||
                        v.ViewType == ViewType.DraftingView ||
                        v.ViewType == ViewType.Rendering
                    ))
                    .OrderBy(v => v.Name)
                    .ToList();

                var viewItems = new List<RevitViewItem>
                {
                    new RevitViewItem { Name = "Vista 3D Automática (Por Defecto)", UniqueId = null }
                };

                foreach (var view in collector)
                {
                    viewItems.Add(new RevitViewItem
                    {
                        Name = $"{view.Name} ({view.ViewType})",
                        UniqueId = view.UniqueId
                    });
                }

                _uiDispatcherService.Invoke(() =>
                {
                    RevitViews.Clear();
                    foreach (var item in viewItems)
                    {
                        RevitViews.Add(item);
                    }
                    SelectedRevitView = RevitViews.FirstOrDefault();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DynamicTableViewModel] Error loading Revit views: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Comando público para disparar la generación manual de previsualización.
    /// </summary>
    [RelayCommand]
    private void GeneratePreview()
    {
        if (IsReadOnly)
        {
            MessageBox.Show(
                "La estructura está en modo lectura porque pertenece a otro modelo de Revit. No se pueden generar previsualizaciones.",
                "Modo Lectura Activo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        if (_isGeneratingPreview)
            return;

        var uniqueIds = _elements.Select(le => le.RevitUniqueId).ToList();
        if (uniqueIds.Count == 0)
        {
            MessageBox.Show(
                "No hay elementos asignados a esta actividad para generar preview.",
                "Actualizar Preview",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        _isGeneratingPreview = true;
        _ = GeneratePreviewInternalAsync(uniqueIds, showMessages: true, SelectedRevitView?.UniqueId);
    }

    /// <summary>
    /// Realiza de forma asíncrona la petición de refresco de previsualización, actualizando el modelo, base de datos y UI.
    /// </summary>
    private async Task GeneratePreviewInternalAsync(List<string> uniqueIds, bool showMessages, string? selectedViewUniqueId = null)
    {
        try
        {
            // 1. Invocar el servicio unificado de actualización de forma asíncrona.
            // Al no ser esperado en el hilo principal de WPF durante el comando,
            // permitimos que el hilo principal retorne inmediatamente a Revit y procese el ExternalEvent.
            var newSnapshot = await _previewRefreshService.RefreshPreviewAsync(
                _budgetId,
                _budgetRow.Code,
                uniqueIds,
                selectedViewUniqueId
            );

            if (newSnapshot == null)
            {
                if (showMessages)
                {
                    _uiDispatcherService.Invoke(() =>
                    {
                        MessageBox.Show(
                            "No se pudo generar la captura de pantalla del modelo de Revit.",
                            "Error de Captura",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    });
                }
                return;
            }

            // 2. Obtener el ítem de presupuesto actualizado de la base de datos para sincronizar estados
            var updatedItems = await _unitOfWork.BudgetItems.FindAsync(i =>
                i.BudgetId == _budgetId && i.Code == _budgetRow.Code
            );
            var updatedBudgetItem = updatedItems.FirstOrDefault();

            // 3. Ejecutar todas las actualizaciones de la UI en el hilo principal de WPF
            _uiDispatcherService.Invoke(() =>
            {
                if (updatedBudgetItem != null)
                {
                    _budgetRow.PreviewImagePath = updatedBudgetItem.PreviewImagePath;
                    _budgetRow.LastPreviewUpdate = updatedBudgetItem.LastPreviewUpdate;
                    _budgetRow.ElementIdsSnapshot = updatedBudgetItem.ElementIdsSnapshot;
                    _budgetRow.PreviewImagesJson = updatedBudgetItem.PreviewImagesJson;
                }
                else
                {
                    _budgetRow.PreviewImagePath = newSnapshot.DebugImagePath;
                    _budgetRow.LastPreviewUpdate = newSnapshot.CapturedAt;
                    _budgetRow.ElementIdsSnapshot = newSnapshot.ElementIds;
                }

                var snapshotsList = Snapshots.ToList();
                if (!snapshotsList.Any(s => s.CapturedAt == newSnapshot.CapturedAt && s.ElementIds == newSnapshot.ElementIds))
                {
                    snapshotsList.Add(newSnapshot);
                }

                _budgetRow.PreviewImagesJson = JsonSerializer.Serialize(snapshotsList);

                // Notificar cambios y refrescar el visor de miniaturas
                _previewRefreshNotifier.NotifyPreviewRefreshed(
                    _budgetId,
                    _budgetRow.Code,
                    newSnapshot,
                    snapshotsList
                );

                _autoSaveService.RequestSave(immediate: true);

                if (showMessages)
                {
                    MessageBox.Show(
                        "Nueva preview generada y guardada en el historial.",
                        "Preview Exitosa",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
            });
        }
        catch (Exception ex)
        {
            if (showMessages)
            {
                _uiDispatcherService.Invoke(() =>
                {
                    MessageBox.Show(
                        $"Error al generar la preview: {ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                });
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DynamicTableViewModel] Error auto-generating preview: {ex.Message}"
                );
            }
        }
        finally
        {
            _isGeneratingPreview = false;
        }
    }

    /// <summary>
    /// Compara la lista actual de elementos vinculados con el último snapshot para decidir si se necesita una regeneración.
    /// </summary>
    /// <returns>True si hay cambios en los elementos o si la captura actual no es válida/no existe en disco.</returns>
    private bool ShouldRefreshPreviewForCurrentElements()
    {
        var currentUniqueIds = _elements
            .Select(le => le.RevitUniqueId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (currentUniqueIds.Count == 0)
        {
            return false;
        }

        if (SelectedSnapshot == null)
        {
            return true;
        }

        bool hasDatabaseImage = !string.IsNullOrWhiteSpace(SelectedSnapshot.ImageDataBase64);
        string? legacyPath = !string.IsNullOrWhiteSpace(SelectedSnapshot.DebugImagePath)
            ? SelectedSnapshot.DebugImagePath
            : SelectedSnapshot.ImagePath;
        if (!hasDatabaseImage && (string.IsNullOrWhiteSpace(legacyPath) || !File.Exists(legacyPath)))
        {
            return true;
        }

        var snapshotUniqueIds = (SelectedSnapshot.ElementIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return snapshotUniqueIds.Count != currentUniqueIds.Count
            || !snapshotUniqueIds.SequenceEqual(currentUniqueIds, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Comando asíncrono para eliminar la captura actual seleccionada del historial y del sistema de archivos.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSnapshot))]
    private async Task DeleteSnapshotAsync()
    {
        // Las del add-in de imágenes son de la BD compartida y las usan otras PCs: se borran desde ese add-in
        if (SelectedSnapshot?.IsFromImagesAddin == true)
            return;

        if (IsReadOnly)
        {
            MessageBox.Show(
                "La estructura está en modo lectura porque pertenece a otro modelo de Revit. No se pueden eliminar capturas.",
                "Modo Lectura Activo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        if (SelectedSnapshot == null)
        {
            MessageBox.Show(
                "Por favor selecciona un snapshot para eliminar.",
                "Eliminar Captura",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        var result = MessageBox.Show(
            "¿Estás seguro de que deseas eliminar esta captura del historial y del disco?",
            "Confirmar Eliminación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var snapToDelete = SelectedSnapshot;
            Snapshots.Remove(snapToDelete);

            // Eliminar copia física del disco si es necesario
            _debugImageStorageService.DeleteDebugCopy(snapToDelete.DebugImagePath ?? snapToDelete.ImagePath);

            SelectedSnapshot = Snapshots.LastOrDefault();

            var list = Snapshots.ToList();
            string? newJson = list.Count > 0 ? JsonSerializer.Serialize(list) : null;
            string? latestPath = list.Count > 0 ? list.Last().DebugImagePath ?? list.Last().ImagePath : null;
            DateTime? latestUpdate = list.Count > 0 ? list.Last().CapturedAt : (DateTime?)null;
            string? latestIds = list.Count > 0 ? list.Last().ElementIds : null;

            // Actualizar la fila enlazada en la UI
            _budgetRow.PreviewImagePath = latestPath;
            _budgetRow.LastPreviewUpdate = latestUpdate;
            _budgetRow.ElementIdsSnapshot = latestIds;
            _budgetRow.PreviewImagesJson = newJson;

            // Actualizar en base de datos
            var items = await _unitOfWork.BudgetItems.FindAsync(i =>
                i.BudgetId == _budgetId && i.Code == _budgetRow.Code
            );
            var budgetItem = items.FirstOrDefault();
            if (budgetItem != null)
            {
                budgetItem.PreviewImagePath = latestPath;
                budgetItem.LastPreviewUpdate = latestUpdate;
                budgetItem.ElementIdsSnapshot = latestIds;
                budgetItem.PreviewImagesJson = newJson;
                _unitOfWork.BudgetItems.Update(budgetItem);
                await _unitOfWork.SaveChangesAsync();

                _eventAggregator?.Publish(
                    new CQEING_CuadroCantidades.Core.Application.Events.PreviewUpdatedEvent(
                        _budgetId,
                        _budgetRow.Code,
                        null,
                        list
                    )
                );
            }

            _autoSaveService.RequestSave(immediate: true);

            MessageBox.Show(
                "La captura ha sido eliminada correctamente.",
                "Captura Eliminada",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error al eliminar la captura: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    /// <summary>
    /// Construye de forma dinámica el <see cref="DataTable"/> agregando las columnas según los parámetros geométricos JSON de los elementos.
    /// </summary>
    /// <param name="elements">Elementos vinculados de donde se extraen las columnas dinámicas.</param>
    private void BuildDynamicTable(IEnumerable<LinkedElement> elements)
    {
        DynamicData = UI.Utils.DynamicTableBuilder.BuildDynamicTable(elements, _budgetRow.Unit);
    }

    /// <summary>
    /// Guarda un campo manual de puertas y ventanas (hojas o apertura) escrito en la tabla.
    /// El valor se copia a todos los elementos de la actividad con el mismo código, porque es un dato del tipo.
    /// </summary>
    /// <param name="uniqueId">Elemento de la fila editada.</param>
    /// <param name="columnName">Columna editada (HOJAS o APERTURA).</param>
    /// <param name="value">Valor nuevo.</param>
    public async Task UpdateDoorWindowManualFieldAsync(string uniqueId, string columnName, string value)
    {
        string? key = DoorWindowManualFields.KeyForColumn(columnName);
        var edited = _elements.FirstOrDefault(el => el.RevitUniqueId == uniqueId);
        if (IsReadOnly || key == null || edited == null)
            return;

        value = value.Trim();
        string? code = ElementDataJsonReader.GetString(edited.ElementDataJson, DoorWindowDataExtractor.CodeKey);
        var targets = string.IsNullOrEmpty(code)
            ? new List<LinkedElement> { edited }
            : _elements.Where(el => ElementDataJsonReader.GetString(el.ElementDataJson, DoorWindowDataExtractor.CodeKey) == code).ToList();

        try
        {
            foreach (var el in targets)
            {
                el.ElementDataJson = ElementDataJsonReader.SetString(el.ElementDataJson, key, value);
                _unitOfWork.LinkedElements.Update(el);
            }
            await _unitOfWork.SaveChangesAsync();

            // Se actualizan las celdas en su lugar: reconstruir la tabla mientras la grilla termina
            // la edición deja a la grilla apuntando a filas viejas y cierra Revit.
            var targetIds = targets.Select(el => el.RevitUniqueId).ToHashSet();
            foreach (DataRow row in DynamicData.Rows)
            {
                if (targetIds.Contains(row["RevitUniqueId"] as string ?? string.Empty)
                    && !Equals(row[columnName], value))
                {
                    row[columnName] = value;
                }
            }

            PublishActivityUpdated();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error al guardar {columnName}: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    /// <summary>
    /// Registra las filas seleccionadas en la tabla, actualiza el panel de caras y las selecciona en Revit.
    /// </summary>
    /// <param name="uniqueIds">UniqueId de los elementos seleccionados.</param>
    /// <param name="syncRevit">False si la selección viene de Revit: no se vuelve a escribir allá.</param>
    public void UpdateSelection(IEnumerable<string> uniqueIds, bool syncRevit = true)
    {
        SelectedElementUniqueIds.Clear();
        SelectedElementUniqueIds.AddRange(uniqueIds);
        RefreshFaceSelection();

        if (syncRevit)
            SyncRevitSelection(zoomToSelection: true);
    }

    /// <summary>
    /// Selección hecha por el usuario en Revit (llega en el Idling, en el hilo de Revit, que es el de la ventana):
    /// si cambia qué elementos de la actividad están seleccionados, la vista marca esas filas.
    /// </summary>
    private void OnRevitSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var written = _lastWrittenRevitSelection;
        _lastWrittenRevitSelection = null;
        if (written != null && written.SetEquals(e.SelectedIds))
            return;

        var activityIds = _elements.Select(el => el.RevitUniqueId).ToHashSet();
        var selected = e.SelectedIds
            .Select(id => e.Document.GetElement(id)?.UniqueId)
            .Where(uid => uid != null && activityIds.Contains(uid))
            .Select(uid => uid!)
            .Distinct()
            .ToList();

        if (selected.ToHashSet().SetEquals(SelectedElementUniqueIds))
            return;

        _uiDispatcherService.Invoke(() => RevitSelectionChanged?.Invoke(this, selected));
    }

    partial void OnRightPanelTabIndexChanged(int value)
    {
        if (_isInitialized)
            SyncRevitSelection(zoomToSelection: false);
    }

    /// <summary>
    /// Selecciona en Revit lo que muestra la ventana. Con la pestaña de caras, las caras elegidas de las columnas
    /// y vigas a las que se aplican los cambios; si no, los elementos seleccionados en la tabla.
    /// </summary>
    /// <param name="zoomToSelection">Acerca la vista a los elementos seleccionados en la tabla.</param>
    private void SyncRevitSelection(bool zoomToSelection)
    {
        var selectedIds = SelectedElementUniqueIds.ToList();
        var faceTargets = IsShowingFaces
            ? FaceTargets()
                .Select(el => (el.RevitUniqueId, Faces: FaceAreaCalculator.Read(el.ElementDataJson, el.Category)))
                .Where(t => t.Faces != null)
                .Select(t => (t.RevitUniqueId, Selection: t.Faces!.Selection))
                .ToList()
            : new List<(string RevitUniqueId, IReadOnlySet<ElementFace> Selection)>();

        RevitEventExecutor.Execute(app =>
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;

            var doc = uidoc.Document;
            var elementIds = selectedIds
                .Select(uid => doc.GetElement(uid))
                .Where(el => el != null)
                .Select(el => el!.Id)
                .ToList();

            if (faceTargets.Count > 0)
            {
                var references = new List<Reference>();
                foreach (var (uniqueId, selection) in faceTargets)
                {
                    var element = doc.GetElement(uniqueId);
                    if (element != null && element.IsValidObject)
                        references.AddRange(FaceAreaExtractor.GetFaceReferences(element, selection));
                }

                try
                {
                    // Sin caras elegidas la selección queda vacía: así se ve que nada suma al área
                    if (references.Count > 0)
                        uidoc.Selection.SetReferences(references);
                    else
                        uidoc.Selection.SetElementIds(new List<ElementId>());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DynamicTableViewModel] No se pudieron seleccionar las caras: {ex.Message}");
                    uidoc.Selection.SetElementIds(elementIds);
                }
            }
            else
            {
                uidoc.Selection.SetElementIds(elementIds);
            }

            // Centrar la vista en el elemento seleccionado. Con varios no: la vista saltaría en cada Ctrl+clic
            if (zoomToSelection && elementIds.Count == 1)
            {
                uidoc.ShowElements(elementIds);
            }

            _lastWrittenRevitSelection = DocumentLifecycleService.GetSelectedElementIds(uidoc).ToHashSet();
        });
    }

    partial void OnIsReadOnlyChanged(bool value) => FaceSelection.IsReadOnly = value;

    /// <summary>
    /// Cambia las caras elegidas de las columnas o vigas seleccionadas (o de toda la actividad si no hay filas
    /// seleccionadas), guarda el cambio y recalcula la actividad.
    /// </summary>
    /// <param name="change">Caras nuevas a partir de los datos de caras de cada elemento.</param>
    private async Task ApplyFaceSelectionAsync(Func<FaceAreaData, IReadOnlySet<ElementFace>> change)
    {
        if (IsReadOnly)
            return;

        var targets = FaceTargets()
            .Select(el => (Element: el, Faces: FaceAreaCalculator.Read(el.ElementDataJson, el.Category)))
            .Where(t => t.Faces != null)
            .ToList();
        if (targets.Count == 0)
            return;

        await _saveLock.WaitAsync();
        try
        {
            foreach (var (el, faces) in targets)
            {
                string selection = FaceAreaCalculator.Format(change(faces!));
                el.ElementDataJson = ElementDataJsonReader.SetString(el.ElementDataJson, FaceAreaCalculator.SelectionKey, selection);
                _unitOfWork.LinkedElements.Update(el);
            }
            await _unitOfWork.SaveChangesAsync();

            _uiDispatcherService.Invoke(() =>
            {
                RefreshFaceCells();
                PublishActivityUpdated();
            });
        }
        catch (Exception ex)
        {
            _uiDispatcherService.Invoke(() => MessageBox.Show(
                $"Error al guardar las caras: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            ));
        }
        finally
        {
            _saveLock.Release();
        }

        _uiDispatcherService.Invoke(() =>
        {
            RefreshFaceSelection();
            SyncRevitSelection(zoomToSelection: false);
        });
    }

    private IEnumerable<LinkedElement> SelectedElements()
    {
        var selected = SelectedElementUniqueIds.ToHashSet();
        return _elements.Where(el => selected.Contains(el.RevitUniqueId));
    }

    /// <summary>
    /// Elementos a los que se aplican las caras: los seleccionados en la tabla, o todos si no hay selección.
    /// </summary>
    private IEnumerable<LinkedElement> FaceTargets() =>
        SelectedElementUniqueIds.Count > 0 ? SelectedElements() : _elements;

    private void RefreshFaceSelection()
    {
        FaceSelection.Refresh(
            FaceTargets()
                .Select(el => FaceAreaCalculator.Read(el.ElementDataJson, el.Category))
                .OfType<FaceAreaData>()
                .ToList(),
            appliesToWholeActivity: SelectedElementUniqueIds.Count == 0
        );
    }

    /// <summary>
    /// Actualiza en su lugar las celdas CARAS y ÁREA CARAS. Reconstruir la tabla borraría la selección de la grilla;
    /// solo se reconstruye si aún no tiene esas columnas.
    /// </summary>
    private void RefreshFaceCells()
    {
        if (!DynamicData.Columns.Contains(FaceAreaCalculator.AreaColumn))
        {
            BuildDynamicTable(_elements);
            return;
        }

        var byUniqueId = _elements
            .GroupBy(el => el.RevitUniqueId)
            .ToDictionary(g => g.Key, g => g.First());
        DataRow? totalRow = null;
        double total = 0;

        foreach (DataRow row in DynamicData.Rows)
        {
            if (row["No."]?.ToString() == "TOTAL")
            {
                totalRow = row;
                continue;
            }

            if (byUniqueId.TryGetValue(row["RevitUniqueId"] as string ?? string.Empty, out var el)
                && FaceAreaCalculator.Read(el.ElementDataJson, el.Category) is { } faces)
            {
                total += UI.Utils.DynamicTableBuilder.SetFaceCells(row, faces);
            }
        }

        if (totalRow != null)
            totalRow[FaceAreaCalculator.AreaColumn] = FaceAreaCalculator.RoundArea(total);
    }

    /// <summary>
    /// Marca la actividad como modificada para enviarla a la API, recalcula su cantidad y redibuja su tabla
    /// en la ventana principal.
    /// </summary>
    private void PublishActivityUpdated()
    {
        _eventAggregator.Publish(
            new ActivityChangedEvent(
                _budgetId,
                _budgetRow.Code,
                _elements.Select(el => el.RevitUniqueId).ToList(),
                ActivityChangeType.ActivityUpdated,
                _elements.ToList()
            )
        );
        _autoSaveService.RequestSave(immediate: true);
    }

    /// <summary>
    /// Comando asíncrono para desvincular los elementos seleccionados en la tabla de la actividad actual.
    /// </summary>
    [RelayCommand]
    private async Task RemoveElementsAsync()
    {
        if (IsReadOnly)
        {
            MessageBox.Show(
                "La estructura está en modo lectura porque pertenece a otro modelo de Revit. No se pueden desvincular elementos.",
                "Modo Lectura Activo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        if (SelectedElementUniqueIds.Count == 0)
        {
            MessageBox.Show(
                "Por favor selecciona uno o más elementos de la tabla para quitar.",
                "Quitar Elementos",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        var result = MessageBox.Show(
            $"¿Estás seguro de que deseas quitar los {SelectedElementUniqueIds.Count} elementos seleccionados de esta actividad?",
            "Confirmar Acción",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var uidsToRemove = SelectedElementUniqueIds.ToList();

            // 1. Liberar la disponibilidad de estos elementos en la base de datos/caché
            await _elementAvailabilityService.ReleaseUsagesAsync(uidsToRemove);

            // 2. Quitarlos de la colección de memoria _elements
            _elements.RemoveAll(el => uidsToRemove.Contains(el.RevitUniqueId));

            // 3. Actualizar título y reconstruir la tabla dinámica
            ActivityTitle = $"Detalle: {_budgetRow.Code} - {_budgetRow.Description} ({_elements.Count} Elementos)";
            BuildDynamicTable(_elements);

            // 4. Recalcular la cantidad total para la actividad basándose en la unidad
            double newQty = QuantificationEngine.CalculateTotalQuantity(_budgetRow.Unit, _elements);
            
            // Sincronizar con los totales generados en la tabla dinámica (maneja traducciones de API)
            if (DynamicData != null && DynamicData.Rows.Count > 0)
            {
                var lastRow = DynamicData.Rows[DynamicData.Rows.Count - 1];
                if (DynamicData.Columns.Contains("No.") && lastRow["No."].ToString() == "TOTAL")
                {
                    string unit = _budgetRow.Unit?.Trim().ToUpperInvariant() ?? "";
                    if ((unit == "M3" || unit == "M³") && DynamicData.Columns.Contains("VOLUMEN"))
                        newQty = lastRow["VOLUMEN"] != DBNull.Value ? Convert.ToDouble(lastRow["VOLUMEN"]) : 0;
                    else if ((unit == "M2" || unit == "M²") && DynamicData.Columns.Contains("ÁREA"))
                        newQty = lastRow["ÁREA"] != DBNull.Value ? Convert.ToDouble(lastRow["ÁREA"]) : 0;
                    else if ((unit == "ML" || unit == "M") && DynamicData.Columns.Contains("LONGITUD"))
                        newQty = lastRow["LONGITUD"] != DBNull.Value ? Convert.ToDouble(lastRow["LONGITUD"]) : 0;
                    else if ((unit == "KG" || unit == "TON") && DynamicData.Columns.Contains("PESO"))
                        newQty = lastRow["PESO"] != DBNull.Value
                            ? QuantificationEngine.ConvertFromKilograms(Convert.ToDouble(lastRow["PESO"]), unit)
                            : 0;
                }
            }
            
            _budgetRow.Quantity = newQty.ToString("0.00");

            // 5. Publicar evento para recalcular totales del capítulo en la UI y disparar regeneración de preview
            var remainingUniqueIds = _elements.Select(el => el.RevitUniqueId).ToList();
            _eventAggregator.Publish(
                new ActivityChangedEvent(
                    _budgetId,
                    _budgetRow.Code,
                    remainingUniqueIds,
                    ActivityChangeType.ElementsRemoved,
                    _elements.ToList()
                )
            );

            // 6. Solicitar guardado automático inmediato
            _autoSaveService.RequestSave(immediate: true);

            MessageBox.Show(
                $"Se han quitado {uidsToRemove.Count} elementos correctamente de la actividad.",
                "Acción Completada",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error al quitar los elementos: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    /// <summary>
    /// Manejador de evento cuando se actualiza la previsualización desde otro componente de la aplicación.
    /// Sincroniza la lista de capturas históricas y selecciona la más reciente.
    /// </summary>
    private void OnPreviewUpdated(PreviewUpdatedEvent e)
    {
        if (e.BudgetId != _budgetId || e.ActivityCode != _budgetRow.Code)
            return;

        // Ejecutar en el hilo de UI
        _uiDispatcherService.Invoke(() =>
        {
            Snapshots.Clear();
            foreach (var snap in e.AllSnapshots)
            {
                Snapshots.Add(snap);
            }
            SelectedSnapshot = Snapshots.LastOrDefault();
        });
    }

    /// <summary>
    /// Resuelve de forma asíncrona, consultando Revit, los datos que no están en el caché local:
    /// niveles faltantes y áreas por cara de columnas, vigas y zapatas asignadas antes de existir la selección de caras.
    /// </summary>
    private async Task ResolveMissingRevitDataAsync()
    {
        var missingLevels = _elements.Where(el =>
        {
            var lvl = ElementDataJsonReader.GetString(el.ElementDataJson, "Nivel", "Level");
            return lvl == null || lvl == "Sin Nivel";
        }).ToList();

        // Áreas por cara de todas las columnas, vigas y zapatas: faltantes, guardadas con menos decimales o con la geometría
        // cambiada. Solo se guardan las que cambian. En otras unidades no se calculan para no marcar la actividad.
        var missingFaces = !IsFaceSelectionAvailable
            ? new List<LinkedElement>()
            : _elements
                .Where(el => FaceAreaCalculator.KindFromCategory(el.Category) != FaceMeasuredKind.None)
                .ToList();

        if (missingLevels.Count == 0 && missingFaces.Count == 0)
            return;

        try
        {
            var resolvedLevels = new Dictionary<string, string>();
            var resolvedFaces = new Dictionary<LinkedElement, string>();

            await RevitEventExecutor.ExecuteAsync(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                foreach (var el in missingLevels)
                {
                    var revitEl = doc.GetElement(el.RevitUniqueId);
                    if (revitEl != null && revitEl.IsValidObject)
                        resolvedLevels[el.RevitUniqueId] = CQEING_CuadroCantidades.Services.ElementLevelReader.GetLevelName(revitEl);
                }

                foreach (var el in missingFaces)
                {
                    var revitEl = doc.GetElement(el.RevitUniqueId);
                    if (revitEl != null && revitEl.IsValidObject
                        && FaceAreaExtractor.TrySyncElementData(revitEl, el.ElementDataJson, out string json))
                    {
                        resolvedFaces[el] = json;
                    }
                }
            });

            if (resolvedLevels.Count == 0 && resolvedFaces.Count == 0)
                return;

            await _saveLock.WaitAsync();
            try
            {
                // Actualizar el caché JSON local de cada elemento y guardarlo en la base de datos local
                _uiDispatcherService.Invoke(() =>
                {
                    foreach (var (el, json) in resolvedFaces)
                    {
                        el.ElementDataJson = json;
                        _unitOfWork.LinkedElements.Update(el);
                    }

                    foreach (var el in missingLevels.Where(el => resolvedLevels.ContainsKey(el.RevitUniqueId)))
                    {
                        el.ElementDataJson = ElementDataJsonReader.SetString(el.ElementDataJson, "Nivel", resolvedLevels[el.RevitUniqueId]);
                        _unitOfWork.LinkedElements.Update(el);
                    }
                });

                await _unitOfWork.SaveChangesAsync();
            }
            finally
            {
                _saveLock.Release();
            }

            // Actualizar la interfaz de usuario en el hilo principal de WPF
            _uiDispatcherService.Invoke(() =>
            {
                UpdateLevelCells(DynamicData, resolvedLevels);
                UpdateLevelCells(_budgetRow.AssignedElementsTable, resolvedLevels);

                if (resolvedFaces.Count > 0)
                {
                    // Las áreas por cara cambian la cantidad en M2 de la actividad
                    RefreshFaceCells();
                    PublishActivityUpdated();
                    RefreshFaceSelection();
                    SyncRevitSelection(zoomToSelection: false);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicTableViewModel] Error al resolver datos de Revit: {ex.Message}");
        }
    }

    private static void UpdateLevelCells(DataTable? table, IReadOnlyDictionary<string, string> resolvedLevels)
    {
        if (table == null || resolvedLevels.Count == 0 || !table.Columns.Contains("NIVEL"))
            return;

        foreach (DataRow row in table.Rows)
        {
            string uid = row["RevitUniqueId"] as string ?? "";
            if (resolvedLevels.TryGetValue(uid, out string? lvl))
            {
                row["NIVEL"] = lvl;
            }
        }
    }

    /// <summary>
    /// Actualiza el JSON de previsualización en la fila de presupuesto para persistir estados como IsSelectedForPublication.
    /// </summary>
    public void UpdatePreviewImagesJson()
    {
        var list = WriteSnapshotsToRow();

        _eventAggregator?.Publish(
            new CQEING_CuadroCantidades.Core.Application.Events.PreviewUpdatedEvent(
                _budgetId,
                _budgetRow.Code,
                null,
                list
            )
        );
        
        _autoSaveService.RequestSave(immediate: true);
    }

    private List<ActivityImageSnapshot> WriteSnapshotsToRow()
    {
        var list = Snapshots.ToList();
        _budgetRow.PreviewImagesJson = JsonSerializer.Serialize(list);
        return list;
    }

    private bool CanDeleteSnapshot() => SelectedSnapshot?.IsFromImagesAddin != true;

    /// <summary>
    /// Trae de la API las imágenes que el add-in de imágenes registró para esta actividad y las combina con la galería.
    /// Si la API no responde, la galería queda como estaba.
    /// </summary>
    private async Task LoadAddinImagesAsync()
    {
        if (string.IsNullOrWhiteSpace(_budgetCode))
            return;

        try
        {
            var budgetImages = await _apiClient.GetImagenesActividadAsync(_budgetCode);
            var activityImages = budgetImages?
                .Where(a => string.Equals(a.CodigoActividad?.Trim(), _budgetRow.Code?.Trim(), StringComparison.OrdinalIgnoreCase))
                .SelectMany(a => a.Imagenes)
                .ToList();

            _uiDispatcherService.Invoke(() => ApplyAddinImages(activityImages));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicTableViewModel] Error loading add-in images: {ex.Message}");
        }
    }

    private void ApplyAddinImages(IReadOnlyList<ApiImagenActividadResponse>? activityImages)
    {
        var merge = AddinImagesGalleryMerger.Merge(Snapshots.ToList(), activityImages);
        if (!merge.Changed)
            return;

        var previous = SelectedSnapshot;
        Snapshots.Clear();
        foreach (var snap in merge.Snapshots)
            Snapshots.Add(snap);

        // Conservar la imagen elegida aunque su objeto se haya renovado (las del add-in se reconocen por código)
        SelectedSnapshot = Snapshots.FirstOrDefault(s => ReferenceEquals(s, previous))
            ?? Snapshots.FirstOrDefault(s => previous?.IsFromImagesAddin == true
                && string.Equals(s.AddinImageCode, previous.AddinImageCode, StringComparison.OrdinalIgnoreCase))
            ?? Snapshots.LastOrDefault();

        // En modo lectura la galería se muestra al día, pero no se guarda nada
        if (IsReadOnly)
            return;

        // Si cambió la URL de una marcada, la actividad se reenvía en el próximo "Guardar Modelo";
        // si no, solo se guarda la galería, sin marcar la actividad como modificada
        if (merge.SelectedUrlChanged)
        {
            UpdatePreviewImagesJson();
        }
        else
        {
            WriteSnapshotsToRow();
            _autoSaveService.RequestSave(immediate: true);
        }
    }

    /// <summary>
    /// Libera los recursos utilizados y cancela las suscripciones a eventos de este ViewModel.
    /// </summary>
    public void Dispose()
    {
        _eventAggregator?.Unsubscribe<PreviewUpdatedEvent>(OnPreviewUpdated);
        DocumentLifecycleService.SelectionChanged -= OnRevitSelectionChanged;
    }
}
