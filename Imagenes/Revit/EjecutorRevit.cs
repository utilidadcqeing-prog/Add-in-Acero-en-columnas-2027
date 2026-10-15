using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace practicacq.Imagenes.Revit;

/// <summary>
/// Cola de acciones que necesitan contexto de API de Revit, pedidas desde la ventana no modal.
/// Copiado de <c>Core/RevitEventExecutor.cs</c> del add-in de cantidades: un solo
/// <see cref="ExternalEvent"/> para todo el add-in de imágenes, en vez de un handler por acción.
/// </summary>
public static class EjecutorRevit
{
    private static ExternalEvent? _evento;
    private static Manejador? _manejador;

    /// <summary>Crea el <see cref="ExternalEvent"/>. Llamar desde un contexto de API (el comando).</summary>
    public static void Inicializar()
    {
        if (_evento is not null) return;
        _manejador = new Manejador();
        _evento = ExternalEvent.Create(_manejador);
    }

    public static void Ejecutar(Action<UIApplication> accion)
    {
        if (_manejador is null || _evento is null)
            throw new InvalidOperationException("EjecutorRevit no fue inicializado.");

        _manejador.Encolar(accion);
        _evento.Raise();
    }

    private sealed class Manejador : IExternalEventHandler
    {
        private readonly ConcurrentQueue<Action<UIApplication>> _cola = new();

        public void Encolar(Action<UIApplication> accion) => _cola.Enqueue(accion);

        public void Execute(UIApplication app)
        {
            while (_cola.TryDequeue(out Action<UIApplication>? accion))
            {
                try
                {
                    accion(app);
                }
                catch (Exception ex)
                {
                    // Cada acción reporta sus propios errores por su callback; esto es solo la red
                    // para que una acción rota no deje sin ejecutar a las siguientes.
                    System.Diagnostics.Debug.WriteLine($"[EjecutorRevit] {ex}");
                }
            }
        }

        public string GetName() => "CQEING Imágenes por actividad";
    }
}
