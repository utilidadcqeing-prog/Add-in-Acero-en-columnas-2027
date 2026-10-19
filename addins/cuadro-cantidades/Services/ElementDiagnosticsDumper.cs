using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CQEING_CuadroCantidades.UI.Models;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Volcado de diagnóstico (solo compilación Debug) de los elementos que el add-in extrae del modelo.
/// Escribe un JSON legible en %APPDATA%\CQEING_CuadroCantidades\Diagnostics para verificar fuera de Revit
/// qué elementos se cargaron, con qué cantidades y cuáles fallaron.
/// </summary>
public static class ElementDiagnosticsDumper
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Escribe el volcado. Nunca lanza excepciones: un fallo de diagnóstico no debe afectar al add-in.
    /// </summary>
    /// <param name="documentTitle">Título del documento de Revit (se usa en el nombre del archivo).</param>
    /// <param name="stage">Etapa que genera el volcado (ej. "elementos-disponibles").</param>
    /// <param name="items">Elementos extraídos correctamente.</param>
    /// <param name="errors">Elementos que fallaron al procesarse (Id y mensaje).</param>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void Dump(
        string documentTitle,
        string stage,
        IReadOnlyCollection<ElementItem> items,
        IReadOnlyCollection<(string ElementId, string Category, string Error)> errors
    )
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CQEING_CuadroCantidades",
                "Diagnostics"
            );
            Directory.CreateDirectory(dir);

            string safeTitle = string.Concat(documentTitle.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

            var root = new JsonObject
            {
                ["generado"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["documento"] = documentTitle,
                ["etapa"] = stage,
                ["total"] = items.Count,
                ["porCategoria"] = new JsonObject(
                    items.GroupBy(i => i.Category)
                        .OrderBy(g => g.Key)
                        .Select(g => KeyValuePair.Create(g.Key, (JsonNode?)g.Count()))
                ),
                ["errores"] = new JsonArray(
                    errors.Select(e => (JsonNode)new JsonObject
                    {
                        ["elementId"] = e.ElementId,
                        ["categoria"] = e.Category,
                        ["error"] = e.Error,
                    }).ToArray()
                ),
                ["elementos"] = new JsonArray(
                    items.OrderBy(i => i.Category).ThenBy(i => i.ElementId)
                        .Select(i => (JsonNode)new JsonObject
                        {
                            ["elementId"] = i.ElementId,
                            ["uniqueId"] = i.UniqueId,
                            ["categoria"] = i.Category,
                            ["nombre"] = i.Name,
                            ["datos"] = ParseOrRaw(i.ElementDataJson),
                        }).ToArray()
                ),
            };

            File.WriteAllText(Path.Combine(dir, $"{safeTitle}_{stage}.json"), root.ToJsonString(WriteOptions));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ElementDiagnosticsDumper] No se pudo escribir el volcado: {ex.Message}");
        }
    }

    private static JsonNode? ParseOrRaw(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return JsonValue.Create(json);
        }
    }
}
