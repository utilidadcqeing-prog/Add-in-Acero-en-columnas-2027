using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IManualPreviewService
{
    Task<ActivityImageSnapshot?> GenerateManualPreviewAsync(string? selectedViewUniqueId = null);
}
