using System;
using System.Collections.Generic;
using CQEING_CuadroCantidades.Core.Application.Events;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Services;

public sealed class PreviewRefreshNotifier : IPreviewRefreshNotifier
{
    private readonly IEventAggregator _eventAggregator;

    public PreviewRefreshNotifier(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    }

    public void NotifyPreviewRefreshed(Guid budgetId, string activityCode, ActivityImageSnapshot newSnapshot, List<ActivityImageSnapshot> allSnapshots)
    {
        _eventAggregator.Publish(new PreviewUpdatedEvent(budgetId, activityCode, newSnapshot, allSnapshots));
    }
}
