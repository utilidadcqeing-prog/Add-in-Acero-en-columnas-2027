using System.Globalization;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// El rótulo de una barra o un bastón, con el formato de DC-CAD (§13 de las notas): <c>"3#5L= 4.00/4.28"</c> (recto /
/// total) o <c>"3#5L= 4.00"</c> sin gancho, en m con 2 decimales. Lo usa el arrastre (#84) y lo reutiliza #100.
/// </summary>
public static class Rotulo
{
    public static string De(IReadOnlyList<GrupoBarras> grupos, int recto, int total)
    {
        string barras = string.Join("+", grupos.Select(g => $"{g.Cantidad}#{g.Diametro}"));
        return total == recto ? $"{barras}L= {M(recto)}" : $"{barras}L= {M(recto)}/{M(total)}";
    }

    private static string M(int mm) => (mm / 1000.0).ToString("0.00", CultureInfo.InvariantCulture);
}
