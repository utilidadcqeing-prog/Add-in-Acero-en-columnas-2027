using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Registro encargado de administrar y persistir la asociación entre elementos de Revit y actividades del presupuesto.
/// </summary>
public sealed class ActivityElementRegistry : IActivityElementRegistry
{
    // Unidad de trabajo para interactuar con la base de datos local (patrón Unit of Work)
    private readonly IUnitOfWork _unitOfWork;
    
    // Índice en memoria que rastrea en tiempo real qué elementos ya están asignados a qué actividades
    private readonly IAssignedElementsIndex _assignedIndex;

    /// <summary>
    /// Constructor de la clase que recibe dependencias mediante inyección de dependencias.
    /// </summary>
    public ActivityElementRegistry(IUnitOfWork unitOfWork, IAssignedElementsIndex assignedIndex)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _assignedIndex = assignedIndex ?? throw new ArgumentNullException(nameof(assignedIndex));
    }

    /// <summary>
    /// Carga en el índice en memoria todos los elementos vinculados correspondientes a un presupuesto específico.
    /// </summary>
    /// <param name="budgetId">Identificador único del presupuesto.</param>
    public async Task LoadRegistryForBudgetAsync(Guid budgetId)
    {
        // Limpiar el índice en memoria para evitar residuos de otros presupuestos
        _assignedIndex.Clear();

        // Buscar todos los ítems de presupuesto que pertenecen a este presupuesto
        var budgetItems = await _unitOfWork.BudgetItems.FindAsync(bi => bi.BudgetId == budgetId);
        var budgetItemIds = budgetItems.Select(bi => bi.Id).ToList();

        if (budgetItemIds.Count > 0)
        {
            // Traer de la base de datos todos los elementos que están vinculados a cualquiera de las actividades obtenidas
            var linkedElements = await _unitOfWork.LinkedElements.FindAsync(le =>
                budgetItemIds.Contains(le.BudgetItemId)
            );
            
            // Agregar cada elemento al índice en memoria (clave: RevitUniqueId, valor: BudgetItemId)
            foreach (var element in linkedElements)
            {
                _assignedIndex.Add(element.RevitUniqueId, element.BudgetItemId);
            }
        }
    }

    /// <summary>
    /// Registra la asociación de un elemento específico de Revit con una actividad del presupuesto.
    /// </summary>
    public async Task RegisterElementAsync(
        Guid budgetItemId,
        string uniqueId,
        string category,
        string name,
        string elementDataJson
    )
    {
        // Verificar si la relación entre el elemento y la actividad ya está guardada en la base de datos
        var exists = await _unitOfWork.LinkedElements.FindAsync(le =>
            le.RevitUniqueId == uniqueId && le.BudgetItemId == budgetItemId
        );
        
        // Si no está registrado, se crea la nueva entidad LinkedElement y se guarda
        if (!exists.Any())
        {
            var linkedElement = new LinkedElement
            {
                BudgetItemId = budgetItemId,
                RevitUniqueId = uniqueId,
                Category = category,
                ElementName = name,
                ElementDataJson = elementDataJson,
            };
            
            // Persistir en base de datos local
            await _unitOfWork.LinkedElements.AddAsync(linkedElement);
            
            // Registrar en el índice en memoria
            _assignedIndex.Add(uniqueId, budgetItemId);
        }
    }

    /// <summary>
    /// Desvincula un elemento de Revit de cualquier actividad en la que estuviera registrado.
    /// </summary>
    /// <param name="uniqueId">Identificador único del elemento de Revit.</param>
    public async Task UnregisterElementAsync(string uniqueId)
    {
        // Buscar registros de este elemento en la base de datos
        var elements = await _unitOfWork.LinkedElements.FindAsync(le =>
            le.RevitUniqueId == uniqueId
        );
        var elementsList = elements.ToList();
        
        // Si existen vínculos, eliminarlos de la base de datos y actualizar el índice en memoria
        if (elementsList.Count > 0)
        {
            _unitOfWork.LinkedElements.RemoveRange(elementsList);
            foreach (var element in elementsList)
            {
                _assignedIndex.Remove(element.RevitUniqueId);
            }
        }
    }

    /// <summary>
    /// Elimina todas las vinculaciones de elementos de Revit asignadas a una actividad en particular.
    /// </summary>
    /// <param name="budgetItemId">Identificador único del ítem del presupuesto (actividad).</param>
    public async Task UnregisterElementsForActivityAsync(Guid budgetItemId)
    {
        // Obtener todos los elementos enlazados a la actividad especificada
        var elements = await _unitOfWork.LinkedElements.FindAsync(le =>
            le.BudgetItemId == budgetItemId
        );
        var elementsList = elements.ToList();
        
        // Eliminar registros de la base de datos y removerlos del índice en memoria
        if (elementsList.Count > 0)
        {
            _unitOfWork.LinkedElements.RemoveRange(elementsList);
            foreach (var element in elementsList)
            {
                _assignedIndex.Remove(element.RevitUniqueId);
            }
        }
    }

    /// <summary>
    /// Obtener la lista completa de elementos de Revit vinculados a una actividad del presupuesto.
    /// </summary>
    /// <param name="budgetItemId">Identificador único del ítem del presupuesto (actividad).</param>
    public async Task<IReadOnlyCollection<LinkedElement>> GetElementsForActivityAsync(
        Guid budgetItemId
    )
    {
        var elements = await _unitOfWork.LinkedElements.FindAsync(le =>
            le.BudgetItemId == budgetItemId
        );
        return elements.ToList();
    }
}
