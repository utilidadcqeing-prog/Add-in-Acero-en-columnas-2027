using System;
using System.Collections.Generic;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IPreviewRefreshNotifier
{
    void NotifyPreviewRefreshed(Guid budgetId, string activityCode, ActivityImageSnapshot newSnapshot, List<ActivityImageSnapshot> allSnapshots);
}
