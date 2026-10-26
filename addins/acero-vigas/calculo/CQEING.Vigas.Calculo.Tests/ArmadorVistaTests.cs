using CQEING.Vigas.Calculo.Estribos;
using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Seccion;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// El pórtico de la maqueta leído de Revit: eje 1, aéreo, de A a D con voladizo, sección 300 × 500. La vista va en las
/// unidades de la maqueta: alzado en m, sección en cm (decisión 4 del plan del tablero #83).
/// </summary>
public class ArmadorVistaTests
{
    internal static PorticoLeido Portico1(Portico? portico = null, TipoViga tipo = TipoViga.Aerea)
    {
        Portico p = portico ?? Datos.PorticoMaqueta();
        var tramos = DistribuidorEstribos.Tramos(p)
            .Select(t => new TramoLeido(t.EsVoladizo ? t.Nombre : "Viga " + t.Nombre, (int)t.CaraIzquierda, (int)t.CaraDerecha, t.EsVoladizo, 300, 500))
            .ToList();
        return new PorticoLeido("Pórtico 1", "1", "Nivel 2", tipo, p, p.Apoyos.Select(_ => TipoApoyo.Columna).ToList(), tramos,
            ["101", "102", "103", "104"]);
    }

    private static VistaPortico Armar(OpcionesTablero? o = null, PorticoLeido? p = null, Reglas? r = null) =>
        ArmadorVista.Armar(p ?? Portico1(), o ?? OpcionesTablero.PorDefecto, r ?? Reglas.Generales);

    private static EntradaVigas EntradaDe(PorticoLeido p) =>
        new(p.Portico, p.Tipo, [new GrupoBarras(3, 5)], [new GrupoBarras(3, 5)], Reglas.Generales);

    [Fact]
    public void LaVistaDiceElGanchoDeCadaPuntaYSiLlegaAlBorde()
    {
        var o = OpcionesTablero.PorDefecto with
        {
            Cambios = new CambiosManuales(Ganchos: new Dictionary<string, TipoGancho> { ["S.inicio"] = TipoGancho.De180, ["BS1.inicio"] = TipoGancho.De135 }),
        };

        VistaPortico v = Armar(o);

        BarraVista s1 = v.Barras.Single(b => b.Id == "S1"), s2 = v.Barras.Single(b => b.Id == "S2"), s4 = v.Barras.Single(b => b.Id == "S4");
        Assert.Equal(("180°", "", true, false), (s1.GanchoInicioTipo, s1.GanchoFinTipo, s1.PuntaInicioEnBorde, s1.PuntaFinEnBorde));
        Assert.Equal((false, false), (s2.PuntaInicioEnBorde, s2.PuntaFinEnBorde));
        Assert.Equal(("90°", true), (s4.GanchoFinTipo, s4.PuntaFinEnBorde));
        BastonVista bs1 = v.Bastones.Single(b => b.Id == "BS1"), bs2 = v.Bastones.Single(b => b.Id == "BS2");
        Assert.Equal(("135°", true, false), (bs1.GanchoInicioTipo, bs1.PuntaInicioEnBorde, bs1.PuntaFinEnBorde));
        Assert.Equal(("", false), (bs2.GanchoInicioTipo, bs2.PuntaInicioEnBorde));
    }

