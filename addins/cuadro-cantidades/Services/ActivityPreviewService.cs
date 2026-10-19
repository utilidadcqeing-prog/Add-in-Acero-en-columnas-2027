using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CQEING_CuadroCantidades.Core;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.Core.Domain.Interfaces;
using CQEING_CuadroCantidades.UI.Models;
using CQEING_CuadroCantidades.UI.ViewModels;

namespace CQEING_CuadroCantidades.Services;

public sealed class ActivityPreviewService : IActivityPreviewService
{
    private readonly IRevitImageCaptureService _captureService;
    private readonly IPreviewDebugImageStorageService _debugImageStorageService;
    private readonly IUnitOfWork _unitOfWork;

    public ActivityPreviewService(
        IRevitImageCaptureService captureService,
        IPreviewDebugImageStorageService debugImageStorageService,
        IUnitOfWork unitOfWork
    )
    {
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _debugImageStorageService =
            debugImageStorageService
            ?? throw new ArgumentNullException(nameof(debugImageStorageService));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task GeneratePreviewAsync(ObservableObject viewModel, BudgetRow row)
    {
        if (row == null || row.Type != "ACTIVIDAD")
            return;
        if (viewModel is not MainWindowViewModel mainVm)
            return;

        // Run entire orchestration on background thread to ensure UI is never blocked
        await Task.Run(async () =>
        {
            try
            {
                if (mainVm.CurrentBudget == null)
                    return;
                var budgetId = mainVm.CurrentBudget.Id;

                // 1. Fetch budget item to get associated elements from database
                var items = await _unitOfWork.BudgetItems.FindAsync(i =>
                    i.BudgetId == budgetId && i.Code == row.Code
                );
                var budgetItem = items.FirstOrDefault();
                if (budgetItem == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ActivityPreviewService] BudgetItem not found for activity {row.Code} in DB. Save budget first."
                    );
                    return;
                }

                var linkedElements = await _unitOfWork.LinkedElements.FindAsync(le =>
                    le.BudgetItemId == budgetItem.Id
                );

                var uniqueIds = linkedElements.Select(le => le.RevitUniqueId).ToList();
                if (uniqueIds.Count == 0)
                {
                    // If no elements are linked, delete and clear all preview images in history
                    if (!string.IsNullOrEmpty(budgetItem.PreviewImagesJson))
                    {
                        try
                        {
                            var existingSnapshots = System.Text.Json.JsonSerializer.Deserialize<
                                List<ActivityImageSnapshot>
                            >(budgetItem.PreviewImagesJson);
                            if (existingSnapshots != null)
                            {
                                foreach (var snap in existingSnapshots)
                                {
                                    _debugImageStorageService.DeleteDebugCopy(
                                        snap.DebugImagePath ?? snap.ImagePath
                                    );
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[ActivityPreviewService] Error deleting old images: {ex.Message}"
                            );
                        }
                    }

                    // Update UI properties safely on the Dispatcher thread
                    mainVm.Dispatcher.Invoke(() =>
                    {
                        row.PreviewImagePath = null;
                        row.LastPreviewUpdate = null;
                        row.ElementIdsSnapshot = null;
                        row.PreviewImagesJson = null;
                    });

                    // Update database item
                    budgetItem.PreviewImagePath = null;
                    budgetItem.LastPreviewUpdate = null;
                    budgetItem.ElementIdsSnapshot = null;
                    budgetItem.PreviewImagesJson = null;
                    _unitOfWork.BudgetItems.Update(budgetItem);
                    await _unitOfWork.SaveChangesAsync();
                    return;
                }

                // 2. Schedule capture on the Revit main thread
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

                        string path = _captureService.CaptureView(doc, uniqueIds, row.Code);
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
                    System.Diagnostics.Debug.WriteLine(
                        "[ActivityPreviewService] Image capture did not return a valid file path."
                    );
                    return;
                }

                var sortedIds = uniqueIds.OrderBy(id => id).ToList();
                string idsSnapshot = string.Join(",", sortedIds);
                DateTime updateTime = DateTime.Now;

                var newSnapshot = ActivityImageSnapshotFactory.CreateFromCapturedFile(
                    tempCapturedPath,
                    row.Code,
                    updateTime,
                    idsSnapshot,
                    _debugImageStorageService
                );

                // Clean up the temp file
                try
                {
                    if (File.Exists(tempCapturedPath))
                    {
                        File.Delete(tempCapturedPath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ActivityPreviewService] Error deleting temp image: {ex.Message}"
                    );
                }

                // Deserialise existing snapshots
                List<ActivityImageSnapshot> snapshotsList = null;
                if (!string.IsNullOrEmpty(budgetItem.PreviewImagesJson))
                {
                    try
                    {
                        snapshotsList = System.Text.Json.JsonSerializer.Deserialize<
                            List<ActivityImageSnapshot>
                        >(budgetItem.PreviewImagesJson);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ActivityPreviewService] Error deserializing existing snapshots: {ex.Message}"
                        );
                    }
                }
                snapshotsList ??= new List<ActivityImageSnapshot>();

                snapshotsList.Add(newSnapshot);

                // Serialize back to JSON
                string newJson = System.Text.Json.JsonSerializer.Serialize(snapshotsList);

                // 4. Update UI Model properties on dispatcher
                mainVm.Dispatcher.Invoke(() =>
                {
                    row.PreviewImagePath = newSnapshot.DebugImagePath;
                    row.LastPreviewUpdate = updateTime;
                    row.ElementIdsSnapshot = idsSnapshot;
                    row.PreviewImagesJson = newJson;
                });

                // 5. Update DB entity and save changes
                budgetItem.PreviewImagePath = newSnapshot.DebugImagePath;
                budgetItem.LastPreviewUpdate = updateTime;
                budgetItem.ElementIdsSnapshot = idsSnapshot;
                budgetItem.PreviewImagesJson = newJson;

                _unitOfWork.BudgetItems.Update(budgetItem);
                await _unitOfWork.SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine(
                    $"[ActivityPreviewService] Generated and saved preview snapshot for {row.Code} in database. Debug copy: {newSnapshot.DebugImagePath}. Total snapshots: {snapshotsList.Count}"
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ActivityPreviewService] Error during preview generation: {ex.Message}"
                );
            }
        });
    }
}
