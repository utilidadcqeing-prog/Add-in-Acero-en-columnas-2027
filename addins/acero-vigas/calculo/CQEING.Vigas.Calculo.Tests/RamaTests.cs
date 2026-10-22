using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// De qué lado de sus barras va la rama del estribo abierto, en la X del corte (+1 a la derecha, −1 a la izquierda), en
/// una viga de 300 de ancho: en C, hacia el centro en las dos barras; en S, abajo del otro lado (hallazgo H6).
/// </summary>
public class RamaTests
{
    [Fact]
    public void EnC_LaRamaVaHaciaElCentroArribaYAbajo()
    {
        Assert.Equal((-1, -1), new Rama(200, 440, 60, 16).Lados(300, FormaGrapa.C));
        Assert.Equal((1, 1), new Rama(100, 440, 60, 16).Lados(300, FormaGrapa.C));
    }

    [Fact]
    public void EnS_AbajoPasaAlOtroLadoDeLaBarra()
    {
        Assert.Equal((1, -1), new Rama(200, 440, 60, 16).Lados(300, FormaGrapa.S));
        Assert.Equal((-1, 1), new Rama(100, 440, 60, 16).Lados(300, FormaGrapa.S));
    }

    [Fact]
    public void EnElCentroExacto_VaALaDerecha()
    {
        Assert.Equal((1, 1), new Rama(150, 440, 60, 16).Lados(300, FormaGrapa.C));
    }
}
