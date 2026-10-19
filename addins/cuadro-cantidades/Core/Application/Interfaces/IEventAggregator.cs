using System;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IEventAggregator
{
    void Publish<TEvent>(TEvent eventMessage);
    void Subscribe<TEvent>(Action<TEvent> action);
    void Unsubscribe<TEvent>(Action<TEvent> action);
}
