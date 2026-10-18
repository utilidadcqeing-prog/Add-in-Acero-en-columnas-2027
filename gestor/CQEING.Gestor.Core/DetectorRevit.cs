namespace CQEING.Gestor.Core;

public static class DetectorRevit
{
    /// <summary>
    /// Años de Revit instalados: carpetas «Revit AAAA» de Autodesk que traen RevitAPI.dll. Una carpeta con nombre
    /// parecido (Revit Viewer, Revit Assistant 2027) o sin la API no cuenta. Recibe Program Files para poder probarlo.
    /// </summary>
    public static IReadOnlyList<string> AniosInstalados(string programFiles)
    {
        string autodesk = Path.Combine(programFiles, "Autodesk");
        if (!Directory.Exists(autodesk)) return [];

        return Directory.GetDirectories(autodesk, "Revit *")
            .Select(carpeta => Path.GetFileName(carpeta)["Revit ".Length..])
            .Where(anio => anio.Length == 4 && anio.All(char.IsAsciiDigit)
                && File.Exists(Path.Combine(autodesk, $"Revit {anio}", "RevitAPI.dll")))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Program Files de 64 bits de este equipo (Revit solo se instala en 64 bits).</summary>
    public static IReadOnlyList<string> AniosInstalados() =>
        AniosInstalados(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
}
