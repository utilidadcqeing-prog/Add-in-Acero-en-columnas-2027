using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IActivityElementRegistry
{
    Task LoadRegistryForBudgetAsync(Guid budgetId);
    Task RegisterElementAsync(Guid budgetItemId, string uniqueId, string category, string name, string elementDataJson);
    Task UnregisterElementAsync(string uniqueId);
    Task UnregisterElementsForActivityAsync(Guid budgetItemId);
    Task<IReadOnlyCollection<LinkedElement>> GetElementsForActivityAsync(Guid budgetItemId);
}
