using System.Text.Json;
using System.Text.Json.Nodes;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// La configuración de un pórtico que se guarda en sus vigas al generar (plan
/// docs/planes/2026-10-22-recordar-portico-generado-vigas.md, decisión 3): las opciones del cálculo y el estado de la
/// página tal cual, en JSON <c>{ "version": 1, "opciones": …, "estado": "…" }</c>. Sin Revit: el add-in solo guarda y
/// lee el texto.
/// </summary>
public static class ConfiguracionGuardada
{
    private const int Version = 1;

    /// <param name="estado">El estado de la página (JSON como texto); null si la página no lo mandó.</param>
    public static string Escribir(OpcionesTablero opciones, string? estado) =>
        JsonSerializer.Serialize(new { version = Version, opciones, estado }, ProtocoloTablero.Json);

    /// <summary>Null si el texto no es una configuración de esta versión con opciones válidas: se ignora.</summary>
    public static (OpcionesTablero Opciones, string? Estado)? Leer(string guardado)
    {
        try
        {
            if (JsonNode.Parse(guardado) is not JsonObject nodo || nodo["version"]?.GetValue<int>() != Version
                || ProtocoloTablero.Opciones(nodo["opciones"]) is not { } opciones)
                return null;
            return (opciones, nodo["estado"]?.GetValue<string>());
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
