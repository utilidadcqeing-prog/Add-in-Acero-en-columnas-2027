namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>
/// Un color por diámetro, el mismo para el tablero y para Revit (#85). Son los mismos colores que usan las columnas
/// (23-oct); el #5 sigue rojo (Carlos). Decisión 9 del plan docs/planes/2026-10-19-ajustes-tablero-vigas-100.md.
/// </summary>
public static class ColoresDiametro
{
    /// <summary>El de un diámetro que no está en la tabla.</summary>
    public const string SinColor = "#5E6670";

    private static readonly Dictionary<int, string> Tabla = new()
    {
        [3] = "#FBBC04", // amarillo
        [4] = "#4285F4", // azul
        [5] = "#EA4335", // rojo
        [6] = "#34A853", // verde
        [7] = "#9334E6", // morado
        [8] = "#8B4513", // marrón
        [9] = "#E91E63", // rosado fuerte
        [10] = "#000000", // negro
        [11] = "#FF6D01", // naranja
    };

    /// <summary>El color de <paramref name="diametro"/> (en octavos de pulgada), como <c>"#EA4335"</c>.</summary>
    public static string De(int diametro) => Tabla.GetValueOrDefault(diametro, SinColor);

    /// <summary>El mismo color en sus tres canales, para los filtros de vista de Revit.</summary>
    public static (byte R, byte G, byte B) Rgb(int diametro)
    {
        int n = Convert.ToInt32(De(diametro)[1..], 16);
        return ((byte)(n >> 16), (byte)(n >> 8), (byte)n);
    }

    /// <summary>
    /// El contorno de toda barra en Revit (la vista previa 3D y el acero generado): casi negro, el mismo para todos los
    /// diámetros (Juan, 21-oct). Marca dónde empieza y termina cada barra y dónde una pasa por otra. No es negro puro: con el
    /// fondo oscuro, Revit pinta de blanco las líneas negras (21-oct, las aristas del acero generado salían blancas).
    /// </summary>
    public static (byte R, byte G, byte B) RgbContorno { get; } = (30, 30, 30);
}
