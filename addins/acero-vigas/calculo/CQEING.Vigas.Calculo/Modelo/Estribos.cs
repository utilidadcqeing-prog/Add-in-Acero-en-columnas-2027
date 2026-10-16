namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>Estribo cerrado (abraza dos barras arriba y dos abajo) o abierto (una rama: una arriba y una abajo).</summary>
public enum TipoEstribo
{
    Cerrado,
    Abierto,
}

/// <summary>Gancho de los estribos: de 135° (por defecto) o de 90°.</summary>
public enum GanchoEstribo
{
    De135,
    De90,
}

/// <summary>
/// Una barra del corte elegida con un clic. <paramref name="Fila"/> y <paramref name="Numero"/> empiezan en 1: la fila
/// 1 es la que va junto a la cara y el número cuenta de izquierda a derecha dentro de la fila.
/// </summary>
public sealed record PuntoBarra(Posicion Cara, int Fila, int Numero);

/// <summary>Un estribo adicional; <paramref name="Barras"/> va en el orden en que se eligieron.</summary>
public sealed record EstriboAdicional(TipoEstribo Tipo, IReadOnlyList<PuntoBarra> Barras);

/// <summary>
/// Los estribos del paso 3 del tablero (#90), del pórtico o de un tramo. <paramref name="Diametro"/> es el número de la
/// barra; las medidas van en mm.
/// </summary>
/// <param name="LargoZona">Largo de la zona junto a cada columna (0 = sin zona).</param>
/// <param name="SeparacionZona">Un estribo cada, en la zona junto a las columnas.</param>
/// <param name="SeparacionCentro">Un estribo cada, como máximo, en el centro de la luz.</param>
public sealed record ConfigEstribos(
    int Diametro = 3,
    GanchoEstribo Gancho = GanchoEstribo.De135,
    int LargoZona = 800,
    int SeparacionZona = 100,
    int SeparacionCentro = 200,
    IReadOnlyList<EstriboAdicional>? Adicionales = null);

/// <summary>
/// Una luz libre (<c>"A-B"</c>) o un voladizo (<c>"Voladizo D"</c>), en mm desde el eje A. En un voladizo, una de las
/// "caras" es la punta del concreto.
/// </summary>
public sealed record Tramo(string Nombre, double CaraIzquierda, double CaraDerecha, bool EsVoladizo);

/// <summary>
/// Lo que necesita el motor de estribos: el pórtico, la sección (sus barras y recubrimientos) y la configuración del
/// pórtico, con la de cada tramo que la cambie. Las claves de <paramref name="PorTramo"/> son los nombres de los tramos
/// (<c>"A-B"</c>, <c>"Voladizo D"</c>). El diámetro del estribo de la sección es el de <paramref name="General"/>.
/// </summary>
public sealed record EntradaEstribos(
    Portico Portico,
    EntradaSeccion Seccion,
    ConfigEstribos General,
    IReadOnlyDictionary<string, ConfigEstribos>? PorTramo = null);

/// <summary>
/// Los estribos iguales de un tramo: <c>"E"</c> es el cerrado de las esquinas y <c>"E1"</c>, <c>"E2"</c>… los
/// adicionales, como en la lista de la maqueta. <paramref name="Posiciones"/> son sus centros en mm desde el eje A; <paramref name="Largo"/> (mm, por el
/// eje del estribo) incluye los ganchos.
/// </summary>
public sealed record EstriboEnPortico(
    string Id,
    TipoEstribo Tipo,
    string Tramo,
    int Diametro,
    GanchoEstribo Gancho,
    IReadOnlyList<double> Posiciones,
    double Largo,
    double Kg);

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
