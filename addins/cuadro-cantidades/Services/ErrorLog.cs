using System;
using System.IO;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Registro de errores en archivo para diagnosticar fallos que no llegan a mostrarse al usuario.
/// Escribe en %AppData%\CQEING_CuadroCantidades\logs\errors.log.
/// </summary>
public static class ErrorLog
{
    private static readonly object _sync = new();

    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CQEING_CuadroCantidades",
        "logs",
        "errors.log"
    );

    /// <summary>
    /// Agrega una línea al log con la fuente, el mensaje y, si hay, el tipo y detalle de la excepción.
    /// Nunca lanza: un fallo al escribir el log no debe interrumpir la operación que lo reporta.
    /// </summary>
    public static void Write(string source, string message, Exception? exception = null)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {message}";
        if (exception != null)
            line += $"{Environment.NewLine}    {exception}";

        System.Diagnostics.Debug.WriteLine(line);

        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // El log es solo diagnóstico
        }
    }
}
