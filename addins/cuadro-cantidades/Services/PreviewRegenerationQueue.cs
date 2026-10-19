using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Application.Interfaces;

namespace CQEING_CuadroCantidades.Services;

public sealed class PreviewRegenerationQueue : IPreviewRegenerationQueue, IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingTasks = new();
    private readonly SemaphoreSlim _executionSemaphore = new(1, 1);
    private readonly int _debounceMs = 800; // Debounce time in ms
    private bool _isDisposed;

    public void Enqueue(string activityCode, Func<CancellationToken, Task> taskFactory)
    {
        if (_isDisposed) return;
        if (string.IsNullOrEmpty(activityCode)) return;

        // Cancel previous task for the same activity code
        Cancel(activityCode);

        var cts = new CancellationTokenSource();
        _pendingTasks[activityCode] = cts;

        // Start processing asynchronously without blocking the caller
        _ = ProcessQueueItemAsync(activityCode, taskFactory, cts);
    }

    private async Task ProcessQueueItemAsync(string activityCode, Func<CancellationToken, Task> taskFactory, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            // 1. Debounce phase
            await Task.Delay(_debounceMs, token);

            // 2. Serialized execution phase
            await _executionSemaphore.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                await taskFactory(token);
            }
            finally
            {
                _executionSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[PreviewRegenerationQueue] Capture task cancelled for activity {activityCode}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PreviewRegenerationQueue] Error executing capture for activity {activityCode}: {ex.Message}");
        }
        finally
        {
            // Remove the CTS if it's the current one
            if (_pendingTasks.TryGetValue(activityCode, out var currentCts) && currentCts == cts)
            {
                _pendingTasks.TryRemove(activityCode, out _);
                cts.Dispose();
            }
        }
    }

    public void Cancel(string activityCode)
    {
        if (_pendingTasks.TryRemove(activityCode, out var cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { }
        }
    }

    public void CancelAll()
    {
        foreach (var key in _pendingTasks.Keys)
        {
            Cancel(key);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        CancelAll();
        _executionSemaphore.Dispose();
    }
}
