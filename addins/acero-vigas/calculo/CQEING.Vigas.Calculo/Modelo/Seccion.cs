namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>Recubrimiento libre (cara del concreto → cara exterior del estribo) de cada cara, en mm.</summary>
public sealed record Recubrimientos(double Arriba, double Abajo, double Lados)
{
    /// <summary>Un valor general para todas las caras (lo que se escribe en el paso 1 del tablero).</summary>
    public static Recubrimientos Iguales(double libre) => new(libre, libre, libre);
}

/// <summary>
/// Lo que hace falta para repartir las barras de una sección en filas (#88). Medidas en mm.
/// </summary>
/// <param name="DiametroEstribo">Número de la barra del estribo (3 = #3).</param>
/// <param name="SeparacionFilas">Separación libre entre filas: 0 = pegadas (por defecto).</param>
/// <param name="MaximoFilas">Filas por cara antes de dar error (3 por defecto).</param>
/// <param name="PermiteUnaBarra">
/// Vigueta (#941): una sola barra por cara no es error. Carlos (24-oct, min 07:20): lleva una arriba y una abajo.
/// </param>
public sealed record EntradaSeccion(
    double Ancho,
    double Alto,
    Recubrimientos Recubrimiento,
    int DiametroEstribo,
    IReadOnlyList<GrupoBarras> Superior,
    IReadOnlyList<GrupoBarras> Inferior,
    Reglas Reglas,
    double SeparacionFilas = 0,
    int MaximoFilas = 3,
    bool PermiteUnaBarra = false);

public enum ErrorCara
{
    Ninguno,
    /// <summary>El estribo cerrado necesita al menos 2 barras en la cara.</summary>
    MenosDeDosBarras,
    /// <summary>No caben en el máximo de filas; el máximo está en <see cref="CaraSeccion.Maximo"/>.</summary>
    NoCaben,
    /// <summary>Las filas de arriba y las de abajo se pisan en el alto de la viga.</summary>
    NoCabenEnElAlto,
}

/// <summary>
/// Una barra en el corte, en mm: x desde la cara izquierda e y desde la cara de abajo de la sección, al centro de
/// la barra. <paramref name="Diametro"/> es el número de la barra.
/// </summary>
public sealed record BarraEnSeccion(int Diametro, double X, double Y);

/// <summary>
/// El reparto de una cara: cuántas caben por fila, las filas (la 1 junto a la cara), el error si lo hay, el máximo
/// de barras que admite y el recubrimiento al eje de la barra de esquina.
/// </summary>
public sealed record CaraSeccion(
    Posicion Cara,
    int CabenPorFila,
    IReadOnlyList<IReadOnlyList<BarraEnSeccion>> Filas,
    ErrorCara Error,
    int Maximo,
    double RecubrimientoAlEje);

public sealed record ResultadoSeccion(CaraSeccion Superior, CaraSeccion Inferior);

/// <summary>Una barra de bastón en el corte. <paramref name="Fila"/> empieza en 1, la fila junto a la cara.</summary>
public sealed record BastonEnSeccion(int Fila, BarraEnSeccion Barra);

/// <summary>
/// Dónde van en el corte los bastones de una cara (#89). Si no hay lugar en el máximo de filas, o la cara ya tenía
/// un error, <paramref name="Barras"/> queda vacía y <paramref name="Error"/> lo dice.
/// </summary>
public sealed record UbicacionBastones(IReadOnlyList<BastonEnSeccion> Barras, ErrorCara Error);
