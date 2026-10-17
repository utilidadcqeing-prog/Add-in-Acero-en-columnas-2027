using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// El zoom de la página del tablero (#83, decisión 6): la página mide 1440 × 900 y se achica entera cuando el área
/// disponible es más chica, en unidades WPF (DIP), para que ningún texto se corte.
/// </summary>
public class CalculadoraZoomTests
{
    [Fact]
    public void EnUnaPantallaGrandeNoSeAchica() => Assert.Equal(1, CalculadoraZoom.Factor(1920, 1040));

    [Fact]
    public void EnUnaPantallaChicaSeAchicaParaEntrar() => Assert.Equal(680.0 / 900, CalculadoraZoom.Factor(1280, 680), 10);

    /// <summary>Una pantalla de 1920 × 1080 al 150 % da 1280 × 720 DIP, con el área de trabajo cerca de 690.</summary>
    [Fact]
    public void Con150PorCientoEn1080p() => Assert.Equal(690.0 / 900, CalculadoraZoom.Factor(1280, 690), 10);
}
