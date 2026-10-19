using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.UI.Models;
using CQEING_CuadroCantidades.UI.ViewModels;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Servicio encargado del guardado automático de los cambios realizados en el presupuesto y la interfaz de usuario.
/// Utiliza un temporizador de rebote (debounce) para evitar guardados excesivos durante la edición continua.
/// </summary>
public sealed class AutoSaveService : IAutoSaveService, IDisposable
{
    private readonly ICurrentProjectContext _context;
    private readonly IProjectStateService _stateService;
    private readonly System.Timers.Timer _debounceTimer;
    private MainWindowViewModel? _viewModel;
    private bool _isSaving;
    private bool _isSuspended;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="AutoSaveService"/>.
    /// </summary>
    /// <param name="context">Contexto del proyecto actual.</param>
    /// <param name="stateService">Servicio para gestionar el estado y almacenamiento del proyecto.</param>
    public AutoSaveService(ICurrentProjectContext context, IProjectStateService stateService)
    {
        _context = context;
        _stateService = stateService;

        // Configurar el temporizador de rebote a 1.5 segundos
        _debounceTimer = new System.Timers.Timer(1500); // 1.5 segundos
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += OnDebounceTimerElapsed;

        // Suscribirse a cambios en el contexto
        _context.ContextChanged += OnContextChanged;
    }

    /// <summary>
    /// Obtiene un valor que indica si el servicio de guardado automático está suspendido temporalmente.
    /// </summary>
    public bool IsSuspended => _isSuspended;

    /// <summary>
    /// Suspende temporalmente el guardado automático.
    /// </summary>
    public void Suspend()
    {
        _isSuspended = true;
        _debounceTimer.Stop();
    }

    /// <summary>
    /// Reanuda el servicio de guardado automático.
    /// </summary>
    public void Resume()
    {
        _isSuspended = false;
    }

    /// <summary>
    /// Inicia el monitoreo de cambios en las propiedades y colecciones del ViewModel especificado.
    /// </summary>
    /// <param name="viewModel">El modelo de vista principal que se va a monitorear.</param>
    public void StartMonitoring(INotifyPropertyChanged viewModel)
    {
        if (viewModel is MainWindowViewModel vm)
        {
            _viewModel = vm;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Suscribirse a los cambios en la colección de filas del presupuesto
            _viewModel.BudgetRows.CollectionChanged += OnBudgetRowsCollectionChanged;

            // Suscribirse a eventos de cambio de propiedades de cada fila existente en el presupuesto
            foreach (var row in _viewModel.BudgetRows)
            {
                row.PropertyChanged += OnBudgetRowPropertyChanged;
            }
        }
    }

