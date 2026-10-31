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
    /// Juan, 30-oct: a 90° el gancho suma la B y la pata por fuera la mide, para que las cotas cierren en el múltiplo. Los
    /// demás suman su arco por el eje más la cola, menos el radio y medio diámetro que el tramo ya cubre. #5:
    /// radio 95 / 2 + 15.9 / 2 = 55.45.
    /// </summary>
    [Theory]
    [InlineData(TipoGancho.De90, 278)]   // la B
    [InlineData(TipoGancho.De135, 162)]  // 55.45·3π/4 + 95 − 63.4: el doblado de gancho, el de la barra en Revit
    [InlineData(TipoGancho.De180, 176)]  // 55.45·π + 65 − 63.4
    [InlineData(TipoGancho.Sin, 0)]
    public void ElGanchoSumaSuArcoYLaColaDelCuadro(TipoGancho t, int largo)
    {
        Assert.Equal(largo, Reglas.LargoGancho(Reglas.Generales.PorDiametro[5], t));
    }

    /// <summary>
    /// Carlos, 29-oct: el largo total del gancho (B) se redondea hacia arriba para cortar y doblar con una medida estándar;
    /// lo que crece va a la cola. #4 a 90°: B 222 → 250 con 5 cm (+28) y → 300 con 10 cm (+78). #5 a 90°: 278 → 300 (+22).
    /// </summary>
    [Theory]
    [InlineData(4, 0, 0)]
    [InlineData(4, 50, 28)]
    [InlineData(4, 100, 78)]
    [InlineData(5, 50, 22)]
    public void ConRedondeo_ElLargoTotalDelGanchoSubeAlSiguienteMultiplo(int numero, int redondeo, int crece)
    {
        ReglaDiametro r = Reglas.Generales.PorDiametro[numero];

        Assert.Equal(Reglas.LargoGancho(r, TipoGancho.De90) + crece, Reglas.LargoGancho(r, TipoGancho.De90, redondeo));
    }

    [Fact]
    public void ConRedondeo_UnGanchoQueYaEsMultiploNoCambiaYSinGanchoSigueEnCero()
    {
        ReglaDiametro r = Reglas.Generales.PorDiametro[5] with { Gancho90 = new Gancho(300, 213, 95, 227) };

        Assert.Equal(Reglas.LargoGancho(r, TipoGancho.De90), Reglas.LargoGancho(r, TipoGancho.De90, 50));
        Assert.Equal(0, Reglas.LargoGancho(r, TipoGancho.Sin, 50));
    }

    [Fact]
    public void PorDefectoLosGanchosSeRedondeanA5cm()
    {
        Assert.Equal(50, Reglas.Generales.RedondeoGancho);
        ReglaDiametro r5 = Reglas.Generales.PorDiametro[5];
        Assert.Equal(Reglas.LargoGancho(r5, TipoGancho.De90, 50), Reglas.Generales.LargoGanchoRedondeado(r5, TipoGancho.De90));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(30, false)]
    [InlineData(-50, false)]
    public void ElRedondeoSoloPuedeSerSin5o10cm(int redondeo, bool valido)
    {
        Assert.Equal(valido, (Reglas.Generales with { RedondeoGancho = redondeo }).Validar().Count == 0);
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
