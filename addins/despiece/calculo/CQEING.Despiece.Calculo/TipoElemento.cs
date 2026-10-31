using System.Globalization;
using System.Text;

namespace CQEING.Despiece.Calculo;

/// <summary>
/// Qué elemento de armazón es vigueta: la misma regla que <c>ReglaVigueta</c> de acero de vigas, en <c>main</c>. Al unir
/// esta rama con <c>main</c>, se enlaza ese archivo y se borra este (decisión 1 del plan de viguetas).
/// </summary>
public static class TipoElemento
{
    /// <summary>Lo más angosto que mide una viga, en mm.</summary>
    private const int AnchoMinimoViga = 200;

    public static bool EsVigueta(string nombreTipo, int anchoMm)
    {
        string nombre = new string(nombreTipo.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
        return nombre.StartsWith("VT", StringComparison.Ordinal) || nombre.StartsWith("VB", StringComparison.Ordinal)
            || nombre.Contains("VIGUETA", StringComparison.Ordinal) || anchoMm < AnchoMinimoViga;
    }
}
