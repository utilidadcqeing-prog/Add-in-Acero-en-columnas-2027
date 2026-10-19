using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace CQEING_CuadroCantidades.Core;

/// <summary>
/// Proporciona un mecanismo seguro para ejecutar código que interactúa con la API de Revit
/// desde hilos externos (como la interfaz de usuario Modeless) a través de eventos externos.
/// </summary>
public static class RevitEventExecutor
{
    /// <summary>
    /// Evento externo de Revit utilizado para activar la ejecución en el hilo principal.
    /// </summary>
    private static ExternalEvent? _externalEvent;

    /// <summary>
    /// Manejador del evento externo que gestiona la cola de tareas.
    /// </summary>
    private static RevitEventHandler? _handler;

    /// <summary>
    /// Inicializa el ejecutor de eventos. Debe llamarse en el hilo principal de Revit durante la inicialización del Add-in.
    /// </summary>
    public static void Initialize()
    {
        if (_externalEvent is not null)
        {
            return;
        }

        _handler = new RevitEventHandler();
        _externalEvent = ExternalEvent.Create(_handler);
    }

    /// <summary>
    /// Ejecuta una acción de forma asíncrona en el contexto del hilo principal de Revit.
    /// </summary>
    /// <param name="action">La acción a ejecutar que recibe la <see cref="UIApplication"/> de Revit.</param>
    /// <exception cref="InvalidOperationException">Se lanza si el ejecutor no ha sido inicializado previamente.</exception>
    public static void Execute(Action<UIApplication> action)
    {
        if (_handler is null || _externalEvent is null)
        {
            throw new InvalidOperationException("RevitEventExecutor no ha sido inicializado.");
        }

        _handler.Enqueue(action);
        _externalEvent.Raise();
    }

    /// <summary>
    /// Igual que <see cref="Execute"/>, pero devuelve una tarea que termina cuando Revit ya ejecutó la acción.
    /// Sirve cuando el código siguiente depende de lo que la acción lee o modifica.
    /// </summary>
    /// <param name="action">La acción a ejecutar que recibe la <see cref="UIApplication"/> de Revit.</param>
    public static Task ExecuteAsync(Action<UIApplication> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Execute(app =>
        {
            try
            {
                action(app);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    /// <summary>
    /// Manejador de eventos personalizado que implementa <see cref="IExternalEventHandler"/>
    /// para procesar acciones en cola de manera segura en el hilo principal de Revit.
    /// </summary>
    private sealed class RevitEventHandler : IExternalEventHandler
    {
        /// <summary>
        /// Cola concurrente de tareas pendientes por ejecutar.
        /// </summary>
        private readonly ConcurrentQueue<Action<UIApplication>> _tasks = new();

        /// <summary>
        /// Agrega una acción a la cola de ejecución.
        /// </summary>
        /// <param name="action">La acción que interactúa con la API de Revit.</param>
        public void Enqueue(Action<UIApplication> action)
        {
            _tasks.Enqueue(action);
        }

        /// <summary>
        /// Método invocado por Revit en su hilo principal cuando se activa el evento externo.
        /// Procesa y ejecuta secuencialmente todas las acciones pendientes.
        /// </summary>
        /// <param name="app">La aplicación de Revit activa.</param>
        public void Execute(UIApplication app)
        {
            // Desencolar y ejecutar cada tarea de forma segura
            while (_tasks.TryDequeue(out Action<UIApplication>? action))
            {
                try
                {
                    action?.Invoke(app);
                }
                catch (Exception exception)
                {
                    // Evitar que una tarea fallida detenga el procesamiento de las demás.
                    // Se reporta el error en la salida de depuración.
                    System.Diagnostics.Debug.WriteLine($"Error en RevitEventExecutor: {exception}");
                }
            }
        }

        /// <summary>
        /// Obtiene el nombre que identifica a este manejador de eventos externos.
        /// </summary>
        /// <returns>Nombre descriptivo del manejador.</returns>
        public string GetName() => "CQEING Cuadro Cantidades Event Handler";
    }
}
