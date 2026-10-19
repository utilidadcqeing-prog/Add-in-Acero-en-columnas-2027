using System;
using System.Collections.Generic;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Core.Application.Events;

public enum ActivityChangeType
{
    ElementsAdded,
    ElementsRemoved,
    SelectionModified,
    ActivityUpdated,
    PreviewCleared
}

public sealed class ActivityChangedEvent
{
    public Guid BudgetId { get; }
    public string ActivityCode { get; }
    public IReadOnlyCollection<string> RevitUniqueIds { get; }
    public ActivityChangeType ChangeType { get; }

    /// <summary>
    /// Elementos que quedan en la actividad, cuando quien publica ya los tiene en memoria.
    /// Con ellos los suscriptores actualizan la vista sin consultar la base de datos, que se comparte
    /// con el guardado automático y no admite dos operaciones a la vez.
    /// </summary>
    public IReadOnlyCollection<LinkedElement>? Elements { get; }

    public ActivityChangedEvent(
        Guid budgetId,
        string activityCode,
        IReadOnlyCollection<string> revitUniqueIds,
        ActivityChangeType changeType,
        IReadOnlyCollection<LinkedElement>? elements = null)
    {
        BudgetId = budgetId;
        ActivityCode = activityCode;
        RevitUniqueIds = revitUniqueIds ?? Array.Empty<string>();
        ChangeType = changeType;
        Elements = elements;
    }
}
