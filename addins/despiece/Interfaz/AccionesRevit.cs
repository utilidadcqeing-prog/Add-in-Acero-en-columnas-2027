using System.Collections.Concurrent;
using System.IO;
using Autodesk.Revit.UI;

namespace CQEING.Despiece.Interfaz;

/// <summary>Cola de trabajos para el hilo de Revit: la ventana no tiene modo, así que todo pasa por un <see cref="ExternalEvent"/>. Se crea dentro de un comando.</summary>
internal sealed class AccionesRevit : IExternalEventHandler, IDisposable
{
    private readonly ConcurrentQueue<Action<UIApplication>> cola = new();
    private readonly ExternalEvent evento;

    public AccionesRevit() => evento = ExternalEvent.Create(this);

    /// <summary>Un trabajo que falló, con su mensaje, para mostrarlo en la página.</summary>
    public event Action<string>? Fallo;

    public void Encolar(Action<UIApplication> trabajo)
    {
        cola.Enqueue(trabajo);
        evento.Raise();
    }

    /// <summary>
    /// Un trabajo por vez, y Revit queda libre entre uno y otro: la ventana vive en su mismo hilo, así que si se hacen
    /// varios seguidos la ventana no responde hasta que terminen (las plantas se generan de fondo, #221).
    /// </summary>
    public void Execute(UIApplication app)
    {
        if (cola.TryDequeue(out Action<UIApplication>? trabajo))
        {
            // Nada se escapa hacia Revit: un error se muestra en la página y la cola sigue.
            try
            {
                Registro.Paso("trabajo: empieza");
                trabajo(app);
                Registro.Paso("trabajo: termina");
            }
            catch (Exception e)
            {
                Registro.Paso("trabajo: FALLA " + e.GetType().Name + ": " + e.Message);
                Registrar(e);
                Fallo?.Invoke(e.Message);
            }
        }
        // Raise varias veces antes de Execute cuenta como una: si quedan trabajos, se pide otra vuelta.
        if (!cola.IsEmpty)
            evento.Raise();
    }

    /// <summary><c>%AppData%\CQEING\despiece\errores.log</c>: el error completo, para saber dónde falló.</summary>
    public static string RutaErrores { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CQEING", "despiece", "errores.log");

    private static void Registrar(Exception e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaErrores)!);
            File.AppendAllText(RutaErrores, $"{DateTime.Now:s} {e}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Si no se puede escribir, se sigue igual.
        }
    }

    public string GetName() => "CQEING Despiece";

    public void Dispose() => evento.Dispose();
}
