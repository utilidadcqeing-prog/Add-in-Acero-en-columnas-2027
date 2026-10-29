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

    [Fact]
    public void UnaBarraConNumerosQueMidenDistinto_LlevaUnRotuloPorMedida()
    {
        var b = new Barra("S1", Posicion.Superior, 50, 3550, 450, 0, 4000, 6000, false, false,
            [new GrupoBarras(2, 8), new GrupoBarras(2, 5)], 0, TipoGancho.De90,
            Menores: [new MedidaBarra(5, 50, 3750, 250, 0, 4000, 6000)]);

        Assert.Equal("2#8L= 3.50/4.00 · 2#5L= 3.70/4.00", Rotulo.De(b));
        Assert.Equal("2Ø1\"L= 3.50/4.00 · 2Ø⅝\"L= 3.70/4.00", Rotulo.EnPulgadas(b));
    }

    [Fact]
    public void UnaBarraConNumerosQueMidenIgual_LlevaUnSoloRotulo()
    {
        var b = new Barra("S1", Posicion.Superior, 50, 3550, 450, 0, 4000, 6000, false, false,
            [new GrupoBarras(2, 5), new GrupoBarras(1, 5, 2)], 0, TipoGancho.De90);

        Assert.Equal("2#5+1#5L= 3.50/4.00", Rotulo.De(b));
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
