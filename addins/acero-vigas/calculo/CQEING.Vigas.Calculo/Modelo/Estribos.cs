namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>Estribo cerrado (abraza dos barras arriba y dos abajo) o abierto (una rama: una arriba y una abajo).</summary>
public enum TipoEstribo
{
    Cerrado,
    Abierto,
}

/// <summary>
/// Gancho de los estribos: de 135° (por defecto), de 90° o de 180° (Carlos, 22-oct, min 50:36: "manejemos el gancho a
/// 180"; el de 180° sale de la fila de ganchos de 180° del cuadro).
/// </summary>
public enum GanchoEstribo
{
    De135,
    De90,
    De180,
}

/// <summary>Forma del estribo abierto: en C, los dos ganchos hacia el mismo lado; en S, uno hacia cada lado.</summary>
public enum FormaGrapa
{
    C,
    S,
}

/// <summary>
/// Una barra del corte elegida con un clic. <paramref name="Fila"/> y <paramref name="Numero"/> empiezan en 1: la fila
/// 1 es la que va junto a la cara y el número cuenta de izquierda a derecha dentro de la fila.
/// </summary>
public sealed record PuntoBarra(Posicion Cara, int Fila, int Numero);

/// <summary>
/// Un estribo adicional; <paramref name="Barras"/> va en el orden en que se eligieron. <paramref name="Forma"/> solo
/// cuenta en el abierto (la elige el usuario, Juan 22-oct).
/// </summary>
public sealed record EstriboAdicional(TipoEstribo Tipo, IReadOnlyList<PuntoBarra> Barras, FormaGrapa Forma = FormaGrapa.C);

/// <summary>
/// Los estribos del paso 3 del tablero (#90), del pórtico o de un tramo. <paramref name="Diametro"/> es el número de la
/// barra; las medidas van en mm.
/// </summary>
/// <param name="LargoZona">Largo de la zona junto a cada columna (0 = sin zona).</param>
/// <param name="SeparacionZona">Un estribo cada, en la zona junto a las columnas.</param>
/// <param name="SeparacionCentro">Un estribo cada, como máximo, en el centro de la luz.</param>
/// <param name="Forma">La de la grapa que lleva la vigueta en vez del cerrado (en S o en C, 26-oct); en una viga no cuenta.</param>
public sealed record ConfigEstribos(
    int Diametro = 3,
    GanchoEstribo Gancho = GanchoEstribo.De135,
    int LargoZona = 800,
    int SeparacionZona = 100,
    int SeparacionCentro = 200,
    IReadOnlyList<EstriboAdicional>? Adicionales = null,
    FormaGrapa Forma = FormaGrapa.S);

/// <summary>Una luz entre dos apoyos, o un voladizo antes del primer apoyo o después del último.</summary>
public enum TipoTramo
{
    Luz,
    /// <summary>Antes del primer apoyo: la columna queda a la derecha.</summary>
    VoladizoInicial,
    /// <summary>Después del último apoyo: la columna queda a la izquierda.</summary>
    VoladizoFinal,
}

/// <summary>
/// Una zona de estribos de un tramo, en mm desde el eje A: <paramref name="Junta"/> = la de confinamiento, junto a una
/// columna; si no, el centro. <paramref name="Desde"/> y <paramref name="Hasta"/> son el primer y el último estribo.
/// <paramref name="Cantidad"/> cuenta los estribos de la zona (el que comparte con el centro es de la zona);
/// <paramref name="Separacion"/> es la real, en mm; <paramref name="Largo"/>, de la cara de la columna al último estribo
/// de una zona de confinamiento (null en el centro o si todo el tramo va como zona).
/// </summary>
public sealed record ZonaTramo(double Desde, double Hasta, bool Junta, int Cantidad = 0, double Separacion = 0, double? Largo = null);

/// <summary>
/// Una luz libre (<c>"A-B"</c>) o un voladizo (<c>"Voladizo D"</c>), en mm desde el eje A. En un voladizo, una de las
/// "caras" es la punta del concreto.
/// </summary>
public sealed record Tramo(string Nombre, double CaraIzquierda, double CaraDerecha, TipoTramo Tipo)
{
    public bool EsVoladizo => Tipo != TipoTramo.Luz;
}

/// <summary>
/// Lo que necesita el motor de estribos: el pórtico, la sección (sus barras y recubrimientos) y la configuración del
/// pórtico, con la de cada tramo que la cambie. Las claves de <paramref name="PorTramo"/> son los nombres de los tramos
/// (<c>"A-B"</c>, <c>"Voladizo D"</c>). El diámetro del estribo de la sección es el de <paramref name="General"/>.
/// <paramref name="Principal"/> es el estribo obligatorio <c>"E"</c>: el cerrado de las esquinas, o en una vigueta
/// (<see cref="TipoEstribo.Abierto"/>) una grapa por el centro que abraza la barra de arriba y la de abajo, en S o en C
/// según <see cref="ConfigEstribos.Forma"/> (decisión 8 del plan docs/planes/2026-10-26-acero-viguetas-938.md).
/// </summary>
public sealed record EntradaEstribos(
    Portico Portico,
    EntradaSeccion Seccion,
    ConfigEstribos General,
    IReadOnlyDictionary<string, ConfigEstribos>? PorTramo = null,
    TipoEstribo Principal = TipoEstribo.Cerrado);

/// <summary>
/// Los estribos iguales de un tramo: <c>"E"</c> es el cerrado de las esquinas y <c>"E1"</c>, <c>"E2"</c>… los
/// adicionales, como en la lista de la maqueta. <paramref name="Posiciones"/> son sus centros en mm desde el eje A; <paramref name="Largo"/> (mm, por el
/// eje del estribo) incluye los ganchos. <paramref name="Forma"/> es la del adicional abierto; el cerrado va en C.
/// </summary>
public sealed record EstriboEnPortico(
    string Id,
    TipoEstribo Tipo,
    string Tramo,
    int Diametro,
    GanchoEstribo Gancho,
    IReadOnlyList<double> Posiciones,
    double Largo,
    double Kg,
    FormaGrapa Forma = FormaGrapa.C);

/// <summary>
/// Un estribo en el corte, por sus caras exteriores en mm (x desde la cara izquierda, y desde la de abajo). El abierto
/// es una sola rama: <paramref name="Izquierda"/> y <paramref name="Derecha"/> son la x de las barras que abraza.
/// </summary>
public sealed record EstriboEnSeccion(string Id, TipoEstribo Tipo, double Izquierda, double Derecha, double Abajo, double Arriba);

/// <summary>
/// Lo que devuelve el motor de estribos. <paramref name="Seccion"/> es el reparto de las barras con las que mueven los
/// adicionales (una sola para todo el pórtico, porque las barras son continuas). <see cref="Kg"/> se suma al de
/// <see cref="ResultadoVigas.Kg"/> en el total del tablero.
/// </summary>
public sealed record ResultadoEstribos(
    IReadOnlyList<EstriboEnPortico> Estribos,
    ResultadoSeccion Seccion,
    IReadOnlyList<EstriboEnSeccion> EnSeccion,
    IReadOnlyList<Aviso> Avisos)
{
    public int Cantidad => Estribos.Sum(e => e.Posiciones.Count);
    public double Kg => Estribos.Sum(e => e.Kg);
}
