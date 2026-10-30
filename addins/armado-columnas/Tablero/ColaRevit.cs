namespace practicacq.Tablero;

using System;
using System.Collections.Concurrent;
using System.IO;
using Autodesk.Revit.UI;

/// <summary>
/// Lo que la ventana de columnas hace en Revit, en el hilo de Revit: la
/// ventana no tiene modo, así que todo pasa por un <see cref="ExternalEvent"/>.
/// Es una cola de trabajos (los mensajes de la página: la sesión lee el
/// modelo; generar; la vista 3D). Se crea dentro de un comando, donde la API
/// es válida. Como <c>AccionesRevit</c> de vigas.
/// </summary>
internal sealed class ColaRevit : IExternalEventHandler, IDisposable
{
    private readonly ConcurrentQueue<Action<UIApplication>> cola = new();
    private readonly ExternalEvent evento;

    public ColaRevit() => evento = ExternalEvent.Create(this);

    /// <summary>Un trabajo que falló, con su mensaje, para mostrarlo en la página.</summary>
    public event Action<string>? Fallo;

    /// <summary>Corre <paramref name="trabajo"/> en el hilo de Revit, cuando Revit quede libre.</summary>
    public void Encolar(Action<UIApplication> trabajo)
    {
        cola.Enqueue(trabajo);
        evento.Raise();
    }

    public void Execute(UIApplication app)
    {
        while (cola.TryDequeue(out Action<UIApplication>? trabajo))
        {
            // Nada se escapa hacia Revit: un error se muestra en la página y la cola sigue.
            try
            {
                trabajo(app);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Registrar(e);
                Fallo?.Invoke(e.Message);
            }
        }
    }

    /// <summary><c>%AppData%\CQEING\columnas\errores.log</c>: el error completo, para saber dónde falló.</summary>
    public static string RutaErrores { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CQEING", "columnas", "errores.log");

    private static void Registrar(Exception e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaErrores)!);
            File.AppendAllText(RutaErrores, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{e}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public string GetName() => "Acero en columnas";

    public void Dispose()
    {
        cola.Clear();
        evento.Dispose();
    }
}
