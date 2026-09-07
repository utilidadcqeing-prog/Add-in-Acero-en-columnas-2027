namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

/// <summary>
/// El "Cuadro de ganchos y traslapos de varillas" de la empresa, en metros.
///
/// Se compara contra esto y no contra lo que Revit calcularía por su cuenta: para
/// una #3, Revit propone 4,8 cm de gancho de estribo y el cuadro pide 13,1 cm. El
/// que manda es el cuadro.
///
/// Las barras se identifican por diámetro nominal y no por nombre, porque en la
/// plantilla conviven nombres imperiales ("#3 COL") y métricos ("6 mm").
/// </summary>
public static class EstandarCQ
{
    public record Varilla(
        int Numero,
        double DiametroMm,
        double GanchoEstribo,
        double Gancho90,
        double Gancho180,
        double Traslapo);

    private static readonly Varilla[] Cuadro =
    {
        new(2,  6.4, 0.113, 0.112, 0.096, 0.40),
        new(3,  9.5, 0.131, 0.166, 0.142, 0.55),
        new(4, 12.7, 0.151, 0.222, 0.190, 0.75),
        new(5, 15.9, 0.189, 0.278, 0.238, 0.90),
        new(6, 19.1, 0.272, 0.334, 0.286, 1.10),
        new(7, 22.2, 0.316, 0.388, 0.333, 1.35),
        new(8, 25.4, 0.362, 0.444, 0.381, 1.70),
    };

    /// <summary>
    /// Busca la varilla del cuadro que corresponde a ese tipo de barra, o null si el
    /// diámetro no es ninguno de los normalizados.
    /// </summary>
    public static Varilla? Buscar(RebarBarType tipo)
    {
        double mm = UnitUtils.ConvertFromInternalUnits(
            tipo.BarNominalDiameter, UnitTypeId.Millimeters);

        return Cuadro.FirstOrDefault(v => System.Math.Abs(v.DiametroMm - mm) < 0.6);
    }

    /// <summary>Los valores del cuadro vienen en metros; la API trabaja en pies.</summary>
    public static double APies(double metros) =>
        UnitUtils.ConvertToInternalUnits(metros, UnitTypeId.Meters);
}
