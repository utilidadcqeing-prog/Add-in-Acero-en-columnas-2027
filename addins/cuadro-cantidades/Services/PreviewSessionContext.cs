using System;
using System.Collections.Generic;
using System.Linq;
using CQEING_CuadroCantidades.Core.Application.Interfaces;

namespace CQEING_CuadroCantidades.Services;

public sealed class PreviewSessionContext : IPreviewSessionContext
{
    public Guid? ActiveBudgetItemId { get; set; }
    public string? ActiveActivityCode { get; set; }
    
    private readonly HashSet<string> _elementUniqueIds = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> ElementUniqueIds 
    { 
        get => _elementUniqueIds.ToList(); 
        set
        {
            _elementUniqueIds.Clear();
            if (value != null)
            {
                foreach (var id in value)
                {
                    _elementUniqueIds.Add(id);
                }
            }
        }
    }

    public void StartSession(Guid budgetItemId, string activityCode, IEnumerable<string> uniqueIds)
    {
        ActiveBudgetItemId = budgetItemId;
        ActiveActivityCode = activityCode;
        _elementUniqueIds.Clear();
        if (uniqueIds != null)
        {
            foreach (var id in uniqueIds)
            {
                _elementUniqueIds.Add(id);
            }
        }
    }

    public void UpdateElements(IEnumerable<string> uniqueIds)
    {
        _elementUniqueIds.Clear();
        if (uniqueIds != null)
        {
            foreach (var id in uniqueIds)
            {
                _elementUniqueIds.Add(id);
            }
        }
    }

    public void EndSession()
    {
        ActiveBudgetItemId = null;
        ActiveActivityCode = null;
        _elementUniqueIds.Clear();
    }
}
