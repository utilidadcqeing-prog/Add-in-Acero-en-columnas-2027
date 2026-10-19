using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Application.Events;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Servicio responsable de comprobar y gestionar la disponibilidad de uso de los elementos de Revit en el presupuesto,
/// garantizando que un elemento no sea asignado a múltiples actividades en conflicto,
/// registrando su uso y notificando cambios de disponibilidad a través del agregador de eventos.
/// </summary>
public sealed class ElementAvailabilityService : IElementAvailabilityService
{
    private readonly IAssignedElementsIndex _assignedIndex;
    private readonly IActivityElementRegistry _registry;
    private readonly IEventAggregator _eventAggregator;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="ElementAvailabilityService"/>.
    /// </summary>
    /// <param name="assignedIndex">Índice en memoria de los elementos asignados.</param>
    /// <param name="registry">Registro persistente de elementos asignados a actividades.</param>
    /// <param name="eventAggregator">Agregador para publicar eventos de disponibilidad.</param>
    /// <param name="unitOfWork">Unidad de trabajo para persistir los cambios en la base de datos.</param>
    public ElementAvailabilityService(
        IAssignedElementsIndex assignedIndex,
        IActivityElementRegistry registry,
        IEventAggregator eventAggregator,
        IUnitOfWork unitOfWork)
    {
        _assignedIndex = assignedIndex ?? throw new ArgumentNullException(nameof(assignedIndex));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <summary>
    /// Determina si un elemento está disponible para ser asignado en base a su identificador único de Revit.
    /// </summary>
    /// <param name="uniqueId">Identificador único del elemento en Revit.</param>
    /// <returns>True si el elemento no está asignado a ninguna actividad; de lo contrario, False.</returns>
    public bool IsElementAvailable(string uniqueId)
    {
        // El elemento está disponible si no se encuentra registrado en el índice de asignaciones
        return !_assignedIndex.IsAssigned(uniqueId);
    }

    /// <summary>
    /// Registra el uso de un elemento en una actividad de presupuesto específica, guarda los cambios y publica el cambio de disponibilidad.
    /// </summary>
    /// <param name="budgetItemId">Identificador único del item de presupuesto (actividad).</param>
    /// <param name="uniqueId">Identificador único del elemento de Revit.</param>
    /// <param name="category">Categoría del elemento en Revit.</param>
    /// <param name="name">Nombre o descripción del elemento.</param>
    /// <param name="elementDataJson">Datos serializados del elemento en formato JSON.</param>
    public async Task RegisterUsageAsync(Guid budgetItemId, string uniqueId, string category, string name, string elementDataJson)
    {
        // Registrar el elemento en el almacén persistente
        await _registry.RegisterElementAsync(budgetItemId, uniqueId, category, name, elementDataJson);
        
        // Confirmar la transacción
        await _unitOfWork.SaveChangesAsync();
        
        // Notificar que el elemento ya no está disponible
        _eventAggregator.Publish(new ElementAvailabilityChangedEvent(new[] { uniqueId }, isAvailable: false));

        var items = await _unitOfWork.BudgetItems.FindAsync(i => i.Id == budgetItemId);
        var budgetItem = items.FirstOrDefault();
        if (budgetItem != null)
        {
            SyncRevitCommentsParameter(new[] { uniqueId }, budgetItem.Code ?? string.Empty);
        }
    }

    /// <summary>
    /// Registra el uso de múltiples elementos en una actividad de presupuesto, guarda los cambios y publica el evento correspondiente.
    /// </summary>
    /// <param name="budgetItemId">Identificador único de la actividad de presupuesto.</param>
    /// <param name="elements">Colección de elementos a registrar con sus detalles.</param>
    public async Task RegisterUsagesAsync(Guid budgetItemId, IEnumerable<(string UniqueId, string Category, string Name, string ElementDataJson)> elements)
    {
        var uids = new List<string>();
        
        // Recorrer y registrar cada elemento individualmente
        foreach (var el in elements)
        {
            await _registry.RegisterElementAsync(budgetItemId, el.UniqueId, el.Category, el.Name, el.ElementDataJson);
            uids.Add(el.UniqueId);
        }
        
        // Guardar todos los registros de una sola vez
        await _unitOfWork.SaveChangesAsync();
        
        // Notificar que los elementos ya no están disponibles
        _eventAggregator.Publish(new ElementAvailabilityChangedEvent(uids, isAvailable: false));

        var items = await _unitOfWork.BudgetItems.FindAsync(i => i.Id == budgetItemId);
        var budgetItem = items.FirstOrDefault();
        if (budgetItem != null)
        {
            SyncRevitCommentsParameter(uids, budgetItem.Code ?? string.Empty);
        }
    }

    /// <summary>
    /// Libera la asociación de un elemento con su actividad asignada, permitiendo que esté disponible nuevamente.
    /// </summary>
    /// <param name="uniqueId">Identificador único del elemento de Revit.</param>
    public async Task ReleaseUsageAsync(string uniqueId)
    {
        // Eliminar el elemento del registro
        await _registry.UnregisterElementAsync(uniqueId);
        
        // Confirmar los cambios en la base de datos
        await _unitOfWork.SaveChangesAsync();
        
        // Notificar que el elemento vuelve a estar disponible
        _eventAggregator.Publish(new ElementAvailabilityChangedEvent(new[] { uniqueId }, isAvailable: true));

        SyncRevitCommentsParameter(new[] { uniqueId }, string.Empty);
    }

    /// <summary>
    /// Libera la asociación de múltiples elementos con sus actividades correspondientes.
    /// </summary>
    /// <param name="uniqueIds">Colección de identificadores únicos de los elementos a liberar.</param>
    public async Task ReleaseUsagesAsync(IEnumerable<string> uniqueIds)
    {
        var uids = new List<string>();
        
        // Eliminar la asociación de cada elemento del registro
        foreach (var uid in uniqueIds)
        {
            await _registry.UnregisterElementAsync(uid);
            uids.Add(uid);
        }
        
        // Confirmar la transacción
        await _unitOfWork.SaveChangesAsync();
        
        // Publicar evento notificando la disponibilidad de estos elementos
        _eventAggregator.Publish(new ElementAvailabilityChangedEvent(uids, isAvailable: true));

        SyncRevitCommentsParameter(uids, string.Empty);
    }

    /// <summary>
    /// Libera todos los elementos asociados a una actividad de presupuesto específica de una sola vez.
    /// </summary>
    /// <param name="budgetItemId">Identificador único de la actividad de presupuesto.</param>
    public async Task ReleaseAllForActivityAsync(Guid budgetItemId)
    {
        // Obtener la lista de elementos actualmente vinculados a la actividad para conocer sus IDs
        var elements = await _registry.GetElementsForActivityAsync(budgetItemId);
        var uids = new List<string>();
        foreach (var el in elements)
        {
            uids.Add(el.RevitUniqueId);
        }
        
        // Eliminar del registro persistente todas las asociaciones para esta actividad
        await _registry.UnregisterElementsForActivityAsync(budgetItemId);
        
        // Guardar cambios en la base de datos
        await _unitOfWork.SaveChangesAsync();
        
        // Notificar que todos estos elementos vuelven a estar disponibles
        _eventAggregator.Publish(new ElementAvailabilityChangedEvent(uids, isAvailable: true));

        SyncRevitCommentsParameter(uids, string.Empty);
    }

    /// <summary>
    /// Sincroniza de forma masiva el parámetro Comments de Revit para todos los elementos vinculados a un presupuesto.
    /// Útil cuando se carga un presupuesto existente desde el backend.
    /// </summary>
    /// <param name="budgetId">Identificador único del presupuesto.</param>
    public async Task SyncAllElementsForBudgetAsync(Guid budgetId)
    {
        var items = await _unitOfWork.BudgetItems.FindAsync(i => i.BudgetId == budgetId);
        var itemsList = items.ToList();
        var itemDict = itemsList.ToDictionary(i => i.Id, i => i.Code);
        
        var itemIds = itemsList.Select(i => i.Id).ToList();

        var linkedElements = await _unitOfWork.LinkedElements.FindAsync(le => itemIds.Contains(le.BudgetItemId));
        
        var elementsByCode = linkedElements
            .GroupBy(le => itemDict.ContainsKey(le.BudgetItemId) ? itemDict[le.BudgetItemId] : string.Empty)
            .ToDictionary(g => g.Key ?? string.Empty, g => g.Select(le => le.RevitUniqueId).ToList());

        CQEING_CuadroCantidades.Core.RevitEventExecutor.Execute(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            bool ownsTransaction = false;
            Autodesk.Revit.DB.Transaction? tx = null;

            if (!doc.IsModifiable)
            {
                tx = new Autodesk.Revit.DB.Transaction(doc, "Sincronizar códigos de actividad al cargar");
                tx.Start();
                ownsTransaction = true;
            }

            foreach (var kvp in elementsByCode)
            {
                string code = kvp.Key;
                foreach (var uid in kvp.Value)
                {
                    try
                    {
                        var el = doc.GetElement(uid);
                        if (el != null)
                        {
                            var param = el.get_Parameter(Autodesk.Revit.DB.BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (param != null && !param.IsReadOnly)
                            {
                                var currentValue = param.AsString() ?? string.Empty;
                                if (currentValue != code)
                                {
                                    param.Set(code);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SyncRevit] Error al sincronizar elemento {uid}: {ex.Message}");
                    }
                }
            }

            if (ownsTransaction && tx != null)
            {
                tx.Commit();
                tx.Dispose();
            }
        });
    }

    /// <summary>
    /// Método privado de ayuda para sincronizar un código de actividad en el parámetro Comments
    /// para una colección de elementos, utilizando la API de Revit de forma segura en su hilo principal.
    /// </summary>
    private void SyncRevitCommentsParameter(IEnumerable<string> uniqueIds, string activityCode)
    {
        CQEING_CuadroCantidades.Core.RevitEventExecutor.Execute(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            bool ownsTransaction = false;
            Autodesk.Revit.DB.Transaction? tx = null;

            if (!doc.IsModifiable)
            {
                tx = new Autodesk.Revit.DB.Transaction(doc, "Sincronizar código de actividad");
                tx.Start();
                ownsTransaction = true;
            }

            foreach (var uid in uniqueIds)
            {
                try
                {
                    var el = doc.GetElement(uid);
                    if (el != null)
                    {
                        var param = el.get_Parameter(Autodesk.Revit.DB.BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (param != null && !param.IsReadOnly)
                        {
                            var currentValue = param.AsString() ?? string.Empty;
                            if (currentValue != activityCode)
                            {
                                param.Set(activityCode ?? string.Empty);
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[SyncRevit] El elemento {uid} no posee el parámetro Comments o es de solo lectura.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SyncRevit] Error al sincronizar Comments del elemento {uid}: {ex.Message}");
                }
            }

            if (ownsTransaction && tx != null)
            {
                tx.Commit();
                tx.Dispose();
            }
        });
    }
}
