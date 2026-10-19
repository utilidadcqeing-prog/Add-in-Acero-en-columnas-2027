using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CQEING_CuadroCantidades.Core;
using CQEING_CuadroCantidades.Core.Application.Events;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;
using CQEING_CuadroCantidades.UI.Models;
using CQEING_CuadroCantidades.UI.ViewModels;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Servicio responsable de monitorear y actualizar las vistas previas en vivo asociadas a las actividades del presupuesto.
/// Escucha los eventos de cambio en las actividades, gestiona la limpieza de imágenes anteriores del disco y de la base de datos,
/// y coordina la captura y guardado de nuevas imágenes de vista previa a través de Revit y su publicación para actualizar la interfaz.
/// </summary>
public sealed class LivePreviewUpdater : ILivePreviewUpdater, IDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IPreviewRegenerationQueue _previewQueue;
    private readonly IRevitImageCaptureService _captureService;
    private readonly IPreviewDebugImageStorageService _debugImageStorageService;
    private readonly IUnitOfWork _unitOfWork;
    private MainWindowViewModel? _mainWindowViewModel;
    private bool _isDisposed;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="LivePreviewUpdater"/>.
    /// </summary>
    /// <param name="eventAggregator">Agregador de eventos para suscribirse y publicar mensajes.</param>
    /// <param name="previewQueue">Cola de regeneración de vistas previas.</param>
    /// <param name="captureService">Servicio para capturar imágenes del modelo 3D en Revit.</param>
    /// <param name="debugImageStorageService">Servicio de almacenamiento persistente para las imágenes de vista previa.</param>
    /// <param name="unitOfWork">Unidad de trabajo para persistir los cambios en la base de datos.</param>
    public LivePreviewUpdater(
        IEventAggregator eventAggregator,
        IPreviewRegenerationQueue previewQueue,
        IRevitImageCaptureService captureService,
        IPreviewDebugImageStorageService debugImageStorageService,
        IUnitOfWork unitOfWork)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _previewQueue = previewQueue ?? throw new ArgumentNullException(nameof(previewQueue));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _debugImageStorageService = debugImageStorageService ?? throw new ArgumentNullException(nameof(debugImageStorageService));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <summary>
    /// Establece la referencia del ViewModel de la ventana principal para sincronizar los cambios gráficos y de datos directamente con la interfaz.
    /// </summary>
    /// <param name="viewModel">ViewModel principal de la interfaz.</param>
    public void SetViewModel(MainWindowViewModel viewModel)
    {
        _mainWindowViewModel = viewModel;
    }

    /// <summary>
    /// Suscribe el actualizador a los eventos de cambio de actividad para iniciar el monitoreo de cambios.
    /// </summary>
    public void StartUpdating()
    {
        _eventAggregator.Subscribe<ActivityChangedEvent>(OnActivityChanged);
    }

    /// <summary>
    /// Detiene la suscripción a eventos de actividad y cancela todas las regeneraciones de vistas previas encoladas.
    /// </summary>
    public void StopUpdating()
    {
        _eventAggregator.Unsubscribe<ActivityChangedEvent>(OnActivityChanged);
        _previewQueue.CancelAll();
    }

    /// <summary>
    /// Manejador de eventos que responde cuando una actividad cambia en el presupuesto.
    /// </summary>
    private void OnActivityChanged(ActivityChangedEvent e)
    {
        if (_mainWindowViewModel == null || _mainWindowViewModel.CurrentBudget == null) return;
        if (e.BudgetId != _mainWindowViewModel.CurrentBudget.Id) return;

        // Si se solicita limpiar la vista previa, se maneja de forma inmediata sin encolar llamadas a la API de Revit
        if (e.ChangeType == ActivityChangeType.PreviewCleared || e.RevitUniqueIds == null || e.RevitUniqueIds.Count == 0)
        {
            _previewQueue.Cancel(e.ActivityCode);
            _ = HandleClearPreviewAsync(e.ActivityCode);
            return;
        }

        // Ya no se genera de forma automática la captura del modelo 3D en Revit al agregar o modificar elementos.
        // La captura se realizará únicamente de forma manual por el usuario desde la ventana de detalles.
    }

    /// <summary>
    /// Borra las imágenes físicas de vista previa del almacenamiento y actualiza la base de datos y la interfaz de usuario.
    /// </summary>
    private async Task HandleClearPreviewAsync(string activityCode)
    {
        try
        {
            if (_mainWindowViewModel?.CurrentBudget == null) return;
            var budgetId = _mainWindowViewModel.CurrentBudget.Id;

            // Buscar la actividad correspondiente en la base de datos
            var items = await _unitOfWork.BudgetItems.FindAsync(i => i.BudgetId == budgetId && i.Code == activityCode);
            var budgetItem = items.FirstOrDefault();
            if (budgetItem == null) return;

            // Eliminar los archivos de imágenes físicas de la vista previa del disco
            if (!string.IsNullOrEmpty(budgetItem.PreviewImagesJson))
            {
                try
                {
                    var existingSnapshots = System.Text.Json.JsonSerializer.Deserialize<List<ActivityImageSnapshot>>(budgetItem.PreviewImagesJson);
                    if (existingSnapshots != null)
                    {
                        foreach (var snap in existingSnapshots)
                        {
                            _debugImageStorageService.DeleteDebugCopy(snap.DebugImagePath ?? snap.ImagePath);
                        }
                    }
                }
                catch { }
            }

            // Actualizar la fila de la interfaz de usuario en el hilo principal
            var row = _mainWindowViewModel.BudgetRows.FirstOrDefault(r => r.Code == activityCode);
            if (row != null)
            {
                _mainWindowViewModel.Dispatcher.Invoke(() =>
                {
                    row.PreviewImagePath = null;
                    row.LastPreviewUpdate = null;
                    row.ElementIdsSnapshot = null;
                    row.PreviewImagesJson = null;
                });
            }

            // Actualizar las propiedades de la actividad en la base de datos
            budgetItem.PreviewImagePath = null;
            budgetItem.LastPreviewUpdate = null;
            budgetItem.ElementIdsSnapshot = null;
            budgetItem.PreviewImagesJson = null;
            _unitOfWork.BudgetItems.Update(budgetItem);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LivePreviewUpdater] Error clearing preview: {ex.Message}");
        }
    }

    /// <summary>
    /// Genera una nueva captura de vista previa para una actividad específica basándose en sus elementos asignados en Revit, y la guarda.
    /// </summary>
    private async Task GenerateAndSavePreviewAsync(string activityCode, IReadOnlyCollection<string> revitUniqueIds, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();

            if (_mainWindowViewModel?.CurrentBudget == null) return;
            var budgetId = _mainWindowViewModel.CurrentBudget.Id;

            // Obtener el item de presupuesto de la base de datos
            var items = await _unitOfWork.BudgetItems.FindAsync(i => i.BudgetId == budgetId && i.Code == activityCode);
            var budgetItem = items.FirstOrDefault();
            if (budgetItem == null) return;

            var uniqueIds = revitUniqueIds.ToList();
            if (uniqueIds.Count == 0)
            {
                // Si no hay elementos seleccionados, se procede a limpiar la vista previa
                await HandleClearPreviewAsync(activityCode);
                return;
            }

            token.ThrowIfCancellationRequested();

            // Ejecutar el proceso de captura en el hilo principal de Revit (External Event Execution)
            var tcs = new TaskCompletionSource<string>();
            RevitEventExecutor.Execute(app =>
            {
                try
                {
                    var doc = app.ActiveUIDocument?.Document;
                    if (doc == null)
                    {
                        tcs.SetResult(string.Empty);
                        return;
                    }
                    string path = _captureService.CaptureView(doc, uniqueIds, activityCode);
                    tcs.SetResult(path);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            // Esperar el resultado de la captura de Revit
            string tempCapturedPath = await tcs.Task;
            token.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(tempCapturedPath) || !File.Exists(tempCapturedPath))
            {
                System.Diagnostics.Debug.WriteLine("[LivePreviewUpdater] Capture failed or file not found.");
                return;
            }

            // Ordenar los IDs para crear una firma/snapshot determinista de los elementos incluidos
            var sortedIds = uniqueIds.OrderBy(id => id).ToList();
            string idsSnapshot = string.Join(",", sortedIds);
            DateTime updateTime = DateTime.Now;

            // Guardar permanentemente la imagen en el directorio de almacenamiento del proyecto
            var newSnapshot = ActivityImageSnapshotFactory.CreateFromCapturedFile(
                tempCapturedPath,
                activityCode,
                updateTime,
                idsSnapshot,
                _debugImageStorageService);

            // Eliminar el archivo temporal de captura generado
            try
            {
                if (File.Exists(tempCapturedPath))
                {
                    File.Delete(tempCapturedPath);
                }
            }
            catch { }

            token.ThrowIfCancellationRequested();

            // Cargar y deserializar las capturas existentes de esta actividad
            List<ActivityImageSnapshot> snapshotsList = null;
            if (!string.IsNullOrEmpty(budgetItem.PreviewImagesJson))
            {
                try
                {
                    snapshotsList = System.Text.Json.JsonSerializer.Deserialize<List<ActivityImageSnapshot>>(budgetItem.PreviewImagesJson);
                }
                catch { }
            }
            snapshotsList ??= new List<ActivityImageSnapshot>();

            // Añadir la nueva captura
            snapshotsList.Add(newSnapshot);

            string newJson = System.Text.Json.JsonSerializer.Serialize(snapshotsList);

            // Actualizar la fila en el modelo de vista (WPF) utilizando el despachador de la UI
            var row = _mainWindowViewModel.BudgetRows.FirstOrDefault(r => r.Code == activityCode);
            if (row != null)
            {
                _mainWindowViewModel.Dispatcher.Invoke(() =>
                {
                    row.PreviewImagePath = newSnapshot.DebugImagePath;
                    row.LastPreviewUpdate = updateTime;
                    row.ElementIdsSnapshot = idsSnapshot;
                    row.PreviewImagesJson = newJson;
                });
            }

            // Guardar los metadatos actualizados de la vista previa en la base de datos
            budgetItem.PreviewImagePath = newSnapshot.DebugImagePath;
            budgetItem.LastPreviewUpdate = updateTime;
            budgetItem.ElementIdsSnapshot = idsSnapshot;
            budgetItem.PreviewImagesJson = newJson;

            _unitOfWork.BudgetItems.Update(budgetItem);
            await _unitOfWork.SaveChangesAsync();

            // Notificar a otros ViewModels sobre la actualización de la vista previa para refrescar interfaces dependientes
            _eventAggregator.Publish(new PreviewUpdatedEvent(budgetId, activityCode, newSnapshot, snapshotsList));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LivePreviewUpdater] Error generating preview: {ex.Message}");
        }
    }

    /// <summary>
    /// Libera los recursos del actualizador de vistas previas.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopUpdating();
    }
}
