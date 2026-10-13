namespace practicacq.Imagenes.Modelo;

/// <summary>
/// Colores sólidos para pintar cada tipo seleccionado. Los mismos 8 de la maqueta de la issue #183,
/// en ese orden; además el usuario puede escribir un color libre (#RRGGBB).
/// </summary>
public static class PaletaColores
{
    public static IReadOnlyList<string> Colores { get; } = new[]
    {
        "#E0453A", "#F08C1A", "#E3BD12", "#2BA36B", "#1F8FD6", "#4B5BD6", "#9B4BD6", "#D6457F",
    };

    /// <summary>Color inicial del tipo número <paramref name="indice"/>: recorre la paleta en orden.</summary>
    public static string PorIndice(int indice) => Colores[indice % Colores.Count];

    /// <summary>Acepta "#RRGGBB" o "RRGGBB" y lo devuelve normalizado en mayúsculas, o null si no es válido.</summary>
    public static string? Normalizar(string? texto)
    {
        string h = (texto ?? string.Empty).Trim().TrimStart('#');
        if (h.Length != 6 || !h.All(Uri.IsHexDigit)) return null;
        return "#" + h.ToUpperInvariant();
    }
}
