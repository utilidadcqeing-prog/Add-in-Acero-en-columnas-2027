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

    /// <summary>
    /// La grapa #3 de la Vigueta T1 (10 × 35, recubrimiento 3, 1 #4 arriba y abajo al centro): radio de doblado por el eje
    /// 23.75 (doblado de estribo de 38). Cada doblez se apoya en su barra: su cara interior la toca por fuera.
    /// </summary>
    private static readonly Rama VigaT1 = new(50, 304.15, 45.85, 12.7);

    [Fact]
    public void ElDoblezSeApoyaEnSuBarra()
    {
        // 23.75 − 9.5/2 − 12.7/2 = 12.65 hacia adentro de la viga desde el centro de la barra
        ((double X, double Y) abajo, (double X, double Y) arriba) = VigaT1.Dobleces(23.75, 9.5);

        Assert.Equal((50, 58.5), (abajo.X, Math.Round(abajo.Y, 6)));
        Assert.Equal((50, 291.5), (arriba.X, Math.Round(arriba.Y, 6)));
    }

    [Fact]
    public void EnC_LasPuntasSonLasDeSiempre()
    {
        // la rama al lado de las barras, un radio hacia el centro, y cada punta en la cara exterior de su doblez
        ((double X, double Y) inicio, (double X, double Y) fin) = VigaT1.Puntas(100, FormaGrapa.C, 23.75, 9.5);

        Assert.Equal((73.75, 30.0), (Math.Round(inicio.X, 6), Math.Round(inicio.Y, 6)));
        Assert.Equal((73.75, 320.0), (Math.Round(fin.X, 6), Math.Round(fin.Y, 6)));
    }

    [Fact]
    public void EnS_LaRamaEsLaTangenteEntreLosDosDobleces()
    {
        // Medido en Revit el 26-oct (vigueta 389858, recubrimiento de la viga sin interferir): con estas puntas los dos
        // dobleces quedan justo en su barra y el recubrimiento de la grapa en 30.
        ((double X, double Y) inicio, (double X, double Y) fin) = VigaT1.Puntas(100, FormaGrapa.S, 23.75, 9.5);

        Assert.Equal((20.94, 35.44), (Math.Round(inicio.X, 2), Math.Round(inicio.Y, 2)));
        Assert.Equal((79.06, 314.56), (Math.Round(fin.X, 2), Math.Round(fin.Y, 2)));
    }
}
