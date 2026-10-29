using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CQEING.Compartido;

/// <summary>Un add-in instalado por el gestor: la DLL vive en ...\CQEING\Addins\&lt;id&gt;\&lt;versión&gt;\&lt;año&gt;\.</summary>
internal sealed record InstalacionAddin(string Id, string Version, string Anio);

/// <summary>
/// Actualización obligatoria (docs/planes/2026-10-29-actualizacion-obligatoria.md). El gestor anota, por año de Revit,
/// la última versión publicada de cada add-in; un add-in instalado por el gestor con una versión más vieja no se deja
/// usar. Se compila dentro de cada add-in y del núcleo del gestor: el formato del archivo tiene una sola fuente.
/// </summary>
internal static class ActualizacionObligatoria
{
    private static readonly Regex PatronVersion = new(@"^([0-9]{1,9})\.([0-9]{1,9})\.([0-9]{1,9})(?:-rc\.([0-9]{1,9}))?$", RegexOptions.CultureInvariant);

    /// <summary>Un archivo por año de Revit: cada año tiene sus propios paquetes.</summary>
    public static string NombreArchivo(string anio) => $"ultimas-versiones-{anio}.json";

    /// <summary>
    /// Id, versión y año de la DLL si la instaló el gestor; null si no (una compilación de desarrollo apunta al repo y
    /// nunca se bloquea).
    /// </summary>
    public static InstalacionAddin? Instalacion(string rutaDll)
    {
        string? carpetaAnio = Path.GetDirectoryName(rutaDll);
        string? carpetaVersion = Path.GetDirectoryName(carpetaAnio);
        string? carpetaId = Path.GetDirectoryName(carpetaVersion);
        string? carpetaAddins = Path.GetDirectoryName(carpetaId);
        if (carpetaAnio is null || carpetaVersion is null || carpetaId is null || carpetaAddins is null
            || !string.Equals(Path.GetFileName(carpetaAddins), "Addins", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(Path.GetDirectoryName(carpetaAddins)), "CQEING", StringComparison.OrdinalIgnoreCase))
            return null;

        string version = Path.GetFileName(carpetaVersion);
        return Partes(version) is null ? null : new InstalacionAddin(Path.GetFileName(carpetaId), version, Path.GetFileName(carpetaAnio));
    }

    /// <summary>
    /// La última versión publicada si es más nueva que la instalada; null si está al día o no se sabe (sin archivo, add-in
    /// que no aparece, archivo dañado). Nunca lanza: corre en OnStartup.
    /// </summary>
    public static string? Pendiente(InstalacionAddin instalacion, string carpeta) =>
        Leer(carpeta, instalacion.Anio).TryGetValue(instalacion.Id, out string? ultima) && EsMasNueva(ultima, instalacion.Version)
            ? ultima
            : null;

    /// <summary>Id → última versión publicada para ese año. Vacío si no hay archivo o no se entiende.</summary>
    public static IReadOnlyDictionary<string, string> Leer(string carpeta, string anio)
    {
        try
        {
            string archivo = Path.Combine(carpeta, NombreArchivo(anio));
            if (!File.Exists(archivo)) return new Dictionary<string, string>();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(archivo)) ?? new Dictionary<string, string>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Lo escribe el gestor cada vez que conoce la lista de releases. Se escribe aparte y se reemplaza de una vez: un add-in
    /// que lo lee a la vez nunca ve medio archivo. Si no se puede escribir, queda el anterior.
    /// </summary>
    public static void Escribir(string carpeta, string anio, IReadOnlyDictionary<string, string> ultimas)
    {
        try
        {
            Directory.CreateDirectory(carpeta);
            string archivo = Path.Combine(carpeta, NombreArchivo(anio));
            string temporal = archivo + ".tmp";
            File.WriteAllText(temporal, JsonSerializer.Serialize(ultimas));
            File.Move(temporal, archivo, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // La próxima consulta lo vuelve a intentar.
        }
    }

    /// <summary>X.Y.Z con -rc.N opcional, como VersionAddin del gestor: la prerelease va antes que su versión estable.</summary>
    public static bool EsMasNueva(string candidata, string actual) =>
        Partes(candidata) is { } a && Partes(actual) is { } b && a.CompareTo(b) > 0;

    private static (int, int, int, int)? Partes(string texto)
    {
        Match m = PatronVersion.Match(texto);
        if (!m.Success) return null;
        static int Numero(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);
        return (Numero(m.Groups[1]), Numero(m.Groups[2]), Numero(m.Groups[3]), m.Groups[4].Success ? Numero(m.Groups[4]) : int.MaxValue);
    }
}
