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

    [Fact]
    public void ElGanchoDe90DelNumero5EsElDelCuadroDeDimensiones()
    {
        Assert.Equal(new Gancho(278, 191, 95, 227), Reglas.Generales.PorDiametro[5].Gancho90);
    }

    [Fact]
    public void ElGanchoDe180DelNumero8EsElDelCuadroDeDimensiones()
    {
        Assert.Equal(new Gancho(381, 102, 152, 319), Reglas.Generales.PorDiametro[8].Gancho180);
    }

    [Fact]
    public void ElEstriboDe135DelNumero3SaleDelCuadroDeDimensiones()
    {
        Assert.Equal(new Gancho(113, 57, 38, 84), Reglas.Generales.PorDiametro[3].Estribo135);
    }

    [Fact]
    public void ElEstriboDe135DelNumero6SaleDelCuadroDeTraslaposSinEDA()
    {
        Assert.Equal(new Gancho(272), Reglas.Generales.PorDiametro[6].Estribo135);
    }

    [Fact]
    public void ElNumero2NoTieneTraslapoYNoSeMuestra()
    {
        Assert.Null(Reglas.Generales.PorDiametro[2].Traslapo);
        Assert.Equal([3, 4, 5, 6, 7, 8], Reglas.DiametrosEnPantalla);
    }

    [Fact]
    public void DosReglasConLosMismosValoresEquivalen()
    {
        Reglas copia = Reglas.Generales with { PorDiametro = new Dictionary<int, ReglaDiametro>(Reglas.Generales.PorDiametro) };

        Assert.True(Reglas.Generales.Equivale(copia));
        Assert.False(Reglas.Generales.Equivale(Reglas.Generales with { Modulo = 600 }));
    }
}
