using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Qué diámetros de las reglas tienen tipo de barra en el modelo (comentario del 17-oct en #85; decisión 7 del plan
/// docs/planes/2026-10-19-generar-acero-vigas-85.md): el diámetro nominal del tipo a menos de 0.5 mm del de la regla.
/// </summary>
public class DiametrosDisponiblesTests
{
    [Fact]
    public void LosQueCoincidenConUnTipoDelModelo()
    {
        // 3/8", 5/8" y 1" en mm, como los da Revit; 12 mm no es el #4 (12.7).
        Assert.Equal([3, 5, 8], DiametrosDisponibles.De([9.525, 15.875, 25.4, 12, 32], Reglas.Generales));
    }

    [Fact]
    public void SinTiposNoHayNinguno()
    {
        Assert.Empty(DiametrosDisponibles.De([], Reglas.Generales));
    }
}
