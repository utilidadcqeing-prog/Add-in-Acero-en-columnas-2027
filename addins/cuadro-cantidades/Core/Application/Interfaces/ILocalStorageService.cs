using System.Threading.Tasks;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface ILocalStorageService
{
    Task SaveAsync<T>(string key, T data);
    Task<T?> LoadAsync<T>(string key);
    bool Exists(string key);
    void Delete(string key);
}
