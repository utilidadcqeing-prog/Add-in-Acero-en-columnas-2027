using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>
/// Una barra física recta (corrida o bastón) para crear en Revit (#85), en el marco del pórtico: <paramref name="Desde"/> y
/// <paramref name="Hasta"/> son las puntas de su parte recta en mm desde el eje A; <paramref name="XCorte"/> e
/// <paramref name="YCorte"/>, su centro en el corte (ver <see cref="MarcoPortico"/>). <paramref name="Pieza"/> es el id del
/// tablero (<c>S1</c>, <c>BI2</c>) y se repite en todas sus barras físicas. Los ganchos doblan siempre hacia adentro de
/// la viga: hacia abajo en la capa superior y hacia arriba en la inferior. <paramref name="Host"/> es el id de la viga.
/// </summary>
public sealed record PiezaLongitudinal(
    string Pieza,
    Posicion Capa,
    int Diametro,
    double XCorte,
    double YCorte,
    int Desde,
    int Hasta,
    TipoGancho GanchoInicio,
    TipoGancho GanchoFin,
    string Host);

/// <summary>
/// Todo lo que se crea en Revit para un pórtico. <paramref name="Portico"/> es su id en el tablero.
/// <paramref name="Diametros"/> son los distintos que se usan (para buscar los tipos de barra);
/// <paramref name="BastonesSinLugar"/>, los bastones que no caben en el corte y no se crean (el tablero ya lo avisa).
/// </summary>
public sealed record PlanAcero(
    string Portico,
    IReadOnlyList<PiezaLongitudinal> Barras,
    IReadOnlyList<PiezaLongitudinal> Bastones,
    IReadOnlyList<string> BastonesSinLugar,
    IReadOnlyList<int> Diametros);
