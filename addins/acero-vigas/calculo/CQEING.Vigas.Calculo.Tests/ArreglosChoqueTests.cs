using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Los arreglos de un choque (decisión 7 del plan docs/planes/2026-10-22-choques-entre-barras-vigas.md, paso 6): se
/// prueban con el cálculo de siempre antes de ofrecerlos.
/// </summary>
public class ArreglosChoqueTests
{
    private static readonly PorticoLeido P = PlanificadorAceroTests.Portico();

    /// <summary>La grapa en S en la barra 2 de 5, con gancho de 180° y doblada a 6 d: se mete en las barras vecinas (H7).</summary>
    private static readonly OpcionesTablero ConGrapa = OpcionesTablero.PorDefecto with
    {
        Superior = [new GrupoBarras(5, 5)],
        Inferior = [new GrupoBarras(5, 5)],
        Bastones = new ArmadoBastones(new GrupoBarras(0, 5), new GrupoBarras(0, 5)),
        Estribos = new ConfigEstribos(Gancho: GanchoEstribo.De180, Adicionales:
            [new EstriboAdicional(TipoEstribo.Abierto, [new PuntoBarra(Posicion.Superior, 1, 2), new PuntoBarra(Posicion.Inferior, 1, 2)], FormaGrapa.S)]),
    };

    private static readonly Dictionary<int, DobladoBarra> A6d = new() { [3] = new(9.5, 57, 57) };

    private static IReadOnlyList<Choque> Choques(OpcionesTablero o, IReadOnlyDictionary<int, DobladoBarra>? d = null) =>
        ArmadorChoques.Detectar(PlanificadorAcero.Planificar("P", P, o, Reglas.Generales)!, P.Ubicacion!, Reglas.Generales, d);

    private static IReadOnlyList<ArregloVista> Arreglos(OpcionesTablero o, int choque, IReadOnlyDictionary<int, DobladoBarra>? d = null) =>
        ArreglosChoque.Buscar("P", P, o, Reglas.Generales, d, choque);

    [Fact]
    public void ElBastonQueChocaOfreceAcortarloConSuLargoNuevo()
    {
        // H6: el choque 1 es la cola del 135° de A-B contra BS2, del lado izquierdo de BS2
        Assert.Equal("BS2", Choques(OpcionesTablero.PorDefecto)[1].B.Pieza);

        IReadOnlyList<ArregloVista> a = Arreglos(OpcionesTablero.PorDefecto, 1);

        Assert.Equal(["Gancho de 180° en los estribos del pórtico", "Gancho de 90° en los estribos del pórtico", "Acortar BS2: 2.00 → 1.00 m"],
            a.Select(x => x.Texto));
        CambioArrastre acorte = Assert.Single(a[2].Cambio.Saltos!);
        Assert.Equal(("salto", "puntas", "BS2"), (a[2].Cambio.Tipo, acorte.Tipo, acorte.Id));
        Assert.Equal(1000, acorte.Hasta - acorte.Desde);
    }

    [Fact]
    public void ElGanchoVaComoLoEscribeLaPagina()
    {
        CambioArreglo c = Arreglos(OpcionesTablero.PorDefecto, 0)[0].Cambio;

        Assert.Equal(new CambioArreglo("gancho", "est", Valor: "180°"), c with { Saltos = null });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CadaArregloOfrecidoQuitaElChoqueSinDejarMasNiImpedirGenerar(bool grapa)
    {
        // la garantía de la decisión 7, sobre todos los arreglos de todos los puntos
        (OpcionesTablero o, Dictionary<int, DobladoBarra>? d) = grapa ? (ConGrapa, A6d) : (OpcionesTablero.PorDefecto, null);
        IReadOnlyList<Choque> antes = Choques(o, d);
        ResultadoVigas vigas = MotorVigas.Calcular(CalculoPortico.EntradaDe(P, o, Reglas.Generales));
        int ofrecidos = 0;

        for (int i = 0; i < antes.Count; i++)
            foreach (ArregloVista a in Arreglos(o, i, d))
            {
                ofrecidos++;
                OpcionesTablero nuevas = ArreglosChoque.Aplicar(o, a.Cambio, vigas);
                PlanAcero? plan = PlanificadorAcero.Planificar("P", P, nuevas, Reglas.Generales);
                Assert.NotNull(plan);
                IReadOnlyList<Choque> despues = ArmadorChoques.Detectar(plan, P.Ubicacion!, Reglas.Generales, d);
                Assert.True(despues.Sum(c => c.Veces) <= antes.Sum(c => c.Veces), a.Texto);
                Assert.DoesNotContain(despues, c => c.A == antes[i].A && c.B == antes[i].B && Math.Abs(c.Punto.X - antes[i].Punto.X) < 1500);
            }
        Assert.True(ofrecidos > 0);
    }

    [Fact]
    public void LaGrapaEnSNoOfrecePasarlaACSiEnCTambienChoca()
    {
        // con la grapa a 6 d el gancho no cabe en C ni en S (H7): solo queda quitarla
        IReadOnlyList<Choque> choques = Choques(ConGrapa, A6d);
        int grapa = choques.ToList().FindIndex(c => c.A.Tipo == TipoTrazo.Grapa);

        IReadOnlyList<ArregloVista> a = Arreglos(ConGrapa, grapa, A6d);

        Assert.Equal(["Quitar la grapa E1"], a.Select(x => x.Texto));
        Assert.Equal(new CambioArreglo("quitar", "est", 0), a[0].Cambio);
        Assert.Equal(FormaGrapa.C, ArreglosChoque.Aplicar(ConGrapa, new CambioArreglo("forma", "est", 0, "C"), null!).Estribos.Adicionales![0].Forma);
    }

    [Fact]
    public void SinArreglosPosiblesLaListaVaVacia()
    {
        // el cerrado doblado a 6 d se mete en sus barras de esquina: ni otro gancho lo despeja
        IReadOnlyList<Choque> choques = Choques(ConGrapa, A6d);
        int cerrado = choques.ToList().FindIndex(c => c.A.Pieza == "E");

        Assert.Empty(Arreglos(ConGrapa, cerrado, A6d));
    }

    [Fact]
    public void UnChoqueQueYaNoEstaNoTraeArreglos()
    {
        Assert.Empty(Arreglos(OpcionesTablero.PorDefecto, 99));
    }

    [Fact]
    public void ElGanchoDeLosTramosMarcadosCambiaSoloEsos()
    {
        OpcionesTablero o = OpcionesTablero.PorDefecto with { TramosMarcados = ["A-B"], EstribosMarcados = new ConfigEstribos() };

        IReadOnlyList<ArregloVista> a = Arreglos(o, 0);

        Assert.Equal("Gancho de 180° en los estribos de los tramos marcados", a[0].Texto);
        OpcionesTablero nuevas = ArreglosChoque.Aplicar(o, a[0].Cambio, null!);
        Assert.Equal((GanchoEstribo.De135, GanchoEstribo.De180), (nuevas.Estribos.Gancho, nuevas.EstribosMarcados!.Gancho));
    }
}
