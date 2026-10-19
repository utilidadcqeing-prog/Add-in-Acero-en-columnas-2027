using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>Los rótulos de DC-CAD en número y en pulgadas (decisión 8 del plan docs/planes/2026-10-19-ajustes-tablero-vigas-100.md).</summary>
public class RotuloTests
{
    [Fact]
    public void EnPulgadasLaCantidadVaConØ()
    {
        Assert.Equal("3Ø⅝\"L= 2.80/3.00", Rotulo.EnPulgadas([new GrupoBarras(3, 5)], 2800, 3000));
    }

    [Fact]
    public void EnPulgadasSinGanchoUnSoloLargoYVariosTiposConMas()
    {
        Assert.Equal("3Ø⅝\"+2Ø½\"L= 5.00", Rotulo.EnPulgadas([new GrupoBarras(3, 5), new GrupoBarras(2, 4)], 5000, 5000));
    }

    [Theory]
    [InlineData(2, "¼\"")]
    [InlineData(3, "⅜\"")]
    [InlineData(4, "½\"")]
    [InlineData(6, "¾\"")]
    [InlineData(7, "⅞\"")]
    [InlineData(8, "1\"")]
    [InlineData(9, "1⅛\"")]
    [InlineData(10, "1¼\"")]
    [InlineData(11, "1⅜\"")]
    public void CadaDiametroEnPulgadasSimplificado(int diametro, string pulgadas)
    {
        Assert.Equal(pulgadas, Rotulo.Pulgadas(diametro));
    }
}
