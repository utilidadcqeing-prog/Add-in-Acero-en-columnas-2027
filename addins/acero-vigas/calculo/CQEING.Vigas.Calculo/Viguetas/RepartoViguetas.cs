namespace CQEING.Vigas.Calculo.Viguetas;

/// <summary>
/// Cuántas viguetas entran en un ancho libre y cuánto queda libre en cada extremo. <paramref name="LibreMaximo"/> es el
/// libre más grande que se acepta en un extremo (eje a eje − ancho, la celda «Esp libre / viguetas» del Excel).
/// </summary>
public sealed record Reparto(int Cantidad, double EjeAEje, double LibreExtremos, double LibreMaximo);

/// <summary>
/// La fórmula del Excel «CANTIDAD Y LONG EXTREMOS POR PANEL» de Carlos (reunión del 24-oct, #950). Unidades libres,
/// las mismas en los tres datos (el add-in usa mm).
/// </summary>
public static class RepartoViguetas
{
    private const double Margen = 1e-9;

    /// <summary>
    /// n = REDONDEAR.MENOS(libre / eje a eje); extremos = (libre − ((n − 1) · eje a eje + ancho)) / 2. Si los extremos
    /// pasan de eje a eje − ancho, se agrega una vigueta y se recalculan. Null si los datos no sirven.
    /// </summary>
    public static Reparto? Calcular(double libre, double ejeAEje, double ancho)
    {
        if (libre <= 0 || ejeAEje <= 0 || ancho <= 0 || ancho >= ejeAEje)
            return null;
        double maximo = ejeAEje - ancho;
        double Extremo(int n) => (libre - ((n - 1) * ejeAEje + ancho)) / 2;

        int cantidad = Math.Max(1, (int)Math.Floor(libre / ejeAEje + Margen));
        if (Extremo(cantidad) > maximo + Margen)
            cantidad++;
        double extremos = Extremo(cantidad);
        return extremos < 0 ? null : new Reparto(cantidad, ejeAEje, extremos, maximo);
    }
}
