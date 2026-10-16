using System.Text.Json;

namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>Las reglas leídas del archivo. <paramref name="Problema"/> dice por qué se usaron las de base, si pasó.</summary>
public sealed record ReglasCargadas(Reglas Reglas, string? Problema);

/// <summary>
/// Guarda las reglas generales en un archivo JSON del PC (#82): valen para todos los proyectos de ese usuario de
/// Windows en ese equipo. Sin archivo se usan <see cref="Reglas.Generales"/>.
/// </summary>
public sealed class AlmacenReglas(string ruta)
{
    private const int Version = 1;
    private const string ProblemaAlLeer = "No se pudieron leer las reglas guardadas; se usan las de base.";

    private static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary><c>%AppData%\CQEING\vigas\reglas.json</c>.</summary>
    public static string RutaPorDefecto { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CQEING", "vigas", "reglas.json");

    /// <summary>Nunca lanza: si el archivo no se puede leer, devuelve las de base con el problema.</summary>
    public ReglasCargadas Cargar()
    {
        if (!File.Exists(ruta))
            return new ReglasCargadas(Reglas.Generales, null);
        try
        {
            Archivo? archivo = JsonSerializer.Deserialize<Archivo>(File.ReadAllText(ruta), Opciones);
            return archivo?.Reglas is { } reglas
                ? new ReglasCargadas(Completar(reglas), null)
                : new ReglasCargadas(Reglas.Generales, ProblemaAlLeer);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ReglasCargadas(Reglas.Generales, ProblemaAlLeer);
        }
    }

    /// <summary>Escribe primero un temporal y lo pone en su lugar, para que un cierre a mitad no deje el archivo cortado.</summary>
    public void Guardar(Reglas reglas)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
        string temporal = ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(new Archivo(Version, reglas), Opciones));
        File.Move(temporal, ruta, overwrite: true);
    }

    /// <summary>
    /// Lo que un archivo de otra versión no trae se toma de la base: los diámetros que faltan y, en los que sí están,
    /// los ganchos que faltan.
    /// </summary>
    private static Reglas Completar(Reglas leidas)
    {
        var porDiametro = new Dictionary<int, ReglaDiametro>(Reglas.Generales.PorDiametro);
        foreach ((int d, ReglaDiametro r) in leidas.PorDiametro ?? new Dictionary<int, ReglaDiametro>())
        {
            ReglaDiametro? baseD = porDiametro.GetValueOrDefault(d);
            porDiametro[d] = r with
            {
                Gancho90 = r.Gancho90 ?? baseD?.Gancho90 ?? throw new JsonException(),
                Gancho180 = r.Gancho180 ?? baseD?.Gancho180 ?? throw new JsonException(),
                Estribo135 = r.Estribo135 ?? baseD?.Estribo135 ?? throw new JsonException(),
            };
        }
        return leidas with { PorDiametro = porDiametro };
    }

    private sealed record Archivo(int Version, Reglas? Reglas);
}
