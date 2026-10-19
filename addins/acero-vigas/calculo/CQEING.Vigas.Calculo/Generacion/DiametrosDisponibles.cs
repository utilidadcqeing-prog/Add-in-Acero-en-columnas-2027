using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>
/// Qué diámetros de las reglas tienen tipo de barra en el modelo de Revit (comentario del 17-oct en #85; decisión 7 del
/// plan docs/planes/2026-10-19-generar-acero-vigas-85.md). Solo esos se ofrecen y se generan.
/// </summary>
public static class DiametrosDisponibles
{
    /// <summary>Diferencia máxima entre el diámetro nominal del tipo y el real de la regla, en mm.</summary>
    public const double Tolerancia = 0.5;

    /// <param name="nominalesMm">El diámetro nominal de cada tipo de barra del modelo, en mm.</param>
    public static IReadOnlyList<int> De(IEnumerable<double> nominalesMm, Reglas r)
    {
        List<double> nominales = nominalesMm.ToList();
        return r.PorDiametro.Where(d => nominales.Any(n => Math.Abs(n - d.Value.Diametro) <= Tolerancia)).Select(d => d.Key).Order().ToList();
    }
}
