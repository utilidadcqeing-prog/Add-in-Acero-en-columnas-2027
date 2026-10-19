using System;
using System.Collections.Generic;

namespace CQEING_CuadroCantidades.Core.Application.Events;

public sealed class ElementAvailabilityChangedEvent
{
    public IReadOnlyCollection<string> RevitUniqueIds { get; }
    public bool IsAvailable { get; }

    public ElementAvailabilityChangedEvent(IReadOnlyCollection<string> revitUniqueIds, bool isAvailable)
    {
        RevitUniqueIds = revitUniqueIds ?? Array.Empty<string>();
        IsAvailable = isAvailable;
    }
}
