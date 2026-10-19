using System;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.UI.Models;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface ICurrentProjectContext
{
    Project? CurrentProject { get; set; }
    Budget? CurrentBudget { get; set; }
    BudgetRow? SelectedBudgetRow { get; set; }
    string? LastStructurePath { get; set; }
    string LoadedStructureName { get; set; }
    string NewDescription { get; set; }
    string NewUnit { get; set; }
    string RevitDocumentPath { get; set; }
    bool IsDirty { get; set; }

    event EventHandler? ContextChanged;

    void SetContext(Project project, Budget budget);
    void Clear();
}
