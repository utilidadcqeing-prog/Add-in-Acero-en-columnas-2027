using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Los puntos donde las barras se tocan (plan docs/planes/2026-10-22-choques-entre-barras-vigas.md, paso 2): chocan si
/// uno se mete en el otro más de medio milímetro; lo que se toca a propósito queda tangente y no cuenta.
/// </summary>
public class DetectorChoquesTests
{
    private static Trazo Recta(string pieza, Punto3 desde, Punto3 hasta, double diametro = 16, TipoTrazo tipo = TipoTrazo.Barra) =>
        new([desde, hasta], 5, diametro, pieza, tipo);

    [Fact]
    public void DosBarrasCruzadasDanUnChoqueEntreLosDosEjes()
    {
        // ejes a 10 mm y radios de 8: se meten 6 mm
        Trazo s1 = Recta("S1", new(0, 0, 0), new(1000, 0, 0));
        Trazo s2 = Recta("S2", new(500, -500, 10), new(500, 500, 10));

        Choque c = Assert.Single(DetectorChoques.Detectar([s1, s2]));

        Assert.Equal(6, c.SeMeteMm, 6);
        Assert.Equal(500, c.Punto.X, 3);
        Assert.Equal(0, c.Punto.Y, 3);
        Assert.Equal(5, c.Punto.Z, 3);
        Assert.Equal((1, TipoChoque.EntreBarras), (c.Veces, c.Tipo));
    }

    [Fact]
    public void ElTraslapoLadoALadoNoCuenta()
    {
        // decisión 2 de #85: las dos barras del traslapo van a un diámetro, tangentes
        Trazo s1 = Recta("S1", new(0, 0, 0), new(1000, 0, 0));
        Trazo s2 = Recta("S1", new(500, 16, 0), new(1500, 16, 0));

        Assert.Empty(DetectorChoques.Detectar([s1, s2]));
    }

    [Fact]
    public void DosBarrasParalelasQueSeMetenChocanEnElMedioDeLoQueComparten()
    {
        Trazo s1 = Recta("S1", new(0, 0, 0), new(1000, 0, 0));
        Trazo s2 = Recta("S2", new(500, 15, 0), new(1500, 15, 0));

        Choque c = Assert.Single(DetectorChoques.Detectar([s1, s2]));

        // se meten de la punta de S2 (un poco antes, donde la distancia a ella baja de 15.5) a la de S1
        double desde = 500 - Math.Sqrt(15.5 * 15.5 - 15 * 15);
        Assert.Equal(1, c.SeMeteMm, 6);
        Assert.Equal((desde + 1000) / 2, c.Punto.X, 3);
        Assert.Equal(7.5, c.Punto.Y, 3);
    }

    [Fact]
    public void ElEstriboSeMeteEnElBastonYNoAlReves()
    {
        Trazo baston = Recta("BS1", new(0, 0, 0), new(1000, 0, 0), tipo: TipoTrazo.Baston);
        Trazo estribo = Recta("E", new(500, -100, 5), new(500, 100, 5), 10, TipoTrazo.Estribo) with { Tramo = "A-B" };

        Choque c = Assert.Single(DetectorChoques.Detectar([baston, estribo]));

        Assert.Equal(("E", "BS1", TipoChoque.EstriboConBarra), (c.A.Pieza, c.B.Pieza, c.Tipo));
        Assert.Equal("A-B", c.A.Tramo);
    }

    [Fact]
    public void ElBastonContraUnaBarraEsBastonConBarra()
    {
        Trazo barra = Recta("S1", new(0, 0, 0), new(1000, 0, 0));
        Trazo baston = Recta("BS1", new(200, 0, 10), new(800, 0, 10), tipo: TipoTrazo.Baston);

        Choque c = Assert.Single(DetectorChoques.Detectar([barra, baston]));

        Assert.Equal(("BS1", "S1", TipoChoque.BastonConBarra), (c.A.Pieza, c.B.Pieza, c.Tipo));
    }

    [Fact]
    public void LosEstribosDeUnaCorridaQueChocanIgualSeJuntanEnElDelMedio()
    {
        // eje de arriba del cerrado en 455; una barra de 16 con su eje en 450 se mete 8 mm en cada uno de los 5
        var u = new UbicacionPortico(1, 0, 0, 0, 3000, 3500, 300, 500, [new("101", 0, 9000)]);
        var g = new GrupoEstribos("E", "A-B", TipoEstribo.Cerrado, 3, GanchoEstribo.De135, 40, 260, 40, 460, new Corrida(500, 5, 100), "101");
        var barra = new PiezaLongitudinal("S2", Posicion.Superior, 5, 150, 450, 0, 2000, TipoGancho.Sin, TipoGancho.Sin, "101", 2000);
        var doblados = new Dictionary<int, DobladoBarra> { [3] = new(10, 40, 40), [5] = new(16, 64, 40) };

        IReadOnlyList<Trazo> trazos = PreviaAcero.Trazos(new PlanAcero("P", [barra], [], [], [g], []), u, Reglas.Generales, doblados);
        Choque c = Assert.Single(DetectorChoques.Detectar(trazos));

        Assert.Equal(5, c.Veces);
        Assert.Equal(700, c.Punto.X, 6);
        Assert.Equal(8, c.SeMeteMm, 6);
        Assert.Equal(("E", "S2"), (c.A.Pieza, c.B.Pieza));
    }

