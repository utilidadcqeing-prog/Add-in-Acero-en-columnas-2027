using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>La extensión del gancho propio para que Revit mida lo mismo que el tablero (#85, decisión 8b).</summary>
public class GanchoPropioTests
{
    [Fact]
    public void ConLaBarraDelModeloDePrueba_DaElGanchoQueMidioRevit()
    {
        // "16 B 400 S": d = 16, doblado del gancho 64. Con el gancho del proyecto (extensión 80) Revit dio 4317 para una
        // línea de 4222: 4222 − (40 + 8) + 62.8 + 80. A 135° la extensión calculada da 4222 + 278.
        double ext = GanchoPropio.Extension(278, 16, 64, 135);

        Assert.Equal(4222 + 278, 4222 - (40 + 8) + 40 * 3 * Math.PI / 4 + ext, 6);
        Assert.Equal(4317, Math.Round(4222 - (40 + 8) + 40 * Math.PI / 2 + 80));
    }

    /// <summary>Juan, 30-oct: a 90° la pata por fuera mide la B, para que las cotas sumen el largo del tablero.</summary>
    [Fact]
    public void A90_LaPataPorFueraMideLaB()
    {
        double ext = GanchoPropio.Extension(300, 16, 64, 90);

        Assert.Equal(300, ext + 40 + 8, 6);
        // cotas 2700 + 300; por el eje: 2700 − 48 + 62.8 + 252
        Assert.Equal(3000, 2700 - 48 + 40 * Math.PI / 2 + ext + GanchoPropio.DiferenciaCotas(16, 64), 6);
    }

    [Fact]
    public void NuncaMenosDeUnDiametro()
    {
        Assert.Equal(16, GanchoPropio.Extension(10, 16, 64, 180));
    }

    [Fact]
    public void DelEstribo_DaElAnguloYLaColaDelCuadro()
    {
        IReadOnlyDictionary<int, ReglaDiametro> r = Reglas.Generales.PorDiametro;

        Assert.Equal((135, 57), GanchoPropio.DeEstribo(GanchoEstribo.De135, r[3]));
        Assert.Equal((180, 65), GanchoPropio.DeEstribo(GanchoEstribo.De180, r[3]));
        Assert.Equal((180, 76), GanchoPropio.DeEstribo(GanchoEstribo.De180, r[6]));
        // sin E en el cuadro: quien lo usa pone 6 diámetros
        Assert.Equal((90, (int?)null), GanchoPropio.DeEstribo(GanchoEstribo.De90, r[3]));
        Assert.Equal((135, (int?)null), GanchoPropio.DeEstribo(GanchoEstribo.De135, r[6]));
    }

    [Fact]
    public void ElNombreDiceAnguloYDiametro()
    {
        Assert.Equal("CQEING 90° #5", GanchoPropio.Nombre(90, 5));
        Assert.Equal("CQEING Estribo 135° #3", GanchoPropio.Nombre(135, 3, estribo: true));
    }
}
