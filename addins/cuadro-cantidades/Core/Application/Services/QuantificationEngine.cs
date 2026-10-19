using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Core.Application.Services;

/// <summary>
/// Motor de cuantificación que calcula las cantidades totales de los elementos vinculados a un ítem de presupuesto,
/// basándose en su unidad de medida y en la información geométrica extraída del modelo.
/// </summary>
public static class QuantificationEngine
{
    /// <summary>
    /// Llave del JSON por elemento con el área lateral en m² (solo la traen los pilares).
    /// En M2 tiene prioridad sobre "Area", que en pilares no existe o no es la superficie a pañetar.
    /// </summary>
    public const string LateralAreaKey = "Área lateral";

    /// <summary>
    /// Calcula la cantidad total sumando las propiedades geométricas (Volumen, Área, Longitud) extraídas del JSON de los elementos enlazados,
    /// o realizando un conteo directo si la unidad corresponde a unidades físicas (ej. UN, UND).
    /// </summary>
    /// <param name="unit">Unidad de medida a evaluar (ej. "M3", "M2", "M", "UN").</param>
    /// <param name="elements">Colección de elementos vinculados al ítem de presupuesto.</param>
    /// <returns>La cantidad total calculada, redondeada a dos decimales.</returns>
    public static double CalculateTotalQuantity(string unit, IEnumerable<LinkedElement> elements)
    {
        if (elements == null || !elements.Any())
            return 0.0;

        // Normalizar la unidad para evitar discrepancias por mayúsculas/minúsculas o espacios
        string normalizedUnit = unit.Trim().ToUpperInvariant();
        string targetProperty = GetTargetPropertyFromUnit(normalizedUnit);

        // Si es una unidad de conteo (como UN o UND), simplemente contamos la cantidad de elementos enlazados.
        if (string.IsNullOrEmpty(targetProperty))
            return elements.Count();

        double total = 0.0;

        foreach (var element in elements)
        {
            if (string.IsNullOrWhiteSpace(element.ElementDataJson))
                continue;

            try
            {
                // Deserializar de forma segura el diccionario de propiedades geométricas
                var data = DeserializeElementData(element.ElementDataJson);
                if (data == null)
                    continue;

                // M2 de columnas y vigas: las caras elegidas; sin datos de caras, el área lateral o el área
                if (targetProperty == "Area"
                    && FaceAreaCalculator.Read(element.ElementDataJson, element.Category) is { SelectedArea: double faceArea })
                    total += faceArea;
                else if (targetProperty == "Area" && data.TryGetValue(LateralAreaKey, out double lateralArea))
                    total += lateralArea;
                else if (data.TryGetValue(targetProperty, out double value))
                    total += value;
            }
            catch
            {
                // Ignorar elementos con JSON malformado o ilegible para no interrumpir el cálculo global
            }
        }

        if (targetProperty == "Peso")
            total = ConvertFromKilograms(total, normalizedUnit);

        // Redondear el total a 2 decimales para precisión de presupuesto
        return Math.Round(total, 2);
    }

    /// <summary>
    /// Convierte un peso en kilogramos a la unidad de la actividad: TON divide entre 1000; KG lo deja igual.
    /// </summary>
    /// <param name="kilograms">Peso en kilogramos (la unidad en la que se extrae el "Peso" del modelo).</param>
    /// <param name="unit">Unidad de medida de la actividad (ej. "KG", "TON").</param>
    public static double ConvertFromKilograms(double kilograms, string? unit)
    {
        return string.Equals(unit?.Trim(), "TON", StringComparison.OrdinalIgnoreCase)
            ? kilograms / 1000.0
            : kilograms;
    }

    /// <summary>
    /// Indica si la unidad de la actividad es metro cuadrado ("M2" o "M²").
    /// </summary>
    public static bool IsSquareMeters(string? unit) =>
        GetTargetPropertyFromUnit(unit?.Trim().ToUpperInvariant() ?? string.Empty) == "Area";

    /// <summary>
    /// Deserializa de forma segura el JSON de datos del elemento, filtrando únicamente los valores numéricos
    /// para evitar fallos si el JSON contiene propiedades de texto como el Nivel.
    /// </summary>
    public static Dictionary<string, double> DeserializeElementData(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, double>();

        try
        {
            // Intentar deserialización directa
            var data = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
            if (data != null)
                return data;
        }
        catch
        {
            // Fallback si contiene strings u otros tipos como el Nivel
            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (raw != null)
                {
                    var result = new Dictionary<string, double>();
                    foreach (var kvp in raw)
                    {
                        if (kvp.Value is JsonElement je)
                        {
                            if (je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out double val))
                            {
                                result[kvp.Key] = val;
                            }
                        }
                        else if (kvp.Value is double d) result[kvp.Key] = d;
                        else if (kvp.Value is float f) result[kvp.Key] = f;
                        else if (kvp.Value is int i) result[kvp.Key] = i;
                        else if (kvp.Value is long l) result[kvp.Key] = l;
                    }
                    return result;
                }
            }
            catch { }
        }

        return new Dictionary<string, double>();
    }

    /// <summary>
    /// Obtiene el nombre de la propiedad geométrica objetivo dentro del JSON según la unidad de medida especificada.
    /// </summary>
    /// <param name="unit">La unidad de medida normalizada en mayúsculas.</param>
    /// <returns>Nombre de la propiedad objetivo (ej. "Volume", "Area", "Length") o una cadena vacía si corresponde a conteo por unidad.</returns>
    private static string GetTargetPropertyFromUnit(string unit)
    {
        return unit switch
        {
            "M3" or "M³" => "Volume",
            "M2" or "M²" => "Area",
            "M" or "ML" => "Length",
            "KG" or "TON" => "Peso", // Se sincroniza con la extracción y Vista Detalles
            _ => string.Empty, // Para UN (Unidades), GL (Global), no se suma una propiedad geométrica, se cuenta.
        };
    }
}
