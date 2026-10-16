using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CQEING.Gestor.Core;

/// <summary>
/// Una instalación del gestor: el .addin de ese año apunta a la carpeta de esa versión. Es lo que está
/// ACTIVO para Revit; no prueba que Revit la tenga cargada ahora mismo (eso solo se sabe dentro de Revit).
/// </summary>
public sealed record AddinInstalado(string Id, string Version, string Anio);

/// <summary>
/// Qué hay instalado, leído del disco: cada %APPDATA%\Autodesk\Revit\Addins\&lt;año&gt;\CQEING.&lt;id&gt;.addin cuyo
/// Assembly apunta a ...\CQEING\Addins\&lt;id&gt;\&lt;versión&gt;\&lt;año&gt;\. Lo demás (un .addin de desarrollo que
/// apunta al repo, uno dañado, uno con otro nombre) no es una instalación del gestor y se ignora sin fallar.
/// </summary>
public static class LectorEstado
{
    private static readonly Regex IdValido = new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex AnioValido = new(@"^[0-9]{4}$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<AddinInstalado> Leer(RutasGestor rutas)
    {
        if (!Directory.Exists(rutas.CarpetaAddinsRevitRaiz)) return [];

        var instalados = new List<AddinInstalado>();
        foreach (string carpetaAnio in Directory.GetDirectories(rutas.CarpetaAddinsRevitRaiz))
        {
            string anio = Path.GetFileName(carpetaAnio);
            if (!AnioValido.IsMatch(anio)) continue;

            foreach (string archivo in Directory.GetFiles(carpetaAnio, "CQEING.*.addin"))
            {
                string id = Path.GetFileNameWithoutExtension(archivo)["CQEING.".Length..];
                if (!IdValido.IsMatch(id)) continue; // RutasGestor lo rechazaría; un .addin ajeno no debe romper la lectura

                string? version = VersionApuntada(archivo, rutas.CarpetaVersiones(id), anio);
                if (version is not null)
                    instalados.Add(new AddinInstalado(id, version, anio));
            }
        }
        return instalados;
    }

    /// <summary>Versiones de ese add-in que ya están descargadas en este equipo para ese año de Revit (sin importar cuál esté activa).</summary>
    public static IReadOnlyList<string> VersionesEnDisco(RutasGestor rutas, string id, string anio)
    {
        string carpetaId = rutas.CarpetaVersiones(id);
        if (!Directory.Exists(carpetaId)) return [];

        return Directory.GetDirectories(carpetaId)
            .Select(Path.GetFileName)
            .Where(nombre => VersionAddin.Interpretar(nombre) is not null
                && Directory.Exists(rutas.CarpetaVersion(id, nombre!, anio)))
            .Select(nombre => nombre!)
            .OrderByDescending(nombre => VersionAddin.Interpretar(nombre))
            .ToList();
    }

    private static string? VersionApuntada(string archivoAddin, string carpetaVersiones, string anio)
    {
        string? assembly;
        try
        {
            assembly = XDocument.Load(archivoAddin).Descendants("Assembly").FirstOrDefault()?.Value.Trim();
        }
        catch (Exception e) when (e is XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (string.IsNullOrEmpty(assembly) || assembly.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(assembly))
            return null;

        // ...\Addins\<id>\<versión>\<año>\<dll>
        string? carpetaAnioInstalada = Path.GetDirectoryName(assembly);
        string? carpetaVersion = Path.GetDirectoryName(carpetaAnioInstalada);
        string? carpetaId = Path.GetDirectoryName(carpetaVersion);
        if (carpetaAnioInstalada is null || carpetaVersion is null || carpetaId is null
            || Path.GetFileName(carpetaAnioInstalada) != anio
            || !string.Equals(Path.GetFullPath(carpetaId), Path.GetFullPath(carpetaVersiones), StringComparison.OrdinalIgnoreCase))
            return null;

        string version = Path.GetFileName(carpetaVersion);
        return VersionAddin.Interpretar(version) is null ? null : version;
    }
}
