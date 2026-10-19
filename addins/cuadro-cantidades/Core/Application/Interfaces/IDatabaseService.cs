using System.Threading.Tasks;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IDatabaseService
{
    Task InitializeDatabaseAsync();
}
