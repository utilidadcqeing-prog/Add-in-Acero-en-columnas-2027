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

    /// <summary>
    /// La ventana arrastrada hasta dejar el navegador sin alto: WebView2 no acepta un zoom de 0 (lanza dentro de un
    /// evento de WPF, en el hilo de Revit). Revisión final de la rama.
    /// </summary>
    [Theory]
    [InlineData(1440, 0)]
    [InlineData(0, 900)]
    [InlineData(10, 10)]
    public void UnAreaSinTamanoNoDaUnZoomInvalido(double ancho, double alto) =>
        Assert.Equal(CalculadoraZoom.Minimo, CalculadoraZoom.Factor(ancho, alto));

    /// <summary>
    /// Revit en el segundo monitor: la ventana se recorta y se encaja en el área de trabajo de ese monitor, que puede
    /// empezar en x = 1920 (antes se usaba siempre la del monitor principal y la ventana saltaba de monitor).
    /// </summary>
    [Fact]
    public void LaVentanaSeEncajaEnElAreaDeSuMonitor()
    {
        var area = new Caja(1920, 0, 1280, 680);

        Assert.Equal(new Caja(1920, 0, 1280, 680), CalculadoraZoom.Encajar(new Caja(1800, -20, 1456, 939), area));
        Assert.Equal(new Caja(2000, 100, 1000, 500), CalculadoraZoom.Encajar(new Caja(2000, 100, 1000, 500), area));
        Assert.Equal(new Caja(2200, 180, 1000, 500), CalculadoraZoom.Encajar(new Caja(2500, 300, 1000, 500), area));
    }
}
