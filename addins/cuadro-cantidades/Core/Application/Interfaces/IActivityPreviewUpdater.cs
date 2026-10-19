using System;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IActivityPreviewUpdater
{
    Task UpdatePreviewAsync(Guid budgetId, string activityCode, ActivityImageSnapshot snapshot);
}
