using System;
using System.Collections.Generic;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IAssignedElementsIndex
{
    void Clear();
    void Add(string uniqueId, Guid budgetItemId);
    void AddRange(IEnumerable<string> uniqueIds, Guid budgetItemId);
    void Remove(string uniqueId);
    void RemoveRange(IEnumerable<string> uniqueIds);
    bool IsAssigned(string uniqueId);
    bool TryGetAssignment(string uniqueId, out Guid budgetItemId);
    IReadOnlySet<string> GetAllAssignedUniqueIds();
}
