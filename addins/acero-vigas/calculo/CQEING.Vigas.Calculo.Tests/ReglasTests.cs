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

    /// <summary>
    /// Juan, 26-oct: la pata del gancho respeta la cola E del cuadro. El tramo recto llega a la cara de afuera del doblez,
    /// así que el gancho suma su arco por el eje más la cola, menos el radio y medio diámetro que el tramo ya cubre. #5:
    /// radio 95 / 2 + 15.9 / 2 = 55.45. Antes sumaba la B entera (278) y Revit alargaba la cola a 256.
    /// </summary>
    [Theory]
    [InlineData(TipoGancho.De90, 215)]   // 55.45·π/2 + 191 − 63.4
    [InlineData(TipoGancho.De135, 162)]  // 55.45·3π/4 + 95 − 63.4: el doblado de gancho, el de la barra en Revit
    [InlineData(TipoGancho.De180, 176)]  // 55.45·π + 65 − 63.4
    [InlineData(TipoGancho.Sin, 0)]
    public void ElGanchoSumaSuArcoYLaColaDelCuadro(TipoGancho t, int largo)
    {
        Assert.Equal(largo, Reglas.LargoGancho(Reglas.Generales.PorDiametro[5], t));
    }

    [Fact]
    public void SinColaEnElCuadro_ElGanchoSumaSuB()
    {
        // el cuadro no trae el 135° del #6 al #8
        Assert.Equal(272, Reglas.LargoGancho(Reglas.Generales.PorDiametro[6], TipoGancho.De135));
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

    [Fact]
    public void LasReglasGeneralesSonValidas()
    {
        Assert.Empty(Reglas.Generales.Validar());
    }

    [Fact]
    public void UnMultiploEnCeroNoEsValido()
    {
        Assert.Equal(["El múltiplo de corte debe ser mayor que 0."], (Reglas.Generales with { Modulo = 0 }).Validar());
    }

    [Fact]
    public void UnaVarillaDe9MetrosNoEsValida()
    {
        Assert.Equal(["La varilla debe ser de 6 m o de 12 m."], (Reglas.Generales with { Varilla = 9000 }).Validar());
    }

    [Fact]
    public void UnTraslapoNegativoDelNumero5NoEsValido()
    {
        Assert.Equal(["El traslapo del #5 debe ser mayor que 0."], ConDiametro(5, r => r with { Traslapo = -1 }).Validar());
    }

    [Fact]
    public void UnPesoEnCeroDelNumero4NoEsValido()
    {
        Assert.Equal(["El peso por metro del #4 debe ser mayor que 0."], ConDiametro(4, r => r with { KgPorMetro = 0 }).Validar());
    }

    [Fact]
    public void UnDiametroRealEnCeroNoEsValido()
    {
        Assert.Equal(["El diámetro del #5 debe ser mayor que 0."], ConDiametro(5, r => r with { Diametro = 0 }).Validar());
    }

    [Fact]
    public void UnaVarillaLargaMasCortaQueLaNormalNoEsValida()
    {
        Assert.Equal(["La varilla larga no puede ser más corta que la normal."],
            (Reglas.Generales with { Varilla = 12000, VarillaLarga = 6000 }).Validar());
    }

    [Fact]
    public void UnDiametroSinTraslapoSiEsValido()
    {
        Assert.Empty(ConDiametro(5, r => r with { Traslapo = null }).Validar());
    }

    private static Reglas ConDiametro(int diametro, Func<ReglaDiametro, ReglaDiametro> cambio) =>
        Reglas.Generales with
        {
            PorDiametro = new Dictionary<int, ReglaDiametro>(Reglas.Generales.PorDiametro)
            {
                [diametro] = cambio(Reglas.Generales.PorDiametro[diametro]),
            },
        };
}