    [Fact]
    public void LaVistaTraeElColorDeCadaDiametroYLosDiametrosParaLaLeyenda()
    {
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(3, 5)], Inferior = [new GrupoBarras(3, 4)] };

        VistaPortico v = Armar(o);

        Assert.All(v.Barras.Where(b => b.Capa == "arriba"), b => Assert.Equal(ColoresDiametro.De(5), b.Color));
        Assert.All(v.Barras.Where(b => b.Capa == "abajo"), b => Assert.Equal(ColoresDiametro.De(4), b.Color));
        Assert.All(v.Bastones, b => Assert.Equal(ColoresDiametro.De(int.Parse(b.Cantidad.Split('#')[1])), b.Color));
        Assert.All(v.Seccion.Barras.Where(b => !b.Baston), b => Assert.Equal(ColoresDiametro.De(b.Arriba ? 5 : 4), b.Color));
        Assert.Equal(["#5", "#4"], v.Diametros!.Select(d => d.Nombre));
        Assert.Equal([ColoresDiametro.De(5), ColoresDiametro.De(4)], v.Diametros!.Select(d => d.Color));
        Assert.Equal(["⅝\"", "½\""], v.Diametros!.Select(d => d.NombrePulgadas));
    }

    [Fact]
    public void CadaBarraYBastonTraeSuRotuloEnNumeroYEnPulgadas()
    {
        VistaPortico v = Armar();

        Assert.All(v.Barras, b => Assert.Matches(b.GanchoInicio || b.GanchoFin ? @"^3#5L= \d+\.\d\d/\d+\.\d\d$" : @"^3#5L= \d+\.\d\d$", b.Rotulo));
        Assert.All(v.Barras, b => Assert.Equal(b.Rotulo.Replace("3#5", "3Ø⅝\""), b.RotuloPulgadas));
        Assert.Contains(v.Barras, b => b.Rotulo.Contains('/'));
        Assert.All(v.Bastones, b => Assert.Matches(@"^\d+#\d+L= \d+\.\d\d(/\d+\.\d\d)?$", b.Rotulo));
        Assert.All(v.Bastones, b => Assert.Contains("Ø", b.RotuloPulgadas));
    }

    [Fact]
    public void ElTraslapoTraeSuLargoA2Decimales()
    {
        VistaPortico v = Armar();

        Assert.All(v.Traslapos, t => Assert.Equal((t.Hasta - t.Desde).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), t.Rotulo));
    }

    [Fact]
    public void CadaZonaDeEstribosTraeCantidadSeparacionYLargoDesdeLaCara()
    {
        VistaPortico v = Armar();
        IReadOnlyList<double> pos = v.PosicionesEstribos!;
        int EnZona(ZonaEstribos z) => pos.Count(x => x >= z.Desde - 1e-6 && x <= z.Hasta + 1e-6);

        // la primera zona: junto a la cara derecha de la columna A, a la separación de la zona (0.10)
        ZonaEstribos primera = v.Zonas[0];
        Assert.True(primera.Junta);
        Assert.Equal($"{EnZona(primera)}e#3", primera.Cantidad);
        Assert.Equal("@.10", primera.Separacion);
        Assert.Equal("L=" + (primera.Hasta - v.Apoyos[0].CaraDerecha).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), primera.Largo);
        // el centro no lleva L, y entre todas las zonas están todos los estribos una sola vez
        Assert.All(v.Zonas.Where(z => !z.Junta), z => Assert.Equal("", z.Largo));
        Assert.Equal(pos.Count, v.Zonas.Sum(z => int.Parse(z.Cantidad.Split('e')[0])));
    }

    [Fact]
    public void CadaTramoTraeSuSeccionEnLaMitad()
    {
        VistaPortico v = Armar();

        Assert.Equal(["30×50", "30×50", "30×50", "30×50"], v.Secciones!.Select(s => s.Texto));
        Assert.Equal((v.Apoyos[0].CaraDerecha + v.Apoyos[1].CaraIzquierda) / 2, v.Secciones![0].X, 6);
        Assert.Equal((v.Apoyos[^1].CaraDerecha + v.Fin) / 2, v.Secciones![^1].X, 6);
    }

    [Fact]
    public void UnaCorridaConDosDiametrosLlevaElColorDelMasGrueso()
    {
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(3, 5), new GrupoBarras(2, 4)] };

        VistaPortico v = Armar(o);

        Assert.All(v.Barras.Where(b => b.Capa == "arriba"), b => Assert.Equal(ColoresDiametro.De(5), b.Color));
    }

    [Fact]
    public void DosBarrasFijasEnLaFila1SeCalculan()
    {
        // Juan, 19-oct: con 2 #5 en la Fila 1 y 7 #4 en Auto, al quitar los #4 el panel no se actualizaba
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(2, 5, 1)] };

        VistaPortico v = Armar(o);

        Assert.Equal([2], v.Superior.PorFila);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(20)]
    public void DosFijasEnLaFila1MasOtrasEnAutoNoRevientan(int n)
    {
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(2, 5, 1), new GrupoBarras(n, 4)] };

        Armar(o);
    }

    [Fact]
    public void CadaColumnaTraeSuSeccionAnchoPorFondo()
    {
        PorticoLeido p = Portico1();
        p = p with
        {
            TiposDeApoyo = [TipoApoyo.Columna, TipoApoyo.Viga, TipoApoyo.Columna, TipoApoyo.Columna],
            FondosDeApoyo = [500, 300, 500, 450],
        };

        VistaPortico v = Armar(p: p);

        // las columnas de la maqueta miden 400 a lo largo de la viga; la que es viga no lleva sección de columna
        Assert.Equal(["40×50", "", "40×50", "40×45"], v.Apoyos.Select(a => a.Seccion));
    }

    [Fact]
    public void SinFondosLaSeccionVaVacia()
    {
        Assert.All(Armar().Apoyos, a => Assert.Equal("", a.Seccion));
    }

    [Fact]
    public void UnApoyoQueEsVigaSeMarcaEnLaVista()
    {
        PorticoLeido p = Portico1();
        p = p with { TiposDeApoyo = [TipoApoyo.Columna, TipoApoyo.Viga, TipoApoyo.Muro, TipoApoyo.Columna] };

        VistaPortico v = Armar(p: p);

        Assert.Equal([false, true, false, false], v.Apoyos.Select(a => a.EsViga));
    }

    [Fact]
    public void LaListaNombraElPorticoComoLaMaqueta()
    {
        PorticoEnLista l = ArmadorVista.EnLista(Portico1());

        Assert.Equal(("Pórtico 1", "Pórtico 1", "Eje 1 · A a D + voladizo", "Aérea"), (l.Id, l.Nombre, l.Sub, l.Tipo));
        Assert.Equal(["Viga A-B", "Viga B-C", "Viga C-D", "Voladizo D"], l.Tramos.Select(t => t.Nombre));
        Assert.Equal(["A-B", "B-C", "C-D", "Voladizo D"], l.Tramos.Select(t => t.Id));
        Assert.Equal((0, 4.40), (l.Tramos[0].Desde, l.Tramos[0].Hasta));
        Assert.Equal((12.30, 14.00), (l.Tramos[^1].Desde, l.Tramos[^1].Hasta));
    }

    [Fact]
    public void UnPorticoDeCimentacionSeNombraPorSuEje()
    {
        Assert.Equal(("Cimentación · eje 1", "Cimentación"),
            (ArmadorVista.EnLista(Portico1(tipo: TipoViga.Cimentacion)).Sub, ArmadorVista.EnLista(Portico1(tipo: TipoViga.Cimentacion)).Tipo));
    }

    /// <summary>
    /// La casilla "Es vigueta" (#940): viene como la detectó el add-in (#939) y el usuario solo la toca si se equivocó.
    /// </summary>
    [Fact]
    public void UnaViguetaDetectada_VieneMarcadaYSinCambioAMano()
    {
        VistaPortico v = Armar(p: Portico1() with { Elemento = Elemento.Vigueta });

        Assert.Equal((true, true), (v.EsVigueta, v.DetectadaVigueta));
    }

    [Fact]
    public void LoQueEligeElUsuario_ManaSobreLoDetectado()
    {
        VistaPortico aViga = Armar(OpcionesTablero.PorDefecto with { Elemento = Elemento.Viga }, Portico1() with { Elemento = Elemento.Vigueta });
        VistaPortico aVigueta = Armar(OpcionesTablero.PorDefecto with { Elemento = Elemento.Vigueta });

        Assert.Equal((false, true), (aViga.EsVigueta, aViga.DetectadaVigueta));
        Assert.Equal((true, false), (aVigueta.EsVigueta, aVigueta.DetectadaVigueta));
    }

    [Fact]
    public void EnUnaVigueta_ElCorteDibujaLaGrapaYLaListaLaNombra()
    {
        PorticoLeido p = Portico1();
        PorticoLeido vigueta = p with { Elemento = Elemento.Vigueta, Tramos = p.Tramos.Select(t => t with { Ancho = 100, Alto = 350 }).ToList() };

        VistaPortico v = Armar(OpcionesTablero.DeVigueta, vigueta);

        FiguraSeccion grapa = v.Seccion.Figuras.Single(f => f.Id == "E");
        Assert.Equal(("Abierto", "S"), (grapa.Tipo, grapa.Forma));
        Assert.NotNull(grapa.Puntos);
        Assert.Equal("Grapa en S", v.ListaEstribos.Single(e => e.Id == "E").Tipo);
    }

    [Fact]
    public void LaGrapaDeLaViguetaEnC_SeDibujaYSeNombraEnC()
    {
        PorticoLeido p = Portico1();
        PorticoLeido vigueta = p with { Elemento = Elemento.Vigueta, Tramos = p.Tramos.Select(t => t with { Ancho = 100, Alto = 350 }).ToList() };
        OpcionesTablero o = OpcionesTablero.DeVigueta with { Estribos = OpcionesTablero.DeVigueta.Estribos with { Forma = FormaGrapa.C } };

        VistaPortico v = Armar(o, vigueta);

        Assert.Equal("C", v.Seccion.Figuras.Single(f => f.Id == "E").Forma);
        Assert.Equal("Grapa en C", v.ListaEstribos.Single(e => e.Id == "E").Tipo);
    }

    [Fact]
    public void EnLaLista_LaViguetaSeVeComoVigueta()
    {
        Assert.Equal("Vigueta", ArmadorVista.EnLista(Portico1() with { Elemento = Elemento.Vigueta }).Tipo);
    }

    [Fact]
    public void LasBarrasVanEnMetrosConSusGanchos()
    {
        Barra s1 = MotorVigas.Calcular(EntradaDe(Portico1())).Barras.Single(b => b.Id == "S1");

        BarraVista v = Armar().Barras.Single(b => b.Id == "S1");

        Assert.Equal((s1.Desde / 1000.0, s1.Hasta / 1000.0, s1.Largo / 1000.0), (v.Desde, v.Hasta, v.Largo));
        Assert.Equal(("arriba", true, false, "3 #5"), (v.Capa, v.GanchoInicio, v.GanchoFin, v.Cantidad));
        Assert.Equal(FormattableString.Invariant($"{s1.LargoRecto / 1000.0:0.00} + gancho"), v.Recta);
        Assert.Equal(s1.Kg, v.Kg, 6);
    }

    [Fact]
    public void ElPorticoVaDePuntaAPuntaConSusApoyos()
    {
        VistaPortico v = Armar();

        Assert.Equal((-0.20, 14.00), (v.Inicio, v.Fin));
        Assert.Equal(new ApoyoVista("B", 4.40, 4.20, 4.60), v.Apoyos[1]);
    }

    [Fact]
    public void LasZonasDeEstribosSalenDelMotor()
    {
        VistaPortico v = Armar();

        Assert.Equal(10, v.Zonas.Count);
        Assert.Equal((0.25, 0.95, true), (Math.Round(v.Zonas[0].Desde, 2), Math.Round(v.Zonas[0].Hasta, 2), v.Zonas[0].Junta));
        Assert.Equal((0.95, 3.45, false), (Math.Round(v.Zonas[1].Desde, 2), Math.Round(v.Zonas[1].Hasta, 2), v.Zonas[1].Junta));
        Assert.Equal((12.55, 13.95, true), (Math.Round(v.Zonas[^1].Desde, 2), Math.Round(v.Zonas[^1].Hasta, 2), v.Zonas[^1].Junta));
    }

    [Fact]
    public void LaSeccionVaEnCmConLasEsquinasMarcadas()
    {
        VistaPortico v = Armar(OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(5, 5)] });

        List<BarraSeccion> arriba = v.Seccion.Barras.Where(b => b.Arriba && !b.Baston).ToList();
        // las de esquina se apoyan en la curva del estribo y entran 0.3236 cm (21-oct)
        Assert.Equal([6.0686, 10.3725, 15, 19.6275, 23.9314], arriba.Select(b => Math.Round(b.X, 4)));
        Assert.Equal(["a1", "a2", "a3", "a4", "a5"], arriba.Select(b => b.Id));
        Assert.Equal([true, false, false, false, true], arriba.Select(b => b.Esquina));
        Assert.Equal(("arriba 1", 6.0686, 1.59), (arriba[0].Nombre, Math.Round(arriba[0].Y, 4), arriba[0].D));
        Assert.Equal((30, 50), (v.Seccion.AnchoCm, v.Seccion.AltoCm));
        Assert.Equal(new FiguraSeccion("E", "Cerrado", 4, 26, 4, 46, DiametroCm: 0.95, Color: ColoresDiametro.De(3)), v.Seccion.Figuras[0]);
    }

    [Fact]
    public void CadaCaraDiceCuantasCabenYSuError()
    {
        VistaPortico v = Armar(OpcionesTablero.PorDefecto with { Inferior = [new GrupoBarras(7, 5)] });

        Assert.Equal((5, "Ninguno", 15), (v.Inferior.CabenPorFila, v.Inferior.Error, v.Inferior.Maximo));
        Assert.Equal([5, 2], v.Inferior.PorFila);
        Assert.Equal("MenosDeDosBarras", Armar(OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(1, 5)] }).Superior.Error);
    }

    [Fact]
    public void LasDosOpcionesDeLaCaraTraenSusKilos()
    {
        ResultadoVigas r = MotorVigas.Calcular(EntradaDe(Portico1()));

        VistaPortico v = Armar();

        Assert.Equal((r.CaraInicio!.Kg, r.CaraFinal!.Kg), (v.Izquierda.Kg, v.Derecha.Kg));
        Assert.Equal(r.CaraMasEconomica == Cara.Inicio ? "izq" : "der", v.CaraMasEconomica);
        Assert.Equal(r.CaraUsada == Cara.Inicio ? "izq" : "der", v.CaraUsada);
    }

    /// <summary>
    /// El caso de <see cref="AvisosTests"/>: D en 11.55 y la capa de abajo al final de la luz. El tablero cierra la punta del
    /// voladizo con el estribo #3 (10 mm), así que las barras terminan 10 mm antes y el traslapo queda 10 mm más largo.
    /// </summary>
    [Fact]
    public void UnTraslapoLargoDaElAvisoConSuTexto()
    {
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 4400), Datos.Columna("C", 7900), Datos.Columna("D", 11550)],
            VoladizoFinal: 1500);

        VistaPortico v = Armar(OpcionesTablero.PorDefecto with { Cara = Cara.Final }, Portico1(portico));

        AvisoVista a = Assert.Single(v.Avisos, a => a.Tipo == "TraslapoLargo" && a.Elemento.StartsWith('I'));
        Assert.Equal("El traslapo I3–I4 queda en 1.30, 0.40 por encima del mínimo. I4 termina en el voladizo y solo puede crecer. "
            + "Si el traslapo va al inicio de la luz no pasa, y además se ahorran 5 kg.", a.Texto);
    }

    /// <summary>
    /// "Cambiar" (la otra cara) solo sale si la otra cara evita el traslapo largo (Juan, 16-oct: el aviso de S1–S2 traía
    /// "Cambiar", que mueve la capa de abajo y no lo arregla).
    /// </summary>
    [Fact]
    public void CambiarSoloSaleSiLaOtraCaraEvitaElTraslapoLargo()
    {
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 4400), Datos.Columna("C", 7900), Datos.Columna("D", 11550)],
            VoladizoFinal: 1500);

        VistaPortico v = Armar(OpcionesTablero.PorDefecto with { Cara = Cara.Final }, Portico1(portico));

        Assert.True(Assert.Single(v.Avisos, a => a.Tipo == "TraslapoLargo" && a.Elemento.StartsWith('I')).CambiarCara);
        AvisoVista arriba = Assert.Single(v.Avisos, a => a.Tipo == "TraslapoLargo" && a.Elemento.StartsWith('S'));
        Assert.False(arriba.CambiarCara);
    }

    [Fact]
    public void LaListaDeEstribosTraeElCerradoYSusKilos()
    {
        ResultadoEstribos r = MotorEstribos.Calcular(new EntradaEstribos(Datos.PorticoMaqueta(), MotorEstribosTests.Seccion(), new ConfigEstribos()));

        FilaEstribos e = Assert.Single(Armar().ListaEstribos);

        Assert.Equal(("E", "Estribo cerrado", 94, "#3"), (e.Id, e.Tipo, e.Cantidad, e.Diametro));
        Assert.Equal(1.390, e.Largo, 3);
        Assert.Equal("zonas 0.80 cada 0.10 · centro cada 0.20", e.Detalle);
        Assert.Equal(r.Kg, e.Kg, 6);
    }

    [Fact]
    public void ElTotalSumaBarrasBastonesYEstribos()
    {
        ResultadoVigas rv = MotorVigas.Calcular(EntradaDe(Portico1()));

        VistaPortico v = Armar();

        Assert.Equal(rv.Kg + v.ListaEstribos.Sum(e => e.Kg), v.Kg, 6);
        Assert.Equal((94, true), (v.Estribos, v.SePuedeGenerar));
        Assert.Equal(rv.Traslapos.Count(), v.ZonasTraslapo.Count);
    }

    [Fact]
    public void UnPorticoDeOchoLuces_ArmaTodosSusTramosYZonas()
    {
        var portico = new Portico(Enumerable.Range(0, 9).Select(i => Datos.Columna(((char)('A' + i)).ToString(), i * 5000)).ToList());

        VistaPortico v = Armar(p: Portico1(portico));

        Assert.Equal(8, ArmadorVista.EnLista(Portico1(portico)).Tramos.Count);
        Assert.Equal(24, v.Zonas.Count);
        Assert.Equal(9, v.Apoyos.Count);
    }

    [Fact]
    public void UnPorticoSinSolucion_DaAvisoYNoGenera()
    {
        var reglas = Reglas.Generales with
        {
            PorDiametro = new Dictionary<int, ReglaDiametro>(Reglas.Generales.PorDiametro) { [5] = Reglas.Generales.PorDiametro[5] with { Traslapo = null } },
        };

        VistaPortico v = Armar(r: reglas);

        Assert.Contains(v.Avisos, a => a.Tipo == "SinTraslapoMinimo" && a.Elemento == "#5");
        Assert.False(v.SePuedeGenerar);
        Assert.Empty(v.Barras);
    }

    [Fact]
    public void UnDiametroQueNoEstaEnLasReglas_AvisaSinReventar()
    {
        VistaPortico v = Armar(OpcionesTablero.PorDefecto with { Inferior = [new GrupoBarras(3, 9)] });

        Assert.Contains(v.Avisos, a => a.Tipo == "DiametroSinReglas" && a.Elemento == "#9");
        Assert.False(v.SePuedeGenerar);
    }

    [Fact]
    public void LasReglasVanConLosDecimalesDeLaMaqueta()
    {
        VistaReglas v = ArmadorVista.Reglas(new ReglasCargadas(Reglas.Generales, null));

        Assert.Equal(["#3", "#4", "#5", "#6", "#7", "#8"], v.PorDiametro.Select(d => d.D));
        Assert.Equal(new ReglaVista("#3", "0.55", "0.166", "0.142", "0.113", "0.560"), v.PorDiametro[0]);
        Assert.Equal(("0.50", "2.5", false, (string?)null), (v.Multiplo, v.DistanciaLibreCm, v.Varilla12, v.Problema));
    }

    [Fact]
    public void LasReglasDeLaPantallaPasanAMm()
    {
        VistaReglas pantalla = ArmadorVista.Reglas(new ReglasCargadas(Reglas.Generales, null));
        pantalla = pantalla with
        {
            PorDiametro = pantalla.PorDiametro.Select(d => d.D == "#5" ? d with { T = "0.95" } : d).ToList(),
            Multiplo = "0,25",
            Varilla12 = true,
        };

        Reglas r = ArmadorVista.ReglasDe(pantalla, Reglas.Generales);

        Assert.Equal((950, 250, 25, 12000), (r.PorDiametro[5].Traslapo!.Value, r.Modulo, r.DistanciaLibre, r.Varilla));
        Assert.Equal(Reglas.Generales.PorDiametro[2], r.PorDiametro[2]);
        Assert.Empty(r.Validar());
    }

    [Fact]
    public void ElGancho180DeLaPantallaCambiaSoloSuLargo()
    {
        VistaReglas pantalla = ArmadorVista.Reglas(new ReglasCargadas(Reglas.Generales, null));
        pantalla = pantalla with { PorDiametro = pantalla.PorDiametro.Select(d => d.D == "#3" ? d with { G180 = "0.150" } : d).ToList() };

        Reglas r = ArmadorVista.ReglasDe(pantalla, Reglas.Generales);

        // B cambia; E, D y A del cuadro quedan (la cola del estribo de 180° sigue en 65)
        Assert.Equal(Reglas.Generales.PorDiametro[3].Gancho180 with { B = 150 }, r.PorDiametro[3].Gancho180);
        Assert.Equal(Reglas.Generales.PorDiametro[4], r.PorDiametro[4]);
    }

    [Fact]
    public void UnGancho180VacioHaceFallarLasReglas()
    {
        VistaReglas pantalla = ArmadorVista.Reglas(new ReglasCargadas(Reglas.Generales, null));
        pantalla = pantalla with { PorDiametro = pantalla.PorDiametro.Select(d => d.D == "#3" ? d with { G180 = "" } : d).ToList() };

        Assert.Equal(["El gancho de 180° del #3 debe ser mayor que 0."], ArmadorVista.ReglasDe(pantalla, Reglas.Generales).Validar());
    }

    [Fact]
    public void UnTextoQueNoEsNumeroHaceFallarLasReglas()
    {
        VistaReglas pantalla = ArmadorVista.Reglas(new ReglasCargadas(Reglas.Generales, null)) with { Multiplo = "abc" };

        Assert.Equal("El múltiplo de corte debe ser mayor que 0.", ArmadorVista.ReglasDe(pantalla, Reglas.Generales).Validar()[0]);
    }

    // Corte 1-1 (#91, tarea 2 del plan docs/planes/2026-10-17-corte-vigas-91.md).

    private static readonly ArmadoBastones BastonArriba = new(new GrupoBarras(1, 5));

    /// <summary>Un x permitido dentro del bastón de arriba del eje B, con las mismas opciones que usa el armador.</summary>
    private static int DentroDelBastonDeB(OpcionesTablero o, PorticoLeido p)
    {
        ResultadoVigas rv = MotorVigas.Calcular(new EntradaVigas(p.Portico, p.Tipo, o.Superior, o.Inferior, Reglas.Generales, o.Cara,
            Cambios: o.Cambios, Bastones: o.Bastones));
        Baston b = rv.Bastones.Single(x => x.Capa == Posicion.Superior && x.Donde == "B");
        Intervalo i = CalculadoraCorte.Permitidos(p.Portico, rv).First(i => i.Hasta > b.Desde && i.Desde < b.Hasta);
        return (int)Math.Round((Math.Max(i.Desde, b.Desde) + Math.Min(i.Hasta, b.Hasta)) / 2);
    }

    [Fact]
    public void ElCorteArrancaEnLaVistaTipo()
    {
        CorteVista c = Armar().Corte!;

        Assert.True(c.EsTipo);
        Assert.False(c.Ajustado);
        Assert.Equal(("derecha", 0.10), (c.Mirando, c.Profundidad));
        Assert.Contains(c.Permitidos, r => r.Desde <= c.X && c.X <= r.Hasta);
        Assert.Contains(c.X, c.Puntos);
        Assert.Null(c.Aviso);
    }

    [Fact]
    public void ElCorteTraeLosBastonesQuePasanPorAhi()
    {
        var o = OpcionesTablero.PorDefecto with { Bastones = BastonArriba };
        o = o with { Corte = new OpcionesCorte(X: DentroDelBastonDeB(o, Portico1())) };

        VistaPortico v = Armar(o);

        // El corte se pega al estribo más cercano (decisión 9), que sigue dentro del bastón.
        BarraSeccion baston = Assert.Single(v.Seccion.Barras, b => b.Baston);
        Assert.Equal(("bs1", "bastón arriba 1", true), (baston.Id, baston.Nombre, baston.Arriba));
    }

    [Fact]
    public void LosAdicionalesSoloSeVenEnSuTramo()
    {
        var adicional = new EstriboAdicional(TipoEstribo.Abierto, [new PuntoBarra(Posicion.Superior, 1, 2), new PuntoBarra(Posicion.Inferior, 1, 2)]);
        var o = OpcionesTablero.PorDefecto with
        {
            TramosMarcados = ["B-C"],
            EstribosMarcados = new ConfigEstribos(Adicionales: [adicional]),
        };

        VistaPortico enAB = Armar(o with { Corte = new OpcionesCorte(X: 2200) });
        VistaPortico enBC = Armar(o with { Corte = new OpcionesCorte(X: 6150) });

        Assert.Equal(("A-B", "B-C"), (enAB.Corte!.Tramo, enBC.Corte!.Tramo));
        Assert.Equal(["E"], enAB.Seccion.Figuras.Select(f => f.Id));
        Assert.Equal(["E", "E1"], enBC.Seccion.Figuras.Select(f => f.Id));
    }

    [Fact]
    public void ElCorteDiceLaFormaDeLaGrapa()
    {
        var enS = new EstriboAdicional(TipoEstribo.Abierto, [new PuntoBarra(Posicion.Superior, 1, 2), new PuntoBarra(Posicion.Inferior, 1, 2)],
            FormaGrapa.S);

        VistaPortico v = Armar(OpcionesTablero.PorDefecto with { Estribos = new ConfigEstribos(Adicionales: [enS]), Corte = new OpcionesCorte(X: 2200) });

        Assert.Equal([("E", "C"), ("E1", "S")], v.Seccion.Figuras.Select(f => (f.Id, f.Forma)));
    }

    private static FiguraSeccion GrapaEnElCorte(GanchoEstribo gancho, FormaGrapa forma)
    {
        var grapa = new EstriboAdicional(TipoEstribo.Abierto, [new PuntoBarra(Posicion.Superior, 1, 2), new PuntoBarra(Posicion.Inferior, 1, 2)], forma);
        VistaPortico v = Armar(OpcionesTablero.PorDefecto with
        {
            Estribos = new ConfigEstribos(Gancho: gancho, Adicionales: [grapa]),
            Corte = new OpcionesCorte(X: 2200),
        });
        return v.Seccion.Figuras.Single(f => f.Id == "E1");
    }

    private static double Cola(FiguraSeccion f) => Math.Sqrt(Math.Pow(f.Puntos![0][0] - f.Puntos[1][0], 2) + Math.Pow(f.Puntos[0][1] - f.Puntos[1][1], 2));

    [Fact]
    public void ElCorteDibujaElGanchoDelAdicionalSegunSuAngulo()
    {
        // la cola en cm: la E del cuadro del #3 (5.7 en 135°, 6.5 en 180°) o 6 diámetros en 90° (5.7)
        Assert.Equal(6.5, Cola(GrapaEnElCorte(GanchoEstribo.De180, FormaGrapa.C)), 6);
        Assert.Equal(5.7, Cola(GrapaEnElCorte(GanchoEstribo.De135, FormaGrapa.C)), 6);
        Assert.Equal(6 * 0.95, Cola(GrapaEnElCorte(GanchoEstribo.De90, FormaGrapa.C)), 6);
    }

    [Fact]
    public void EnElCorteLaGrapaEnSTieneLasColasAUnLadoYAlOtroDeSusBarras()
    {
        FiguraSeccion c = GrapaEnElCorte(GanchoEstribo.De180, FormaGrapa.C), s = GrapaEnElCorte(GanchoEstribo.De180, FormaGrapa.S);
        double Lado(FiguraSeccion f, double[] p) => Math.Sign(p[0] - f.Izquierda);

        Assert.Equal(Lado(c, c.Puntos![0]), Lado(c, c.Puntos[^1]));
        Assert.Equal(-Lado(s, s.Puntos![0]), Lado(s, s.Puntos[^1]));
    }

    [Fact]
    public void CadaEstriboDelCorteDiceSuDiametroEnCm()
    {
        VistaPortico v = Armar(OpcionesTablero.PorDefecto with
        {
            Estribos = new ConfigEstribos(Diametro: 4,
                Adicionales: [new EstriboAdicional(TipoEstribo.Abierto, [new PuntoBarra(Posicion.Superior, 1, 2), new PuntoBarra(Posicion.Inferior, 1, 2)])]),
            Corte = new OpcionesCorte(X: 2200),
        });

        Assert.All(v.Seccion.Figuras, f => Assert.Equal(1.27, f.DiametroCm, 6));
    }

    [Fact]
    public void ElCerradoDeLasEsquinasNoTraeTrazo()
    {
        VistaPortico v = Armar(OpcionesTablero.PorDefecto with { Corte = new OpcionesCorte(X: 2200) });

        Assert.Null(v.Seccion.Figuras.Single(f => f.Id == "E").Puntos);
    }

    [Fact]
    public void UnCorteQueQuedaEnUnTraslapoAlRecalcularSeAjusta()
    {
        Rango z = Armar().ZonasTraslapo[0];
        int enMedio = (int)Math.Round((z.Desde + z.Hasta) / 2 * 1000);

        VistaPortico v = Armar(OpcionesTablero.PorDefecto with { Corte = new OpcionesCorte(X: enMedio) });

        Assert.True(v.Corte!.Ajustado);
        Assert.All(v.ZonasTraslapo, r => Assert.True(v.Corte.X < r.Desde || v.Corte.X > r.Hasta));
    }

    /// <summary>
    /// El corte pone cada bastón donde lo pone Revit: si en su puesto se metería en la barra que traslapa al lado o abajo,
    /// los dos lo cambian de puesto igual (24-oct, viga de 25 con 4 #5 + 2 #4 y bastón 5 #6).
    /// </summary>
    [Fact]
    public void ElCorteUbicaLosBastonesComoElPlanDeRevit()
    {
        PorticoLeido p = PlanificadorAceroTests.Portico() is var q ? q with { Tramos = q.Tramos.Select(t => t with { Ancho = 250 }).ToList() } : null!;
        var o = OpcionesTablero.PorDefecto with
        {
            Superior = [new GrupoBarras(4, 5), new GrupoBarras(2, 4)],
            Bastones = new ArmadoBastones(new GrupoBarras(5, 6)),
        };
        PlanAcero plan = PlanificadorAcero.Planificar("P", p, o, Reglas.Generales)!;
        ResultadoVigas rv = MotorVigas.Calcular(new EntradaVigas(p.Portico, p.Tipo, o.Superior, o.Inferior, Reglas.Generales, o.Cara,
            Cambios: o.Cambios, Bastones: o.Bastones));
        double alto = p.Tramos[0].Alto;

        foreach (Baston b in rv.Bastones.Where(x => x.Capa == Posicion.Superior))
        {
            Intervalo i = CalculadoraCorte.Permitidos(p.Portico, rv).First(i => i.Hasta > b.Desde && i.Desde < b.Hasta);
            int x = (int)Math.Round((Math.Max(i.Desde, b.Desde) + Math.Min(i.Hasta, b.Hasta)) / 2);

            VistaPortico v = Armar(o with { Corte = new OpcionesCorte(X: x) }, p);

            var enElCorte = v.Seccion.Barras.Where(s => s.Baston && s.Arriba).Select(s => (Math.Round(s.X, 2), Math.Round(s.Y, 2))).Order();
            var enRevit = plan.Bastones.Where(s => s.Pieza == b.Id).Select(s => (Math.Round(s.XCorte / 10, 2), Math.Round((alto - s.YCorte) / 10, 2))).Order();
            Assert.Equal(enRevit, enElCorte);
        }
    }

    [Fact]
    public void LosBastonesQueNoCabenAvisan()
    {
        PorticoLeido angosto = Portico1() is var p ? p with { Tramos = p.Tramos.Select(t => t with { Ancho = 200 }).ToList() } : null!;
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(5, 5)], Bastones = new ArmadoBastones(new GrupoBarras(5, 8)) };
        o = o with { Corte = new OpcionesCorte(X: DentroDelBastonDeB(o, angosto)) };

        VistaPortico v = Armar(o, angosto);

        Assert.Equal("Los bastones de arriba no caben en el corte.", v.Corte!.Aviso);
        Assert.DoesNotContain(v.Seccion.Barras, b => b.Baston);
    }

    [Fact]
    public void ElAlzadoTraeCadaEstriboEnSuPosicion()
    {
        // Juan, 17-oct: al cambiar "cada" el dibujo no cambiaba, y la línea 1-1 (que va de estribo en estribo) no caía
        // sobre las rayas del alzado.
        VistaPortico cada20 = Armar();
        VistaPortico cada15 = Armar(OpcionesTablero.PorDefecto with { Estribos = new ConfigEstribos(SeparacionCentro: 150) });

        Assert.Equal(cada20.Estribos, cada20.PosicionesEstribos!.Count);
        Assert.Equal(cada20.PosicionesEstribos.Order(), cada20.PosicionesEstribos);
        Assert.True(cada15.PosicionesEstribos!.Count > cada20.PosicionesEstribos.Count);
        Assert.All(cada20.Corte!.Puntos, x => Assert.Contains(x, cada20.PosicionesEstribos));
    }

    [Fact]
    public void SinBastonesLaSeccionEsLaDeSiempre()
    {
        // Con 0, la cara no lleva bastones (#89); null sería 1 barra del diámetro de las esquinas.
        var sinBastones = new ArmadoBastones(new GrupoBarras(0, 5), new GrupoBarras(0, 5));

        VistaPortico v = Armar(OpcionesTablero.PorDefecto with { Bastones = sinBastones });

        Assert.Equal(["a1", "a2", "a3", "b1", "b2", "b3"], v.Seccion.Barras.Select(b => b.Id));
        Assert.DoesNotContain(v.Seccion.Barras, b => b.Baston);
        Assert.Empty(v.Corte!.Proyeccion);
    }
}
