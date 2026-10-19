namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// El "Cuadro de ganchos y traslapos de varillas" de la empresa, en metros, sin
/// ninguna dependencia de la API de Revit.
///
/// Vive separado de <see cref="EstandarCQ"/> (que sí referencia Revit, porque
/// busca por <c>RebarBarType</c>) para que la ventana pueda elegir el tipo de
/// barra por defecto POR DIÁMETRO sin arrastrar Revit a <c>Modelo/</c>/<c>Vistas/</c>
/// -- la misma frontera que ya respetan <see cref="TipoGancho"/> e
/// <see cref="IProveedorTipos"/>. <c>tools/PreviewUI</c> excluye
/// <c>EstandarCQ.cs</c> justamente por eso, y necesita igual la tabla.
///
/// Las barras se identifican por diámetro nominal y no por nombre, porque en la
/// plantilla conviven nombres imperiales ("#3 COL") y métricos ("6 mm").
/// </summary>
public static class CuadroVarillas
{
    /// <param name="GanchoEstribo">
    /// Longitud del gancho de estribo de 135° (la "B" del cuadro "Dimensiones
    /// mínimas para ganchos estándar"). Para los estribos manda ESE cuadro y
    /// no la columna ESTRIBOS del cuadro de ganchos y traslapos, que para la
    /// #2 y la #3 dice otra cosa (0,113 y 0,131) -- confirmado por Luciano el
    /// 8-oct, issue #65. El cuadro de dimensiones mínimas solo llega hasta la
    /// #5; de la #6 a la #8 se conserva el valor del otro cuadro.
    /// </param>
    /// <param name="Traslapo">
    /// El de la columna "VIGAS" del cuadro, que aplica también a columnas
    /// (confirmado por Luciano con el cuadro nuevo, 8-oct). <c>null</c> = el
    /// cuadro no da traslapo para esa barra en vigas y columnas: es el caso de
    /// la #2, que solo lo tiene en muros (0,40 m). El cuadro trae además
    /// columnas "LOSAS" y "MUROS", hoy con los mismos valores que "VIGAS" para
    /// las barras #3 a #8; no se cargan porque nada las usa todavía.
    /// </param>
    public record Varilla(
        int Numero,
        double DiametroMm,
        double GanchoEstribo,
        double Gancho90,
        double Gancho180,
        double? Traslapo);

    /// <summary>
    /// Dos barras del cuadro que se lleven menos que esto son la misma: los
    /// diámetros nominales están separados por 3 mm o más, y lo que llega del
    /// documento puede venir con redondeos de conversión.
    /// </summary>
    public const double ToleranciaMm = 0.6;

    public static readonly IReadOnlyList<Varilla> Cuadro = new[]
    {
        new Varilla(2,  6.4, 0.076, 0.112, 0.096, null),
        new Varilla(3,  9.5, 0.113, 0.166, 0.142, 0.55),
        new Varilla(4, 12.7, 0.151, 0.222, 0.190, 0.75),
        new Varilla(5, 15.9, 0.189, 0.278, 0.238, 0.90),
        new Varilla(6, 19.1, 0.272, 0.334, 0.286, 1.10),
        new Varilla(7, 22.2, 0.316, 0.388, 0.333, 1.35),
        new Varilla(8, 25.4, 0.362, 0.444, 0.381, 1.70),
    };

    /// <summary>La varilla del cuadro con ese diámetro, o null si no es ninguno de los normalizados.</summary>
    public static Varilla? PorDiametro(double diametroMm) =>
        Cuadro.FirstOrDefault(v => System.Math.Abs(v.DiametroMm - diametroMm) < ToleranciaMm);

    /// <summary>El diámetro nominal de la varilla número N, o null si ese número no está en el cuadro.</summary>
    public static double? DiametroDe(int numero) =>
        Cuadro.FirstOrDefault(v => v.Numero == numero)?.DiametroMm;

    /// <summary>
    /// Nombre del tipo de barra propio del add-in para la varilla número N
    /// ("#4 CQEING"). Se busca por NOMBRE y no por diámetro porque por diámetro
    /// una barra métrica pasa por nuestra: la "16 B 400 S" de una plantilla
    /// europea queda a 0,1 mm de la #5 (19-oct). Ver
    /// <c>docs/planes/2026-10-19-varillas-propias-cqeing.md</c>.
    /// </summary>
    public static string NombrePropio(int numero) => $"#{numero} CQEING";

    /// <summary>La varilla del cuadro cuyo tipo propio se llama así, o null si el nombre no es uno de los nuestros.</summary>
    public static Varilla? PorNombrePropio(string nombre) =>
        Cuadro.FirstOrDefault(v => NombrePropio(v.Numero) == nombre);
}
