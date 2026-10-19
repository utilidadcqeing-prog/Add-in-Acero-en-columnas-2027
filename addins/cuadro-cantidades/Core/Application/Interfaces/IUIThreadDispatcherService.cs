using System;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IUIThreadDispatcherService
{
    void Invoke(Action action);
    T Invoke<T>(Func<T> func);
}
