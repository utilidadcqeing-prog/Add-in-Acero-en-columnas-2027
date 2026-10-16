using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

public class ReglasTests
{
    [Fact]
    public void LasReglasGeneralesTraenElDiametroRealDeCadaBarra()
    {
        Assert.Equal(15.9, Reglas.Generales.PorDiametro[5].Diametro);
        Assert.Equal(9.5, Reglas.Generales.PorDiametro[3].Diametro);
    }

    [Fact]
    public void LaDistanciaLibreEntreBarrasEsDe25mmPorDefecto()
    {
        Assert.Equal(25, Reglas.Generales.DistanciaLibre);
    }
}
