using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Services;

public sealed class ManualPreviewService : IManualPreviewService
{
    private readonly IRevitImageCaptureService _captureService;
    private readonly IPreviewDebugImageStorageService _debugImageStorageService;
    private readonly IPreviewSessionContext _sessionContext;

    public ManualPreviewService(
        IRevitImageCaptureService captureService,
        IPreviewDebugImageStorageService debugImageStorageService,
        IPreviewSessionContext sessionContext)
    {
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _debugImageStorageService = debugImageStorageService ?? throw new ArgumentNullException(nameof(debugImageStorageService));
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
    }

    public async Task<ActivityImageSnapshot?> GenerateManualPreviewAsync(string? selectedViewUniqueId = null)
    {
        var budgetItemId = _sessionContext.ActiveBudgetItemId;
        var activityCode = _sessionContext.ActiveActivityCode;
        var uniqueIds = _sessionContext.ElementUniqueIds.ToList();

        if (budgetItemId == null || string.IsNullOrEmpty(activityCode) || uniqueIds.Count == 0)
        {
            return null;
        }

        var tcs = new TaskCompletionSource<string>();
        RevitEventExecutor.Execute(app =>
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    tcs.SetResult(string.Empty);
                    return;
                }
                string path = _captureService.CaptureView(doc, uniqueIds, activityCode, selectedViewUniqueId);
                tcs.SetResult(path);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        string tempCapturedPath = await tcs.Task;
        if (string.IsNullOrEmpty(tempCapturedPath) || !File.Exists(tempCapturedPath))
        {
            return null;
        }

        var sortedIds = uniqueIds.OrderBy(id => id).ToList();
        string idsSnapshot = string.Join(",", sortedIds);
        DateTime updateTime = DateTime.Now;

        var snapshot = ActivityImageSnapshotFactory.CreateFromCapturedFile(
            tempCapturedPath,
            activityCode,
            updateTime,
            idsSnapshot,
            _debugImageStorageService);

        try
        {
            if (File.Exists(tempCapturedPath))
            {
                File.Delete(tempCapturedPath);
            }
        }
        catch { }

        return snapshot;
    }
}
