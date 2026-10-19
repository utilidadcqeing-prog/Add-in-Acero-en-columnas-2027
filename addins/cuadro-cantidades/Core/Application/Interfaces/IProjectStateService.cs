using System.Collections.Generic;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Domain.Entities;
using CQEING_CuadroCantidades.UI.Models;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IProjectStateService
{
    Task SaveStateAsync();
    Task<bool> RestoreStateAsync();
    Task<AppState?> GetSavedStateAsync();
    Task SaveStructuralDataAsync(IEnumerable<BudgetRow> budgetRows);
}
