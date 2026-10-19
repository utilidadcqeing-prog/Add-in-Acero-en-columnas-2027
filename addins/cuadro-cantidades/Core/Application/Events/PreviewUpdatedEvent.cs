using System;
using System.Collections.Generic;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Core.Application.Events;

public sealed class PreviewUpdatedEvent
{
    public Guid BudgetId { get; }
    public string ActivityCode { get; }
    public ActivityImageSnapshot? NewSnapshot { get; }
    public List<ActivityImageSnapshot> AllSnapshots { get; }

    public PreviewUpdatedEvent(Guid budgetId, string activityCode, ActivityImageSnapshot? newSnapshot, List<ActivityImageSnapshot> allSnapshots)
    {
        BudgetId = budgetId;
        ActivityCode = activityCode;
        NewSnapshot = newSnapshot;
        AllSnapshots = allSnapshots ?? new List<ActivityImageSnapshot>();
    }
}
