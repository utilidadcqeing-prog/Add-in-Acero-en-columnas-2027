namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>Aérea (entrepiso, cubierta) o de cimentación. Decide qué capa traslapa al centro de la luz.</summary>
public enum TipoViga
{
    Aerea,
    Cimentacion,
}

/// <summary>
/// Viga o vigueta de losa nervada (#939). La vigueta se arma igual, pero se apoya en vigas que llegan a columnas
/// (Carlos, 24-oct). Va aparte de <see cref="TipoViga"/>: una vigueta también es aérea.
/// </summary>
public enum Elemento
{
    Viga,
    Vigueta,
}

/// <summary>Capa de barras según su posición física en la viga.</summary>
public enum Posicion
{
    Superior,
    Inferior,
}

/// <summary>Dónde traslapa la capa de la cara dentro de cada luz.</summary>
public enum Cara
{
    /// <summary>Junto a la cara del apoyo izquierdo de la luz.</summary>
    Inicio,
    /// <summary>Junto a la cara del apoyo derecho de la luz.</summary>
    Final,
}

/// <summary>
/// Apoyo del pórtico (columna, muro o viga cargadora). Posiciones en mm desde el eje A (el primer eje).
/// </summary>
public sealed record Apoyo(string Eje, int PosicionEje, int CaraIzquierda, int CaraDerecha);

/// <summary>
/// Vigas continuas sobre un mismo eje. <paramref name="Apoyos"/> va de izquierda a derecha; los voladizos se
/// miden desde la cara exterior del apoyo de borde (0 = no hay voladizo). Todo en mm.
/// </summary>
public sealed record Portico(IReadOnlyList<Apoyo> Apoyos, int VoladizoInicio = 0, int VoladizoFinal = 0);

/// <summary>
/// Barras iguales dentro de una capa: <c>3 #5</c> es <c>new(3, 5)</c>. <paramref name="Fila"/> (#84) es la fila del
/// corte elegida a mano, desde 1; null = Auto.
/// </summary>
public sealed record GrupoBarras(int Cantidad, int Diametro, int? Fila = null);
