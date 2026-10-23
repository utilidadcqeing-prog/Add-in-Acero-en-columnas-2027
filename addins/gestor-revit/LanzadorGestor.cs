using System.IO;

namespace CQEING.GestorRevit;

/// <summary>Dónde está el gestor instalado con Velopack: la misma ruta a la que apunta su acceso directo del menú Inicio.</summary>
internal static class LanzadorGestor
{
    /// <summary>%LOCALAPPDATA%\CQEING.Gestor\current\CQEING.Gestor.exe, o null si el gestor no está instalado.</summary>
    public static string? Ruta()
    {
        string ruta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEING.Gestor", "current", "CQEING.Gestor.exe");
        return File.Exists(ruta) ? ruta : null;
    }
}
