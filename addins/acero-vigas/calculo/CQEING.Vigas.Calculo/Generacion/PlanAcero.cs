using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>
/// Una barra física recta (corrida o bastón) para crear en Revit (#85), en el marco del pórtico: <paramref name="Desde"/> y
/// <paramref name="Hasta"/> son las puntas de su parte recta en mm desde el eje A; <paramref name="XCorte"/> e
/// <paramref name="YCorte"/>, su centro en el corte (ver <see cref="MarcoPortico"/>). <paramref name="Pieza"/> es el id del
/// tablero (<c>S1</c>, <c>BI2</c>) y se repite en todas sus barras físicas. Los ganchos doblan siempre hacia adentro de
/// la viga: hacia abajo en la capa superior y hacia arriba en la inferior. <paramref name="Host"/> es el id de la viga.
/// <paramref name="Largo"/> es el del tablero, con los ganchos, para compararlo con el de Revit (decisión 8).
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
    string Host,
    int Largo);

/// <summary>
/// Una corrida de estribos iguales de un tramo, para crear como un conjunto de Revit (#85). <paramref name="Pieza"/> es el
/// id del tablero (<c>E</c> el cerrado de las esquinas, <c>E1</c>… los adicionales). <paramref name="Izquierda"/>,
/// <paramref name="Derecha"/>, <paramref name="Abajo"/> y <paramref name="Arriba"/> son sus caras exteriores en el corte,
/// en mm, como <see cref="EstriboEnSeccion"/>; en el abierto, izquierda y derecha son la X de su rama.
/// <paramref name="Host"/> es la viga del tramo.
/// </summary>
public sealed record GrupoEstribos(
    string Pieza,
    string Tramo,
    TipoEstribo Tipo,
    int Diametro,
    GanchoEstribo Gancho,
    double Izquierda,
    double Derecha,
    double Abajo,
    double Arriba,
    Corrida Corrida,
    string Host);

/// <summary>
/// Todo lo que se crea en Revit para un pórtico. <paramref name="Portico"/> es su id en el tablero.
/// <paramref name="Diametros"/> son los distintos que se usan, también los de los estribos (para buscar los tipos de
/// barra); <paramref name="BastonesSinLugar"/>, los bastones que no caben en el corte y no se crean (el tablero ya lo
/// avisa).
/// </summary>
public sealed record PlanAcero(
    string Portico,
    IReadOnlyList<PiezaLongitudinal> Barras,
    IReadOnlyList<PiezaLongitudinal> Bastones,
    IReadOnlyList<string> BastonesSinLugar,
    IReadOnlyList<GrupoEstribos> Estribos,
    IReadOnlyList<int> Diametros);
