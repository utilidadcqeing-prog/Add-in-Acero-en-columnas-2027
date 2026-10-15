using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CQEING.Gestor.Core;

public sealed record PaqueteManifiesto(string Revit, string Framework, string Archivo, string Sha256);

/// <summary>manifest.json de una release (esquema 1, sección 4 de la spec).</summary>
public sealed record Manifiesto(
    int Esquema,
    string Id,
    string Nombre,
    string Descripcion,
    string Version,
    DateTimeOffset PublicadoEn,
    string Notas,
    IReadOnlyList<string> Heredados,
    IReadOnlyList<PaqueteManifiesto> Paquetes)
{
    public PaqueteManifiesto? PaqueteParaRevit(string anio) => Paquetes.FirstOrDefault(p => p.Revit == anio);
}

public static class SerializadorManifiesto
{
    public const int EsquemaActual = 1;

    private static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // tildes legibles en el archivo
    };

    private static readonly Regex Sha256Valido = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex NombreAddinValido = new(@"^[A-Za-z0-9._-]+\.addin$", RegexOptions.CultureInvariant);

    public static string Escribir(Manifiesto manifiesto) => JsonSerializer.Serialize(manifiesto, Opciones);

    /// <summary>
    /// Lee y valida. Los nombres de archivo y de .addin heredados se usan para descargar y para BORRAR
    /// archivos del usuario, así que solo se aceptan nombres sueltos, sin carpetas.
    /// </summary>
    public static Manifiesto Leer(string json)
    {
        Manifiesto? m;
        try
        {
            m = JsonSerializer.Deserialize<Manifiesto>(json, Opciones);
        }
        catch (JsonException e)
        {
            throw Invalido("no es un JSON válido.", e);
        }

        if (m is null) throw Invalido("está vacío.");
        if (m.Esquema != EsquemaActual)
            throw Invalido($"usa el esquema {m.Esquema} y este gestor entiende el {EsquemaActual}. Actualice el gestor.");
        if (string.IsNullOrWhiteSpace(m.Id) || string.IsNullOrWhiteSpace(m.Nombre) || VersionAddin.Interpretar(m.Version) is null)
            throw Invalido("no trae id, nombre o una versión válida.");
        if (m.Paquetes is not { Count: > 0 } || !m.Paquetes.All(PaqueteValido))
            throw Invalido("no trae paquetes válidos.");

        IReadOnlyList<string> heredados = m.Heredados ?? [];
        if (!heredados.All(h => h is not null && NombreAddinValido.IsMatch(h)))
            throw Invalido("trae un .addin heredado con un nombre no permitido.");

        return m with { Descripcion = m.Descripcion ?? string.Empty, Notas = m.Notas ?? string.Empty, Heredados = heredados };
    }

    private static bool PaqueteValido(PaqueteManifiesto? p) =>
        p is not null
        && !string.IsNullOrWhiteSpace(p.Revit)
        && p.Archivo is not null
        && p.Archivo == Path.GetFileName(p.Archivo)
        && p.Archivo.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        && p.Sha256 is not null
        && Sha256Valido.IsMatch(p.Sha256);

    private static GestorException Invalido(string detalle, Exception? causa = null) =>
        new(TipoError.ManifiestoInvalido, $"El manifiesto {detalle}", causa);
}
