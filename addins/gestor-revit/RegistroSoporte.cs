using System.IO;
using CQEING.Gestor.Core;

namespace CQEING.GestorRevit;

/// <summary>
/// Archivos de %LOCALAPPDATA%\CQEING\Gestor\ que explican, a quien da soporte, por qué algo que se hace en segundo plano
/// al abrir Revit no salió. Se guarda solo el último problema; cuando sale bien, el archivo se borra.
/// </summary>
internal static class RegistroSoporte
{
    public static void Anotar(string nombreArchivo, string? problema)
    {
        string archivo = Path.Combine(Path.GetDirectoryName(RutasGestor.DelUsuario().CarpetaCache)!, nombreArchivo);
        try
        {
            if (problema is null) File.Delete(archivo);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(archivo)!);
                File.WriteAllText(archivo, $"{DateTime.Now:yyyy-MM-dd HH:mm} {problema}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Anotar es opcional.
        }
    }
}
