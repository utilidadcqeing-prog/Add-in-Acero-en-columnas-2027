using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>
/// Una barra física recta (corrida o bastón) para crear en Revit (#85), en el marco del pórtico: <paramref name="Desde"/> y
/// <paramref name="Hasta"/> son las puntas de su parte recta en mm desde el eje A; <paramref name="XCorte"/> e
/// <paramref name="YCorte"/>, su centro en el corte (ver <see cref="MarcoPortico"/>). <paramref name="Pieza"/> es el id del
/// tablero (<c>S1</c>, <c>BI2</c>) y se repite en todas sus barras físicas. Los ganchos doblan siempre hacia adentro de
/// la viga: hacia abajo en la capa superior y hacia arriba en la inferior. <paramref name="Host"/> es el id de la viga.
/// <paramref name="Largo"/> es el del tablero, con los ganchos, para compararlo con el de Revit (decisión 8).
/// <paramref name="Fila"/> empieza en 1, junto a la cara, y cuenta las filas que tienen barras (si el usuario deja la 2
/// vacía, su fila 3 es la 2); la que traslapa al lado va en la fila de la barra que acompaña.
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
    int Largo,
    int Fila = 1);

/// <summary>
/// Las barras iguales de una fila, que se crean en Revit como un solo conjunto (Santiago, 24-oct: "3 #7" y no "1 #7"
/// tres veces). <paramref name="Piezas"/> va de izquierda a derecha en el corte; todas tienen el mismo largo y ganchos, y
/// cada una conserva su punto (la de esquina se apoya en la curva del estribo y queda unos mm más adentro).
/// </summary>
public sealed record ConjuntoBarras(IReadOnlyList<PiezaLongitudinal> Piezas)
{
    /// <summary>La de más a la izquierda: la que Revit crea, y de la que reparte las demás.</summary>
    public PiezaLongitudinal Primera => Piezas[0];
}

/// <summary>
/// Una corrida de estribos iguales de un tramo, para crear como un conjunto de Revit (#85). <paramref name="Pieza"/> es el
/// id del tablero (<c>E</c> el cerrado de las esquinas, <c>E1</c>… los adicionales). <paramref name="Izquierda"/>,
/// <paramref name="Derecha"/>, <paramref name="Abajo"/> y <paramref name="Arriba"/> son sus caras exteriores en el corte,
/// en mm, como <see cref="EstriboEnSeccion"/>; en el abierto, izquierda y derecha son la X de su rama.
/// <paramref name="Host"/> es la viga del tramo. <paramref name="Forma"/> solo cuenta en el abierto.
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
    string Host,
    Rama? Rama = null,
    FormaGrapa Forma = FormaGrapa.C);

/// <summary>
/// Las dos barras que abraza un estribo abierto, por su centro en el corte (mm), y el diámetro mayor de las dos (hallazgo
/// E13): la rama va al lado de ellas y sus ganchos las abrazan.
/// </summary>
public sealed record Rama(double X, double YArriba, double YAbajo, double Diametro)
{
    /// <summary>
    /// De qué lado de cada barra va la rama, en la X del corte: +1 a la derecha, −1 a la izquierda. En C va hacia el
    /// centro de la viga en las dos; en S, abajo pasa al otro lado de la barra y la rama queda un poco en diagonal, para
    /// que cada gancho abrace su barra desde un lado distinto (hallazgo H6 del plan de 22-oct).
    /// </summary>
    public (int Abajo, int Arriba) Lados(double anchoViga, FormaGrapa forma)
    {
        int alCentro = X <= anchoViga / 2 ? 1 : -1;
        return (forma == FormaGrapa.S ? -alCentro : alCentro, alCentro);
    }

    /// <summary>
    /// Dónde va el centro de cada doblez en el corte: la cara interior del doblez toca su barra por fuera (abajo de la de
    /// abajo, arriba de la de arriba), así la grapa queda en el recubrimiento. <paramref name="radio"/> es el del doblez
    /// por el eje y <paramref name="d"/> el diámetro de la grapa, en mm.
    /// </summary>
    public ((double X, double Y) Abajo, (double X, double Y) Arriba) Dobleces(double radio, double d)
    {
        double adentro = radio - d / 2 - Diametro / 2;
        return ((X, YAbajo + adentro), (X, YArriba - adentro));
    }

    /// <summary>
    /// La línea que se le pasa a Revit, de abajo hacia arriba: la rama es la tangente a los dos dobleces, cada uno del lado
    /// de <see cref="Lados"/>, y cada punta sigue la rama hasta la cara exterior de su doblez (Revit recorta de ahí un
    /// radio más medio diámetro y dobla). En C la rama es vertical; en S queda en diagonal y Revit pone cada gancho
    /// perpendicular a ella, así que con las puntas en vertical los dobleces se corrían (medido en Revit el 26-oct).
    /// </summary>
    public ((double X, double Y) Inicio, (double X, double Y) Fin) Puntas(double anchoViga, FormaGrapa forma, double radio, double d)
    {
        ((double X, double Y) abajo, (double X, double Y) arriba) = Dobleces(radio, d);
        (int ladoAbajo, int ladoArriba) = Lados(anchoViga, forma);
        // De centro a centro: dx = largo·u + k·n, con n la normal a la izquierda de la rama (u girado 90°).
        double dx = arriba.X - abajo.X, dy = arriba.Y - abajo.Y, k = (ladoArriba - ladoAbajo) * radio;
        double d2 = dx * dx + dy * dy, largo = Math.Sqrt(Math.Max(0, d2 - k * k));
        double ux = (largo * dx + k * dy) / d2, uy = (largo * dy - k * dx) / d2, nx = -uy, ny = ux;
        // la rama pasa a un radio del centro, del lado que le toca a cada barra
        double alDoblez = radio + d / 2;
        return ((abajo.X - ladoAbajo * radio * nx - alDoblez * ux, abajo.Y - ladoAbajo * radio * ny - alDoblez * uy),
            (arriba.X - ladoArriba * radio * nx + alDoblez * ux, arriba.Y - ladoArriba * radio * ny + alDoblez * uy));
    }
}

/// <summary>
/// Todo lo que se crea en Revit para un pórtico. <paramref name="Portico"/> es su id en el tablero.
/// <paramref name="Diametros"/> son los distintos que se usan, también los de los estribos (para buscar los tipos de
/// barra); <paramref name="BastonesSinLugar"/>, los bastones que no caben en el corte y no se crean (el tablero ya lo
/// avisa); <paramref name="BastonesMontados"/>, los que no tienen un puesto sin meterse en una barra que traslapa al lado
/// o abajo y se crean igual, montados en ella (el resumen de la generación lo avisa).
/// </summary>
public sealed record PlanAcero(
    string Portico,
    IReadOnlyList<PiezaLongitudinal> Barras,
    IReadOnlyList<PiezaLongitudinal> Bastones,
    IReadOnlyList<string> BastonesSinLugar,
    IReadOnlyList<GrupoEstribos> Estribos,
    IReadOnlyList<int> Diametros,
    IReadOnlyList<string>? BastonesMontados = null);
