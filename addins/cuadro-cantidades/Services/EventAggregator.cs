using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CQEING_CuadroCantidades.Core.Application.Interfaces;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Implementación de un agregador de eventos (<see cref="IEventAggregator"/>) thread-safe que permite la comunicación desacoplada 
/// entre componentes mediante el patrón Publicador/Suscriptor (Pub/Sub).
/// </summary>
public sealed class EventAggregator : IEventAggregator
{
    // Diccionario concurrente para almacenar las listas de suscriptores asociadas a cada tipo de evento
    private readonly ConcurrentDictionary<Type, List<object>> _subscribers = new();

    /// <summary>
    /// Publica un mensaje de evento a todos los suscriptores registrados para el tipo de evento especificado.
    /// </summary>
    /// <typeparam name="TEvent">El tipo de evento que se va a publicar.</typeparam>
    /// <param name="eventMessage">El objeto que contiene los datos del evento.</param>
    public void Publish<TEvent>(TEvent eventMessage)
    {
        if (eventMessage == null) return;
        var eventType = typeof(TEvent);
        if (_subscribers.TryGetValue(eventType, out var list))
        {
            List<object> subscribersCopy;
            // Bloquear la lista para crear una copia segura y evitar excepciones de modificación concurrente durante la iteración
            lock (list)
            {
                subscribersCopy = new List<object>(list);
            }
            foreach (var subscriber in subscribersCopy)
            {
                if (subscriber is Action<TEvent> action)
                {
                    try
                    {
                        // Invocar la acción registrada por el suscriptor
                        action(eventMessage);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[EventAggregator] Error executing subscriber action: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Registra una acción de devolución de llamada (callback) que se ejecutará cuando se publique un evento del tipo especificado.
    /// </summary>
    /// <typeparam name="TEvent">El tipo de evento al que suscribirse.</typeparam>
    /// <param name="action">La acción que se ejecutará al recibir el evento.</param>
    public void Subscribe<TEvent>(Action<TEvent> action)
    {
        if (action == null) return;
        var eventType = typeof(TEvent);
        
        // Obtener la lista existente para el tipo de evento o crear una nueva si no existe
        var list = _subscribers.GetOrAdd(eventType, _ => new List<object>());
        lock (list)
        {
            // Evitar duplicar suscripciones para la misma acción
            if (!list.Contains(action))
            {
                list.Add(action);
            }
        }
    }

    /// <summary>
    /// Cancela la suscripción de una acción de devolución de llamada para el tipo de evento especificado.
    /// </summary>
    /// <typeparam name="TEvent">El tipo de evento de la suscripción.</typeparam>
    /// <param name="action">La acción que se desea desvincular.</param>
    public void Unsubscribe<TEvent>(Action<TEvent> action)
    {
        if (action == null) return;
        var eventType = typeof(TEvent);
        if (_subscribers.TryGetValue(eventType, out var list))
        {
            // Remover la acción de la lista de suscriptores de forma segura
            lock (list)
            {
                list.Remove(action);
            }
        }
    }
}
