using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CQEING_CuadroCantidades.UI.Utils
{
    /// <summary>
    /// Lee y escribe valores de texto del ElementDataJson de un elemento vinculado.
    /// Las claves se comparan sin distinguir mayúsculas: al guardar en la API el JSON se arma
    /// con las columnas de la tabla ("NIVEL", "ANFITRIÓN"), mientras que al asignar se escribe
    /// "Nivel" y "Anfitrión".
    /// </summary>
    public static class ElementDataJsonReader
    {
        /// <summary>
        /// Devuelve el primer valor de texto no vacío entre las claves indicadas, o null si no hay ninguno.
        /// </summary>
        public static string? GetString(string? json, params string[] keys)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                var properties = doc.RootElement.EnumerateObject().ToList();

                // Primero la clave exacta: si el JSON trae "Nivel" y "NIVEL", gana la que escribe el add-in
                foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
                {
                    foreach (var key in keys)
                    {
                        foreach (var prop in properties)
                        {
                            if (string.Equals(prop.Name, key, comparison)
                                && prop.Value.ValueKind == JsonValueKind.String)
                            {
                                var value = prop.Value.GetString();
                                if (!string.IsNullOrWhiteSpace(value))
                                    return value;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // JSON corrupto: se trata como si no tuviera el valor
            }

            return null;
        }

        /// <summary>
        /// Devuelve el JSON con la clave escrita en el valor indicado. Quita antes las variantes de la
        /// misma clave con otras mayúsculas ("HOJAS" que vuelve de la API) para que no quede un valor viejo.
        /// </summary>
        public static string SetString(string? json, string key, string value)
        {
            Dictionary<string, object> data;
            try
            {
                data = string.IsNullOrWhiteSpace(json)
                    ? new Dictionary<string, object>()
                    : JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
            }
            catch (JsonException)
            {
                data = new Dictionary<string, object>();
            }

            foreach (var existing in data.Keys.Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                data.Remove(existing);
            }

            data[key] = value;
            return JsonSerializer.Serialize(data, CQEING_CuadroCantidades.Core.JsonConfig.RelaxedOptions);
        }
    }
}
