namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>
/// Valores de un diámetro, en mm y kg/m. <paramref name="Traslapo"/> es null si la tabla no lo trae (#2 en vigas).
/// <paramref name="Gancho90"/> es la longitud B del cuadro "Dimensiones mínimas para ganchos estándar".
/// </summary>
public sealed record ReglaDiametro(int? Traslapo, int Gancho90, double KgPorMetro);

/// <summary>
/// Reglas generales (#82), en mm. Las claves de <paramref name="PorDiametro"/> son el número de la barra (5 = #5).
/// </summary>
/// <param name="Modulo">Múltiplo de corte.</param>
/// <param name="Varilla">Varilla comercial normal (6 m o 12 m, según el botón de Reglas).</param>
/// <param name="VarillaLarga">La que se usa sola cuando un tramo no se resuelve con la normal.</param>
public sealed record Reglas(IReadOnlyDictionary<int, ReglaDiametro> PorDiametro, int Modulo = 500, int Varilla = 6000,
    int VarillaLarga = 12000)
{
    /// <summary>Los cuadros de Carlos (reunión del 16-oct-2026) y los pesos estándar.</summary>
    public static Reglas Generales { get; } = new(new Dictionary<int, ReglaDiametro>
    {
        [2] = new(null, 112, 0.250),
        [3] = new(550, 166, 0.560),
        [4] = new(750, 222, 0.994),
        [5] = new(900, 278, 1.552),
        [6] = new(1100, 334, 2.235),
        [7] = new(1350, 388, 3.042),
        [8] = new(1700, 444, 3.973),
    });
}
