using System.Globalization;

namespace CQEING.Despiece.Calculo;

/// <summary>Una barra de un elemento. <paramref name="PosicionMm"/>: dónde está su centro, desde un extremo del elemento.</summary>
public record BarraLeida(string Tipo, string Forma, int Cantidad, int LargoMm, int PosicionMm);

/// <summary>Una viga continua (o una columna) leída del modelo, con su acero.</summary>
public record ElementoLeido(string Id, string Ejes, int AnchoMm, int AltoMm, int LargoMm, IReadOnlyList<BarraLeida> Barras);

/// <summary>Los elementos con el mismo acero: llevan un solo despiece (#217).</summary>
public record GrupoIguales(string Nombre, IReadOnlyList<ElementoLeido> Elementos)
{
    public bool TieneAcero => Elementos[0].Barras.Count > 0;
}

/// <summary>Junta los elementos que llevan el mismo acero (Juan, 30-oct: "si elijo una y hay varias iguales, automáticamente esas se harán").</summary>
public static class AgrupadorIguales
{
    /// <summary>Las medidas se comparan al centímetro: el modelo no da dos largos idénticos al milímetro.</summary>
    private const int Tolerancia = 10;

    public static IReadOnlyList<GrupoIguales> Agrupar(IEnumerable<ElementoLeido> elementos) =>
        elementos.GroupBy(Firma).Select(g => new GrupoIguales(Nombre(g.ToList()), g.ToList())).ToList();

    /// <summary>Iguales en sección y barras, en cualquier orden y sin importar hacia qué lado se dibujó el elemento.</summary>
    private static string Firma(ElementoLeido e)
    {
        // Sin acero no hay nada que comparar: cada uno va solo.
        if (e.Barras.Count == 0)
            return "sin acero " + e.Id;
        string ida = Barras(e, b => b.PosicionMm);
        string vuelta = Barras(e, b => e.LargoMm - b.PosicionMm);
        string barras = string.CompareOrdinal(ida, vuelta) <= 0 ? ida : vuelta;
        return $"{e.AnchoMm}x{e.AltoMm}|{Redondear(e.LargoMm)}|{barras}";
    }

    private static string Barras(ElementoLeido e, Func<BarraLeida, int> posicion) => string.Join(";", e.Barras
        .Select(b => $"{b.Tipo},{b.Forma},{b.Cantidad},{Redondear(b.LargoMm)},{Redondear(posicion(b))}")
        .Order(StringComparer.Ordinal));

    private static int Redondear(int mm) => (int)Math.Round((double)mm / Tolerancia) * Tolerancia;

    private static string Nombre(IReadOnlyList<ElementoLeido> elementos)
    {
        string ejes = string.Join("-", elementos.Select(e => e.Ejes).Where(e => e.Length > 0).Distinct().Order(StringComparer.Ordinal));
        string seccion = string.Format(CultureInfo.InvariantCulture, "({0:0.00}x{1:0.00})", elementos[0].AnchoMm / 1000.0, elementos[0].AltoMm / 1000.0);
        return ejes.Length > 0 ? $"V {ejes} - {seccion}" : $"V - {seccion}";
    }
}
