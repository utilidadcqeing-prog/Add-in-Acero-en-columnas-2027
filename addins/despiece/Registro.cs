using System.IO;

namespace CQEING.Despiece;

/// <summary>
/// La traza del despiece: <c>%AppData%\CQEING\despiece\traza.log</c>, una línea por paso, escrita al momento. Si Revit se
/// cae a mitad de algo, el último paso anotado dice dónde (#221: el 30-oct Revit se cerró sin dejar ningún error).
/// </summary>
internal static class Registro
{
    public static string Ruta { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CQEING", "despiece", "traza.log");

    /// <summary>Anota un paso. Nunca falla: un registro que tumbe el add-in no sirve.</summary>
    public static void Paso(string texto)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            File.AppendAllText(Ruta, $"{DateTime.Now:HH:mm:ss.fff} {texto}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Sin disco para escribir se sigue igual.
        }
    }
}
