namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Color de cada barra según su diámetro: la tabla de la empresa (Luciano,
/// 20-oct). La usan la vista previa del panel y los filtros de vista de Revit,
/// para que los dos pinten igual. Ver
/// <c>docs/planes/2026-10-20-diseno-vista-previa-y-colores.md</c>, sección 0.
/// Desde el 21-oct (#143) los colores son la paleta "Estándar" de Google
/// Sheets: <c>docs/planes/2026-10-21-colores-paleta-estandar.md</c>.
/// </summary>
public static class ColoresVarilla
{
    public readonly record struct ColorRgb(byte R, byte G, byte B);

    /// <summary>Lo que no está en la tabla (incluido el #2).</summary>
    public static ColorRgb Gris { get; } = new(94, 102, 112);

    /// <summary>
    /// #9 a #11 no están en <see cref="CuadroVarillas"/> (no hay datos de
    /// traslapo ni gancho para armar con ellas): acá solo se conoce su
    /// diámetro, para reconocerlas y pintarlas (Luciano, opción b).
    /// </summary>
    private static readonly Dictionary<int, double> DiametrosFueraDelCuadro = new()
    {
        [9] = 28.7,
        [10] = 32.3,
        [11] = 35.8,
    };

    private static readonly Dictionary<int, ColorRgb> ColorPorNumero = new()
    {
        [3] = new(251, 188, 4),   // #FBBC04 amarillo
        [4] = new(255, 109, 1),   // #FF6D01 naranja
        [5] = new(234, 67, 53),   // #EA4335 rojo
        [6] = new(52, 168, 83),   // #34A853 verde
        [7] = new(147, 52, 230),  // #9334E6 morado (en lugar del celeste)
        [8] = new(66, 133, 244),  // #4285F4 azul
        [9] = new(139, 69, 19),   // #8B4513 marrón (fuera de la paleta)
        [10] = new(0, 0, 0),      // #000000 negro
        [11] = new(233, 30, 99),  // #E91E63 rosado fuerte (fuera de la paleta)
    };

    /// <summary>Número de varilla, de #3 a #11, con su diámetro y su color, en orden.</summary>
    public static IReadOnlyList<(int Numero, double DiametroMm, ColorRgb Color)> Tabla { get; } =
        ColorPorNumero
            .OrderBy(p => p.Key)
            .Select(p => (p.Key, DiametroDeNumero(p.Key), p.Value))
            .ToList();

    /// <summary>
    /// El número de varilla de ese diámetro, o null si no es ninguno conocido.
    /// Por diámetro y no por nombre del tipo: una barra del modelo con el
    /// grosor de un #5 sale roja aunque no se llame "#5 CQEING".
    /// </summary>
    public static int? NumeroDe(double diametroMm)
    {
        if (CuadroVarillas.PorDiametro(diametroMm) is { } varilla) return varilla.Numero;

        foreach (KeyValuePair<int, double> par in DiametrosFueraDelCuadro)
            if (System.Math.Abs(par.Value - diametroMm) < CuadroVarillas.ToleranciaMm) return par.Key;

        return null;
    }

    public static ColorRgb ColorDe(double diametroMm) =>
        NumeroDe(diametroMm) is int numero && ColorPorNumero.TryGetValue(numero, out ColorRgb color) ? color : Gris;

    /// <summary>
    /// El color de la tabla para un filtro de vista del proyecto que se llama
    /// "ACERO #N" (los de la empresa, que en la vista quedan por encima de los
    /// nuestros y ganan): Luciano eligió repintarlos con la tabla (20-oct).
    /// <c>null</c> = no es uno de esos y no se toca.
    /// </summary>
    public static ColorRgb? ColorDeFiltroEmpresa(string nombreFiltro)
    {
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
            nombreFiltro, @"^\s*ACERO\s*#(\d+)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        return ColorPorNumero.TryGetValue(int.Parse(m.Groups[1].Value), out ColorRgb color) ? color : Gris;
    }

    private static double DiametroDeNumero(int numero) =>
        CuadroVarillas.DiametroDe(numero) ?? DiametrosFueraDelCuadro[numero];
}
