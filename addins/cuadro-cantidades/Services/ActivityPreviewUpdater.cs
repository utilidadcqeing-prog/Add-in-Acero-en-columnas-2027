using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;

namespace CQEING_CuadroCantidades.Services;

public sealed class ActivityPreviewUpdater : IActivityPreviewUpdater
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ActivityPreviewUpdater(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task UpdatePreviewAsync(Guid budgetId, string activityCode, ActivityImageSnapshot snapshot)
    {
        if (snapshot == null) return;

        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var items = await unitOfWork.BudgetItems.FindAsync(i => i.BudgetId == budgetId && i.Code == activityCode);
        var budgetItem = items.FirstOrDefault();
        if (budgetItem == null) return;

        // Load existing snapshots
        List<ActivityImageSnapshot> snapshotsList = null;
        if (!string.IsNullOrEmpty(budgetItem.PreviewImagesJson))
        {
            try
            {
                snapshotsList = JsonSerializer.Deserialize<List<ActivityImageSnapshot>>(budgetItem.PreviewImagesJson);
            }
            catch { }
        }
        snapshotsList ??= new List<ActivityImageSnapshot>();

        snapshotsList.Add(snapshot);
        string newJson = JsonSerializer.Serialize(snapshotsList);

        // Update database item
        budgetItem.PreviewImagePath = snapshot.DebugImagePath;
        budgetItem.LastPreviewUpdate = snapshot.CapturedAt;
        budgetItem.ElementIdsSnapshot = snapshot.ElementIds;
        budgetItem.PreviewImagesJson = newJson;

        unitOfWork.BudgetItems.Update(budgetItem);
        await unitOfWork.SaveChangesAsync();
    }
}
