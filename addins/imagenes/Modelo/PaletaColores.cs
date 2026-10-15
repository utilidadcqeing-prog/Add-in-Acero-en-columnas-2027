namespace practicacq.Imagenes.Modelo;

/// <summary>
/// Colores sólidos para pintar cada tipo seleccionado:
/// - <see cref="Colores"/>: los 8 rápidos de la maqueta de la issue #183, en ese orden.
/// - <see cref="Ampliada"/>: la paleta del botón "+", para elegir cualquier otro color con un clic
///   (pedido del 2026-10-13: elegir de una paleta en vez de escribir el código RGB).
/// </summary>
public static class PaletaColores
{
    public static IReadOnlyList<string> Colores { get; } = new[]
    {
        "#E0453A", "#F08C1A", "#E3BD12", "#2BA36B", "#1F8FD6", "#4B5BD6", "#9B4BD6", "#D6457F",
    };

    /// <summary>Tonos de la paleta ampliada (grados del círculo de color): rojo → rosa, más café.</summary>
    private static readonly int[] Tonos = { 0, 20, 35, 50, 75, 110, 150, 175, 195, 215, 240, 270, 310 };

    /// <summary>Intensidades de claro a oscuro (luminosidad HSL en %).</summary>
    private static readonly int[] Luminosidades = { 82, 68, 54, 42, 30 };

    /// <summary>
    /// Filas por intensidad (de claro a oscuro), cada una con los 13 tonos, y al final una fila de
    /// grises. Se genera una sola vez.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Ampliada { get; } = ArmarAmpliada();

    private static List<IReadOnlyList<string>> ArmarAmpliada()
    {
        var filas = Luminosidades
            .Select(l => (IReadOnlyList<string>)Tonos.Select(t => DesdeHsl(t, 0.75, l / 100.0)).ToList())
            .ToList();
        filas.Add(new[] { "#FFFFFF", "#E6E8EB", "#CDD2D8", "#B2B9C2", "#97A0AB", "#7C8794", "#636E7B", "#4B5561", "#353D47", "#22282F", "#14181D", "#0A0C0F", "#000000" });
        return filas;
    }

    private static string DesdeHsl(double tono, double saturacion, double luminosidad)
    {
        double c = (1 - Math.Abs(2 * luminosidad - 1)) * saturacion;
        double x = c * (1 - Math.Abs(tono / 60 % 2 - 1));
        double m = luminosidad - c / 2;
        (double r, double g, double b) = tono switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static int A255(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return $"#{A255(r + m):X2}{A255(g + m):X2}{A255(b + m):X2}";
    }

    /// <summary>Color inicial del tipo número <paramref name="indice"/>: recorre la paleta en orden.</summary>
    public static string PorIndice(int indice) => Colores[indice % Colores.Count];
}
