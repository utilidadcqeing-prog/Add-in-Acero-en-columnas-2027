using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace CQEING.Compartido;

/// <summary>Un add-in instalado por el gestor: la DLL vive en ...\CQEING\Addins\&lt;id&gt;\&lt;versión&gt;\&lt;año&gt;\.</summary>
internal sealed record InstalacionAddin(string Id, string Version, string Anio);

/// <summary>
/// Actualización obligatoria (docs/planes/2026-10-29-actualizacion-obligatoria.md). El gestor anota, por año de Revit,
/// la última versión publicada de cada add-in y del gestor; un add-in instalado por el gestor con una versión más vieja,
/// o con el gestor desactualizado, no se deja usar. Se compila dentro de cada add-in y del núcleo del gestor: el formato del archivo tiene una sola fuente.
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
    /// Por qué no se deja usar el add-in, o null si está al día o no se sabe (sin archivo, add-in que no aparece, archivo
    /// dañado). Primero el add-in; después el gestor: con el gestor desactualizado no se usa ningún add-in, porque es el
    /// que trae las versiones nuevas. <paramref name="carpetaAddinsRevit"/> es %APPDATA%\Autodesk\Revit\Addins\&lt;año&gt;.
    /// </summary>
    public static string? Pendiente(InstalacionAddin instalacion, string carpeta, string carpetaAddinsRevit, string? claveRegistro)
    {
        IReadOnlyDictionary<string, string> ultimas = Leer(carpeta, instalacion.Anio, claveRegistro);
        if (ultimas.TryGetValue(instalacion.Id, out string? ultima) && EsMasNueva(ultima, instalacion.Version))
            return $"Hay una versión nueva de este add-in ({ultima}). Actualícela desde el gestor y reinicie Revit para seguir usándolo.";
        if (ultimas.TryGetValue(ClaveGestor, out string? gestor) && !GestorAlDia(carpetaAddinsRevit, gestor))
            return $"Hay una versión nueva del gestor de add-ins ({gestor}). Ábralo, pulse «Actualizar y reiniciar» y reinicie Revit: " +
                   "mientras tanto no se puede usar ningún add-in.";
        return null;
    }

    /// <summary>Clave del gestor en el archivo de últimas versiones; es el id de sus etiquetas (gestor-vX.Y.Z).</summary>
    public const string ClaveGestor = "gestor";

    /// <summary>El .addin del componente del gestor en Revit; el «zz-» lo hace cargar el último (RutasGestor).</summary>
    public const string ArchivoComplementoGestor = "CQEING.zz-gestor-revit.addin";

    /// <summary>
    /// El componente del gestor que carga este Revit es <paramref name="ultima"/> o más nuevo. Vive en
    /// ...\CQEING\Gestor\revit\&lt;versión&gt;\&lt;año&gt;\. Sin componente no está al día (borrarlo no sirve para esquivar la
    /// regla); uno que apunta a otra carpeta es de desarrollo y no se juzga.
    /// </summary>
    public static bool GestorAlDia(string carpetaAddinsRevit, string ultima)
    {
        string? dll;
        try
        {
            string archivo = Path.Combine(carpetaAddinsRevit, ArchivoComplementoGestor);
            if (!File.Exists(archivo)) return false;
            dll = XDocument.Load(archivo).Descendants("Assembly").FirstOrDefault()?.Value.Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            return true;
        }
        if (string.IsNullOrEmpty(dll) || dll.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return true;

        string? carpetaVersion = Path.GetDirectoryName(Path.GetDirectoryName(dll));
        string? carpetaRevit = Path.GetDirectoryName(carpetaVersion);
        if (carpetaVersion is null || carpetaRevit is null
            || !string.Equals(Path.GetFileName(carpetaRevit), "revit", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(Path.GetDirectoryName(carpetaRevit)), "Gestor", StringComparison.OrdinalIgnoreCase))
            return true;
        return !EsMasNueva(ultima, Path.GetFileName(carpetaVersion));
    }

    /// <summary>
    /// Copia en el registro del usuario (HKCU), un valor por año, que nunca baja: guarda la versión más alta que se conoció
    /// de cada uno. Borrar el archivo o volver a instalar un gestor viejo no desbloquea nada. Las pruebas pasan su propia
    /// clave, o null para no tocar el registro.
    /// </summary>
    public const string ClaveRegistro = @"Software\CQEING\Gestor";

    /// <summary>
    /// Id → última versión publicada para ese año: la más alta entre el archivo y el registro. Vacío si no hay nada o no
    /// se entiende.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Leer(string carpeta, string anio, string? claveRegistro = null) =>
        Mayores(LeerArchivo(carpeta, anio), LeerRegistro(claveRegistro, anio));

    /// <summary>
    /// Lo escribe el gestor cada vez que conoce la lista de releases. El archivo se escribe aparte y se reemplaza de una
    /// vez: un add-in que lo lee a la vez nunca ve medio archivo. En el registro solo sube. Si no se puede escribir, queda
    /// lo anterior.
    /// </summary>
    public static void Escribir(string carpeta, string anio, IReadOnlyDictionary<string, string> ultimas, string? claveRegistro = null)
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
        EscribirRegistro(claveRegistro, anio, Mayores(LeerRegistro(claveRegistro, anio), ultimas));
    }

    private static Dictionary<string, string> LeerArchivo(string carpeta, string anio)
    {
        try
        {
            string archivo = Path.Combine(carpeta, NombreArchivo(anio));
            return File.Exists(archivo) ? Interpretar(File.ReadAllText(archivo)) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static Dictionary<string, string> LeerRegistro(string? claveRegistro, string anio)
    {
        if (claveRegistro is null || !OperatingSystem.IsWindows()) return [];
        try
        {
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(claveRegistro);
            return clave?.GetValue(ValorRegistro(anio)) is string json ? Interpretar(json) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static void EscribirRegistro(string? claveRegistro, string anio, IReadOnlyDictionary<string, string> ultimas)
    {
        if (claveRegistro is null || !OperatingSystem.IsWindows()) return;
        try
        {
            using RegistryKey clave = Registry.CurrentUser.CreateSubKey(claveRegistro);
            clave.SetValue(ValorRegistro(anio), JsonSerializer.Serialize(ultimas));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // La próxima consulta lo vuelve a intentar.
        }
    }

    private static string ValorRegistro(string anio) => $"u{anio}";

    private static Dictionary<string, string> Interpretar(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Por cada id, la versión más alta de las dos listas.</summary>
    private static Dictionary<string, string> Mayores(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        var mayores = new Dictionary<string, string>(a);
        foreach ((string id, string version) in b)
            if (!mayores.TryGetValue(id, out string? otra) || EsMasNueva(version, otra))
                mayores[id] = version;
        return mayores;
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
