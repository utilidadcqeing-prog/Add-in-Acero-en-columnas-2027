using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Bastones (#89) en el pórtico de la maqueta: ejes A, B, C y D en 0, 4.40, 7.90 y 12.30 m, columnas de 0.40 m,
/// voladizo de 1.50 m y puntas a 50 mm de la cara exterior (las barras van de −150 a 13950).
/// </summary>
public class BastonesTests
{
    private static EntradaVigas Entrada(ArmadoBastones? bastones = null, TipoViga tipo = TipoViga.Aerea,
        Portico? portico = null, IReadOnlyList<GrupoBarras>? inferior = null) =>
        new(portico ?? Datos.PorticoMaqueta(), tipo, [new GrupoBarras(3, 5)], inferior ?? [new GrupoBarras(3, 5)],
            Reglas.Generales, Bastones: bastones);

    private static Baston De(ResultadoVigas r, string id) => r.Bastones.Single(b => b.Id == id);

    [Fact]
    public void EnLaVigaAerea_ArribaVanEnTodosLosApoyos()
    {
        ResultadoVigas r = MotorVigas.Calcular(Entrada());
        List<Baston> arriba = r.Bastones.Where(b => b.Capa == Posicion.Superior).ToList();

        Assert.Equal(["BS1", "BS2", "BS3", "BS4"], arriba.Select(b => b.Id));
        Assert.Equal(["A", "B", "C", "D"], arriba.Select(b => b.Donde));
        Assert.All(arriba, b => Assert.True(b.EnApoyo));
        Assert.Equal((3400, 5400, 2000), (De(r, "BS2").Desde, De(r, "BS2").Hasta, De(r, "BS2").Largo));
    }

    [Fact]
    public void ElDelApoyoDeBordeSeCortaEnLaPuntaDeLaViga()
    {
        Baston bs1 = De(MotorVigas.Calcular(Entrada()), "BS1");

        Assert.Equal((-150, 1000, 1150), (bs1.Desde, bs1.Hasta, bs1.Largo));
        Assert.Equal((0, 0), (bs1.GanchoInicio, bs1.GanchoFin));
    }

    [Fact]
    public void AbajoVanEnElCentroDeLaLuzLibre()
    {
        ResultadoVigas r = MotorVigas.Calcular(Entrada());
        List<Baston> abajo = r.Bastones.Where(b => b.Capa == Posicion.Inferior).ToList();

        Assert.Equal(["BI1", "BI2", "BI3"], abajo.Select(b => b.Id));
        Assert.Equal(["A-B", "B-C", "C-D"], abajo.Select(b => b.Donde));
        Assert.All(abajo, b => Assert.False(b.EnApoyo));
        Assert.Equal((6150, 5150, 7150), (De(r, "BI2").Eje, De(r, "BI2").Desde, De(r, "BI2").Hasta));
    }

    [Fact]
    public void ConGancho_SoloLaPuntaQueLlegaAlBordeLoLleva()
    {
        ResultadoVigas r = MotorVigas.Calcular(Entrada(new ArmadoBastones(ConGancho: true)));

        Assert.Equal((278, 0, 1150 + 278), (De(r, "BS1").GanchoInicio, De(r, "BS1").GanchoFin, De(r, "BS1").Largo));
        Assert.Equal((0, 0, 2000), (De(r, "BS2").GanchoInicio, De(r, "BS2").GanchoFin, De(r, "BS2").Largo));
    }

    [Fact]
    public void ConVoladizoCorto_ElDelApoyoLlegaALaPuntaDelVoladizo()
    {
        Portico corto = Datos.PorticoMaqueta() with { VoladizoFinal = 500 };

        Baston bs4 = De(MotorVigas.Calcular(Entrada(new ArmadoBastones(ConGancho: true), portico: corto)), "BS4");

        Assert.Equal((11300, 12950, 278), (bs4.Desde, bs4.Hasta, bs4.GanchoFin));
    }

    [Fact]
    public void EnCimentacion_SeInvierten()
    {
        ResultadoVigas r = MotorVigas.Calcular(Entrada(tipo: TipoViga.Cimentacion));

        Assert.Equal(["BI1", "BI2", "BI3", "BI4"], r.Bastones.Where(b => b.EnApoyo).Select(b => b.Id));
        Assert.All(r.Bastones.Where(b => b.EnApoyo), b => Assert.Equal(Posicion.Inferior, b.Capa));
        Assert.Equal(["BS1", "BS2", "BS3"], r.Bastones.Where(b => !b.EnApoyo).Select(b => b.Id));
        Assert.All(r.Bastones.Where(b => !b.EnApoyo), b => Assert.Equal(Posicion.Superior, b.Capa));
    }

    [Fact]
    public void PorDefecto_UnaBarraDelDiametroDeLasEsquinas()
    {
        ResultadoVigas r = MotorVigas.Calcular(Entrada(inferior: [new GrupoBarras(2, 4), new GrupoBarras(1, 5)]));

        Assert.All(r.Bastones.Where(b => b.Capa == Posicion.Superior), b => Assert.Equal(new GrupoBarras(1, 5), b.Grupo));
        Assert.All(r.Bastones.Where(b => b.Capa == Posicion.Inferior), b => Assert.Equal(new GrupoBarras(1, 4), b.Grupo));
    }

    [Fact]
    public void ElUsuarioEligeElAceroDeLosDeArriba()
    {
        Baston bs2 = De(MotorVigas.Calcular(Entrada(new ArmadoBastones(Superior: new GrupoBarras(2, 6)))), "BS2");

        Assert.Equal(new GrupoBarras(2, 6), bs2.Grupo);
        Assert.Equal(2.0 * 2 * 2.235, bs2.Kg, precision: 6);
    }

    [Fact]
    public void ConCantidadCero_EsaCapaNoLlevaBastones()
    {
        ResultadoVigas r = MotorVigas.Calcular(Entrada(new ArmadoBastones(Inferior: new GrupoBarras(0, 5))));

        Assert.DoesNotContain(r.Bastones, b => b.Capa == Posicion.Inferior);
        Assert.Equal(4, r.Bastones.Count);
    }

    [Fact]
    public void UnLargoEscritoNoSeRedondea()
    {
        var largos = new Dictionary<string, int> { ["BS2"] = 2300 };
        ResultadoVigas r = MotorVigas.Calcular(Entrada(new ArmadoBastones(Largos: largos)));

        Assert.Equal((3250, 5550, true), (De(r, "BS2").Desde, De(r, "BS2").Hasta, De(r, "BS2").Manual));
        Assert.Equal((2000, false), (De(r, "BS3").Largo, De(r, "BS3").Manual));
    }

    [Fact]
    public void UnLargoImparQuedaCompleto()
    {
        Baston bs2 = De(MotorVigas.Calcular(Entrada(new ArmadoBastones(Largo: 2301))), "BS2");

        Assert.Equal(2301, bs2.Hasta - bs2.Desde);
    }

    [Fact]
    public void LosKilosDeLosBastonesSumanAlTotal()
    {
        ResultadoVigas r = MotorVigas.Calcular(Entrada());

        Assert.NotEmpty(r.Bastones);
        Assert.Equal(r.Barras.Sum(b => b.Kg) + r.Bastones.Sum(b => b.Kg), r.Kg, precision: 6);
        Assert.Equal(1.150 * 1.552, De(r, "BS1").Kg, precision: 6);
    }

    [Fact]
    public void UnDiametroSinReglas_AvisaYNoPoneEsosBastones()
    {
        ResultadoVigas r = MotorVigas.Calcular(Entrada(new ArmadoBastones(Superior: new GrupoBarras(1, 9))));

        Assert.Contains(r.Avisos, a => a.Tipo == TipoAviso.DiametroSinReglas && a.Elemento == "#9");
        Assert.DoesNotContain(r.Bastones, b => b.Capa == Posicion.Superior);
        Assert.Equal(3, r.Bastones.Count(b => b.Capa == Posicion.Inferior));
    }
}
