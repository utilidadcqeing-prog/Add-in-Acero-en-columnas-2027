namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>Hacia dónde mira el corte 1-1: de ese lado se ve la proyección.</summary>
public enum MiraCorte
{
    Derecha,
    Izquierda,
}

/// <summary>
/// Lo que el usuario elige del corte 1-1 (#91), en mm desde el eje A. <paramref name="X"/> null = la vista tipo, la
/// sección que más se repite. <paramref name="Profundidad"/> es el ancho de la franja que se ve detrás (Carlos: 0.10 m).
/// </summary>
public sealed record OpcionesCorte(int? X = null, int Profundidad = 100, MiraCorte Mirando = MiraCorte.Derecha);

/// <summary>Un tramo de x, en mm desde el eje A.</summary>
public sealed record Intervalo(double Desde, double Hasta);

/// <summary>
/// El corte resuelto, en mm: dónde quedó, si es la vista tipo, si lo pedido cayó donde no se puede cortar (traslapo,
/// apoyo o fuera de la viga; pegarse al estribo más cercano no cuenta), el tramo
/// (<c>"A-B"</c>, <c>"Voladizo D"</c>; vacío si no cae en ninguno), los bastones que pasan por ahí, los que se ven en
/// la franja de la profundidad y los tramos donde se puede poner. <paramref name="Puntos"/> son los centros de los
/// estribos que caen en esos tramos: el corte va de estribo en estribo (vacío = sin estribos, el corte es continuo).
/// <paramref name="SinLugar"/> = el pórtico no tiene ningún punto permitido y el corte se quedó donde estaba.
/// </summary>
public sealed record CorteEnPunto(
    double X,
    bool EsTipo,
    bool Ajustado,
    bool SinLugar,
    string Tramo,
    IReadOnlyList<Baston> Cortados,
    IReadOnlyList<Baston> Proyectados,
    IReadOnlyList<Intervalo> Permitidos,
    IReadOnlyList<double> Puntos);
