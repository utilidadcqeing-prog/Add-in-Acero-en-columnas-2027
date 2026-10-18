namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>
/// Lo que el usuario elige de los bastones en el paso 2 del tablero (#89). <paramref name="Superior"/> e
/// <paramref name="Inferior"/> son las barras de cada bastón de esa cara; null = 1 del diámetro de las esquinas de
/// esa capa, y cantidad 0 = esa cara no lleva bastones. <paramref name="Largo"/> es el largo general en mm y
/// <paramref name="Largos"/> los que se escribieron a mano, por id.
/// </summary>
public sealed record ArmadoBastones(
    GrupoBarras? Superior = null,
    GrupoBarras? Inferior = null,
    int Largo = 2000,
    bool ConGancho = false,
    IReadOnlyDictionary<string, int>? Largos = null);

/// <summary>
/// Un bastón, tal como lo pinta el tablero, en mm desde el eje A. <paramref name="Donde"/> es el eje del apoyo
/// ("B") o el nombre de la luz ("A-B") y <paramref name="Eje"/> el punto donde se centra. <paramref name="Desde"/> y
/// <paramref name="Hasta"/> son las puntas de su parte recta; <paramref name="Largo"/> incluye los ganchos.
/// </summary>
public sealed record Baston(
    string Id,
    Posicion Capa,
    string Donde,
    bool EnApoyo,
    int Eje,
    int Desde,
    int Hasta,
    int GanchoInicio,
    int GanchoFin,
    int Largo,
    bool Manual,
    GrupoBarras Grupo,
    double Kg,
    TipoGancho TipoInicio = TipoGancho.Sin,
    TipoGancho TipoFin = TipoGancho.Sin,
    bool PuntaInicioEnBorde = false,
    bool PuntaFinEnBorde = false)
{
    public int LargoRecto => Hasta - Desde;
}
