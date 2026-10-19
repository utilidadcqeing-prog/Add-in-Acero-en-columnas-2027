using System.Globalization;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// El rótulo de una barra o un bastón, con el formato de DC-CAD (§13 de las notas): <c>"3#5L= 4.00/4.28"</c> (recto /
/// total) o <c>"3#5L= 4.00"</c> sin gancho, en m con 2 decimales. Lo usa el arrastre (#84) y lo reutiliza #100.
/// </summary>
public static class Rotulo
{
    public static string De(IReadOnlyList<GrupoBarras> grupos, int recto, int total) =>
        Armar(string.Join("+", grupos.Select(g => $"{g.Cantidad}#{g.Diametro}")), recto, total);

    /// <summary>
    /// El mismo rótulo con el diámetro en pulgadas: <c>"3Ø⅝\"L= 2.80/3.00"</c>. La <c>Ø</c> separa la cantidad de la
    /// fracción: <c>3⅝"</c> se leería como tres y cinco octavos (decisión 8 del plan
    /// docs/planes/2026-10-19-ajustes-tablero-vigas-100.md).
    /// </summary>
    public static string EnPulgadas(IReadOnlyList<GrupoBarras> grupos, int recto, int total) =>
        Armar(string.Join("+", grupos.Select(g => $"{g.Cantidad}Ø{Pulgadas(g.Diametro)}")), recto, total);

    /// <summary>El #n en pulgadas (n/8"), simplificado: <c>"⅝\""</c>, <c>"1\""</c>, <c>"1¼\""</c>.</summary>
    public static string Pulgadas(int diametro)
    {
        int enteras = diametro / 8, octavos = diametro % 8;
        string fraccion = octavos switch
        {
            1 => "⅛", 2 => "¼", 3 => "⅜", 4 => "½", 5 => "⅝", 6 => "¾", 7 => "⅞", _ => "",
        };
        return (enteras > 0 ? enteras.ToString(CultureInfo.InvariantCulture) : "") + fraccion + "\"";
    }

    private static string Armar(string barras, int recto, int total) =>
        total == recto ? $"{barras}L= {M(recto)}" : $"{barras}L= {M(recto)}/{M(total)}";

    private static string M(int mm) => (mm / 1000.0).ToString("0.00", CultureInfo.InvariantCulture);
}
