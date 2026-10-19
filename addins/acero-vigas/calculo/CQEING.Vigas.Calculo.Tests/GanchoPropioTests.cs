using CQEING.Vigas.Calculo.Generacion;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>La extensión del gancho propio para que Revit mida lo mismo que el tablero (#85, decisión 8b).</summary>
public class GanchoPropioTests
{
    [Fact]
    public void ConLaBarraDelModeloDePrueba_DaElGanchoQueMidioRevit()
    {
        // "16 B 400 S": d = 16, doblado del gancho 64. Con el gancho del proyecto (extensión 80) Revit dio 4317 para una
        // línea de 4222: 4222 − (40 + 8) + 62.8 + 80. Con la extensión calculada tiene que dar 4222 + 278.
        double ext = GanchoPropio.Extension(278, 16, 64, 90);

        Assert.Equal(278 + 40 + 8 - 40 * Math.PI / 2, ext, 6);
        Assert.Equal(4222 + 278, 4222 - (40 + 8) + 40 * Math.PI / 2 + ext, 6);
        Assert.Equal(4317, Math.Round(4222 - (40 + 8) + 40 * Math.PI / 2 + 80));
    }

    [Fact]
    public void NuncaMenosDeUnDiametro()
    {
        Assert.Equal(16, GanchoPropio.Extension(10, 16, 64, 180));
    }

    [Fact]
    public void ElNombreDiceAnguloYDiametro()
    {
        Assert.Equal("CQEING 90° #5", GanchoPropio.Nombre(90, 5));
        Assert.Equal("CQEING Estribo 135° #3", GanchoPropio.Nombre(135, 3, estribo: true));
    }
}
