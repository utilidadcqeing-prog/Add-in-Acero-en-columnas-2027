namespace practicacq.Modelo;

using System.Text.Json.Serialization;

/// <summary>Grapa o caja — la rejilla de L4 alterna entre estos dos según lo que clickee el usuario.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoEstriboMenor
{
    Grapa,
    Caja
}

/// <summary>
/// Un estribo menor (grapa o caja) ubicado en la rejilla que arma L4 a partir
/// de <see cref="ConfigLongitudinal.BarrasPorCaraX"/>/<see cref="ConfigLongitudinal.BarrasPorCaraY"/>.
///
/// La posición es un índice de rejilla, no una coordenada — <c>PosicionX = 1</c>
/// significa "entre la primera y la segunda barra de la cara X", no un metro
/// ni un pie. Convertir eso a geometría real es trabajo de J4, no de este POCO.
/// </summary>
public sealed class EstriboMenor
{
    public int PosicionX { get; set; }
    public int PosicionY { get; set; }
    public TipoEstriboMenor Tipo { get; set; }
}
