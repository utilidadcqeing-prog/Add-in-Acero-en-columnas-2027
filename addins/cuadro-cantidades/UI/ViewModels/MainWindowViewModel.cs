using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEING_CuadroCantidades.App;
using CQEING_CuadroCantidades.Core;
using CQEING_CuadroCantidades.Core.Application.Events;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Application.Services;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;
using CQEING_CuadroCantidades.Services;
using CQEING_CuadroCantidades.UI.Models;
using CQEING_CuadroCantidades.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace CQEING_CuadroCantidades.UI.ViewModels;

/// <summary>
/// Modelo de vista principal para la ventana de Cuadro de Cantidades.
/// Administra la estructura del presupuesto, los elementos de Revit vinculados y las previsualizaciones de actividades.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly UIApplication _uiApplication;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentProjectContext _projectContext;
    private readonly IProjectStateService _stateService;
    private readonly IAutoSaveService _autoSaveService;
    private readonly IActivityPreviewService _activityPreviewService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IElementAvailabilityService _elementAvailabilityService;
    private readonly IActivityElementRegistry _activityElementRegistry;
    private readonly ILivePreviewUpdater _livePreviewUpdater;
    private readonly IPreviewSessionContext _sessionContext;
    private readonly IManualPreviewService _manualPreviewService;
    private readonly IActivityPreviewUpdater _activityPreviewUpdater;
    private readonly IPreviewRefreshNotifier _previewRefreshNotifier;
    private readonly IPreviewRefreshService _previewRefreshService;
    private readonly IPreviewImageLoaderService _previewImageLoaderService;
    private readonly IUIThreadDispatcherService _uiDispatcherService;
    private readonly IApiClient _apiClient;
    private readonly IImageUploadService _imageUploadService;
    private readonly IPendingActivitySyncStore _pendingSyncStore;
    private readonly Dispatcher _dispatcher;
    private Budget? _currentBudget;
    private bool _isSyncingSelection = false;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _modifiedActivityCodes = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<object>? ElementSelectedForScroll;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isReadOnlyMode;

    [ObservableProperty]
    private bool _showNetworkError;

    [ObservableProperty]
    private string _networkErrorMessage = string.Empty;

    /// <summary>
    /// Obtiene el despachador de hilos de WPF.
    /// </summary>
    public Dispatcher Dispatcher => _dispatcher;

    /// <summary>
    /// Obtiene el presupuesto (estructura) cargado actualmente.
    /// </summary>
    public Budget? CurrentBudget
    {
        get => _currentBudget;
        private set
        {
            if (SetProperty(ref _currentBudget, value))
            {
                OnPropertyChanged(nameof(IsBudgetLoaded));
                OnPropertyChanged(nameof(ModelBindingStatusText));
            }
        }
    }

    /// <summary>
    /// Indica si hay una estructura de presupuesto cargada.
    /// </summary>
    public bool IsBudgetLoaded => _currentBudget != null;

    /// <summary>
    /// Obtiene el texto informativo sobre la vinculación de modelo para el presupuesto actual.
    /// </summary>
    public string ModelBindingStatusText
    {
        get
        {
            if (_currentBudget == null)
                return "Sin estructura cargada";

            var doc = _uiApplication.ActiveUIDocument?.Document;
            string activeModelId = doc != null ? RevitModelIdentifier.GetModelUniqueId(doc) : string.Empty;
            string? linkedModelId = _currentBudget.RevitModelId;

            if (string.IsNullOrEmpty(linkedModelId))
                return "El presupuesto actual no está vinculado a ningún modelo.";

            if (CQEING_CuadroCantidades.Services.RevitModelIdentifier.IsSameOrCompatibleModelId(linkedModelId, activeModelId))
                return "El presupuesto está vinculado al modelo de Revit actual.";

            return $"El presupuesto está vinculado a otro modelo (ID: {linkedModelId}).";
        }
    }

    /// <summary>
    /// Recalcula el modo lectura y el texto del vínculo a partir del vínculo actual del presupuesto.
    /// El presupuesto queda en solo lectura si está vinculado a un modelo distinto del documento activo.
    /// Se llama al cargar, vincular y desvincular para que ambos no se contradigan.
    /// </summary>
    private void RefreshModelBindingState()
    {
        var doc = _uiApplication.ActiveUIDocument?.Document;
        string activeModelId = doc != null ? RevitModelIdentifier.GetModelUniqueId(doc) : string.Empty;
        string? linkedModelId = _currentBudget?.RevitModelId;

        IsReadOnlyMode = !string.IsNullOrEmpty(linkedModelId)
            && !RevitModelIdentifier.IsSameOrCompatibleModelId(linkedModelId, activeModelId);
        OnPropertyChanged(nameof(ModelBindingStatusText));
    }

    /// <summary>
    /// Obtiene o establece el título del documento activo de Revit.
    /// </summary>
    [ObservableProperty]
    private string _documentTitle;

    /// <summary>
    /// Obtiene o establece el nombre de la estructura actualmente cargada.
    /// </summary>
    [ObservableProperty]
    private string _loadedStructureName = "Sin estructura cargada";

    /// <summary>
    /// Obtiene o establece la ruta del último archivo de estructura Excel importado.
    /// </summary>
    [ObservableProperty]
    private string? _lastStructurePath;

    /// <summary>
    /// Obtiene o establece la fila del presupuesto seleccionada en la UI.
    /// </summary>
    [ObservableProperty]
    private BudgetRow? _selectedBudgetRow;

    /// <summary>
    /// Obtiene la colección observable de filas que conforman la estructura del presupuesto.
    /// </summary>
    public ObservableCollection<BudgetRow> BudgetRows { get; } = new();

    /// <summary>
    /// Obtiene la colección observable de elementos de Revit no asignados aún a ninguna actividad.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ElementItem> _unassignedElements = new();

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="MainWindowViewModel"/>.
    /// </summary>
    public MainWindowViewModel(
        UIApplication uiApplication,
        IUnitOfWork unitOfWork,
        ICurrentProjectContext projectContext,
        IProjectStateService stateService,
        IAutoSaveService autoSaveService,
        IActivityPreviewService activityPreviewService,
        IEventAggregator eventAggregator,
        IElementAvailabilityService elementAvailabilityService,
        IActivityElementRegistry activityElementRegistry,
        ILivePreviewUpdater livePreviewUpdater,
        IPreviewSessionContext sessionContext,
        IManualPreviewService manualPreviewService,
        IActivityPreviewUpdater activityPreviewUpdater,
        IPreviewRefreshNotifier previewRefreshNotifier,
        IPreviewRefreshService previewRefreshService,
        IPreviewImageLoaderService previewImageLoaderService,
        IUIThreadDispatcherService uiDispatcherService,
        IApiClient apiClient,
        IImageUploadService imageUploadService,
        IPendingActivitySyncStore pendingSyncStore
    )
    {
        _uiApplication = uiApplication;
        _pendingSyncStore = pendingSyncStore;
        _unitOfWork = unitOfWork;
        _projectContext = projectContext;
        _stateService = stateService;
        _autoSaveService = autoSaveService;
        _activityPreviewService = activityPreviewService;
        _eventAggregator = eventAggregator;
        _elementAvailabilityService = elementAvailabilityService;
        _activityElementRegistry = activityElementRegistry;
        _livePreviewUpdater = livePreviewUpdater;
        _sessionContext = sessionContext;
        _manualPreviewService = manualPreviewService;
        _activityPreviewUpdater = activityPreviewUpdater;
        _previewRefreshNotifier = previewRefreshNotifier;
        _previewRefreshService = previewRefreshService;
        _previewImageLoaderService = previewImageLoaderService;
        _uiDispatcherService = uiDispatcherService;
        _apiClient = apiClient;
        _imageUploadService = imageUploadService;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _documentTitle = uiApplication.ActiveUIDocument?.Document.Title ?? "Sin documento";

        // Suscribirse a eventos de Revit y del agregador
        DocumentLifecycleService.SelectionChanged += OnSelectionChanged;
        DocumentLifecycleService.DocumentChanged += OnDocumentChanged;

        _eventAggregator.Subscribe<ElementAvailabilityChangedEvent>(OnElementAvailabilityChanged);
        _eventAggregator.Subscribe<ActivityChangedEvent>(OnActivityChangedEventReceived);
        _eventAggregator.Subscribe<PreviewUpdatedEvent>(OnPreviewUpdatedEventReceived);

        if (_livePreviewUpdater is LivePreviewUpdater liveUpdater)
        {
            liveUpdater.SetViewModel(this);
        }
        _livePreviewUpdater.StartUpdating();

        // Carga Inicial Asíncrona (con restauración del estado guardado)
        _ = LoadInitialBudgetAsync();
    }

    /// <summary>
    /// Método parcial invocado al cambiar el BudgetRow seleccionado.
    /// </summary>
    /// <param name="value">Nueva fila seleccionada.</param>
    partial void OnSelectedBudgetRowChanged(BudgetRow? value)
    {
        if (value is not null)
        {
            _projectContext.SelectedBudgetRow = value;
        }
    }

    /// <summary>
    /// Realiza la carga inicial del presupuesto restaurando el estado persistido localmente o cargando un presupuesto base.
    /// </summary>
    private async Task LoadInitialBudgetAsync()
    {
        IsLoading = true;
        System.Diagnostics.Debug.WriteLine(
            $"[CuadroCantidades][Carga] Iniciando carga inicial del presupuesto. Hora: {DateTime.Now}"
        );
        if (_uiApplication.ActiveUIDocument?.Document is not Document doc)
        {
            IsLoading = false;
            return;
        }

        var projectPath = doc.PathName;
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            IsLoading = false;
            return;
        }

        _autoSaveService.StopMonitoring();
        _projectContext.RevitDocumentPath = projectPath;

        try
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CuadroCantidades][Restauracion] Buscando estado anterior guardado para el documento: {projectPath}"
            );

            // Cargar estado desde el archivo JSON de persistencia local
            var savedState = await _stateService.GetSavedStateAsync();

            if (savedState != null)
            {
                if (savedState.BudgetId == Guid.Empty)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[CuadroCantidades][Restauracion] El estado anterior indica una sesion vacia (limpiada por el usuario). Iniciando con panel vacio."
                    );

                    // Buscar si ya existe el proyecto en la base de datos para mantener el contexto del proyecto
                    var projects = await _unitOfWork.Projects.FindAsync(p =>
                        p.RevitDocumentPath == projectPath
                    );
                    var project = projects.FirstOrDefault();
                    if (project == null)
                    {
                        project = new Project
                        {
                            RevitDocumentPath = projectPath,
                            ProjectNumber = doc.Title,
                        };
                        await _unitOfWork.Projects.AddAsync(project);
                        await _unitOfWork.SaveChangesAsync();
                    }

                    // Sesión limpia: sin presupuesto activo, sin filas cargadas
                    CurrentBudget = null;
                    _projectContext.Clear();
                    _projectContext.RevitDocumentPath = projectPath;
                    _projectContext.CurrentProject = project;

                    _dispatcher.Invoke(() =>
                    {
                        BudgetRows.Clear();
                        SelectedBudgetRow = null;
                        LoadedStructureName = "Sin estructura cargada";
                        _projectContext.LoadedStructureName = LoadedStructureName;
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[CuadroCantidades][Restauracion] Intentando restaurar presupuesto activo: {savedState.BudgetId}"
                    );
                    bool restored = await _stateService.RestoreStateAsync();
                    if (restored && _projectContext.CurrentBudget != null)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[CuadroCantidades][Restauracion] Restauracion exitosa desde JSON. BudgetId: {_projectContext.CurrentBudget.Id}"
                        );
                        CurrentBudget = _projectContext.CurrentBudget;

                        await LoadBudgetRowsFromDbAsync(CurrentBudget.Id);
                        await RestorePendingActivitiesAsync(CurrentBudget.Id);

                        // Restaurar selección anterior si existía
                        if (_projectContext.SelectedBudgetRow != null)
                        {
                            var codeToSelect = _projectContext.SelectedBudgetRow.Code;
                            var row = BudgetRows.FirstOrDefault(r => r.Code == codeToSelect);
                            if (row != null)
                            {
                                SelectedBudgetRow = row;
                            }
                        }

                        // Restaurar valores de inputs y estado del panel principal
                        LastStructurePath = _projectContext.LastStructurePath;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "[CuadroCantidades][Restauracion] No se pudo restaurar el presupuesto del estado (probablemente fue eliminado de la DB). Cargando presupuesto activo desde la base de datos."
                        );
                        await LoadFallbackBudgetAsync(projectPath, doc.Title);
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    "[CuadroCantidades][Restauracion] No existe archivo de estado previo. Cargando desde la base de datos."
                );
                await LoadFallbackBudgetAsync(projectPath, doc.Title);
            }

            await LoadUnassignedElementsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CuadroCantidades][Error] Error al cargar presupuesto inicial: {ex.Message}"
            );
        }
        finally
        {
            _autoSaveService.StartMonitoring(this);
            IsLoading = false;
        }
    }

    /// <summary>
    /// Carga el presupuesto alternativo (fallback) cuando no se encuentra un estado persistido previamente.
    /// </summary>
    private async Task LoadFallbackBudgetAsync(string projectPath, string documentTitle)
    {
        var projects = await _unitOfWork.Projects.FindAsync(p =>
            p.RevitDocumentPath == projectPath
        );
        var project = projects.FirstOrDefault();

        if (project == null)
        {
            System.Diagnostics.Debug.WriteLine(
                "[CuadroCantidades][Carga] Proyecto nuevo en base de datos. Dejando sesion limpia."
            );
            CurrentBudget = null;
            _projectContext.Clear();
            _projectContext.RevitDocumentPath = projectPath;

            _dispatcher.Invoke(() =>
            {
                BudgetRows.Clear();
                SelectedBudgetRow = null;
                LoadedStructureName = "Sin estructura cargada";
                _projectContext.LoadedStructureName = LoadedStructureName;
            });
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(
                "[CuadroCantidades][Carga] Proyecto existente encontrado. Buscando presupuesto activo en DB."
            );
            var budgets = await _unitOfWork.Budgets.FindAsync(b =>
                b.ProjectId == project.Id && b.IsActive
            );
            var budget = budgets.FirstOrDefault();

            if (budget != null)
            {
                // Verificar si tiene elementos de presupuesto
                var items = (
                    await _unitOfWork.BudgetItems.FindAsync(bi => bi.BudgetId == budget.Id)
                ).ToList();
                if (items.Any())
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[CuadroCantidades][Carga] Presupuesto activo encontrado con {items.Count} elementos. Cargando."
                    );
                    CurrentBudget = budget;
                    _projectContext.SetContext(project, budget);
                    await LoadBudgetRowsFromDbAsync(CurrentBudget.Id);

                    _dispatcher.Invoke(() =>
                    {
                        LoadedStructureName = CurrentBudget.VersionName;
                    });
                    return;
                }
            }

            // Si el proyecto existe pero no tiene un presupuesto activo con filas, iniciar sesion limpia
            System.Diagnostics.Debug.WriteLine(
                "[CuadroCantidades][Carga] No se encontro presupuesto activo con elementos. Dejando sesion limpia."
            );
            CurrentBudget = null;
            _projectContext.Clear();
            _projectContext.RevitDocumentPath = projectPath;
            _projectContext.CurrentProject = project;

            _dispatcher.Invoke(() =>
            {
                BudgetRows.Clear();
                SelectedBudgetRow = null;
                LoadedStructureName = "Sin estructura cargada";
                _projectContext.LoadedStructureName = LoadedStructureName;
            });
        }
    }

    /// <summary>
    /// Carga el presupuesto indicado en la interfaz por su identificador único.
    /// </summary>
    /// <param name="budgetId">Identificador único del presupuesto.</param>
    /// <param name="versionName">Nombre o código de la versión del presupuesto (opcional).</param>
    public async Task LoadBudgetAsync(Guid budgetId, string? versionName = null)
    {
        string targetVersionName = versionName ?? $"PROY_{IdMapper.GuidToInt(budgetId):D3}";

        // Consultar la API antes de tocar la base local: si el servidor no responde,
        // se conserva intacto lo que haya en local (incluidos los cambios pendientes de guardar).
        IsLoading = true;
        var structure = await _apiClient.GetBudgetStructureAsync(targetVersionName);
        if (structure == null)
        {
            IsLoading = false;
            ShowNetworkError = true;
            NetworkErrorMessage =
                "No hay conexión con el servidor. Se conservan los datos locales sin cambios.";
            MessageBox.Show(
                "No se pudo obtener el presupuesto desde el servidor.\n\nVerifique que el servicio esté encendido. Los datos locales no se modificaron.",
                "Servidor no disponible",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        // La base local se reemplaza con lo del servidor: ya no queda nada local pendiente de enviar
        _modifiedActivityCodes.Clear();
        _ = _pendingSyncStore.SaveAsync(budgetId, Array.Empty<string>());
        System.Diagnostics.Debug.WriteLine(
            $"[CuadroCantidades][Carga] Cargando presupuesto con ID {budgetId} desde la API..."
        );
        _autoSaveService.StopMonitoring();
        _unitOfWork.ClearTracker(); // Limpiar cualquier cambio pendiente en la base de datos antes de iniciar la carga
        _autoSaveService.Resume(); // Asegurar que el autoguardado esté activo para la nueva carga
        IsLoading = true;
        try
        {
            // Guardar copia de seguridad de las imágenes locales y snapshots para no perderlas al resincronizar
            var localPreviewsBackup = new Dictionary<string, (string? Path, string? Json)>();
            try
            {
                var existingLocalItems = await _unitOfWork.BudgetItems.FindAsync(bi =>
                    bi.BudgetId == budgetId
                );
                foreach (var item in existingLocalItems)
                {
                    if (!string.IsNullOrEmpty(item.Code))
                    {
                        localPreviewsBackup[item.Code] = (
                            item.PreviewImagePath,
                            item.PreviewImagesJson
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CuadroCantidades][Backup] Error al respaldar vistas previas locales: {ex.Message}"
                );
            }

            // Limpiar todo el contenido de la base de datos local en memoria antes de cargar para evitar colisiones de IDs (UNIQUE constraint failed: BudgetItems.Id)
            var allLinked = await _unitOfWork.LinkedElements.GetAllAsync();
            _unitOfWork.LinkedElements.RemoveRange(allLinked);

            var allItems = await _unitOfWork.BudgetItems.GetAllAsync();
            _unitOfWork.BudgetItems.RemoveRange(allItems);

            var allBudgets = await _unitOfWork.Budgets.GetAllAsync();
            _unitOfWork.Budgets.RemoveRange(allBudgets);

            var allProjects = await _unitOfWork.Projects.GetAllAsync();
            _unitOfWork.Projects.RemoveRange(allProjects);

            await _unitOfWork.SaveChangesAsync();
            _unitOfWork.ClearTracker(); // Desacoplar cualquier entidad eliminada del tracker de Entity Framework

            // Obtener el revitModelId actual desde el servidor
            string? serverRevitModelId = null;
            try
            {
                var apiBudgets = await _apiClient.GetBudgetsAsync();
                if (apiBudgets != null)
                {
                    var matching = apiBudgets.FirstOrDefault(b => b.CodigoPresupuesto == targetVersionName);
                    if (matching != null)
                    {
                        serverRevitModelId = matching.ActualRevitModelId;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadBudget] Error fetching budget RevitModelId: {ex.Message}");
            }

            // Inicializar el objeto Budget
            int apiId = IdMapper.GuidToInt(budgetId);
            var budget = new Budget
            {
                Id = budgetId,
                VersionName = targetVersionName,
                IsActive = true,
                RevitModelId = serverRevitModelId
            };

            // 1. La estructura completa ya se obtuvo de la API (PostgreSQL en el backend) al inicio
            if (structure != null)
            {
                // Asegurar que el proyecto existe localmente en el contexto de memoria
                var projectPath = _projectContext.RevitDocumentPath;
                var project = new Project
                {
                    RevitDocumentPath = projectPath,
                    ProjectNumber = structure.CodigoPresupuesto,
                };
                await _unitOfWork.Projects.AddAsync(project);
                await _unitOfWork.SaveChangesAsync();

                // Asegurar que el presupuesto existe localmente en el contexto de memoria
                budget.ProjectId = project.Id;
                await _unitOfWork.Budgets.AddAsync(budget);
                await _unitOfWork.SaveChangesAsync();

                // Reconstruir estructura local basada en los datos frescos de la API PostgreSQL
                int order = 1;
                foreach (var cap in structure.Capitulos)
                {
                    var capItem = new BudgetItem
                    {
                        BudgetId = budget.Id,
                        ItemType = "CAPITULO",
                        Code = cap.CodigoCapitulo,
                        Description = cap.Nombre,
                        SortOrder = order++,
                    };
                    await _unitOfWork.BudgetItems.AddAsync(capItem);

                    foreach (var subcap in cap.Subcapitulos)
                    {
                        var subcapItem = new BudgetItem
                        {
                            BudgetId = budget.Id,
                            ItemType = "SUBCAPITULO",
                            Code = subcap.CodigoSubcapitulo,
                            Description = subcap.Nombre,
                            SortOrder = order++,
                        };
                        await _unitOfWork.BudgetItems.AddAsync(subcapItem);

                        // Descargar los elementos vinculados para cada actividad en paralelo desde PostgreSQL
                        var itemsTasks = subcap
                            .Items.Select(async apiItem =>
                            {
                                var elements = await _apiClient.GetLinkedElementsAsync(
                                    apiItem.IdItem
                                );
                                return new
                                {
                                    ApiItem = apiItem,
                                    Elements = elements ?? new List<ApiLinkedElementResponse>(),
                                };
                            })
                            .ToList();

                        var itemsResults = await Task.WhenAll(itemsTasks);

                        foreach (var res in itemsResults)
                        {
                            var apiItem = res.ApiItem;
                            var firstElWithImages = res.Elements.FirstOrDefault(el =>
                                el.UrlsImagenes != null && el.UrlsImagenes.Count > 0
                            );
                            string? firstCloudinaryUrl = firstElWithImages != null ? string.Join(",", firstElWithImages.UrlsImagenes) : null;

                            var actItem = new BudgetItem
                            {
                                Id = IdMapper.IntToGuid(apiItem.IdItem),
                                BudgetId = budget.Id,
                                ItemType = "ACTIVIDAD",
                                Code = apiItem.CodigoActividad,
                                Description = apiItem.Descripcion,
                                Unit = apiItem.Unidad,
                                SortOrder = order++,
                                PreviewImagePath = firstCloudinaryUrl,
                            };

                            // Restaurar imagen local o galería de capturas si existe en la copia de seguridad
                            if (
                                localPreviewsBackup.TryGetValue(
                                    apiItem.CodigoActividad,
                                    out var backup
                                )
                            )
                            {
                                if (!string.IsNullOrEmpty(backup.Path))
                                {
                                    actItem.PreviewImagePath = backup.Path;
                                }
                                if (!string.IsNullOrEmpty(backup.Json))
                                {
                                    actItem.PreviewImagesJson = backup.Json;
                                }
                            }

                            await _unitOfWork.BudgetItems.AddAsync(actItem);

                            // Agregar elementos vinculados
                            foreach (var apiEl in res.Elements)
                            {
                                var linkedEl = new LinkedElement
                                {
                                    BudgetItemId = actItem.Id,
                                    RevitUniqueId = apiEl.RevitUniqueId,
                                    Category = apiEl.Categoria,
                                    ElementName = apiEl.NombreElemento,
                                    ElementDataJson = apiEl.DatosJson,
                                    UrlImagen = apiEl.UrlsImagenes != null && apiEl.UrlsImagenes.Count > 0 ? string.Join(",", apiEl.UrlsImagenes) : null,
                                    IsDeletedInRevit = apiEl.EliminadoEnRevit,
                                };
                                await _unitOfWork.LinkedElements.AddAsync(linkedEl);
                            }
                        }
                    }
                }

                await _unitOfWork.SaveChangesAsync();
            }

            // Recargar datos finales
            budget = await _unitOfWork.Budgets.GetByIdAsync(budgetId);
            if (budget == null)
                return;

            var currentProj = await _unitOfWork.Projects.GetByIdAsync(budget.ProjectId);
            if (currentProj != null)
            {
                _projectContext.SetContext(currentProj, budget);
                CurrentBudget = budget;
                RefreshModelBindingState();
                await LoadBudgetRowsFromDbAsync(budgetId);
            }

            System.Diagnostics.Debug.WriteLine(
                $"[CuadroCantidades][Carga] Presupuesto cargado y sincronizado con PostgreSQL exitosamente."
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CuadroCantidades][Error] Error al cargar presupuesto: {ex.Message}"
            );
            var detailedMessage = ex.Message;
            var inner = ex.InnerException;
            while (inner != null)
            {
                detailedMessage += $"\n\nInner Exception: {inner.Message}";
                inner = inner.InnerException;
            }
            MessageBox.Show(
                $"No se pudo cargar la estructura del presupuesto:\n\n{detailedMessage}",
                "Error al Cargar Presupuesto",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            _autoSaveService.StartMonitoring(this);
            // Notificamos el cambio para guardar inmediatamente la nueva estructura cargada
            _autoSaveService.RequestSave(immediate: true);
            IsLoading = false;
        }
    }

    /// <summary>
    /// Obtiene de la base de datos las filas y elementos enlazados del presupuesto especificado.
    /// </summary>
    private async Task LoadBudgetRowsFromDbAsync(Guid budgetId)
    {
        // Flush cualquier cambio pendiente y limpiar tracker de EF para garantizar
        // que la carga de BudgetItems y LinkedElements obtenga todas las relaciones de DB.
        // Esto soluciona el error de cantidades en 0 después de actualizar (Escenario B).
        await _unitOfWork.SaveChangesAsync();
        _unitOfWork.ClearTracker();

        await _activityElementRegistry.LoadRegistryForBudgetAsync(budgetId);
        
        // Sincronizar parámetro Comments de los elementos de Revit con los datos guardados
        await _elementAvailabilityService.SyncAllElementsForBudgetAsync(budgetId);

        var items = await _unitOfWork.BudgetItems.FindAsync(i => i.BudgetId == budgetId);

        var budgetItemIds = items.Select(i => i.Id).ToList();
        var linkedElements = await _unitOfWork.LinkedElements.FindAsync(l =>
            budgetItemIds.Contains(l.BudgetItemId)
        );
        var groupedElements = linkedElements
            .GroupBy(l => l.BudgetItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var item in items)
        {
            if (groupedElements.TryGetValue(item.Id, out var els))
            {
                item.LinkedElements = els;
            }
        }

        _dispatcher.Invoke(() =>
        {
            BudgetRows.Clear();
            foreach (var item in items.OrderBy(i => i.SortOrder))
            {
                int level = item.ItemType switch
                {
                    "CAPITULO" => 0,
                    "SUBCAPITULO" => 1,
                    "ACTIVIDAD" => 2,
                    _ => 0,
                };
                string outline = (level == 0 || level == 1) ? "▼" : "";
                double quantity =
                    (level == 2)
                        ? QuantificationEngine.CalculateTotalQuantity(
                            item.Unit,
                            item.LinkedElements
                        )
                        : 0;

                var newRow = new BudgetRow
                {
                    RowNumber = item.SortOrder,
                    Outline = outline,
                    Type = item.ItemType,
                    Code = item.Code,
                    Description = item.Description,
                    Unit = item.Unit,
                    Quantity = level == 2 ? quantity.ToString("0.00") : "",
                    Level = level,
                    CanEdit = level == 2,
                    PreviewImagePath = item.PreviewImagePath,
                    LastPreviewUpdate = item.LastPreviewUpdate,
                    ElementIdsSnapshot = item.ElementIdsSnapshot,
                    PreviewImagesJson = item.PreviewImagesJson,
                    IsExpanded = false
                };

                if (level == 2 && item.LinkedElements != null)
                {
                    newRow.AssignedElementsTable = UI.Utils.DynamicTableBuilder.BuildDynamicTable(item.LinkedElements, item.Unit);
                    
                    // Sincronizar cantidad principal con los totales de la tabla si existen (maneja traducciones de API)
                    if (newRow.AssignedElementsTable.Rows.Count > 0)
                    {
                        var lastRow = newRow.AssignedElementsTable.Rows[newRow.AssignedElementsTable.Rows.Count - 1];
                        if (newRow.AssignedElementsTable.Columns.Contains("No.") && lastRow["No."].ToString() == "TOTAL")
                        {
                            string unit = item.Unit?.Trim().ToUpperInvariant() ?? "";
                            if ((unit == "M3" || unit == "M³") && newRow.AssignedElementsTable.Columns.Contains("VOLUMEN"))
                                quantity = lastRow["VOLUMEN"] != DBNull.Value ? Convert.ToDouble(lastRow["VOLUMEN"]) : 0;
                            else if ((unit == "M2" || unit == "M²") && newRow.AssignedElementsTable.Columns.Contains("ÁREA"))
                                quantity = lastRow["ÁREA"] != DBNull.Value ? Convert.ToDouble(lastRow["ÁREA"]) : 0;
                            else if ((unit == "ML" || unit == "M") && newRow.AssignedElementsTable.Columns.Contains("LONGITUD"))
                                quantity = lastRow["LONGITUD"] != DBNull.Value ? Convert.ToDouble(lastRow["LONGITUD"]) : 0;
                            else if ((unit == "KG" || unit == "TON") && newRow.AssignedElementsTable.Columns.Contains("PESO"))
                                quantity = lastRow["PESO"] != DBNull.Value
                                    ? QuantificationEngine.ConvertFromKilograms(Convert.ToDouble(lastRow["PESO"]), unit)
                                    : 0;

                            newRow.Quantity = quantity.ToString("0.00");
                        }
                    }
                    
                    foreach (var linkedEl in item.LinkedElements)
                    {
                        newRow.AssignedElementsPreview.Add(new ElementItem
                        {
                            UniqueId = linkedEl.RevitUniqueId,
                            Category = linkedEl.Category,
                            Name = linkedEl.ElementName,
                            IsSelected = false
                        });
                    }
                }

                BudgetRows.Add(newRow);
            }

            if (CurrentBudget != null)
            {
                LoadedStructureName = CurrentBudget.VersionName;
                _projectContext.LoadedStructureName = LoadedStructureName;
            }
            CalculateChapterTotals();

            // Verificación de duplicidad de códigos de actividad
            var duplicateCodes = BudgetRows
                .Where(r => IsActivity(r) && !string.IsNullOrWhiteSpace(r.Code))
                .GroupBy(r => r.Code)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateCodes.Count > 0)
            {
                MessageBox.Show(
                    $"Se ha detectado duplicidad en los siguientes códigos de actividad:\n\n{string.Join("\n", duplicateCodes)}\n\nNo se le permitirá agregar elementos hasta que no corrija este inconveniente.",
                    "Alerta de Duplicidad de Códigos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        });

        await LoadUnassignedElementsAsync();
    }

    /// <summary>
    /// Carga los elementos del modelo de Revit que tienen propiedades de material cuantificables y que están disponibles (no asignados).
    /// </summary>
    private Task LoadUnassignedElementsAsync()
    {
        // Ejecutar la lectura de Revit en el hilo de Revit usando el ejecutor dedicado
        RevitEventExecutor.Execute(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return;

            // Recolectar todos los elementos del modelo (que no sean tipos y no dependan de una vista específica)
            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WhereElementIsViewIndependent();

            var elementItems = new List<ElementItem>();
            var elementErrors = new List<(string ElementId, string Category, string Error)>();
            foreach (var element in collector)
            {
                try
                {
                    if (element == null || !element.IsValidObject)
                        continue;

                    var category = element.Category;
                    if (category == null)
                        continue;

                    // Asegurar que la categoría es de modelo físico (las categorías analíticas o de sistema suelen fallar)
                    if (category.CategoryType != CategoryType.Model)
                        continue;

                    bool isRebar = category.Id.Value == (long)BuiltInCategory.OST_Rebar;
                    bool isStairs = category.Id.Value == (long)BuiltInCategory.OST_Stairs;
                    // El refuerzo de área no tiene cantidades de material, pero su contorno es el M2 del acero de losas
                    bool isAreaReinforcement = AreaReinforcementQuantityExtractor.IsAreaReinforcement(element);
                    if (!category.HasMaterialQuantities && !isRebar && !isStairs && !isAreaReinforcement)
                        continue;

                    // Comprobar si el elemento no está asignado ya a otra actividad
                    if (!_elementAvailabilityService.IsElementAvailable(element.UniqueId))
                        continue;

                    var paramDict = new System.Collections.Generic.Dictionary<string, double>();

                    // 1. Extraer el volumen
                    if (isStairs)
                    {
                        double totalVolume = 0.0;
                        try
                        {
                            var matIds = element.GetMaterialIds(false);
                            foreach (var matId in matIds)
                            {
                                if (matId != ElementId.InvalidElementId)
                                {
                                    double matVol = element.GetMaterialVolume(matId);
                                    if (matVol > 0)
                                    {
                                        totalVolume += matVol;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Ignorar fallos de extracción de material
                        }
                        
                        double totalM3 = totalVolume * 0.0283168; // Conversión consistente del add-in
                        
                        if (totalVolume > 0)
                            paramDict["Volume"] = totalM3;
                    }
                    else
                    {
                        // Extraer el volumen hospedado del elemento (si existe y tiene valor) y convertirlo a metros cúbicos
                        Parameter? volParam = element.get_Parameter(
                            BuiltInParameter.HOST_VOLUME_COMPUTED
                        );
                        if (volParam != null && volParam.HasValue)
                        {
                            paramDict["Volume"] = volParam.AsDouble() * 0.0283168; // Factor de conversión de pies cúbicos a metros cúbicos
                        }
                        else
                        {
                            Parameter? vParam = element.LookupParameter("Volume")
                                ?? element.LookupParameter("Volumen");
                            if (vParam != null && vParam.HasValue)
                            {
                                paramDict["Volume"] = vParam.AsDouble() * 0.0283168;
                            }
                        }
                    }

                    // 2. Extraer el área hospedada del elemento y convertirla a metros cuadrados
                    Parameter? areaParam = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (areaParam != null && areaParam.HasValue)
                    {
                        paramDict["Area"] = areaParam.AsDouble() * 0.092903; // pies cuadrados a metros cuadrados
                    }
                    else
                    {
                        Parameter? aParam = element.LookupParameter("Area")
                            ?? element.LookupParameter("Área");
                        if (aParam != null && aParam.HasValue)
                        {
                            paramDict["Area"] = aParam.AsDouble() * 0.092903;
                        }
                    }

                    // 3. Extraer dimensiones específicas según clase de elemento (muros, suelos, zapatas)
                    if (element is Wall wallInstance)
                    {
                        paramDict["Width"] = wallInstance.Width * 0.3048;
                        paramDict["Thickness"] = wallInstance.Width * 0.3048;
                    }
                    else if (element is Floor floorInstance)
                    {
                        var floorType = floorInstance.FloorType;
                        if (floorType != null)
                        {
                            var cs = floorType.GetCompoundStructure();
                            if (cs != null)
                            {
                                paramDict["Thickness"] = cs.GetWidth() * 0.3048;
                            }
                        }
                    }

                    // 3b. Extracción específica para zapatas (Structural Foundations)
                    {
                        string catNameLower = category.Name.ToLowerInvariant();
                        bool isFoundation = catNameLower.Contains("foundation") || catNameLower.Contains("zapata") || catNameLower.Contains("cimentación");
                        if (isFoundation)
                        {
                            // Intentar extraer Length y Width desde el tipo de familia
                            var foundationType = element.Document.GetElement(element.GetTypeId()) as ElementType;
                            if (foundationType != null)
                            {
                                // Buscar Largo en parámetros del tipo
                                if (!paramDict.ContainsKey("Length"))
                                {
                                    string[] lengthNames = { "Length", "Largo", "Longitud", "Foundation Length", "l", "a" };
                                    foreach (var name in lengthNames)
                                    {
                                        var p = foundationType.LookupParameter(name);
                                        if (p != null && p.HasValue)
                                        {
                                            paramDict["Length"] = p.AsDouble() * 0.3048;
                                            break;
                                        }
                                    }
                                }
                                // Buscar Ancho en parámetros del tipo
                                if (!paramDict.ContainsKey("Width"))
                                {
                                    string[] widthNames = { "Width", "Ancho", "Foundation Width", "b" };
                                    foreach (var name in widthNames)
                                    {
                                        var p = foundationType.LookupParameter(name);
                                        if (p != null && p.HasValue)
                                        {
                                            paramDict["Width"] = p.AsDouble() * 0.3048;
                                            break;
                                        }
                                    }
                                }
                                // Buscar Grosor/Espesor en parámetros del tipo
                                if (!paramDict.ContainsKey("Thickness"))
                                {
                                    string[] thickNames = { "Thickness", "Foundation Thickness", "Grosor", "Espesor", "h" };
                                    foreach (var name in thickNames)
                                    {
                                        var p = foundationType.LookupParameter(name);
                                        if (p != null && p.HasValue)
                                        {
                                            paramDict["Thickness"] = p.AsDouble() * 0.3048;
                                            break;
                                        }
                                    }
                                }
                            }

                            // Intentar también desde la instancia del elemento
                            if (!paramDict.ContainsKey("Length"))
                            {
                                string[] lengthNames = { "Length", "Largo", "Longitud", "Foundation Length" };
                                foreach (var name in lengthNames)
                                {
                                    var p = element.LookupParameter(name);
                                    if (p != null && p.HasValue)
                                    {
                                        paramDict["Length"] = p.AsDouble() * 0.3048;
                                        break;
                                    }
                                }
                            }
                            if (!paramDict.ContainsKey("Width"))
                            {
                                string[] widthNames = { "Width", "Ancho", "Foundation Width" };
                                foreach (var name in widthNames)
                                {
                                    var p = element.LookupParameter(name);
                                    if (p != null && p.HasValue)
                                    {
                                        paramDict["Width"] = p.AsDouble() * 0.3048;
                                        break;
                                    }
                                }
                            }

                            // Fallback: BoundingBox para estimar Largo y Ancho si aún faltan
                            if (!paramDict.ContainsKey("Length") || !paramDict.ContainsKey("Width"))
                            {
                                var bb = element.get_BoundingBox(null);
                                if (bb != null)
                                {
                                    double dx = Math.Abs(bb.Max.X - bb.Min.X) * 0.3048;
                                    double dy = Math.Abs(bb.Max.Y - bb.Min.Y) * 0.3048;
                                    double dz = Math.Abs(bb.Max.Z - bb.Min.Z) * 0.3048;

                                    // El eje mayor horizontal es el largo, el menor es el ancho
                                    double largo = Math.Max(dx, dy);
                                    double ancho = Math.Min(dx, dy);

                                    if (!paramDict.ContainsKey("Length"))
                                        paramDict["Length"] = Math.Round(largo, 2);
                                    if (!paramDict.ContainsKey("Width"))
                                        paramDict["Width"] = Math.Round(ancho, 2);
                                    if (!paramDict.ContainsKey("Thickness") && dz > 0.001)
                                        paramDict["Thickness"] = Math.Round(dz, 2);
                                }
                            }
                        }
                    }

                    // 4. Extraer dimensiones estándar por BuiltInParameters
                    Parameter? lengthParam = null;
                    bool isBeam = category != null && 
                                  ((BuiltInCategory)category.Id.Value == BuiltInCategory.OST_StructuralFraming || 
                                   category.Name.IndexOf("viga", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                   category.Name.IndexOf("framing", StringComparison.OrdinalIgnoreCase) >= 0);
                    
                    if (isBeam)
                    {
                        lengthParam = element.get_Parameter(BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH)
                            ?? element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                    }
                    else
                    {
                        lengthParam = element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)
                            ?? element.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM)
                            ?? element.get_Parameter(BuiltInParameter.STRUCTURAL_FOUNDATION_LENGTH);
                    }

                    if (lengthParam != null && lengthParam.HasValue && !paramDict.ContainsKey("Length"))
                    {
                        paramDict["Length"] = lengthParam.AsDouble() * 0.3048;
                    }


                    Parameter? widthParam = element.get_Parameter(BuiltInParameter.STRUCTURAL_FOUNDATION_WIDTH);
                    if (widthParam == null && element.GetTypeId() is { } typeId1 && element.Document.GetElement(typeId1) is ElementType typeEl1)
                    {
                        widthParam = typeEl1.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
                    }
                    if (widthParam != null && widthParam.HasValue && !paramDict.ContainsKey("Width"))
                    {
                        paramDict["Width"] = widthParam.AsDouble() * 0.3048;
                    }

                    Parameter? thicknessParam = element.get_Parameter(BuiltInParameter.STRUCTURAL_FOUNDATION_THICKNESS);
                    if (thicknessParam == null && element.GetTypeId() is { } typeId2 && element.Document.GetElement(typeId2) is ElementType typeEl2)
                    {
                        thicknessParam = typeEl2.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                    }
                    if (thicknessParam != null && thicknessParam.HasValue && !paramDict.ContainsKey("Thickness"))
                    {
                        paramDict["Thickness"] = thicknessParam.AsDouble() * 0.3048;
                    }

                    // 5. Extraer por nombres de parámetros (búsqueda dinámica)
                    void ExtractDimension(string paramName, double factor = 0.3048)
                    {
                        Parameter? p = element.LookupParameter(paramName);
                        if (p == null)
                        {
                            var elementType = element.Document.GetElement(element.GetTypeId()) as ElementType;
                            if (elementType != null)
                                p = elementType.LookupParameter(paramName);
                        }
                        if (p != null && p.HasValue && !paramDict.ContainsKey(paramName))
                        {
                            paramDict[paramName] = p.AsDouble() * factor;
                        }
                    }

                    string[] dimParams =
                    {
                        "Longitud", "Largo", "Ancho", "Base", "b", "Height", "Altura", "h",
                        "Grosor", "Depth", "Foundation Thickness", "Espesor"
                    };
                    foreach (var dp in dimParams)
                    {
                        ExtractDimension(dp, 0.3048);
                    }

                    string[] weightParams = { "kg/m", "Mass per unit length", "Peso", "Weight" };
                    foreach (var wp in weightParams)
                    {
                        ExtractDimension(wp, 1.0);
                    }

                    // 5.5 Barras de acero (Rebar y RebarInSystem): longitud total, kg/m y peso
                    RebarQuantityExtractor.AddQuantities(element, paramDict);

                    // 5.6 Perfiles metálicos (armazón y pilares): kg/m del parámetro de peso lineal y peso
                    SteelProfileQuantityExtractor.AddQuantities(element, paramDict);

                    // 5.7 Pilares: área lateral (caras verticales del sólido) para actividades en M2
                    ColumnLateralAreaExtractor.AddQuantities(element, paramDict);

                    // 5.75 Columnas, vigas y zapatas: área de cada cara (ejes globales) para medir en M2 solo las caras elegidas
                    FaceAreaExtractor.AddQuantities(element, paramDict);

                    // 5.8 Puertas y ventanas: área = anchura × altura para actividades en M2
                    DoorWindowDataExtractor.AddQuantities(element, paramDict);

                    // 5.9 Refuerzo de área (acero de losas): área del contorno para actividades en M2
                    AreaReinforcementQuantityExtractor.AddQuantities(element, paramDict);

                    // 6. Cálculos de respaldo si faltan dimensiones
                    if (!paramDict.ContainsKey("Thickness") && paramDict.ContainsKey("Volume") && paramDict.ContainsKey("Area"))
                    {
                        double a = paramDict["Area"];
                        if (a > 0.0001)
                            paramDict["Thickness"] = paramDict["Volume"] / a;
                    }
                    if (!paramDict.ContainsKey("Area") && paramDict.ContainsKey("Volume") && paramDict.ContainsKey("Thickness"))
                    {
                        double t = paramDict["Thickness"];
                        if (t > 0.0001)
                            paramDict["Area"] = paramDict["Volume"] / t;
                    }

                    // 7. Redondear valores a 2 decimales y asegurar llaves bilingües (Excepto KG/M)
                    var tempDict = new System.Collections.Generic.Dictionary<string, double>();
                    foreach (var kvp in paramDict)
                    {
                        if (kvp.Key.Equals("kg/m", StringComparison.OrdinalIgnoreCase))
                        {
                            tempDict[kvp.Key] = kvp.Value; // Conservar la precisión original que entrega la API
                        }
                        else if (FaceAreaCalculator.IsAreaKey(kvp.Key))
                        {
                            tempDict[kvp.Key] = kvp.Value; // Áreas por cara con 4 decimales: se redondea solo su suma
                        }
                        else
                        {
                            tempDict[kvp.Key] = Math.Round(kvp.Value, 2);
                        }
                    }

                    void EnsureKey(string key1, string key2)
                    {
                        if (tempDict.TryGetValue(key1, out double val))
                            tempDict[key2] = val;
                        else if (tempDict.TryGetValue(key2, out double val2))
                            tempDict[key1] = val2;
                    }

                    if (!tempDict.ContainsKey("Length") && !tempDict.ContainsKey("Longitud"))
                    {
                        if (tempDict.TryGetValue("Height", out double hVal))
                        {
                            tempDict["Length"] = hVal;
                        }
                        else if (tempDict.TryGetValue("Altura", out double hVal2))
                        {
                            tempDict["Length"] = hVal2;
                        }
                    }

                    EnsureKey("Length", "Largo");
                    EnsureKey("Length", "Longitud");
                    EnsureKey("Width", "Ancho");
                    EnsureKey("Width", "Base");
                    EnsureKey("Thickness", "Grosor");
                    EnsureKey("Thickness", "Espesor");
                    EnsureKey("Volume", "Volumen");
                    EnsureKey("Area", "Área");

                    paramDict = tempDict;

                    var serializedDict = new System.Collections.Generic.Dictionary<string, object>();
                    foreach (var kvp in paramDict)
                    {
                        serializedDict[kvp.Key] = kvp.Value;
                    }
                    string lvlName = ElementLevelReader.GetLevelName(element);
                    serializedDict["Nivel"] = lvlName;

                    string? hostName = RebarQuantityExtractor.GetHostName(element);
                    if (hostName != null)
                    {
                        serializedDict["Anfitrión"] = hostName;
                    }

                    foreach (var kvp in DoorWindowDataExtractor.GetData(element))
                    {
                        serializedDict[kvp.Key] = kvp.Value;
                    }

                    foreach (var kvp in FaceAreaExtractor.GetData(element))
                    {
                        serializedDict[kvp.Key] = kvp.Value;
                    }

                    string jsonParams = System.Text.Json.JsonSerializer.Serialize(serializedDict, CQEING_CuadroCantidades.Core.JsonConfig.RelaxedOptions);

                    // Obtener el TIPO real del elemento usando la API oficial de Revit 2026.
                    // GetTypeId() es universal para cualquier categoría y totalmente agnóstico al idioma.
                    // Nunca se usa LookupParameter("Comentarios"/"Comments") porque ese campo
                    // es editable por el usuario y puede contener datos de actividad (ACT_XXX).
                    string customName = element.Name; // fallback seguro: nombre de instancia
                    {
                        var typeId = element.GetTypeId();
                        if (typeId != ElementId.InvalidElementId)
                        {
                            var elementType = element.Document.GetElement(typeId);
                            if (elementType != null && !string.IsNullOrWhiteSpace(elementType.Name))
                                customName = elementType.Name;
                        }
                    }

                    elementItems.Add(
                        new ElementItem
                        {
                            ElementId = element.Id.ToString(),
                            UniqueId = element.UniqueId,
                            Name = customName,
                            Category = category.Name,
                            ElementDataJson = jsonParams,
                            IsSelected = false,
                        }
                    );
                }
                catch (Exception ex)
                {
                    elementErrors.Add((element?.Id.ToString() ?? "?", element?.Category?.Name ?? "?", ex.Message));
                    System.Diagnostics.Debug.WriteLine(
                        $"[MainWindowViewModel] Error procesando elemento Revit: {ex.Message}"
                    );
                }
            }

            ElementDiagnosticsDumper.Dump(doc.Title, "elementos-disponibles", elementItems, elementErrors);

            // Actualizar la lista enlazable en el hilo de UI de una sola vez
            _dispatcher.InvokeAsync(() =>
            {
                UnassignedElements = new ObservableCollection<ElementItem>(elementItems);
            });
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Comando para guardar la estructura del presupuesto y sus estados localmente.
    /// </summary>
    [RelayCommand]
    private async Task SaveModelAsync()
    {
        if (IsReadOnlyMode)
        {
            MessageBox.Show(
                "La estructura está en modo lectura porque pertenece a otro modelo de Revit. No se pueden guardar cambios.",
                "Modo Lectura Activo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        if (CurrentBudget == null)
        {
            MessageBox.Show(
                "Por favor seleccione una estructura antes de guardar.",
                "Guardar Modelo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        if (string.IsNullOrEmpty(CurrentBudget.RevitModelId))
        {
            MessageBox.Show(
                "El presupuesto no está vinculado a ningún modelo de Revit. Use \"Vincular Modelo\" antes de guardar.",
                "Guardar Modelo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        if (BudgetRows.Count == 0)
        {
            MessageBox.Show(
                "No hay estructura para guardar.",
                "Guardar",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        IsLoading = true;
        ShowNetworkError = false;

        // Requirement 2: Reminder for activities without images
        var activitiesWithoutImages = new List<string>();
        foreach (var row in BudgetRows)
        {
            if (row.Type == "ACTIVIDAD")
            {
                bool hasImage = false;
                
                if (!string.IsNullOrWhiteSpace(row.PreviewImagesJson))
                {
                    try
                    {
                        var snapshots = System.Text.Json.JsonSerializer.Deserialize<List<CQEING_CuadroCantidades.Core.Domain.Entities.ActivityImageSnapshot>>(row.PreviewImagesJson);
                        if (snapshots != null && snapshots.Any(s => s.IsSelectedForPublication))
                        {
                            hasImage = true;
                        }
                    }
                    catch { }
                }
                
                if (!hasImage && !string.IsNullOrWhiteSpace(row.PreviewImagePath))
                {
                    hasImage = true;
                }

                if (!hasImage)
                {
                    activitiesWithoutImages.Add(row.Code);
                }
            }
        }

        if (activitiesWithoutImages.Count > 0)
        {
            IsLoading = false; // pause loading to show MessageBox
            string message = "Las siguientes actividades no tienen imágenes seleccionadas para publicar:\n\n" +
                             string.Join("\n", activitiesWithoutImages.Select(c => $"* {c}")) +
                             "\n\n¿Desea continuar de todas formas?";

            var result = MessageBox.Show(
                message, 
                "Confirmar guardado", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question
            );
            
            if (result == MessageBoxResult.No)
            {
                return;
            }
            IsLoading = true; // resume loading if Yes
        }

        try
        {
            // Guardar estructura de capítulos/actividades y estado del AppState
            await _stateService.SaveStructuralDataAsync(BudgetRows.ToList());
            await _stateService.SaveStateAsync();

            // Sincronizar elementos enlazados con la API web para cada ítem de presupuesto
            var items = await _unitOfWork.BudgetItems.FindAsync(i =>
                i.BudgetId == CurrentBudget.Id
            );
            var itemIds = items.Select(i => i.Id).ToList();

            // Cargar todos los elementos vinculados en una sola consulta para evitar N+1 queries a la base de datos
            var allLinkedElements = await _unitOfWork.LinkedElements.FindAsync(le =>
                itemIds.Contains(le.BudgetItemId)
            );
            var linkedGroupByItem = allLinkedElements
                .GroupBy(le => le.BudgetItemId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Actualizar nombres personalizados antes de sincronizar o enviar a BD local/API
            // Si algún nombre cambia, la actividad padre se marca como modificada para que
            // la API también reciba los datos actualizados en este mismo guardado.
            var itemIdToCode = items
                .Where(i => i.ItemType == "ACTIVIDAD" && !string.IsNullOrWhiteSpace(i.Code))
                .ToDictionary(i => i.Id, i => i.Code!);

            // Se espera a Revit: los datos sincronizados aquí (nombres, anfitrión, puertas y ventanas, caras) deben estar
            // en los elementos antes de armar lo que se envía a la API y se guarda en la base local.
            var itemsWithDoorWindowChanges = new HashSet<Guid>();
            var itemsWithAreaChanges = new HashSet<Guid>();
            var squareMeterItemIds = items
                .Where(i => QuantificationEngine.IsSquareMeters(i.Unit))
                .Select(i => i.Id)
                .ToHashSet();
            await RevitEventExecutor.ExecuteAsync(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc != null)
                {
                    foreach (var le in allLinkedElements)
                    {
                        if (string.IsNullOrEmpty(le.RevitUniqueId)) continue;
                        var el = doc.GetElement(le.RevitUniqueId);
                        if (el != null)
                        {
                            // Misma lógica universal de extracción de TIPO que en LoadUnassignedElementsAsync.
                            // Al guardar, esto actualiza automáticamente los registros existentes
                            // que tenían ACT_XXX como nombre, sin necesidad de reasignar elementos.
                            string customName = el.Name; // fallback seguro
                            {
                                var typeId = el.GetTypeId();
                                if (typeId != ElementId.InvalidElementId)
                                {
                                    var elementType = doc.GetElement(typeId);
                                    if (elementType != null && !string.IsNullOrWhiteSpace(elementType.Name))
                                        customName = elementType.Name;
                                }
                            }
                            if (le.ElementName != customName)
                            {
                                le.ElementName = customName;
                                // Se registrará como modificado en Entity Framework
                                _unitOfWork.LinkedElements.Update(le);

                                // Marcar la actividad padre como modificada para que la API también reciba
                                // el nombre corregido en este mismo Guardar Modelo (migración transparente).
                                if (itemIdToCode.TryGetValue(le.BudgetItemId, out string? parentCode))
                                {
                                    MarkActivityModified(parentCode);
                                }
                            }

                            // Sincronizar el Host (Anfitrión) en ElementDataJson para elementos Rebar.
                            // Esto corrige registros existentes que fueron guardados sin Host porque
                            // GetHostId() devolvía InvalidElementId en el equipo donde se asignaron.
                            string? hostName = RebarQuantityExtractor.GetHostName(el);
                            if (hostName != null)
                            {
                                // Solo actualizar si el valor en el JSON es diferente al actual
                                try
                                {
                                    string jsonInput = string.IsNullOrWhiteSpace(le.ElementDataJson) ? "{}" : le.ElementDataJson;
                                    var dataDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonInput)
                                        ?? new Dictionary<string, object>();

                                    bool hostChanged = !dataDict.TryGetValue("Anfitrión", out var existingHost)
                                        || existingHost?.ToString() != hostName;

                                    if (hostChanged)
                                    {
                                        dataDict["Anfitrión"] = hostName;
                                        le.ElementDataJson = System.Text.Json.JsonSerializer.Serialize(dataDict, CQEING_CuadroCantidades.Core.JsonConfig.RelaxedOptions);
                                        _unitOfWork.LinkedElements.Update(le);

                                        if (itemIdToCode.TryGetValue(le.BudgetItemId, out string? codeForHost))
                                        {
                                            MarkActivityModified(codeForHost);
                                        }
                                    }
                                }
                                catch { }
                            }

                            // Sincronizar los datos de puertas y ventanas (código, lugar, medidas…) para que las asignadas
                            // antes de existir su tabla los tengan sin reasignarlas.
                            var doorWindowData = DoorWindowDataExtractor.GetData(el);
                            if (doorWindowData.Count > 0)
                            {
                                try
                                {
                                    string jsonInput = string.IsNullOrWhiteSpace(le.ElementDataJson) ? "{}" : le.ElementDataJson;
                                    var dataDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonInput)
                                        ?? new Dictionary<string, object>();

                                    bool doorWindowChanged = false;
                                    foreach (var kvp in doorWindowData)
                                    {
                                        if (!dataDict.TryGetValue(kvp.Key, out var existing) || existing?.ToString() != kvp.Value)
                                        {
                                            dataDict[kvp.Key] = kvp.Value;
                                            doorWindowChanged = true;
                                        }
                                    }

                                    // Área (anchura × altura) para actividades en M2, con las mismas llaves bilingües de la extracción
                                    double doorWindowArea = Math.Round(DoorWindowDataExtractor.GetAreaSquareMeters(el), 2);
                                    if (doorWindowArea > 0)
                                    {
                                        foreach (var areaKey in new[] { "Area", "Área" })
                                        {
                                            if (!dataDict.TryGetValue(areaKey, out var existingArea) || !IsSameNumber(existingArea, doorWindowArea))
                                            {
                                                dataDict[areaKey] = doorWindowArea;
                                                doorWindowChanged = true;
                                            }
                                        }
                                    }

                                    if (doorWindowChanged)
                                    {
                                        le.ElementDataJson = System.Text.Json.JsonSerializer.Serialize(dataDict, CQEING_CuadroCantidades.Core.JsonConfig.RelaxedOptions);
                                        _unitOfWork.LinkedElements.Update(le);
                                        itemsWithDoorWindowChanges.Add(le.BudgetItemId);

                                        if (itemIdToCode.TryGetValue(le.BudgetItemId, out string? codeForDoorWindow))
                                        {
                                            MarkActivityModified(codeForDoorWindow);
                                        }
                                    }
                                }
                                catch { }
                            }

                            // Áreas por cara de columnas, vigas y zapatas en actividades M2 asignadas antes de existir la selección
                            // de caras (o traídas de la API sin ellas): se calculan sin reasignarlas y se conservan las caras elegidas.
                            if (squareMeterItemIds.Contains(le.BudgetItemId)
                                && FaceAreaExtractor.TrySyncElementData(el, le.ElementDataJson, out string faceJson))
                            {
                                le.ElementDataJson = faceJson;
                                _unitOfWork.LinkedElements.Update(le);
                                itemsWithAreaChanges.Add(le.BudgetItemId);

                                if (itemIdToCode.TryGetValue(le.BudgetItemId, out string? codeForFaces))
                                {
                                    MarkActivityModified(codeForFaces);
                                }
                            }

                            // Refuerzo de área: su área y su peso cambian al editar su contorno, los huecos de la losa o las varillas
                            if (AreaReinforcementQuantityExtractor.IsAreaReinforcement(el)
                                && AreaReinforcementQuantityExtractor.TrySyncElementData(el, le.ElementDataJson, out string areaJson))
                            {
                                le.ElementDataJson = areaJson;
                                _unitOfWork.LinkedElements.Update(le);
                                itemsWithAreaChanges.Add(le.BudgetItemId);

                                if (itemIdToCode.TryGetValue(le.BudgetItemId, out string? codeForArea))
                                {
                                    MarkActivityModified(codeForArea);
                                }
                            }
                        }
                    }
                }
            });

            // Redibujar la tabla desplegada de las actividades cuyas puertas o ventanas recibieron datos nuevos
            foreach (var itemId in itemsWithDoorWindowChanges)
            {
                if (!itemIdToCode.TryGetValue(itemId, out string? code)
                    || !linkedGroupByItem.TryGetValue(itemId, out var itemElements))
                    continue;

                var row = BudgetRows.FirstOrDefault(r => r.Code == code);
                if (row != null)
                {
                    row.AssignedElementsTable = UI.Utils.DynamicTableBuilder.BuildDynamicTable(itemElements, row.Unit);
                }
            }

            // Las caras y el refuerzo de área cambian la cantidad de la actividad: se recalcula, no solo su tabla
            foreach (var itemId in itemsWithAreaChanges)
            {
                if (!itemIdToCode.TryGetValue(itemId, out string? code)
                    || !linkedGroupByItem.TryGetValue(itemId, out var itemElements))
                    continue;

                var row = BudgetRows.FirstOrDefault(r => r.Code == code);
                if (row != null)
                {
                    ApplyActivityElements(row, row.Unit ?? string.Empty, itemElements);
                }
            }

            // Obtener estructura completa de la API web para mapear IDs reales de PostgreSQL (para presupuestos importados de Excel o creados localmente)
            var codeToApiId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool serverReachable = false;
            try
            {
                var structure = await _apiClient.GetBudgetStructureAsync(
                    CurrentBudget.VersionName
                );
                serverReachable = structure != null;
                if (structure?.Capitulos != null)
                {
                    foreach (var cap in structure.Capitulos)
                    {
                        if (cap.Subcapitulos == null)
                            continue;
                        foreach (var sub in cap.Subcapitulos)
                        {
                            if (sub.Items == null)
                                continue;
                            foreach (var apiItem in sub.Items)
                            {
                                if (!string.IsNullOrWhiteSpace(apiItem.CodigoActividad))
                                {
                                    codeToApiId[apiItem.CodigoActividad] = apiItem.IdItem;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MainWindowViewModel] No se pudo obtener la estructura de la API para mapear IDs: {ex.Message}"
                );
            }

            var apiTasks = new List<Task<bool>>();
            var imagePublisher = new ActivityImagePublisher(_imageUploadService);
            var imageReport = new ImagePublishReport();
            var activitiesWithPendingImages = new List<string>();

            foreach (var item in items)
            {
                // Solo sincronizar con la API los ítems de tipo ACTIVIDAD
                if (item.ItemType != "ACTIVIDAD")
                    continue;

                // Solo sincronizar actividades que hayan sido modificadas en esta sesión
                if (!_modifiedActivityCodes.ContainsKey(item.Code))
                    continue;

                if (!codeToApiId.TryGetValue(item.Code, out int apiItemId))
                {
                    // Si la actividad no existe en el presupuesto del servidor,
                    // no intentamos sincronizar sus elementos vinculados para evitar errores 404/500.
                    continue;
                }

                if (linkedGroupByItem.TryGetValue(item.Id, out var linked) && linked.Any())
                {
                    var publishResult = await imagePublisher.PublishAsync(item, imageReport);
                    var matchingRow = BudgetRows.FirstOrDefault(r => r.Code == item.Code);

                    // Guardar las URLs nuevas en el historial para no volver a subir esas imágenes.
                    // También en la fila: el auto-guardado copia el historial de la fila a la base local.
                    if (publishResult.UpdatedSnapshotsJson != null)
                    {
                        item.PreviewImagesJson = publishResult.UpdatedSnapshotsJson;
                        _unitOfWork.BudgetItems.Update(item);
                        if (matchingRow != null)
                        {
                            _uiDispatcherService.Invoke(() =>
                            {
                                matchingRow.PreviewImagesJson = publishResult.UpdatedSnapshotsJson;
                            });
                        }
                    }

                    string? imageUrl;
                    if (publishResult.IsComplete)
                    {
                        imageUrl = publishResult.ImageUrls;

                        if (item.PreviewImagePath != imageUrl)
                        {
                            item.PreviewImagePath = imageUrl;
                            _unitOfWork.BudgetItems.Update(item);

                            if (matchingRow != null)
                            {
                                _uiDispatcherService.Invoke(() =>
                                {
                                    matchingRow.PreviewImagePath = imageUrl;
                                });
                            }
                        }

                        // Sincronizar la URL de la imagen en los elementos vinculados locales
                        foreach (var le in linked)
                        {
                            if (le.UrlImagen != imageUrl)
                            {
                                le.UrlImagen = imageUrl;
                                _unitOfWork.LinkedElements.Update(le);
                            }
                        }
                    }
                    else
                    {
                        // Alguna imagen marcada no se pudo subir: la web reemplaza las imágenes en cada envío,
                        // así que se reenvían las URLs ya publicadas y la actividad queda pendiente para reintentar.
                        imageUrl = linked
                            .Select(le => le.UrlImagen)
                            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
                        activitiesWithPendingImages.Add(item.Code);
                    }

                    double totalQuantity = QuantificationEngine.CalculateTotalQuantity(
                        item.Unit,
                        linked
                    );

                    var dataTable = CQEING_CuadroCantidades.UI.Utils.DynamicTableBuilder.BuildDynamicTable(linked, item.Unit);
                    var totalsDict = new Dictionary<string, object>();
                    if (dataTable.Rows.Count > 0)
                    {
                        var lastRow = dataTable.Rows[dataTable.Rows.Count - 1];
                        if (dataTable.Columns.Contains("No.") && lastRow["No."].ToString() == "TOTAL")
                        {
                            foreach (System.Data.DataColumn col in dataTable.Columns)
                            {
                                if (col.ColumnName != "RevitUniqueId" && col.ColumnName != "No." && col.ColumnName != "TIPO" && col.ColumnName != "NIVEL" && col.ColumnName != "ANFITRIÓN")
                                {
                                    if (lastRow[col] != DBNull.Value)
                                    {
                                        totalsDict[col.ColumnName] = lastRow[col];
                                    }
                                }
                            }
                        }
                    }

                    var apiRequests = linked
                        .Select(le =>
                        {
                            var elementDict = new Dictionary<string, object>();
                            System.Data.DataRow row = null;
                            foreach (System.Data.DataRow r in dataTable.Rows)
                            {
                                if (r.Table.Columns.Contains("RevitUniqueId") && r["RevitUniqueId"]?.ToString() == le.RevitUniqueId)
                                {
                                    row = r;
                                    break;
                                }
                            }

                            if (row != null)
                            {
                                foreach (System.Data.DataColumn col in dataTable.Columns)
                                {
                                    if (col.ColumnName != "RevitUniqueId")
                                    {
                                        if (row[col] != DBNull.Value)
                                        {
                                            elementDict[col.ColumnName] = row[col];
                                        }
                                    }
                                }
                            }
                            else
                            {
                                try
                                {
                                    string jsonInput = string.IsNullOrWhiteSpace(le.ElementDataJson) ? "{}" : le.ElementDataJson;
                                    elementDict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonInput) ?? new Dictionary<string, object>();
                                }
                                catch { }
                            }

                            double principalQuantity = 1.0;
                            string unit = item.Unit?.Trim().ToUpperInvariant() ?? "";
                            if (unit == "M3" || unit == "M³")
                            {
                                if (elementDict.TryGetValue("VOLUMEN", out var vol)) principalQuantity = Convert.ToDouble(vol);
                                else if (elementDict.TryGetValue("Volume", out var v)) principalQuantity = Convert.ToDouble(v);
                            }
                            else if (unit == "M2" || unit == "M²")
                            {
                                if (elementDict.TryGetValue("ÁREA", out var area)) principalQuantity = Convert.ToDouble(area);
                                else if (elementDict.TryGetValue("Area", out var a)) principalQuantity = Convert.ToDouble(a);
                            }
                            else if (unit == "ML" || unit == "M")
                            {
                                if (elementDict.TryGetValue("LONGITUD", out var len)) principalQuantity = Convert.ToDouble(len);
                                else if (elementDict.TryGetValue("Length", out var l)) principalQuantity = Convert.ToDouble(l);
                            }
                            else if (unit == "KG" || unit == "TON")
                            {
                                if (elementDict.TryGetValue("PESO", out var peso)) principalQuantity = Convert.ToDouble(peso);
                                else if (elementDict.TryGetValue("Weight", out var w)) principalQuantity = Convert.ToDouble(w);
                            }
                            elementDict["Cantidad"] = totalQuantity;
                            
                            if (totalsDict.Count > 0)
                            {
                                elementDict["Totales"] = totalsDict;
                            }

                            string modifiedJson = "{}";
                            try
                            {
                                modifiedJson = JsonSerializer.Serialize(elementDict, CQEING_CuadroCantidades.Core.JsonConfig.RelaxedOptions);
                            }
                            catch { }

                            return new ApiLinkedElementRequest
                            {
                                RevitUniqueId = le.RevitUniqueId ?? string.Empty,
                                Categoria = le.Category ?? string.Empty,
                                NombreElemento = le.ElementName ?? string.Empty,
                                DatosJson = modifiedJson,
                                UrlsImagenes = string.IsNullOrWhiteSpace(imageUrl) ? new List<string>() : imageUrl.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList(),
                                EliminadoEnRevit = le.IsDeletedInRevit,
                            };
                        })
                        .ToList();

                    // Iniciamos la llamada asíncrona en paralelo
                    apiTasks.Add(_apiClient.UpdateLinkedElementsAsync(apiItemId, apiRequests));
                }
                else
                {
                    // Si no quedan elementos vinculados en local, enviamos un arreglo vacío
                    // para vaciar la actividad en el servidor.
                    var emptyRequests = new List<ApiLinkedElementRequest>();
                    apiTasks.Add(_apiClient.UpdateLinkedElementsAsync(apiItemId, emptyRequests));
                }
            }

            // Persistir localmente las URLs públicas de Cloudinary en items y elementos vinculados
            await _unitOfWork.SaveChangesAsync();

            // Mostrar reporte de carga
            string report =
                $"Reporte de carga de imágenes:\n"
                + $"- Nuevas subidas a Cloudinary: {imageReport.Uploaded}\n"
                + $"- URLs ya existentes conservadas: {imageReport.ReusedUrls}\n"
                + $"- Archivos no encontrados: {imageReport.FileNotFound}\n"
                + $"- Actividades sin imagen: {imageReport.WithoutImage}\n"
                + (
                    activitiesWithPendingImages.Count > 0
                        ? $"- Actividades con imágenes pendientes (se conservan las publicadas y se reintentará al guardar): {string.Join(", ", activitiesWithPendingImages)}\n"
                        : string.Empty
                )
                + $"\nDetalles de ejecuciones:\n"
                + (
                    imageReport.Details.Count > 0
                        ? string.Join("\n", imageReport.Details)
                        : "Sincronización de imágenes finalizada. No se requirieron nuevas cargas."
                );

            MessageBox.Show(
                report,
                "Diagnóstico de Imágenes",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            // Sin respuesta del servidor no se envió nada: las actividades quedan pendientes
            bool apiSyncSuccess = serverReachable;
            if (apiTasks.Any())
            {
                var apiResults = await Task.WhenAll(apiTasks);
                if (apiResults.Any(success => !success))
                {
                    apiSyncSuccess = false;
                }
            }

            if (apiSyncSuccess)
            {
                _modifiedActivityCodes.Clear();

                // Las actividades con imágenes sin subir siguen pendientes para reintentarlas en el próximo guardado
                foreach (var code in activitiesWithPendingImages)
                {
                    _modifiedActivityCodes[code] = 0;
                }
                PersistPendingActivities();

                MessageBox.Show(
                    "Presupuesto guardado y sincronizado correctamente.",
                    "Guardado Exitoso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            else
            {
                ShowNetworkError = true;
                NetworkErrorMessage =
                    "Los cambios locales se guardaron, pero falló la sincronización con la API web.";

                MessageBox.Show(
                    "Los cambios se guardaron localmente, pero NO se pudieron sincronizar con el servidor.\n\nVerifique que el servicio esté encendido y vuelva a presionar 'Guardar Modelo'.",
                    "Sincronización Pendiente",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }
        catch (Exception ex)
        {
            ShowNetworkError = true;
            NetworkErrorMessage = $"Fallo en la comunicación con la API externa: {ex.Message}";
            MessageBox.Show(
                $"No se pudo guardar el presupuesto localmente: {ex.Message}",
                "Error de Guardado",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            IsLoading = false;
        }
    }



    /// <summary>
    /// Manejador de evento invocado cuando el usuario cambia la selección de elementos en Revit.
    /// Sincroniza la selección en la lista de elementos no asignados.
    /// </summary>
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection)
            return;

        // Extraer los identificadores como strings de forma síncrona en el hilo de Revit
        var selectedIdsStr = e.SelectedIds.Select(id => id.ToString()).ToHashSet();

        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                _isSyncingSelection = true;
                object? firstSelected = null;
                foreach (var item in UnassignedElements)
                {
                    item.IsSelected = selectedIdsStr.Contains(item.ElementId);
                    if (item.IsSelected && firstSelected == null)
                    {
                        firstSelected = item;
                    }
                }
                if (firstSelected != null)
                {
                    ElementSelectedForScroll?.Invoke(this, firstSelected);
                }
            }
            catch
            {
                // Ignorar excepciones al intentar actualizar selección
            }
            finally
            {
                _isSyncingSelection = false;
            }
        });
    }

    /// <summary>
    /// Sincroniza la selección de elementos de la UI hacia la selección en la vista activa de Revit.
    /// </summary>
    public void SyncSelectionToRevit()
    {
        if (_isSyncingSelection)
            return;

        _isSyncingSelection = true;
        try
        {
            var selectedIds = UnassignedElements
                .Where(e => e.IsSelected)
                .Select(e =>
                {
                    if (long.TryParse(e.ElementId, out long idValue))
                    {
                        return new ElementId(idValue);
                    }
#pragma warning disable CS8603 // Posible tipo de valor devuelto de referencia nulo
                    return null;
#pragma warning restore CS8603 // Posible tipo de valor devuelto de referencia nulo
                })
                .Where(id => id != null)
                .ToList();

            RevitEventExecutor.Execute(app =>
            {
                try
                {
                    if (app.ActiveUIDocument is { } uidoc)
                    {
                        uidoc.Selection.SetElementIds(selectedIds);
                    }
                }
                catch
                {
                    // Ignorar excepciones de selección
                }
            });
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    /// <summary>
    /// Manejador de eventos que responde a modificaciones físicas o eliminaciones en el documento de Revit.
    /// Elimina de la UI los elementos no asignados que fueron borrados, y busca/libera de la DB los elementos enlazados que hayan sido eliminados en Revit.
    /// </summary>
    private void OnDocumentChanged(
        object? sender,
        Autodesk.Revit.DB.Events.DocumentChangedEventArgs e
    )
    {
        var deletedIds = e.GetDeletedElementIds();
        if (deletedIds.Count == 0)
            return;

        // Obtener los identificadores eliminados como strings inmediatamente en el hilo de Revit
        var deletedIdStrings = deletedIds.Select(id => id.ToString()).ToList();

        // 1. Quitar elementos no asignados de la UI que hayan sido eliminados en el modelo
        _dispatcher.InvokeAsync(() =>
        {
            var elementsToRemove = UnassignedElements
                .Where(item => deletedIdStrings.Contains(item.ElementId))
                .ToList();

            foreach (var item in elementsToRemove)
            {
                UnassignedElements.Remove(item);
            }
        });

        // 2. Comprobar si algún elemento enlazado en el presupuesto activo fue eliminado en Revit
        var activeBudget = _currentBudget;
        if (activeBudget == null)
            return;

        // Correr la comprobación en base de datos de manera asíncrona para no bloquear el hilo de Revit
        _ = Task.Run(async () =>
        {
            try
            {
                // Obtener todos los ítems de presupuesto para el presupuesto actual
                var budgetItems = await _unitOfWork.BudgetItems.FindAsync(bi =>
                    bi.BudgetId == activeBudget.Id
                );
                var budgetItemIds = budgetItems.Select(bi => bi.Id).ToList();
                if (budgetItemIds.Count == 0)
                    return;

                // Obtener todos los elementos vinculados a estos ítems
                var linkedElements = await _unitOfWork.LinkedElements.FindAsync(le =>
                    budgetItemIds.Contains(le.BudgetItemId)
                );
                if (!linkedElements.Any())
                    return;

                // Consultar en Revit qué elementos ya no existen
                var tcs =
                    new TaskCompletionSource<System.Collections.Generic.List<Core.Domain.Entities.LinkedElement>>();
                RevitEventExecutor.Execute(app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null)
                    {
                        tcs.SetResult(
                            new System.Collections.Generic.List<Core.Domain.Entities.LinkedElement>()
                        );
                        return;
                    }

                    var missing =
                        new System.Collections.Generic.List<Core.Domain.Entities.LinkedElement>();
                    foreach (var le in linkedElements)
                    {
                        try
                        {
                            var elem = doc.GetElement(le.RevitUniqueId);
                            if (elem == null)
                            {
                                missing.Add(le);
                            }
                        }
                        catch
                        {
                            missing.Add(le);
                        }
                    }
                    tcs.SetResult(missing);
                });

                var missingElements = await tcs.Task;
                if (missingElements.Count == 0)
                    return;

                System.Diagnostics.Debug.WriteLine(
                    $"[CuadroCantidades][Limpieza] Se detectaron {missingElements.Count} elementos vinculados que fueron eliminados de Revit."
                );

                // Agrupar los elementos enlazados huérfanos por su ítem de presupuesto
                var groupedByBudgetItem = missingElements.GroupBy(le => le.BudgetItemId);

                foreach (var group in groupedByBudgetItem)
                {
                    var budgetItem = budgetItems.FirstOrDefault(bi => bi.Id == group.Key);
                    if (budgetItem == null)
                        continue;

                    var groupElements = group.ToList();
                    var groupUniqueIds = groupElements.Select(le => le.RevitUniqueId).ToList();

                    // Release usage through service
                    await _elementAvailabilityService.ReleaseUsagesAsync(groupUniqueIds);

                    // Get remaining linked elements for this activity
                    var remaining = await _unitOfWork.LinkedElements.FindAsync(le =>
                        le.BudgetItemId == budgetItem.Id
                    );
                    var remainingList = remaining.ToList();
                    // Publish event to trigger preview generation and quantity recalculation
                    _eventAggregator.Publish(
                        new ActivityChangedEvent(
                            activeBudget.Id,
                            budgetItem.Code,
                            remainingList.Select(le => le.RevitUniqueId).ToList(),
                            ActivityChangeType.ElementsRemoved
                        )
                    );
                }

                // Force save the structure context state after updates
                _autoSaveService.RequestSave(immediate: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CuadroCantidades][Error] Error al procesar eliminacion de elementos del modelo: {ex.Message}"
                );
            }
        });
    }

    /// <summary>
    /// Comando asíncrono para asignar los elementos no asignados seleccionados en la UI a la actividad de presupuesto activa.
    /// Registra el uso de los elementos, recalcula la cantidad total y dispara la actualización de previsualización.
    /// </summary>
    [RelayCommand]
    private async Task AssignElementsAsync()
    {
        if (IsReadOnlyMode)
        {
            MessageBox.Show(
                "Esta estructura se encuentra en modo lectura porque pertenece a otro modelo de Revit. No se pueden realizar modificaciones ni asignar elementos.",
                "Modo Lectura Activo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        try
        {
            var duplicateCodes = BudgetRows
                .Where(r => IsActivity(r) && !string.IsNullOrWhiteSpace(r.Code))
                .GroupBy(r => r.Code)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateCodes.Count > 0)
            {
                MessageBox.Show(
                    $"No se permite agregar elementos porque se ha detectado duplicidad en los siguientes códigos de actividad:\n\n{string.Join("\n", duplicateCodes)}\n\nPor favor, corrija este inconveniente en el presupuesto antes de continuar.",
                    "Alerta de Duplicidad de Códigos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            if (SelectedBudgetRow is null || !IsActivity(SelectedBudgetRow))
            {
                MessageBox.Show(
                    "Debes seleccionar una ACTIVIDAD en la tabla para asignar los elementos.",
                    "Asignar Elementos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            var elementsToAssign = UnassignedElements.Where(e => e.IsSelected).ToList(); // Prevenir excepción de modificación concurrente
            if (elementsToAssign.Count == 0)
            {
                MessageBox.Show(
                    "No hay elementos seleccionados para asignar.",
                    "Asignar Elementos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            if (_currentBudget == null)
            {
                MessageBox.Show(
                    "Por favor seleccione una estructura antes de asignar elementos.",
                    "Asignar Elementos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            string targetUnit = SelectedBudgetRow.Unit?.Trim().ToUpperInvariant() ?? string.Empty;

            // Validar compatibilidad de categoría con la unidad de la actividad
            foreach (var el in elementsToAssign)
            {
                string cat = el.Category?.Trim().ToUpperInvariant() ?? string.Empty;
                bool isCompatible = true;

                if (cat.Contains("WALL") || cat.Contains("MURO"))
                {
                    isCompatible = (targetUnit == "M2" || targetUnit == "M²" || 
                                    targetUnit == "ML" || targetUnit == "M" || 
                                    targetUnit == "UND" || targetUnit == "UN" ||
                                    targetUnit == "M3" || targetUnit == "M³");
                }
                else if (cat.Contains("FLOOR") || cat.Contains("SUELO"))
                {
                    isCompatible = (targetUnit == "M3" || targetUnit == "M³" || 
                                    targetUnit == "M2" || targetUnit == "M²" || 
                                    targetUnit == "UND" || targetUnit == "UN");
                }

                if (!isCompatible)
                {
                    MessageBox.Show(
                        $"Incompatibilidad de unidad: No puedes agregar elementos de la categoría '{el.Category}' a una actividad configurada en '{SelectedBudgetRow.Unit}'.",
                        "Asignación bloqueada",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return; // Abortar la asignación
                }
            }

            // Obtener el ID interno de la base de datos correspondiente a la fila de la vista por código
            var items = await _unitOfWork.BudgetItems.FindAsync(i =>
                i.BudgetId == _currentBudget.Id && i.Code == SelectedBudgetRow.Code
            );
            var budgetItem = items.FirstOrDefault();

            if (budgetItem == null)
            {
                MessageBox.Show(
                    "Primero debes guardar el modelo para poder asignar elementos a esta nueva actividad.",
                    "Asignar Elementos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            var elementsTuple = elementsToAssign
                .Select(e => (e.UniqueId, e.Category, e.Name, e.ElementDataJson))
                .ToList();

            // Registrar los elementos como asignados a la actividad en la base de datos
            await _elementAvailabilityService.RegisterUsagesAsync(budgetItem.Id, elementsTuple);

            // Obtener todos los elementos vinculados activos para incluirlos en el proceso de captura de pantalla
            var allLinkedElements = await _unitOfWork.LinkedElements.FindAsync(le =>
                le.BudgetItemId == budgetItem.Id
            );
            var allUniqueIds = allLinkedElements.Select(le => le.RevitUniqueId).ToList();

            _eventAggregator.Publish(
                new ActivityChangedEvent(
                    _currentBudget.Id,
                    budgetItem.Code,
                    allUniqueIds,
                    ActivityChangeType.ElementsAdded
                )
            );

            // Actualizar la previsualización en la UI
            foreach (var el in elementsToAssign)
            {
                SelectedBudgetRow.AssignedElementsPreview.Add(new ElementItem
                {
                    UniqueId = el.UniqueId,
                    Category = el.Category,
                    Name = el.Name,
                    IsSelected = false
                });
            }

            _autoSaveService.RequestSave(immediate: true);

            _dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    $"Se han asignado {elementsToAssign.Count} nuevos elementos a la actividad {budgetItem.Code}.",
                    "Asignación Exitosa",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    $"Ocurrió un error crítico: {ex.Message}\n\n{ex.StackTrace}",
                    "Error Fatal Controlado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            });
        }
    }

    /// <summary>
    /// Comando asíncrono para abrir el detalle de elementos y visualización dinámica de la actividad seleccionada.
    /// </summary>
    [RelayCommand]
    private async Task ShowDetailsAsync()
    {
        if (SelectedBudgetRow is null || !IsActivity(SelectedBudgetRow))
        {
            MessageBox.Show(
                "Debes seleccionar una ACTIVIDAD para ver sus detalles.",
                "Detalles",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        if (_currentBudget == null)
            return;

        var items = await _unitOfWork.BudgetItems.FindAsync(i =>
            i.BudgetId == _currentBudget.Id && i.Code == SelectedBudgetRow.Code
        );
        var budgetItem = items.FirstOrDefault();

        if (budgetItem == null)
            return;

        var linkedElements = await _unitOfWork.LinkedElements.FindAsync(le =>
            le.BudgetItemId == budgetItem.Id
        );

        _dispatcher.Invoke(() =>
        {
            // Sincronizar datos de la imagen y previsualización con la fila de la interfaz
            SelectedBudgetRow.PreviewImagePath = budgetItem.PreviewImagePath;
            SelectedBudgetRow.LastPreviewUpdate = budgetItem.LastPreviewUpdate;
            SelectedBudgetRow.ElementIdsSnapshot = budgetItem.ElementIdsSnapshot;
            SelectedBudgetRow.PreviewImagesJson = budgetItem.PreviewImagesJson;

            var captureService =
                AppServiceProvider.ServiceProvider!.GetRequiredService<IRevitImageCaptureService>();
            var debugImageStorageService =
                AppServiceProvider.ServiceProvider!.GetRequiredService<IPreviewDebugImageStorageService>();

            // Buscar si ya existe una ventana de detalles abierta para esta actividad
            var existingWindow = System.Windows.Application.Current.Windows
                .OfType<CQEING_CuadroCantidades.UI.Views.DynamicTableWindow>()
                .FirstOrDefault(w => w.DataContext is CQEING_CuadroCantidades.UI.ViewModels.DynamicTableViewModel vm && vm.ActivityBudgetRow.Code == SelectedBudgetRow.Code);

            if (existingWindow != null)
            {
                if (existingWindow.WindowState == System.Windows.WindowState.Minimized)
                {
                    existingWindow.WindowState = System.Windows.WindowState.Normal;
                }
                existingWindow.Activate();
                return;
            }

            // Instanciar el modelo de vista para la ventana de detalles dinámicos
            var dynamicViewModel = new DynamicTableViewModel(
                _currentBudget.Id,
                SelectedBudgetRow,
                linkedElements,
                _unitOfWork,
                captureService,
                debugImageStorageService,
                _autoSaveService,
                _eventAggregator,
                _elementAvailabilityService,
                _sessionContext,
                _manualPreviewService,
                _activityPreviewUpdater,
                _previewRefreshNotifier,
                _previewRefreshService,
                _previewImageLoaderService,
                _uiDispatcherService,
                _apiClient,
                _currentBudget.VersionName
            );
            dynamicViewModel.IsReadOnly = IsReadOnlyMode;
            var window = new DynamicTableWindow(dynamicViewModel);

            SetWindowOwner(window);
            window.Closed += (s, e) =>
            {
                if (_uiApplication != null && _uiApplication.MainWindowHandle != IntPtr.Zero)
                {
                    SetForegroundWindow(_uiApplication.MainWindowHandle);
                }
            };

            window.Show();
        });
    }

    /// <summary>
    /// Comando asíncrono que abre la ventana de selección de estructura para cargar otra versión de presupuesto.
    /// </summary>
    [RelayCommand]
    private async Task SelectStructureAsync()
    {
        if (CurrentBudget != null)
        {
            MessageBox.Show(
                "Ya tiene una estructura abierta en la sesión actual.\nPor favor, limpie la estructura actual usando el botón 'Limpiar Todo' antes de cargar otra.",
                "Estructura ya abierta",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        try
        {
            var selectViewModel = new SelectStructureViewModel(
                _unitOfWork,
                _projectContext,
                _apiClient
            );
            var window = new SelectStructureWindow(selectViewModel);

            SetWindowOwner(window);
            window.Closed += (s, e) =>
            {
                if (_uiApplication != null && _uiApplication.MainWindowHandle != IntPtr.Zero)
                {
                    SetForegroundWindow(_uiApplication.MainWindowHandle);
                }
            };

            if (window.ShowDialog() == true && selectViewModel.SelectedBudget != null)
            {
                var selectedBudget = selectViewModel.SelectedBudget;
                var doc = _uiApplication.ActiveUIDocument?.Document;
                if (doc != null)
                {
                    string activeModelId = RevitModelIdentifier.GetModelUniqueId(doc);
                    string? linkedModelId = selectedBudget.RevitModelId;

                    if (!string.IsNullOrEmpty(linkedModelId) && !CQEING_CuadroCantidades.Services.RevitModelIdentifier.IsSameOrCompatibleModelId(linkedModelId, activeModelId))
                    {
                        // Se debe usar el primer endpoint para verificar si ese presupuesto tiene asociado otro modelo
                        bool showWarning = false;
                        IsLoading = true;
                        try
                        {
                            var budgetWithModel = await _apiClient.GetBudgetByRevitModelAsync(linkedModelId);
                            if (budgetWithModel != null)
                            {
                                showWarning = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SelectStructure] Error al verificar modelo asociado: {ex.Message}");
                        }
                        finally
                        {
                            IsLoading = false;
                        }

                        // Si el servidor confirma la asociación, o localmente ya tenemos la vinculación cargada:
                        if (showWarning || !string.IsNullOrEmpty(selectedBudget.RevitModelId))
                        {
                            var result = MessageBox.Show(
                                $"Este presupuesto ya está asociado a otro modelo de Revit (ID: {linkedModelId}).\n" +
                                "¿Desea cargarlo de todas formas en modo lectura?",
                                "Presupuesto Asociado a Otro Modelo",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning
                            );
                            if (result != MessageBoxResult.Yes)
                            {
                                return;
                            }
                        }
                    }
                }

                await LoadBudgetAsync(
                    selectedBudget.Id,
                    selectedBudget.VersionName
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ocurrió un error al intentar cargar la estructura:\n\n{ex.Message}",
                "Error de Carga",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    /// <summary>
    /// Comando asíncrono para recargar los datos del presupuesto actual desde la base de datos local.
    /// </summary>
    [RelayCommand]
    private async Task UpdateStructureAsync()
    {
        if (_currentBudget == null)
        {
            MessageBox.Show(
                "No hay ninguna estructura cargada para actualizar.",
                "Actualizar Estructura",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        IsLoading = true;
        ShowNetworkError = false;
        try
        {
            // Sincronizar estructura desde la API web (Phase 2/Task 4 requirement)
            var structure = await _apiClient.GetBudgetStructureAsync(_currentBudget.VersionName);
            if (structure == null)
            {
                ShowNetworkError = true;
                NetworkErrorMessage =
                    "No se pudo sincronizar la estructura con la API web. Cargando local.";
            }
        }
        catch (Exception ex)
        {
            ShowNetworkError = true;
            NetworkErrorMessage = $"Error de red al actualizar estructura: {ex.Message}";
        }
        finally
        {
            await LoadBudgetAsync(_currentBudget.Id, _currentBudget.VersionName);
            IsLoading = false;
        }
    }

    /// <summary>
    /// Carga la estructura del presupuesto desde un archivo de Excel local.
    /// </summary>
    /// <param name="filePath">Ruta física del archivo Excel.</param>
    private void LoadStructure(string filePath)
    {
        try
        {
            IReadOnlyList<BudgetRow> rows = ExcelStructureReader.ReadBudgetRows(filePath);
            BudgetRows.Clear();

            foreach (BudgetRow row in rows)
            {
                BudgetRows.Add(row);
            }

            SelectedBudgetRow = BudgetRows.FirstOrDefault();
            LastStructurePath = filePath;
            LoadedStructureName = Path.GetFileName(filePath);
            _autoSaveService.RequestSave(immediate: true);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            MessageBox.Show(
                $"No se pudo cargar la estructura seleccionada.\n\n{exception.Message}",
                "Error al cargar estructura",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    /// <summary>
    /// Limpia los metadatos y la sesión activa en el contexto del proyecto.
    /// </summary>
    private void ClearCurrentSession()
    {
        System.Diagnostics.Debug.WriteLine(
            $"[CuadroCantidades][Limpieza] Limpiando metadatos de sesion activa. Hora: {DateTime.Now}"
        );

        var documentPath = _projectContext.RevitDocumentPath;
        _projectContext.Clear();
        _projectContext.RevitDocumentPath = documentPath;

        CurrentBudget = null;
        LastStructurePath = null;
        IsReadOnlyMode = false;
    }

    /// <summary>
    /// Restablece el estado visual y los campos de entrada de la interfaz de usuario.
    /// </summary>
    private void ResetVisualState()
    {
        System.Diagnostics.Debug.WriteLine(
            $"[CuadroCantidades][Limpieza] Restableciendo interfaz de usuario visible. Hora: {DateTime.Now}"
        );

        _dispatcher.Invoke(() =>
        {
            BudgetRows.Clear();
            SelectedBudgetRow = null;
            LoadedStructureName = "Sin estructura cargada";
            _projectContext.LoadedStructureName = LoadedStructureName;
        });
    }

    /// <summary>
    /// Comando para limpiar la estructura cargada actualmente en la vista y en el AppState.
    /// </summary>
    [RelayCommand]
    private async Task ClearStructure()
    {
        if (_currentBudget == null)
        {
            MessageBox.Show(
                "No hay ninguna estructura cargada para limpiar.",
                "Operación no permitida",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            "Esto quitara todos los capitulos, subcapitulos y actividades del panel de control.\n\nLos datos guardados en la base de datos NO se eliminaran y podras volver a cargarlos en cualquier momento.",
            "Limpiar Todo",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning
        );
        if (result != MessageBoxResult.OK)
            return;

        try
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CuadroCantidades][Limpieza] Iniciando 'Limpiar Todo'. Hora: {DateTime.Now}"
            );

            // 1. Suspender el autoguardado para proteger la base de datos de eliminaciones accidentales
            _autoSaveService.Suspend();

            // 2. Limpiar referencias del contexto de la sesión actual
            ClearCurrentSession();

            // Limpiar todo el contenido de la base de datos local en memoria
            var allLinked = await _unitOfWork.LinkedElements.GetAllAsync();
            _unitOfWork.LinkedElements.RemoveRange(allLinked);

            var allItems = await _unitOfWork.BudgetItems.GetAllAsync();
            _unitOfWork.BudgetItems.RemoveRange(allItems);

            var allBudgets = await _unitOfWork.Budgets.GetAllAsync();
            _unitOfWork.Budgets.RemoveRange(allBudgets);

            var allProjects = await _unitOfWork.Projects.GetAllAsync();
            _unitOfWork.Projects.RemoveRange(allProjects);

            await _unitOfWork.SaveChangesAsync();

            // Limpiar el tracker del DbContext para descartar cualquier cambio pendiente que haya quedado acumulado en memoria
            _unitOfWork.ClearTracker();

            // 3. Restablecer la visualización y las colecciones observables
            ResetVisualState();

            // Reanudar el autoguardado para permitir registrar cambios en el futuro
            _autoSaveService.Resume();

            // 4. Persistir el estado limpio (BudgetId vacío) de forma asíncrona en AppState
            _ = Task.Run(async () =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[CuadroCantidades][Guardado] Persistiendo sesion limpia en AppState JSON..."
                    );
                    await _stateService.SaveStateAsync();
                    System.Diagnostics.Debug.WriteLine(
                        "[CuadroCantidades][Guardado] Estado limpio persistido."
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[CuadroCantidades][Error] Error al persistir estado limpio: {ex.Message}"
                    );
                }
            });

            MessageBox.Show(
                "La sesion se ha limpiado correctamente. Los datos en la base de datos permanecen seguros.",
                "Limpiar Todo",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CuadroCantidades][Error] Error en 'Limpiar Todo': {ex.Message}"
            );
        }
        finally
        {
            // 5. Reanudar el autoguardado
            _autoSaveService.Resume();
            System.Diagnostics.Debug.WriteLine("[CuadroCantidades][Limpieza] Autosave reanudado.");
        }
    }

    /// <summary>
    /// Comando para limpiar todos los elementos vinculados de la estructura, manteniendo las actividades visibles.
    /// </summary>
    [RelayCommand]
    private async Task ClearElementsAsync()
    {
        if (IsReadOnlyMode)
        {
            MessageBox.Show(
                "La estructura está en modo lectura porque pertenece a otro modelo de Revit. No se pueden desvincular elementos.",
                "Modo Lectura Activo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        if (_currentBudget == null)
        {
            MessageBox.Show(
                "No hay ninguna estructura cargada para limpiar.",
                "Operación no permitida",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        var result = MessageBox.Show(
            "¿Estás seguro de que deseas limpiar la estructura?\n\nEsto desvinculará todos los elementos de todas las actividades del presupuesto actual, pero mantendrá la estructura visible. Esta acción no se puede deshacer.",
            "Limpiar Estructura",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            // 1. Obtener todos los ítems de presupuesto para el presupuesto actual
            var items = await _unitOfWork.BudgetItems.FindAsync(i => i.BudgetId == _currentBudget.Id);
            var itemIds = items.Select(i => i.Id).ToList();

            // Marcar todas las actividades como modificadas para asegurar su sincronización a vacío en la base de datos
            // Y vaciar sus imágenes históricas en la base de datos para que la ventana Detalles no las cargue después.
            foreach (var item in items.Where(i => i.ItemType == "ACTIVIDAD" && !string.IsNullOrWhiteSpace(i.Code)))
            {
                MarkActivityModified(item.Code);
                
                item.PreviewImagePath = null;
                item.LastPreviewUpdate = null;
                item.ElementIdsSnapshot = null;
                item.PreviewImagesJson = null;
                _unitOfWork.BudgetItems.Update(item);
            }

            // 2. Obtener todos los elementos vinculados para estos ítems
            var linkedElements = await _unitOfWork.LinkedElements.FindAsync(le => itemIds.Contains(le.BudgetItemId));
            var linkedElementsList = linkedElements.ToList();

            if (linkedElementsList.Count > 0)
            {
                var uniqueIds = linkedElementsList.Select(le => le.RevitUniqueId).Distinct().ToList();

                // 3. Eliminar los elementos vinculados de la base de datos
                _unitOfWork.LinkedElements.RemoveRange(linkedElementsList);

                // 4. Notificar que estos elementos vuelven a estar disponibles
                _eventAggregator.Publish(new ElementAvailabilityChangedEvent(uniqueIds, isAvailable: true));
            }
            
            // Guardar tanto los BudgetItems modificados como los LinkedElements eliminados
            await _unitOfWork.SaveChangesAsync();

            // 5. Reiniciar las cantidades a vacío en la UI
            foreach (var row in BudgetRows)
            {
                row.IsExpanded = false; // Colapsar TODO (capítulos, subcapítulos, actividades)
                
                if (IsActivity(row))
                {
                    row.Quantity = "";
                    row.ElementIdsSnapshot = null;
                    row.PreviewImagePath = null;
                    row.LastPreviewUpdate = null;
                    row.PreviewImagesJson = null;
                    
                    row.AssignedElementsPreview?.Clear(); // Limpiar preview
                    row.AssignedElementsTable = null; // Limpiar datatable
                }
            }
            
            SelectedBudgetRow = null; // Limpiar selección

            // Limpiar cualquier ventana de Detalles que esté abierta para que desaparezca el historial inmediatamente
            var dynamicWindows = System.Windows.Application.Current.Windows.OfType<CQEING_CuadroCantidades.UI.Views.DynamicTableWindow>();
            foreach (var win in dynamicWindows)
            {
                if (win.DataContext is CQEING_CuadroCantidades.UI.ViewModels.DynamicTableViewModel vm)
                {
                    vm.Snapshots.Clear();
                    vm.SelectedSnapshot = null;
                }
            }

            // 6. Recalcular los totales de los capítulos
            CalculateChapterTotals();

            // 7. Solicitar el autoguardado automático inmediato
            _autoSaveService.RequestSave(immediate: true);

            MessageBox.Show(
                "Se han desvinculado todos los elementos correctamente. La estructura permanece visible.",
                "Limpieza Completada",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error al limpiar la estructura: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    /// <summary>
    /// Comando para exportar la estructura y cantidades actuales del presupuesto a un libro de Excel.
    /// </summary>
    [RelayCommand]
    private void ExportStructure()
    {
        if (BudgetRows.Count == 0)
        {
            MessageBox.Show(
                "No hay estructura para exportar.",
                "Exportar",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        string exportFileName = "Cuadro_de_Cantidades";
        if (_currentBudget != null && !string.IsNullOrWhiteSpace(_currentBudget.VersionName))
        {
            exportFileName += $"_{_currentBudget.VersionName}";
        }
        exportFileName += ".xlsx";

        var dialog = new SaveFileDialog
        {
            Title = "Exportar cuadro de cantidades",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = exportFileName,
            AddExtension = true,
            DefaultExt = ".xlsx",
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            // Escribir las filas en el archivo de Excel especificado
            ExcelStructureWriter.WriteBudgetRows(dialog.FileName, BudgetRows);
            MessageBox.Show(
                "La estructura se exporto correctamente.",
                "Exportar",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (IOException exception)
        {
            MessageBox.Show(
                $"No se pudo exportar el archivo.\n\n{exception.Message}",
                "Error al exportar",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    /// <summary>
    /// Comando para expandir/contraer capítulos y subcapítulos en el accordion de la estructura.
    /// Alterna la visibilidad de las filas hijas sin modificar datos ni lógica de negocio.
    /// </summary>
    [RelayCommand]
    private void ToggleExpand(BudgetRow row)
    {
        if (row == null) return;

        row.IsExpanded = !row.IsExpanded;

        if (IsChapter(row))
        {
            int chapterIdx = BudgetRows.IndexOf(row);
            int endIdx = FindChapterEndIndex(row);

            for (int i = chapterIdx + 1; i < endIdx; i++)
            {
                var child = BudgetRows[i];
                if (row.IsExpanded)
                {
                    // Al expandir capítulo: mostrar subcapítulos, actividades según estado del subcapítulo padre
                    if (IsSubchapter(child))
                    {
                        child.IsRowVisible = true;
                    }
                    else if (IsActivity(child))
                    {
                        // Buscar el subcapítulo padre de esta actividad
                        BudgetRow? parentSub = null;
                        for (int j = i - 1; j > chapterIdx; j--)
                        {
                            if (IsSubchapter(BudgetRows[j]))
                            {
                                parentSub = BudgetRows[j];
                                break;
                            }
                        }
                        child.IsRowVisible = parentSub?.IsExpanded ?? true;
                    }
                }
                else
                {
                    // Al contraer capítulo: ocultar todo
                    child.IsRowVisible = false;
                }
            }
        }
        else if (IsSubchapter(row))
        {
            int subIdx = BudgetRows.IndexOf(row);
            int endIdx = FindSubchapterEndIndex(row);

            for (int i = subIdx + 1; i < endIdx; i++)
            {
                BudgetRows[i].IsRowVisible = row.IsExpanded;
            }
        }
    }

    /// <summary>
    /// Busca el índice donde termina el capítulo especificado (es decir, el inicio del siguiente capítulo o el fin de la colección).
    /// </summary>
    private int FindChapterEndIndex(BudgetRow chapter)
    {
        int chapterIndex = BudgetRows.IndexOf(chapter);
        for (int index = chapterIndex + 1; index < BudgetRows.Count; index++)
        {
            if (IsChapter(BudgetRows[index]))
                return index;
        }
        return BudgetRows.Count;
    }

    /// <summary>
    /// Busca el índice donde termina el subcapítulo especificado (es decir, el inicio del siguiente capítulo/subcapítulo o el fin de la colección).
    /// </summary>
    private int FindSubchapterEndIndex(BudgetRow subchapter)
    {
        int subchapterIndex = BudgetRows.IndexOf(subchapter);
        for (int index = subchapterIndex + 1; index < BudgetRows.Count; index++)
        {
            if (IsChapter(BudgetRows[index]) || IsSubchapter(BudgetRows[index]))
                return index;
        }
        return BudgetRows.Count;
    }

    /// <summary>
    /// Comprueba si la fila es de tipo capítulo.
    /// </summary>
    private static bool IsChapter(BudgetRow row) =>
        row.Type.Equals("CAPITULO", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Comprueba si la fila es de tipo subcapítulo.
    /// </summary>
    private static bool IsSubchapter(BudgetRow row) =>
        row.Type.Equals("SUBCAPITULO", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Comprueba si la fila es de tipo actividad.
    /// </summary>
    private static bool IsActivity(BudgetRow row) =>
        row.Type.Equals("ACTIVIDAD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Calcula las sumas totales de cantidades de abajo hacia arriba (Actividades -> Subcapítulos -> Capítulos).
    /// </summary>
    private void CalculateChapterTotals()
    {
        // 1. Calcular cantidades acumuladas para subcapítulos (sumando sus actividades hijas)
        for (int i = BudgetRows.Count - 1; i >= 0; i--)
        {
            var row = BudgetRows[i];
            if (row.Level == 1) // Subcapítulo
            {
                int end = FindSubchapterEndIndex(row);
                double sum = 0;
                for (int j = i + 1; j < end; j++)
                {
                    if (
                        BudgetRows[j].Level == 2
                        && double.TryParse(BudgetRows[j].Quantity, out double q)
                    )
                    {
                        sum += q;
                    }
                }
                row.Quantity = sum > 0 ? sum.ToString("0.00") : "";
            }
        }

        // 2. Calcular cantidades acumuladas para capítulos (sumando sus subcapítulos hijos)
        for (int i = BudgetRows.Count - 1; i >= 0; i--)
        {
            var row = BudgetRows[i];
            if (row.Level == 0) // Capítulo
            {
                int end = FindChapterEndIndex(row);
                double sum = 0;
                for (int j = i + 1; j < end; j++)
                {
                    if (
                        BudgetRows[j].Level == 1
                        && double.TryParse(BudgetRows[j].Quantity, out double q)
                    )
                    {
                        sum += q;
                    }
                }
                row.Quantity = sum > 0 ? sum.ToString("0.00") : "";
            }
        }
    }

    /// <summary>
    /// Manejador de evento del agregador para responder a cambios de disponibilidad en elementos de Revit.
    /// Agrega los elementos que se han liberado y quita los que se han asignado de la colección de no asignados en UI.
    /// </summary>
    private void OnElementAvailabilityChanged(ElementAvailabilityChangedEvent e)
    {
        if (e.IsAvailable)
        {
            RevitEventExecutor.Execute(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                    return;

                var elementsToAdd = new List<ElementItem>();
                foreach (var uniqueId in e.RevitUniqueIds)
                {
                    try
                    {
                        var element = doc.GetElement(uniqueId);
                        if (element == null || element.Category == null)
                            continue;

                        bool isRebar = element.Category.Id.Value == (long)BuiltInCategory.OST_Rebar;
                        bool isStairs = element.Category.Id.Value == (long)BuiltInCategory.OST_Stairs;
                        // El refuerzo de área no tiene cantidades de material, pero su contorno es el M2 del acero de losas
                        bool isAreaReinforcement = AreaReinforcementQuantityExtractor.IsAreaReinforcement(element);

                        if (!element.Category.HasMaterialQuantities && !isRebar && !isStairs && !isAreaReinforcement)
                            continue;

                        var paramDict = new Dictionary<string, double>();

                        if (isStairs)
                        {
                            double totalVolume = 0.0;
                            try
                            {
                                var matIds = element.GetMaterialIds(false);
                                foreach (var matId in matIds)
                                {
                                    if (matId != ElementId.InvalidElementId)
                                    {
                                        double matVol = element.GetMaterialVolume(matId);
                                        if (matVol > 0)
                                        {
                                            totalVolume += matVol;
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // Ignorar fallos de extracción de material
                            }
                            
                            double totalM3 = totalVolume * 0.0283168; 
                            if (totalVolume > 0)
                                paramDict["Volume"] = totalM3;
                        }
                        else
                        {
                            Parameter? volParam = element.get_Parameter(
                                BuiltInParameter.HOST_VOLUME_COMPUTED
                            );
                            if (volParam != null && volParam.HasValue)
                            {
                                paramDict["Volume"] = volParam.AsDouble() * 0.0283168;
                            }
                            else
                            {
                                Parameter? vParam = element.LookupParameter("Volume")
                                    ?? element.LookupParameter("Volumen");
                                if (vParam != null && vParam.HasValue)
                                {
                                    paramDict["Volume"] = vParam.AsDouble() * 0.0283168;
                                }
                            }
                        }

                        bool isFloor =
                            element.Category.Name.Equals(
                                "Floors",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || element.Category.Name.Equals(
                                "Suelos",
                                StringComparison.OrdinalIgnoreCase
                            );

                        // 2. Extraer el área hospedada del elemento y convertirla a metros cuadrados
                        Parameter? areaParam = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                        if (areaParam != null && areaParam.HasValue)
                        {
                            paramDict["Area"] = areaParam.AsDouble() * 0.092903; // pies cuadrados a metros cuadrados
                        }
                        else
                        {
                            Parameter? aParam = element.LookupParameter("Area")
                                ?? element.LookupParameter("Área");
                            if (aParam != null && aParam.HasValue)
                            {
                                paramDict["Area"] = aParam.AsDouble() * 0.092903;
                            }
                        }

                        // 3. Extraer dimensiones específicas según clase de elemento (muros, suelos, zapatas)
                        if (element is Wall wallInstance)
                        {
                            paramDict["Width"] = wallInstance.Width * 0.3048;
                            paramDict["Thickness"] = wallInstance.Width * 0.3048;
                        }
                        else if (element is Floor floorInstance)
                        {
                            var floorType = floorInstance.FloorType;
                            if (floorType != null)
                            {
                                var cs = floorType.GetCompoundStructure();
                                if (cs != null)
                                {
                                    paramDict["Thickness"] = cs.GetWidth() * 0.3048;
                                }
                            }
                        }

                        // 3b. Extracción específica para zapatas (Structural Foundations)
                        {
                            string catNameLower = element.Category.Name.ToLowerInvariant();
                            bool isFoundation = catNameLower.Contains("foundation") || catNameLower.Contains("zapata") || catNameLower.Contains("cimentación");
                            if (isFoundation)
                            {
                                var foundationType = element.Document.GetElement(element.GetTypeId()) as ElementType;
                                if (foundationType != null)
                                {
                                    if (!paramDict.ContainsKey("Length"))
                                    {
                                        string[] lengthNames = { "Length", "Largo", "Longitud", "Foundation Length", "l", "a" };
                                        foreach (var name in lengthNames)
                                        {
                                            var p = foundationType.LookupParameter(name);
                                            if (p != null && p.HasValue)
                                            {
                                                paramDict["Length"] = p.AsDouble() * 0.3048;
                                                break;
                                            }
                                        }
                                    }
                                    if (!paramDict.ContainsKey("Width"))
                                    {
                                        string[] widthNames = { "Width", "Ancho", "Foundation Width", "b" };
                                        foreach (var name in widthNames)
                                        {
                                            var p = foundationType.LookupParameter(name);
                                            if (p != null && p.HasValue)
                                            {
                                                paramDict["Width"] = p.AsDouble() * 0.3048;
                                                break;
                                            }
                                        }
                                    }
                                    if (!paramDict.ContainsKey("Thickness"))
                                    {
                                        string[] thickNames = { "Thickness", "Foundation Thickness", "Grosor", "Espesor", "h" };
                                        foreach (var name in thickNames)
                                        {
                                            var p = foundationType.LookupParameter(name);
                                            if (p != null && p.HasValue)
                                            {
                                                paramDict["Thickness"] = p.AsDouble() * 0.3048;
                                                break;
                                            }
                                        }
                                    }
                                }

                                if (!paramDict.ContainsKey("Length"))
                                {
                                    string[] lengthNames = { "Length", "Largo", "Longitud", "Foundation Length" };
                                    foreach (var name in lengthNames)
                                    {
                                        var p = element.LookupParameter(name);
                                        if (p != null && p.HasValue)
                                        {
                                            paramDict["Length"] = p.AsDouble() * 0.3048;
                                            break;
                                        }
                                    }
                                }
                                if (!paramDict.ContainsKey("Width"))
                                {
                                    string[] widthNames = { "Width", "Ancho", "Foundation Width" };
                                    foreach (var name in widthNames)
                                    {
                                        var p = element.LookupParameter(name);
                                        if (p != null && p.HasValue)
                                        {
                                            paramDict["Width"] = p.AsDouble() * 0.3048;
                                            break;
                                        }
                                    }
                                }

                                if (!paramDict.ContainsKey("Length") || !paramDict.ContainsKey("Width"))
                                {
                                    var bb = element.get_BoundingBox(null);
                                    if (bb != null)
                                    {
                                        double dx = Math.Abs(bb.Max.X - bb.Min.X) * 0.3048;
                                        double dy = Math.Abs(bb.Max.Y - bb.Min.Y) * 0.3048;
                                        double dz = Math.Abs(bb.Max.Z - bb.Min.Z) * 0.3048;

                                        double largo = Math.Max(dx, dy);
                                        double ancho = Math.Min(dx, dy);

                                        if (!paramDict.ContainsKey("Length"))
                                            paramDict["Length"] = Math.Round(largo, 2);
                                        if (!paramDict.ContainsKey("Width"))
                                            paramDict["Width"] = Math.Round(ancho, 2);
                                        if (!paramDict.ContainsKey("Thickness") && dz > 0.001)
                                            paramDict["Thickness"] = Math.Round(dz, 2);
                                    }
                                }
                            }
                        }

                        Parameter? lengthParam = null;
                        bool isBeam = element.Category != null && 
                                      ((BuiltInCategory)element.Category.Id.Value == BuiltInCategory.OST_StructuralFraming || 
                                       element.Category.Name.IndexOf("viga", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                       element.Category.Name.IndexOf("framing", StringComparison.OrdinalIgnoreCase) >= 0);
                        
                        if (isBeam)
                        {
                            lengthParam = element.get_Parameter(BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH)
                                ?? element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                        }
                        else
                        {
                            lengthParam = element.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)
                                ?? element.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM)
                                ?? element.get_Parameter(BuiltInParameter.STRUCTURAL_FOUNDATION_LENGTH);
                        }
                        if (lengthParam != null && lengthParam.HasValue && !paramDict.ContainsKey("Length"))
                        {
                            paramDict["Length"] = lengthParam.AsDouble() * 0.3048;
                        }

                        Parameter? widthParam = element.get_Parameter(BuiltInParameter.STRUCTURAL_FOUNDATION_WIDTH);
                        if (widthParam == null && element.GetTypeId() is { } typeId1 && element.Document.GetElement(typeId1) is ElementType typeEl1)
                        {
                            widthParam = typeEl1.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
                        }
                        if (widthParam != null && widthParam.HasValue && !paramDict.ContainsKey("Width"))
                        {
                            paramDict["Width"] = widthParam.AsDouble() * 0.3048;
                        }

                        Parameter? thicknessParam = element.get_Parameter(BuiltInParameter.STRUCTURAL_FOUNDATION_THICKNESS);
                        if (thicknessParam == null && element.GetTypeId() is { } typeId2 && element.Document.GetElement(typeId2) is ElementType typeEl2)
                        {
                            thicknessParam = typeEl2.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                        }
                        if (thicknessParam != null && thicknessParam.HasValue && !paramDict.ContainsKey("Thickness"))
                        {
                            paramDict["Thickness"] = thicknessParam.AsDouble() * 0.3048;
                        }

                        // 5. Extraer por nombres de parámetros (búsqueda dinámica)
                        void ExtractDimension(string paramName, double factor = 0.3048)
                        {
                            Parameter? p = element.LookupParameter(paramName);
                            if (p == null)
                            {
                                var elementType = element.Document.GetElement(element.GetTypeId()) as ElementType;
                                if (elementType != null)
                                    p = elementType.LookupParameter(paramName);
                            }
                            if (p != null && p.HasValue && !paramDict.ContainsKey(paramName))
                            {
                                paramDict[paramName] = p.AsDouble() * factor;
                            }
                        }

                        string[] dimParams =
                        {
                            "Longitud", "Largo", "Ancho", "Base", "b", "Height", "Altura", "h",
                            "Grosor", "Depth", "Foundation Thickness", "Espesor"
                        };
                        foreach (var dp in dimParams)
                        {
                            ExtractDimension(dp, 0.3048);
                        }

                        string[] weightParams = { "kg/m", "Mass per unit length", "Peso", "Weight" };
                        foreach (var wp in weightParams)
                        {
                            ExtractDimension(wp, 1.0);
                        }

                        // 6. Cálculos de respaldo si faltan dimensiones
                        // Barras de acero (Rebar y RebarInSystem): longitud total, kg/m y peso
                        RebarQuantityExtractor.AddQuantities(element, paramDict);

                        // Perfiles metálicos (armazón y pilares): kg/m del parámetro de peso lineal y peso
                        SteelProfileQuantityExtractor.AddQuantities(element, paramDict);

                        // Pilares: área lateral (caras verticales del sólido) para actividades en M2
                        ColumnLateralAreaExtractor.AddQuantities(element, paramDict);

                        // Columnas, vigas y zapatas: área de cada cara (ejes globales) para medir en M2 solo las caras elegidas
                        FaceAreaExtractor.AddQuantities(element, paramDict);

                        // Puertas y ventanas: área = anchura × altura para actividades en M2
                        DoorWindowDataExtractor.AddQuantities(element, paramDict);

                        // Refuerzo de área (acero de losas): área del contorno para actividades en M2
                        AreaReinforcementQuantityExtractor.AddQuantities(element, paramDict);

                        if (!paramDict.ContainsKey("Thickness") && paramDict.ContainsKey("Volume") && paramDict.ContainsKey("Area"))
                        {
                            double a = paramDict["Area"];
                            if (a > 0.0001)
                                paramDict["Thickness"] = paramDict["Volume"] / a;
                        }
                        if (!paramDict.ContainsKey("Area") && paramDict.ContainsKey("Volume") && paramDict.ContainsKey("Thickness"))
                        {
                            double t = paramDict["Thickness"];
                            if (t > 0.0001)
                                paramDict["Area"] = paramDict["Volume"] / t;
                        }

                        // 7. Redondear valores a 2 decimales y asegurar llaves bilingües (Excepto KG/M)
                        var tempDict = new System.Collections.Generic.Dictionary<string, double>();
                        foreach (var kvp in paramDict)
                        {
                            if (kvp.Key.Equals("kg/m", StringComparison.OrdinalIgnoreCase))
                            {
                                tempDict[kvp.Key] = kvp.Value; // Conservar la precisión original que entrega la API
                            }
                            else if (FaceAreaCalculator.IsAreaKey(kvp.Key))
                            {
                                tempDict[kvp.Key] = kvp.Value; // Áreas por cara con 4 decimales: se redondea solo su suma
                            }
                            else
                            {
                                tempDict[kvp.Key] = Math.Round(kvp.Value, 2);
                            }
                        }

                        void EnsureKey(string key1, string key2)
                        {
                            if (tempDict.TryGetValue(key1, out double val))
                                tempDict[key2] = val;
                            else if (tempDict.TryGetValue(key2, out double val2))
                                tempDict[key1] = val2;
                        }

                        if (!tempDict.ContainsKey("Length") && !tempDict.ContainsKey("Longitud"))
                        {
                            if (tempDict.TryGetValue("Height", out double hVal))
                            {
                                tempDict["Length"] = hVal;
                            }
                            else if (tempDict.TryGetValue("Altura", out double hVal2))
                            {
                                tempDict["Length"] = hVal2;
                            }
                        }

                        EnsureKey("Length", "Largo");
                        EnsureKey("Length", "Longitud");
                        EnsureKey("Width", "Ancho");
                        EnsureKey("Width", "Base");
                        EnsureKey("Thickness", "Grosor");
                        EnsureKey("Thickness", "Espesor");
                        EnsureKey("Volume", "Volumen");
                        EnsureKey("Area", "Área");

                        paramDict = tempDict;

                        var serializedDict = new System.Collections.Generic.Dictionary<string, object>();
                        foreach (var kvp in paramDict)
                        {
                            serializedDict[kvp.Key] = kvp.Value;
                        }
                        string lvlName = ElementLevelReader.GetLevelName(element);
                        serializedDict["Nivel"] = lvlName;

                        string? hostName = RebarQuantityExtractor.GetHostName(element);
                        if (hostName != null)
                        {
                            serializedDict["Anfitrión"] = hostName;
                        }

                        foreach (var kvp in DoorWindowDataExtractor.GetData(element))
                        {
                            serializedDict[kvp.Key] = kvp.Value;
                        }

                        foreach (var kvp in FaceAreaExtractor.GetData(element))
                        {
                            serializedDict[kvp.Key] = kvp.Value;
                        }

                        string jsonParams = System.Text.Json.JsonSerializer.Serialize(serializedDict, CQEING_CuadroCantidades.Core.JsonConfig.RelaxedOptions);

                        string customName = element.Name;
                        if (RebarQuantityExtractor.IsRebarBar(element))
                        {
                            var rebarType = element.Document.GetElement(element.GetTypeId()) as ElementType;
                            if (rebarType != null)
                            {
                                customName = rebarType.Name;
                            }
                        }

                        elementsToAdd.Add(
                            new ElementItem
                            {
                                ElementId = element.Id.ToString(),
                                UniqueId = element.UniqueId,
                                Name = customName,
                                Category = element.Category.Name,
                                ElementDataJson = jsonParams,
                                IsSelected = false,
                            }
                        );
                    }
                    catch { }
                }

                if (elementsToAdd.Count > 0)
                {
                    _dispatcher.InvokeAsync(() =>
                    {
                        foreach (var item in elementsToAdd)
                        {
                            if (!UnassignedElements.Any(ue => ue.UniqueId == item.UniqueId))
                            {
                                UnassignedElements.Add(item);
                            }
                        }
                    });
                }
            });
        }
        else
        {
            _dispatcher.InvokeAsync(() =>
            {
                foreach (var uniqueId in e.RevitUniqueIds)
                {
                    var item = UnassignedElements.FirstOrDefault(ue => ue.UniqueId == uniqueId);
                    if (item != null)
                    {
                        UnassignedElements.Remove(item);
                    }
                }
            });
        }
    }

    /// <summary>
    /// Manejador de evento del agregador que se ejecuta al modificarse los elementos de una actividad específica.
    /// Recalcula cantidades y actualiza los totales.
    /// </summary>
    private void OnActivityChangedEventReceived(ActivityChangedEvent e)
    {
        if (_currentBudget == null || e.BudgetId != _currentBudget.Id)
            return;

        MarkActivityModified(e.ActivityCode);

        // Si el evento trae los elementos (Detalles), se usa lo que ya está en memoria: consultar la base aquí
        // choca con el guardado automático, que usa el mismo contexto al mismo tiempo, y la tabla no se actualizaba.
        if (e.Elements != null)
        {
            var elements = e.Elements.ToList();
            _dispatcher.Invoke(() =>
            {
                var row = BudgetRows.FirstOrDefault(r => r.Code == e.ActivityCode);
                if (row != null)
                    ApplyActivityElements(row, row.Unit ?? string.Empty, elements);
            });
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var items = await _unitOfWork.BudgetItems.FindAsync(bi =>
                    bi.BudgetId == _currentBudget.Id && bi.Code == e.ActivityCode
                );
                var budgetItem = items.FirstOrDefault();
                if (budgetItem == null)
                    return;

                var remaining = await _unitOfWork.LinkedElements.FindAsync(le =>
                    le.BudgetItemId == budgetItem.Id
                );

                _dispatcher.Invoke(() =>
                {
                    var row = BudgetRows.FirstOrDefault(r => r.Code == e.ActivityCode);
                    if (row != null)
                        ApplyActivityElements(row, budgetItem.Unit, remaining.ToList());
                });
            }
            catch (Exception ex)
            {
                ErrorLog.Write(nameof(MainWindowViewModel), $"No se pudo actualizar la actividad {e.ActivityCode}", ex);
            }
        });
    }

    /// <summary>
    /// Marca la actividad para enviarla a la API en el próximo "Guardar Modelo" y guarda la marca en disco,
    /// para que no se pierda si Revit se cierra antes de guardar.
    /// </summary>
    private void MarkActivityModified(string activityCode)
    {
        if (_modifiedActivityCodes.TryAdd(activityCode, 0))
            PersistPendingActivities();
    }

    private void PersistPendingActivities()
    {
        if (_currentBudget != null)
            _ = _pendingSyncStore.SaveAsync(_currentBudget.Id, _modifiedActivityCodes.Keys.ToList());
    }

    /// <summary>
    /// Recupera las actividades que quedaron sin enviar en una sesión anterior.
    /// </summary>
    private async Task RestorePendingActivitiesAsync(Guid budgetId)
    {
        foreach (var code in await _pendingSyncStore.LoadAsync(budgetId))
        {
            _modifiedActivityCodes[code] = 0;
        }
    }

    /// <summary>
    /// Recalcula la cantidad de la actividad y redibuja su tabla desplegada con los elementos que tiene.
    /// </summary>
    private void ApplyActivityElements(BudgetRow row, string unit, IReadOnlyCollection<LinkedElement> elements)
    {
        row.Quantity = QuantificationEngine.CalculateTotalQuantity(unit, elements).ToString("0.00");

        row.AssignedElementsTable = UI.Utils.DynamicTableBuilder.BuildDynamicTable(elements, unit);
        row.AssignedElementsPreview.Clear();
        foreach (var el in elements)
        {
            row.AssignedElementsPreview.Add(new ElementItem
            {
                UniqueId = el.RevitUniqueId,
                Category = el.Category,
                Name = el.ElementName,
                IsSelected = false
            });
        }

        CalculateChapterTotals();
    }

    /// <summary>
    /// Manejador de evento que se dispara cuando la imagen de vista previa de una actividad se ha actualizado.
    /// Marca la actividad como modificada para asegurar que su imagen se suba a Cloudinary al guardar.
    /// </summary>
    private void OnPreviewUpdatedEventReceived(PreviewUpdatedEvent e)
    {
        if (_currentBudget == null || e.BudgetId != _currentBudget.Id)
            return;

        MarkActivityModified(e.ActivityCode);
    }

    /// <summary>
    /// Libera recursos y cancela suscripciones a eventos de la aplicación.
    /// </summary>
    public void Dispose()
    {
        DocumentLifecycleService.SelectionChanged -= OnSelectionChanged;
        DocumentLifecycleService.DocumentChanged -= OnDocumentChanged;

        if (_eventAggregator != null)
        {
            _eventAggregator.Unsubscribe<ElementAvailabilityChangedEvent>(
                OnElementAvailabilityChanged
            );
            _eventAggregator.Unsubscribe<ActivityChangedEvent>(OnActivityChangedEventReceived);
            _eventAggregator.Unsubscribe<PreviewUpdatedEvent>(OnPreviewUpdatedEventReceived);
        }

        if (_livePreviewUpdater != null)
        {
            _livePreviewUpdater.StopUpdating();
        }
    }

    [RelayCommand]
    private void DismissNetworkError()
    {
        ShowNetworkError = false;
    }

    private int GetApiItemId(Guid localId)
    {
        return IdMapper.GuidToInt(localId);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void SetWindowOwner(Window window)
    {
        var wpfMainWindow = System.Windows.Application.Current.Windows
            .OfType<CQEING_CuadroCantidades.UI.MainWindow>()
            .FirstOrDefault();
            
        if (wpfMainWindow != null)
        {
            window.Owner = wpfMainWindow;
        }
        else if (_uiApplication != null && _uiApplication.MainWindowHandle != IntPtr.Zero)
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(window);
            helper.Owner = _uiApplication.MainWindowHandle;
        }
    }

    /// <summary>
    /// Compara un valor leído del JSON del elemento con un número ya redondeado a 2 decimales.
    /// </summary>
    private static bool IsSameNumber(object? jsonValue, double value) =>
        jsonValue is JsonElement { ValueKind: JsonValueKind.Number } number
        && number.TryGetDouble(out double current)
        && Math.Abs(current - value) < 0.005;

    /// <summary>
    /// Comando asíncrono para vincular el modelo de Revit actual con el presupuesto cargado.
    /// </summary>
    [RelayCommand]
    private async Task LinkModelAsync()
    {
        if (CurrentBudget == null)
        {
            MessageBox.Show(
                "No hay ninguna estructura o presupuesto cargado.",
                "Vincular Modelo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        var doc = _uiApplication.ActiveUIDocument?.Document;
        if (doc == null)
        {
            MessageBox.Show(
                "No hay ningún documento activo de Revit.",
                "Vincular Modelo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        string activeModelId = RevitModelIdentifier.GetModelUniqueId(doc);
        IsLoading = true;
        try
        {
            var result = await _apiClient.LinkBudgetToRevitModelAsync(CurrentBudget.VersionName, activeModelId);
            if (result.Success)
            {
                CurrentBudget.RevitModelId = activeModelId;
                RefreshModelBindingState();

                try
                {
                    var localBudget = await _unitOfWork.Budgets.GetByIdAsync(CurrentBudget.Id);
                    if (localBudget != null)
                    {
                        localBudget.RevitModelId = activeModelId;
                        await _unitOfWork.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LinkModel] Error al actualizar BD local: {ex.Message}");
                }

                MessageBox.Show(
                    "El modelo de Revit se asoció correctamente al presupuesto.",
                    "Vincular Modelo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            else
            {
                string message = $"No se pudo vincular el modelo al presupuesto \"{CurrentBudget.VersionName}\".\n\n{result.ErrorMessage}";

                // Si el modelo ya pertenece a otro presupuesto, indicar cuál para que el usuario sepa dónde desvincularlo.
                var ownerBudget = await _apiClient.GetBudgetByRevitModelAsync(activeModelId);
                if (ownerBudget != null && ownerBudget.CodigoPresupuesto != CurrentBudget.VersionName)
                {
                    message += $"\n\nEl modelo está vinculado a \"{ownerBudget.CodigoPresupuesto}\". " +
                               "Cargue ese presupuesto y use \"Desvincular Modelo\" antes de vincularlo aquí.";
                }

                MessageBox.Show(
                    message,
                    "Error de Vinculación",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ocurrió un error al vincular el modelo:\n\n{ex.Message}",
                "Error de Vinculación",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Comando asíncrono para desvincular el modelo de Revit del presupuesto cargado.
    /// </summary>
    [RelayCommand]
    private async Task UnlinkModelAsync()
    {
        if (CurrentBudget == null)
        {
            MessageBox.Show(
                "No hay ninguna estructura o presupuesto cargado.",
                "Desvincular Modelo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _apiClient.UnlinkBudgetFromRevitModelAsync(CurrentBudget.VersionName);
            if (result.Success)
            {
                CurrentBudget.RevitModelId = null;
                RefreshModelBindingState();

                try
                {
                    var localBudget = await _unitOfWork.Budgets.GetByIdAsync(CurrentBudget.Id);
                    if (localBudget != null)
                    {
                        localBudget.RevitModelId = null;
                        await _unitOfWork.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UnlinkModel] Error al actualizar BD local: {ex.Message}");
                }

                MessageBox.Show(
                    "Se logró desvincular el modelo del presupuesto correctamente.",
                    "Desvincular Modelo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            else
            {
                MessageBox.Show(
                    $"No se pudo desvincular el modelo del presupuesto \"{CurrentBudget.VersionName}\".\n\n{result.ErrorMessage}",
                    "Error de Desvinculación",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ocurrió un error al desvincular el modelo:\n\n{ex.Message}",
                "Error de Desvinculación",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            IsLoading = false;
        }
    }
}
