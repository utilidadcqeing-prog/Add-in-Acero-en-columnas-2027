using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Application.Interfaces;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Guarda los códigos de actividades pendientes de enviar a la API en un JSON por presupuesto.
/// Las escrituras se hacen de a una para que dos guardados seguidos no compitan por el mismo archivo.
/// </summary>
public sealed class PendingActivitySyncStore : IPendingActivitySyncStore
{
    private readonly ILocalStorageService _storage;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public PendingActivitySyncStore(ILocalStorageService storage)
    {
        _storage = storage;
    }

    private static string Key(Guid budgetId) => $"pending_sync_{budgetId}";

    public async Task<IReadOnlyCollection<string>> LoadAsync(Guid budgetId)
    {
        try
        {
            return await _storage.LoadAsync<List<string>>(Key(budgetId)) ?? new List<string>();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(nameof(PendingActivitySyncStore), "No se pudieron leer las actividades pendientes", ex);
            return new List<string>();
        }
    }

    public async Task SaveAsync(Guid budgetId, IReadOnlyCollection<string> activityCodes)
    {
        await _writeLock.WaitAsync();
        try
        {
            await _storage.SaveAsync(Key(budgetId), activityCodes);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(nameof(PendingActivitySyncStore), "No se pudieron guardar las actividades pendientes", ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
