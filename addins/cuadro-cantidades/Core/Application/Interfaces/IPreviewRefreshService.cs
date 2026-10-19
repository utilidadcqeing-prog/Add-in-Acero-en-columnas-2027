using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IPreviewRefreshService
{
    Task<ActivityImageSnapshot?> RefreshPreviewAsync(Guid budgetId, string activityCode, IEnumerable<string> uniqueIds, string? selectedViewUniqueId = null);
}
