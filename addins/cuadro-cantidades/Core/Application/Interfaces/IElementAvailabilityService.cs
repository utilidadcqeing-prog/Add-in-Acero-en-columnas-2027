using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IElementAvailabilityService
{
    bool IsElementAvailable(string uniqueId);
    Task RegisterUsageAsync(Guid budgetItemId, string uniqueId, string category, string name, string elementDataJson);
    Task RegisterUsagesAsync(Guid budgetItemId, IEnumerable<(string UniqueId, string Category, string Name, string ElementDataJson)> elements);
    Task ReleaseUsageAsync(string uniqueId);
    Task ReleaseUsagesAsync(IEnumerable<string> uniqueIds);
    Task ReleaseAllForActivityAsync(Guid budgetItemId);
    Task SyncAllElementsForBudgetAsync(Guid budgetId);
}
