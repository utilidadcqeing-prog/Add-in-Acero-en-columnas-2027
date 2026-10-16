using System.Text.Json;
using System.Text.Json.Nodes;

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
    private const string ProblemaInvalidas = "Las reglas guardadas tienen valores inválidos; se usan las de base.";

    private static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary><c>%AppData%\CQEING\vigas\reglas.json</c>.</summary>
    public static string RutaPorDefecto { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CQEING", "vigas", "reglas.json");

    /// <summary>
    /// Nunca lanza: si el archivo no se puede leer o trae valores inválidos, devuelve las de base con el problema.
    /// Lo que el archivo no trae, en cualquier nivel, sale de la base (un archivo de otra versión o hecho a mano).
    /// </summary>
    public ReglasCargadas Cargar()
    {
        if (!File.Exists(ruta))
            return new ReglasCargadas(Reglas.Generales, null);
        Reglas? reglas;
        try
        {
            JsonNode? mezcla = Mezclar(JsonSerializer.SerializeToNode(new Archivo(Version, Reglas.Generales), Opciones),
                JsonNode.Parse(File.ReadAllText(ruta)));
            reglas = mezcla?.Deserialize<Archivo>(Opciones)?.Reglas;
        }
        catch (Exception)
        {
            reglas = null;
        }
        if (reglas is null || !Completas(reglas))
            return new ReglasCargadas(Reglas.Generales, ProblemaAlLeer);
        return reglas.Validar().Count == 0
            ? new ReglasCargadas(reglas, null)
            : new ReglasCargadas(Reglas.Generales, ProblemaInvalidas);
    }

    /// <summary>
    /// Escribe primero un temporal y lo pone en su lugar, para que un cierre a mitad no deje el archivo cortado.
    /// Lanza <see cref="IOException"/> o <see cref="UnauthorizedAccessException"/> si no puede escribir.
    /// </summary>
    public void Guardar(Reglas reglas)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
        string temporal = ruta + ".tmp";
        try
        {
            File.WriteAllText(temporal, JsonSerializer.Serialize(new Archivo(Version, reglas), Opciones));
            File.Move(temporal, ruta, overwrite: true);
        }
        finally
        {
            File.Delete(temporal);
        }
    }

    /// <summary>
    /// Pone el archivo encima de la base: los objetos se mezclan propiedad por propiedad; un valor (o un null escrito)
    /// reemplaza al de la base.
    /// </summary>
    private static JsonNode? Mezclar(JsonNode? baseNodo, JsonNode? archivo)
    {
        if (baseNodo is not JsonObject b || archivo is not JsonObject a)
            return archivo?.DeepClone();
        foreach ((string clave, JsonNode? valor) in a)
            b[clave] = Mezclar(b[clave], valor);
        return b;
    }

    /// <summary>System.Text.Json deja null lo que el archivo pone en null aunque el tipo no lo admita.</summary>
    private static bool Completas(Reglas r) =>
        r.PorDiametro is not null && r.PorDiametro.Values.All(d =>
            d is { Gancho90: not null, Gancho180: not null, Estribo135: not null });

    private sealed record Archivo(int Version, Reglas? Reglas);
}
