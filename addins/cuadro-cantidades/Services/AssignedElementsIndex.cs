using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CQEING_CuadroCantidades.Core.Application.Interfaces;

namespace CQEING_CuadroCantidades.Services;

public sealed class AssignedElementsIndex : IAssignedElementsIndex
{
    private readonly ConcurrentDictionary<string, Guid> _index = new(StringComparer.OrdinalIgnoreCase);

    public void Clear()
    {
        _index.Clear();
    }

    public void Add(string uniqueId, Guid budgetItemId)
    {
        if (string.IsNullOrEmpty(uniqueId)) return;
        _index[uniqueId] = budgetItemId;
    }

    public void AddRange(IEnumerable<string> uniqueIds, Guid budgetItemId)
    {
        if (uniqueIds == null) return;
        foreach (var uid in uniqueIds)
        {
            Add(uid, budgetItemId);
        }
    }

    public void Remove(string uniqueId)
    {
        if (string.IsNullOrEmpty(uniqueId)) return;
        _index.TryRemove(uniqueId, out _);
    }

    public void RemoveRange(IEnumerable<string> uniqueIds)
    {
        if (uniqueIds == null) return;
        foreach (var uid in uniqueIds)
        {
            Remove(uid);
        }
    }

    public bool IsAssigned(string uniqueId)
    {
        if (string.IsNullOrEmpty(uniqueId)) return false;
        return _index.ContainsKey(uniqueId);
    }

    public bool TryGetAssignment(string uniqueId, out Guid budgetItemId)
    {
        if (string.IsNullOrEmpty(uniqueId))
        {
            budgetItemId = Guid.Empty;
            return false;
        }
        return _index.TryGetValue(uniqueId, out budgetItemId);
    }

    public IReadOnlySet<string> GetAllAssignedUniqueIds()
    {
        return new HashSet<string>(_index.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
