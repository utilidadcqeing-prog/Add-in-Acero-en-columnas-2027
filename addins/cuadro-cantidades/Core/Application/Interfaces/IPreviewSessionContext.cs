using System;
using System.Collections.Generic;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IPreviewSessionContext
{
    Guid? ActiveBudgetItemId { get; set; }
    string? ActiveActivityCode { get; set; }
    IReadOnlyCollection<string> ElementUniqueIds { get; set; }
    void StartSession(Guid budgetItemId, string activityCode, IEnumerable<string> uniqueIds);
    void UpdateElements(IEnumerable<string> uniqueIds);
    void EndSession();
}
