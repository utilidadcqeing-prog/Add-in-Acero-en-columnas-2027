using System.Diagnostics;

namespace CQEING.Publicador;

public sealed class CompiladorDotnet : ICompilador
{
    public string Compilar(string rutaProyecto, string anio, string version)
    {
        // Misma carpeta que fija addins/Directory.Build.props (OutputPath). Se vacía antes para que
        // nada de una compilación anterior se cuele en el ZIP.
        string salida = Path.Combine(Path.GetDirectoryName(rutaProyecto)!, "bin", "Release", anio);
        if (Directory.Exists(salida)) Directory.Delete(salida, recursive: true);

        var inicio = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (string argumento in new[] { "build", rutaProyecto, "-c", "Release", $"-p:RevitVersion={anio}", $"-p:Version={version}" })
            inicio.ArgumentList.Add(argumento);

        using Process proceso = Process.Start(inicio) ?? throw new InvalidOperationException("No se pudo ejecutar dotnet.");
        proceso.WaitForExit();
        if (proceso.ExitCode != 0)
            throw new InvalidOperationException($"Falló la compilación de {Path.GetFileName(rutaProyecto)} para Revit {anio}.");
        return salida;
    }
}
