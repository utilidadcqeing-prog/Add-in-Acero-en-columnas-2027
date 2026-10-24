using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace CQEING.GestorRevit;

/// <summary>Dónde está el gestor instalado con Velopack: la misma ruta a la que apunta su acceso directo del menú Inicio.</summary>
internal static class LanzadorGestor
{
    public const string Titulo = "Gestor de add-ins CQEING";
    private const string EnlaceDescarga = "https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/releases";

    /// <summary>%LOCALAPPDATA%\CQEING.Gestor\current\CQEING.Gestor.exe, o null si el gestor no está instalado.</summary>
    public static string? Ruta()
    {
        string ruta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CQEING.Gestor", "current", "CQEING.Gestor.exe");
        return File.Exists(ruta) ? ruta : null;
    }

    /// <summary>Abre el gestor. Lo usan el botón y el aviso. Devuelve qué decirle al usuario si no se pudo, o null si se abrió.</summary>
    public static string? Abrir()
    {
        string? gestor = Ruta();
        if (gestor is null)
            return "El gestor no está instalado en este equipo.\n\n" +
                "Descargue «CQEING.Gestor-win-Setup.exe» de la última versión del gestor en:\n" + EnlaceDescarga;

        try
        {
            Process.Start(new ProcessStartInfo(gestor) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(gestor)! });
            return null;
        }
        catch (Exception e) when (e is Win32Exception or IOException)
        {
            return $"No se pudo abrir el gestor:\n{e.Message}";
        }
    }
}
