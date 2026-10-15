using System.Text.RegularExpressions;

namespace CQEING.Gestor.Core;

/// <summary>
/// Dónde vive cada cosa del gestor. Recibe las carpetas base para poder probarlo sin tocar las del usuario.
/// El id, la versión y el año terminan en carpetas que el gestor crea y BORRA, así que solo se aceptan con su
/// formato exacto (sin barras ni «..»); cualquier otro valor es un error de programación y lanza ArgumentException.
/// </summary>
public sealed class RutasGestor(string localAppData, string appData)
{
    private static readonly Regex IdValido = new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex AnioValido = new(@"^[0-9]{4}$", RegexOptions.CultureInvariant);

    public static RutasGestor DelUsuario() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public string CarpetaAddins => Path.Combine(localAppData, "CQEING", "Addins");

    public string CarpetaCache => Path.Combine(localAppData, "CQEING", "Gestor", "cache");

    public string CarpetaVersiones(string id) => Path.Combine(CarpetaAddins, ValidarId(id));

    /// <summary>Una carpeta por versión y año: instalar con Revit abierto no choca con la DLL que Revit tiene cargada.</summary>
    public string CarpetaVersion(string id, string version, string anio) =>
        Path.Combine(CarpetaVersiones(id), ValidarVersion(version), ValidarAnio(anio));

    public string CarpetaAddinsRevitRaiz => Path.Combine(appData, "Autodesk", "Revit", "Addins");

    public string CarpetaAddinsRevit(string anio) => Path.Combine(CarpetaAddinsRevitRaiz, ValidarAnio(anio));

    public string ArchivoAddin(string id, string anio) => Path.Combine(CarpetaAddinsRevit(anio), $"CQEING.{ValidarId(id)}.addin");

    private static string ValidarId(string id) =>
        id is not null && IdValido.IsMatch(id) ? id : throw new ArgumentException($"«{id}» no es un id de add-in válido.", nameof(id));

    private static string ValidarAnio(string anio) =>
        anio is not null && AnioValido.IsMatch(anio) ? anio : throw new ArgumentException($"«{anio}» no es un año de Revit válido.", nameof(anio));

    private static string ValidarVersion(string version) =>
        VersionAddin.Interpretar(version) is not null ? version : throw new ArgumentException($"«{version}» no es una versión X.Y.Z válida.", nameof(version));
}
