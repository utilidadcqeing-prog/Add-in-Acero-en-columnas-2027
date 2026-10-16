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