    [Fact]
    public void ElPorticoDeLaMaquetaSinBastonesNoTieneChoques()
    {
        // los estribos que abrazan sus barras, la barra de esquina en la curva y los traslapos lado a lado
        PorticoLeido p = PlanificadorAceroTests.Portico();
        OpcionesTablero o = OpcionesTablero.PorDefecto with { Bastones = new ArmadoBastones(new GrupoBarras(0, 5), new GrupoBarras(0, 5)) };
        PlanAcero plan = PlanificadorAcero.Planificar("P", p, o, Reglas.Generales)!;

        Assert.Empty(plan.Bastones);
        Assert.Empty(DetectorChoques.Detectar(PreviaAcero.Trazos(plan, p.Ubicacion!, Reglas.Generales)));
    }

    [Fact]
    public void ElBastonDeArribaJuntoALaEsquinaChocaConLaColaDel135()
    {
        // Choque real (Juan, 22-oct): el segundo gancho del cerrado sale de la curva de la esquina (centro en 68.5, 431.5,
        // radio 23.75) a 45° y su cola baja en diagonal hacia el núcleo. El bastón de arriba, en (105.34, 442.55), queda a
        // 10.12 mm de ella y no a 4.75 + 7.95: se mete 2.58 mm, en los 8 estribos de cada zona junto a una columna.
        PorticoLeido p = PlanificadorAceroTests.Portico();
        PlanAcero plan = PlanificadorAcero.Planificar("P", p, OpcionesTablero.PorDefecto, Reglas.Generales)!;

        IReadOnlyList<Choque> choques = DetectorChoques.Detectar(PreviaAcero.Trazos(plan, p.Ubicacion!, Reglas.Generales));

        Assert.Equal(7, choques.Count);
        Assert.All(choques, c =>
        {
            Assert.Equal(("E", TipoTrazo.Baston, TipoChoque.EstriboConBarra, 8), (c.A.Pieza, c.B.Tipo, c.Tipo, c.Veces));
            Assert.Equal(2.58, c.SeMeteMm, 2);
        });
        Assert.Equal(["BS1", "BS2", "BS2", "BS3", "BS3", "BS4", "BS4"], choques.Select(c => c.B.Pieza));
    }

    [Theory]
    [InlineData(FormaGrapa.C)]
    [InlineData(FormaGrapa.S)]
    public void LaGrapaDe180DobladaA6dNoCabeEntreBarrasJuntasYSeMeteEnLasVecinas(FormaGrapa forma)
    {
        // H8 del plan del 180°: doblada a 6 d (57 en el #3) el gancho no cabe entre barras. Con barras #5 a 32 mm de centro
        // a centro, el doblez (radio 28.5 + 4.75 = 33.25, centro a 28.5 − 7.95 = 20.55 de la barra que abraza) queda a
        // √(32² + 20.55²) − 33.25 = 4.78 de la vecina: se mete 12.7 − 4.78 = 7.92 en el arco, unas décimas menos en la
        // cuerda de 15° del trazo. Choca con las dos vecinas de arriba y las dos de abajo.
        var u = new UbicacionPortico(1, 0, 0, 0, 3000, 3350, 250, 350, [new("101", 0, 9000)]);
        PiezaLongitudinal Barra(string id, double x, double y) =>
            new(id, y > 175 ? Posicion.Superior : Posicion.Inferior, 5, x, y, 0, 2000, TipoGancho.Sin, TipoGancho.Sin, "101", 2000);
        PiezaLongitudinal[] barras =
            [Barra("S1", 93, 292.55), Barra("S2", 125, 292.55), Barra("S3", 157, 292.55), Barra("I1", 93, 57.45), Barra("I2", 125, 57.45), Barra("I3", 157, 57.45)];
        var grapa = new GrupoEstribos("E1", "A-B", TipoEstribo.Abierto, 3, GanchoEstribo.De180, 125, 125, 40, 310, new Corrida(1000, 1, 0), "101",
            new Rama(125, 292.55, 57.45, 15.9), forma);
        var doblados = new Dictionary<int, DobladoBarra> { [3] = new(9.5, 57, 57), [5] = new(15.9, 95.4, 95.4) };

        IReadOnlyList<Choque> choques = DetectorChoques.Detectar(PreviaAcero.Trazos(new PlanAcero("P", barras, [], [], [grapa], []), u, Reglas.Generales, doblados));

        Assert.Equal(["I1", "I3", "S1", "S3"], choques.Select(c => c.B.Pieza).Order());
        Assert.All(choques, c => Assert.InRange(c.SeMeteMm, 7.6, 7.92));
        Assert.All(choques, c => Assert.Equal(("E1", TipoTrazo.Grapa, TipoChoque.EstriboConBarra), (c.A.Pieza, c.A.Tipo, c.Tipo)));
    }
}
