using System.Text.Encodings.Web;
using System.Text.Json;

namespace CQEING_CuadroCantidades.Core;

/// <summary>
/// Provee opciones comunes de serialización JSON para evitar el escape innecesario de caracteres Unicode y símbolos.
/// </summary>
public static class JsonConfig
{
    /// <summary>
    /// Opciones por defecto que utilizan <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>
    /// para preservar acentos (Á, é, etc.) y símbolos (+, <, >, etc.) sin secuencias \u00XX.
    /// </summary>
    public static readonly JsonSerializerOptions RelaxedOptions = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };
}
