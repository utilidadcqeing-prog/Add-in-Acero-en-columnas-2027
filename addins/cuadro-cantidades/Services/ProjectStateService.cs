using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;
using CQEING_CuadroCantidades.UI.Models;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Servicio encargado de gestionar y sincronizar el estado persistente del proyecto.
/// Guarda y restaura los metadatos de la interfaz de usuario en el almacenamiento local (JSON)
/// y guarda los datos estructurales del presupuesto en la base de datos local.
/// </summary>
public sealed class ProjectStateService : IProjectStateService
{
    private readonly ICurrentProjectContext _context;
    private readonly ILocalStorageService _localStorage;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="ProjectStateService"/>.
    /// </summary>
    /// <param name="context">Contexto actual del proyecto.</param>
    /// <param name="localStorage">Servicio de almacenamiento local para serialización JSON.</param>
    /// <param name="unitOfWork">Unidad de trabajo para interactuar con la base de datos local.</param>
    public ProjectStateService(
        ICurrentProjectContext context,
        ILocalStorageService localStorage,
        IUnitOfWork unitOfWork)
    {
        _context = context;
        _localStorage = localStorage;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Guarda de forma asíncrona el estado de la aplicación actual en formato JSON usando la ruta del documento de Revit como clave.
    /// </summary>
    public async Task SaveStateAsync()
    {
        if (string.IsNullOrWhiteSpace(_context.RevitDocumentPath))
            return;

        try
        {
            var state = new AppState
            {
                RevitDocumentPath = _context.RevitDocumentPath,
                ProjectId = _context.CurrentProject?.Id ?? Guid.Empty,
                BudgetId = _context.CurrentBudget?.Id ?? Guid.Empty,
                SelectedRowCode = _context.SelectedBudgetRow?.Code ?? string.Empty,
                LastStructurePath = _context.LastStructurePath ?? string.Empty,
                LoadedStructureName = _context.LoadedStructureName,
                NewDescription = _context.NewDescription,
                NewUnit = _context.NewUnit,
                LastSaved = DateTime.UtcNow
            };

            // Escribir el estado en el almacenamiento local JSON
            await _localStorage.SaveAsync(_context.RevitDocumentPath, state);
            
            // Marcar el contexto como limpio una vez completado el guardado de metadatos
            _context.IsDirty = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al guardar el estado del proyecto: {ex.Message}");
        }
    }

    /// <summary>
    /// Restaura de forma asíncrona el estado guardado del proyecto desde el almacenamiento local JSON.
    /// Verifica que las entidades correspondientes (Proyecto y Presupuesto) sigan existiendo localmente.
    /// </summary>
    /// <returns>True si el estado pudo ser restaurado exitosamente; de lo contrario, False.</returns>
    public async Task<bool> RestoreStateAsync()
    {
        if (string.IsNullOrWhiteSpace(_context.RevitDocumentPath))
            return false;

        try
        {
            var state = await _localStorage.LoadAsync<AppState>(_context.RevitDocumentPath);
            if (state == null)
                return false;

            // Verificar si el proyecto aún existe en la base de datos local
            var project = await _unitOfWork.Projects.GetByIdAsync(state.ProjectId);
            if (project == null)
                return false;

            var budget = await _unitOfWork.Budgets.GetByIdAsync(state.BudgetId);
            if (budget == null)
                return false;

            // Desacoplar temporalmente la bandera dirty para evitar disparar eventos durante la asignación masiva de propiedades
            _context.IsDirty = false;

            _context.CurrentProject = project;
            _context.CurrentBudget = budget;
            _context.LastStructurePath = string.IsNullOrEmpty(state.LastStructurePath) ? null : state.LastStructurePath;
            _context.LoadedStructureName = state.LoadedStructureName;
            _context.NewDescription = state.NewDescription;
            _context.NewUnit = state.NewUnit;

            // Almacenar temporalmente el código seleccionado en un objeto BudgetRow temporal
            // para que el ViewModel lo consuma e inicialice la selección de la fila en pantalla.
            if (_context.SelectedBudgetRow == null && !string.IsNullOrEmpty(state.SelectedRowCode))
            {
                _context.SelectedBudgetRow = new BudgetRow { Code = state.SelectedRowCode };
            }

            _context.IsDirty = false;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al restaurar el estado del proyecto: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Recupera de forma asíncrona el estado guardado sin aplicarlo directamente al contexto.
    /// </summary>
    /// <returns>El estado guardado <see cref="AppState"/>, o null si no se encuentra.</returns>
    public async Task<AppState?> GetSavedStateAsync()
    {
        if (string.IsNullOrWhiteSpace(_context.RevitDocumentPath))
            return null;

        try
        {
            return await _localStorage.LoadAsync<AppState>(_context.RevitDocumentPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CuadroCantidades][Error] Error al cargar AppState: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sincroniza y guarda los datos estructurales del presupuesto (filas) en la base de datos local.
    /// Actualiza los ítems existentes, añade los nuevos y elimina aquellos que hayan sido removidos del presupuesto.
    /// </summary>
    /// <param name="budgetRows">Colección de filas de presupuesto a sincronizar.</param>
    public async Task SaveStructuralDataAsync(IEnumerable<BudgetRow> budgetRows)
    {
        if (_context.CurrentBudget == null)
            return;

        try
        {
            var budgetId = _context.CurrentBudget.Id;
            var existingItems = (await _unitOfWork.BudgetItems.FindAsync(i =>
                i.BudgetId == budgetId
            )).ToList();

            // Agrupar por código manteniendo una cola de elementos para emparejar duplicados secuencialmente
            var existingItemsDict = existingItems
                .GroupBy(i => i.Code)
                .ToDictionary(g => g.Key, g => new Queue<BudgetItem>(g.OrderBy(i => i.SortOrder)));
            var codesInNewStructure = new HashSet<string>();

            int order = 1;
            var rowsList = budgetRows.ToList();

            foreach (var row in rowsList)
            {
                codesInNewStructure.Add(row.Code);

                if (existingItemsDict.TryGetValue(row.Code, out var queue) && queue.Count > 0)
                {
                    // Obtener y remover el primer elemento de la cola para que el siguiente duplicado tome el siguiente
                    var item = queue.Dequeue();

                    // Actualizar los campos del elemento de presupuesto existente
                    item.Description = row.Description;
                    item.Unit = row.Unit;
                    item.ItemType = row.Type;
                    item.SortOrder = order++;
                    
                    // Solo actualizar las propiedades de imagen si la fila actual de la interfaz
                    // realmente tiene información, o si ambas están vacías. 
                    // Esto evita que un duplicado sin imagen borre la imagen de otro.
                    if (!string.IsNullOrWhiteSpace(row.PreviewImagePath) || string.IsNullOrWhiteSpace(item.PreviewImagePath))
                    {
                        item.PreviewImagePath = row.PreviewImagePath;
                        item.LastPreviewUpdate = row.LastPreviewUpdate;
                        item.ElementIdsSnapshot = row.ElementIdsSnapshot;
                        item.PreviewImagesJson = row.PreviewImagesJson;
                    }

                    _unitOfWork.BudgetItems.Update(item);
                }
                else
                {
                    // Crear un nuevo elemento de presupuesto
                    var newItem = new BudgetItem
                    {
                        BudgetId = budgetId,
                        ItemType = row.Type,
                        Code = row.Code,
                        Description = row.Description,
                        Unit = row.Unit,
                        SortOrder = order++,
                        PreviewImagePath = row.PreviewImagePath,
                        LastPreviewUpdate = row.LastPreviewUpdate,
                        ElementIdsSnapshot = row.ElementIdsSnapshot,
                        PreviewImagesJson = row.PreviewImagesJson
                    };
                    await _unitOfWork.BudgetItems.AddAsync(newItem);
                }
            }

            // Identificar y eliminar elementos de presupuesto que ya no forman parte de la nueva estructura
            var itemsToRemove = existingItems.Where(i => !codesInNewStructure.Contains(i.Code)).ToList();
            if (itemsToRemove.Count > 0)
            {
                _unitOfWork.BudgetItems.RemoveRange(itemsToRemove);
            }

            // Guardar todos los cambios acumulados en la base de datos
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al guardar los datos relacionales del presupuesto: {ex.Message}");
            throw; // Re-lanzar para que el llamador pueda procesar el error o alertar al usuario
        }
    }
}
