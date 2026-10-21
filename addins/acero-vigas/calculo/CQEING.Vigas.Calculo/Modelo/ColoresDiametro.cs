namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>
/// Un color por diámetro, el mismo para el tablero y para Revit (#85). Carlos pidió el #5 rojo y el #4 naranja; el
/// resto lo propusimos y Juan lo aprobó "por ahora" (decisión 9 del plan docs/planes/2026-10-19-ajustes-tablero-vigas-100.md).
/// </summary>
public static class ColoresDiametro
{
    /// <summary>El de un diámetro que no está en la tabla.</summary>
    public const string SinColor = "#5E6670";

    private static readonly Dictionary<int, string> Tabla = new()
    {
        [3] = "#B8860B", // amarillo
        [4] = "#E8710A", // naranja
        [5] = "#D32F2F", // rojo
        [6] = "#C2185B", // magenta
        [7] = "#7B1FA2", // morado
        [8] = "#1565C0", // azul
        [9] = "#00838F", // cian
        [10] = "#2E7D32", // verde
        [11] = "#6D4C41", // café
    };

    /// <summary>El color de <paramref name="diametro"/> (en octavos de pulgada), como <c>"#D32F2F"</c>.</summary>
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
