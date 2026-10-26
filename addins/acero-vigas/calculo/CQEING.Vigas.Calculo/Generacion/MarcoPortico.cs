using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>Un punto del modelo, en mm.</summary>
public readonly record struct Punto3(double X, double Y, double Z);

/// <summary>
/// Pasa del marco del pórtico a las coordenadas del modelo (#85). En el marco, x va a lo largo del pórtico en mm desde el
/// eje A (como el motor); en el corte, X va desde la cara izquierda mirando hacia +x e Y desde la cara de abajo (como
/// <c>BarraEnSeccion</c>).
/// </summary>
public static class MarcoPortico
{
    public static Punto3 AlModelo(UbicacionPortico u, double x, double xCorte, double yCorte)
    {
        // n = (−Uy, Ux) es la izquierda mirando hacia +x: la cara izquierda del corte está a +Ancho/2 sobre n.
        double lado = u.Ancho / 2 - xCorte;
        return new Punto3(u.OrigenX + x * u.Ux - lado * u.Uy, u.OrigenY + x * u.Uy + lado * u.Ux, u.ZAbajo + yCorte);
    }

    /// <summary>La X y la Y del corte de un punto del modelo: la vuelta de <see cref="AlModelo"/>.</summary>
    public static (double X, double Y) EnCorte(UbicacionPortico u, Punto3 p)
    {
        double lado = -(p.X - u.OrigenX) * u.Uy + (p.Y - u.OrigenY) * u.Ux;
        return (u.Ancho / 2 - lado, p.Z - u.ZAbajo);
    }

    /// <summary>La x de un punto del modelo en el marco del pórtico: mm desde el eje A, a lo largo del pórtico.</summary>
    public static double EnPortico(UbicacionPortico u, Punto3 p) => (p.X - u.OrigenX) * u.Ux + (p.Y - u.OrigenY) * u.Uy;

    /// <summary>
    /// La viga que contiene <paramref name="x"/> o, si cae fuera de todas (en un apoyo, por ejemplo), la más cercana; con
    /// empate, la primera. Null si el pórtico no tiene vigas.
    /// </summary>
    public static string? VigaEn(UbicacionPortico u, double x) =>
        u.Vigas.OrderBy(v => x < v.Desde ? v.Desde - x : x > v.Hasta ? x - v.Hasta : 0).FirstOrDefault()?.Id;
}
