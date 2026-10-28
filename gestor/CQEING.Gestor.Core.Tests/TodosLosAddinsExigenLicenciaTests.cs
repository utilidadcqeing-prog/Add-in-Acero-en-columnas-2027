namespace CQEING.Gestor.Core.Tests;

/// <summary>
/// Vigila que ningún add-in arranque sin preguntar por la licencia (issue #189): un add-in nuevo que se olvide de la
/// línea haría que la protección no sirva para él. El componente del gestor no la exige: siempre crea su botón para que
/// se pueda pedir acceso.
/// </summary>
public class TodosLosAddinsExigenLicenciaTests
{
    [Fact]
    public void Cada_IExternalApplication_salvo_el_gestor_pregunta_por_la_licencia()
    {
        string? raiz = AppContext.BaseDirectory;
        while (raiz is not null && !File.Exists(Path.Combine(raiz, "CQEING-Addins.sln")))
            raiz = Path.GetDirectoryName(raiz);
        Assert.True(raiz is not null, "No se encontró la raíz del repositorio (CQEING-Addins.sln) desde la carpeta de las pruebas.");

        string addins = Path.Combine(raiz!, "addins");
        string[] aplicaciones = Directory.GetFiles(addins, "*.cs", SearchOption.AllDirectories)
            .Where(archivo => !EnCarpeta(addins, archivo, "bin", "obj", "gestor-revit"))
            .Where(archivo => File.ReadAllText(archivo).Contains(": IExternalApplication"))
            .ToArray();

        Assert.True(aplicaciones.Length >= 4, $"Se esperaban al menos 4 add-ins y se encontraron {aplicaciones.Length}.");
        string[] sinLicencia = aplicaciones
            .Where(archivo => !File.ReadAllText(archivo).Contains("Licencia.Vigente("))
            .Select(archivo => Path.GetRelativePath(raiz!, archivo))
            .ToArray();
        Assert.True(sinLicencia.Length == 0,
            "Estos add-ins no preguntan por la licencia al arrancar. Agregar como primera línea de OnStartup " +
            $"«if (!Licencia.Vigente(out _)) return Result.Succeeded;»: {string.Join(", ", sinLicencia)}");
    }

    private static bool EnCarpeta(string addins, string archivo, params string[] carpetas) =>
        Path.GetRelativePath(addins, archivo).Split(Path.DirectorySeparatorChar).Any(carpetas.Contains);
}
