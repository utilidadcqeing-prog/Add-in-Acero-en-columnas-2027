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
    public record Varilla(
        int Numero,
        double DiametroMm,
        double GanchoEstribo,
        double Gancho90,
        double Gancho180,
        double Traslapo);

    /// <summary>
    /// Dos barras del cuadro que se lleven menos que esto son la misma: los
    /// diámetros nominales están separados por 3 mm o más, y lo que llega del
    /// documento puede venir con redondeos de conversión.
    /// </summary>
    public const double ToleranciaMm = 0.6;

    public static readonly IReadOnlyList<Varilla> Cuadro = new[]
    {
        new Varilla(2,  6.4, 0.113, 0.112, 0.096, 0.40),
        new Varilla(3,  9.5, 0.131, 0.166, 0.142, 0.55),
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
}