    /// <summary>
    /// Detiene el monitoreo del ViewModel actual y limpia las suscripciones a eventos para evitar fugas de memoria.
    /// </summary>
    public void StopMonitoring()
    {
        _debounceTimer.Stop();
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.BudgetRows.CollectionChanged -= OnBudgetRowsCollectionChanged;

            foreach (var row in _viewModel.BudgetRows)
            {
                row.PropertyChanged -= OnBudgetRowPropertyChanged;
            }
            _viewModel = null;
        }
    }

    /// <summary>
    /// Solicita una operación de guardado, ya sea de forma diferida (con temporizador) o inmediata.
    /// </summary>
    /// <param name="immediate">True para guardar de forma inmediata; False para diferirlo mediante el temporizador.</param>
    public void RequestSave(bool immediate = false)
    {
        if (_isSuspended || _viewModel == null || _context.CurrentBudget == null)
            return;

        if (immediate)
        {
            _debounceTimer.Stop();
            // Ejecución asíncrona sin bloquear el hilo principal
            Task.Run(async () => await SaveAllAsync());
        }
        else
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    /// <summary>
    /// Notifica que ha ocurrido un cambio y solicita un guardado automático diferido.
    /// </summary>
    public void NotifyChange()
    {
        RequestSave(immediate: false);
    }

    /// <summary>
    /// Manejador de eventos que responde a los cambios en el contexto del proyecto.
    /// </summary>
    private void OnContextChanged(object? sender, EventArgs e)
    {
        if (!_isSuspended && _context.IsDirty)
        {
            NotifyChange();
        }
    }

    /// <summary>
    /// Manejador de eventos que responde al cambio de propiedades específicas del ViewModel.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSuspended)
            return;

        // Propiedades de ViewModel que queremos monitorear y sincronizar con el contexto
        if (
            e.PropertyName == nameof(MainWindowViewModel.SelectedBudgetRow)
        )
        {
            if (_viewModel != null)
            {
                if (_viewModel.SelectedBudgetRow != null)
                {
                    _context.SelectedBudgetRow = _viewModel.SelectedBudgetRow;
                }
            }
            NotifyChange();
        }
    }

    /// <summary>
    /// Manejador de eventos que se dispara cuando se agregan o eliminan filas de la colección de presupuesto.
    /// </summary>
    private void OnBudgetRowsCollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e
    )
    {
        if (_isSuspended)
            return;

        // Suscribirse a cambios en las propiedades de las nuevas filas agregadas
        if (e.NewItems != null)
        {
            foreach (BudgetRow row in e.NewItems)
            {
                row.PropertyChanged += OnBudgetRowPropertyChanged;
            }
        }

        // Cancelar la suscripción de las filas eliminadas
        if (e.OldItems != null)
        {
            foreach (BudgetRow row in e.OldItems)
            {
                row.PropertyChanged -= OnBudgetRowPropertyChanged;
            }
        }

        NotifyChange();
    }

    /// <summary>
    /// Manejador de eventos que responde cuando cambia alguna propiedad individual dentro de una fila de presupuesto.
    /// </summary>
    private void OnBudgetRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSuspended)
            return;

        // Si se modifica una propiedad crítica dentro de una fila (ej. Código, Descripción, Unidad, Cantidad)
        if (
            e.PropertyName == nameof(BudgetRow.Description)
            || e.PropertyName == nameof(BudgetRow.Unit)
            || e.PropertyName == nameof(BudgetRow.Code)
            || e.PropertyName == nameof(BudgetRow.Quantity)
        )
        {
            NotifyChange();
        }
    }

    /// <summary>
    /// Manejador de eventos que se ejecuta al expirar el temporizador de rebote (debounce).
    /// </summary>
    private void OnDebounceTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _ = SaveAllAsync();
    }

    /// <summary>
    /// Ejecuta el proceso de guardado asíncrono tanto de los datos estructurales en la base de datos local como del estado de la UI en JSON.
    /// </summary>
    private async Task SaveAllAsync()
    {
        if (_isSaving || _isSuspended || _viewModel == null || _context.CurrentBudget == null)
            return;

        _isSaving = true;
        System.Diagnostics.Debug.WriteLine(
            $"[CuadroCantidades][Guardado] Iniciando ciclo de auto-guardado a las {DateTime.Now}..."
        );

        try
        {
            // Clonar filas de forma segura en el hilo de la UI (WPF) en caso de que cambien durante el guardado
            System.Collections.Generic.List<BudgetRow> rowsSnapshot;
            if (_viewModel.Dispatcher.CheckAccess())
            {
                rowsSnapshot = _viewModel.BudgetRows.ToList();
            }
            else
            {
                rowsSnapshot = _viewModel.Dispatcher.Invoke(() => _viewModel.BudgetRows.ToList());
            }

            // Sincronizar datos con la base de datos local
            await _stateService.SaveStructuralDataAsync(rowsSnapshot);

            // Sincronizar metadatos de UI con JSON
            await _stateService.SaveStateAsync();
            System.Diagnostics.Debug.WriteLine(
                "[CuadroCantidades][Guardado] Ciclo de auto-guardado completado correctamente."
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CuadroCantidades][Error] Error en el ciclo de auto-guardado: {ex.Message}"
            );
        }
        finally
        {
            _isSaving = false;
        }
    }

    /// <summary>
    /// Libera los recursos utilizados por el servicio y realiza un guardado final de seguridad si es necesario.
    /// </summary>
    public void Dispose()
    {
        if (
            _viewModel != null
            && _context.CurrentBudget != null
            && (_debounceTimer.Enabled || _context.IsDirty)
        )
        {
            _debounceTimer.Stop();
            try
            {
                // Tomar el snapshot de los datos directamente en el hilo de la UI (WPF)
                System.Collections.Generic.List<BudgetRow> rowsSnapshot;
                if (_viewModel.Dispatcher.CheckAccess())
                {
                    rowsSnapshot = _viewModel.BudgetRows.ToList();
                }
                else
                {
                    rowsSnapshot = _viewModel.Dispatcher.Invoke(() => _viewModel.BudgetRows.ToList());
                }

                // Ejecutar el guardado asíncrono en un hilo del ThreadPool (Task.Run)
                // y esperar a que complete. Al correr en el ThreadPool, los awaits internos
                // no intentarán volver al hilo de la UI, evitando el deadlock.
                Task.Run(async () =>
                    {
                        await _stateService.SaveStructuralDataAsync(rowsSnapshot);
                        await _stateService.SaveStateAsync();
                    })
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AutoSave] Error en guardado final durante Dispose: {ex.Message}"
                );
            }
        }

        StopMonitoring();
        _context.ContextChanged -= OnContextChanged;
        _debounceTimer.Dispose();
    }
}
