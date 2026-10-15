using System.Text.Json;

namespace CQEING.Publicador;

/// <summary>addins/&lt;id&gt;/addin.json: qué es el add-in, qué proyecto compilar y para qué años de Revit.</summary>
public sealed record ConfiguracionAddin(
    string Id,
    string Nombre,
    string Descripcion,
    string Proyecto,
    IReadOnlyList<string> Revit,
    IReadOnlyList<string> Heredados)
{
    private static readonly JsonSerializerOptions Opciones = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static ConfiguracionAddin Leer(string ruta)
    {
        ConfiguracionAddin c = JsonSerializer.Deserialize<ConfiguracionAddin>(File.ReadAllText(ruta), Opciones)
            ?? throw new InvalidOperationException($"{ruta} está vacío.");
        if (string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Nombre) || string.IsNullOrWhiteSpace(c.Proyecto) || c.Revit is not { Count: > 0 })
            throw new InvalidOperationException($"{ruta}: faltan id, nombre, proyecto o revit.");
        return c with { Descripcion = c.Descripcion ?? string.Empty, Heredados = c.Heredados ?? [] };
    }
}
