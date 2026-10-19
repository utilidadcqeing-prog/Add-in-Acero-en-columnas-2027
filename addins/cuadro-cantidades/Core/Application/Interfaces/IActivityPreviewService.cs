using System.Threading.Tasks;
using CQEING_CuadroCantidades.UI.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IActivityPreviewService
{
    Task GeneratePreviewAsync(ObservableObject viewModel, BudgetRow row);
}
