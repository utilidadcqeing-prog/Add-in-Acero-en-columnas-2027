using System.IO;
using System.Reflection;
using CQEING.Gestor.Core;

namespace CQEING.Gestor;

/// <summary>
/// Deja el botón del gestor (panel «Gestor») listo en cada Revit instalado, con la versión del componente que trae
/// este gestor. Es una ayuda: si no se puede (por ejemplo Revit tiene la carpeta abierta), el gestor sigue igual y se
/// reintenta al abrirlo otra vez.
/// </summary>
public static class ComplementoEnRevit
{
    public static void Registrar(RutasGestor rutas)
    {
        string paquete = Path.Combine(AppContext.BaseDirectory, "revit");
        if (!Directory.Exists(paquete)) return; // ejecutado desde el código fuente: no trae el componente

        ResultadoRegistroComplemento resultado =
            new RegistroComplementoRevit(rutas).Registrar(paquete, VersionDelGestor(typeof(ComplementoEnRevit).Assembly), DetectorRevit.AniosInstalados());
        Anotar(rutas, resultado.Problemas);
    }

    /// <summary>La versión X.Y.Z del gestor, sin el «+commit» que agrega el compilador.</summary>
    public static string VersionDelGestor(Assembly ensamblado)
    {
        string? version = ensamblado.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return (version ?? "0.0.0").Split('+')[0];
    }

    /// <summary>Deja en %LOCALAPPDATA%\CQEING\Gestor\registro-revit.log lo que no se pudo registrar, para que quien da soporte lo vea.</summary>
    private static void Anotar(RutasGestor rutas, IReadOnlyList<string> problemas)
    {
        string archivo = Path.Combine(Path.GetDirectoryName(rutas.CarpetaCache)!, "registro-revit.log");
        try
        {
            if (problemas.Count == 0) File.Delete(archivo);
            else File.WriteAllLines(archivo, problemas.Prepend($"{DateTime.Now:yyyy-MM-dd HH:mm} No se pudo registrar el botón del gestor en Revit:"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Anotar es opcional.
        }
    }
}
