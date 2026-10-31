using System.Globalization;
using System.Text;

namespace CQEING.Vigas.Calculo.Porticos;

/// <summary>
/// Qué elemento de armazón cuenta como vigueta (#939). La usan los pórticos del tablero y los paneles de «Crear
/// viguetas» (#947), para que las dos digan lo mismo.
/// </summary>
public static class ReglaVigueta
{
    /// <summary>Ancho mínimo de una viga, en mm (Carlos, 24-oct, min 02:30): lo más angosto es vigueta.</summary>
    public const double AnchoMinimoViga = 200;

    /// <summary>Vigueta por el tipo (VT, VB o «vigueta», como en PROY_JULIO_BETO) o por medir menos de 200 mm.</summary>
    public static bool Es(string? nombreTipo, double anchoMm) => PorNombre(nombreTipo) || Math.Round(anchoMm) < AnchoMinimoViga;

    public static bool PorNombre(string? nombreTipo)
    {
        if (nombreTipo is null)
            return false;
        string nombre = SinTildes(nombreTipo).ToUpperInvariant();
        return nombre.StartsWith("VT") || nombre.StartsWith("VB") || nombre.Contains("VIGUETA");
    }

    public static string SinTildes(string texto) =>
        new(texto.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
}
