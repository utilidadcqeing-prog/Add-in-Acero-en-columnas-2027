namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Color de cada barra según su diámetro: la tabla de la empresa (Luciano,
/// 20-oct). La usan la vista previa del panel y los filtros de vista de Revit,
/// para que los dos pinten igual. Ver
/// <c>docs/planes/2026-10-20-diseno-vista-previa-y-colores.md</c>, sección 0.
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
        [3] = new(184, 134, 11),
        [4] = new(232, 113, 10),
        [5] = new(211, 47, 47),
        [6] = new(194, 24, 91),
        [7] = new(123, 31, 162),
        [8] = new(21, 101, 192),
        [9] = new(0, 131, 143),
        [10] = new(46, 125, 50),
        [11] = new(109, 76, 65),
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
