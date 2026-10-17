using CQEING.Vigas.Calculo.Estribos;
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

        Assert.Equal(11, v.Zonas.Count);
        Assert.Equal((0.25, 0.95, true), (Math.Round(v.Zonas[0].Desde, 2), Math.Round(v.Zonas[0].Hasta, 2), v.Zonas[0].Junta));
        Assert.Equal((0.95, 3.45, false), (Math.Round(v.Zonas[1].Desde, 2), Math.Round(v.Zonas[1].Hasta, 2), v.Zonas[1].Junta));
        Assert.Equal((12.55, 13.25, true), (Math.Round(v.Zonas[^2].Desde, 2), Math.Round(v.Zonas[^2].Hasta, 2), v.Zonas[^2].Junta));
        Assert.Equal((13.25, 13.95, false), (Math.Round(v.Zonas[^1].Desde, 2), Math.Round(v.Zonas[^1].Hasta, 2), v.Zonas[^1].Junta));
    }

    [Fact]
    public void LaSeccionVaEnCmConLasEsquinasMarcadas()
    {
        VistaPortico v = Armar(OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(5, 5)] });

        List<BarraSeccion> arriba = v.Seccion.Barras.Where(b => b.Arriba && !b.Baston).ToList();
        Assert.Equal([5.745, 10.3725, 15, 19.6275, 24.255], arriba.Select(b => Math.Round(b.X, 4)));
        Assert.Equal(["a1", "a2", "a3", "a4", "a5"], arriba.Select(b => b.Id));
        Assert.Equal([true, false, false, false, true], arriba.Select(b => b.Esquina));
        Assert.Equal(("arriba 1", 5.745, 1.59), (arriba[0].Nombre, Math.Round(arriba[0].Y, 4), arriba[0].D));
        Assert.Equal((30, 50), (v.Seccion.AnchoCm, v.Seccion.AltoCm));
        Assert.Equal(new FiguraSeccion("E", "Cerrado", 4, 26, 4, 46), v.Seccion.Figuras[0]);
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

    /// <summary>El caso de <see cref="AvisosTests"/>: D en 11.55 y la capa de abajo al final de la luz.</summary>
    [Fact]
    public void UnTraslapoLargoDaElAvisoConSuTexto()
    {
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 4400), Datos.Columna("C", 7900), Datos.Columna("D", 11550)],
            VoladizoFinal: 1500);

        VistaPortico v = Armar(OpcionesTablero.PorDefecto with { Cara = Cara.Final }, Portico1(portico));

        AvisoVista a = Assert.Single(v.Avisos, a => a.Tipo == "TraslapoLargo" && a.Elemento.StartsWith('I'));
        Assert.Equal("El traslapo I3–I4 queda en 1.27, 0.37 por encima del mínimo. I4 termina en el voladizo y solo puede crecer. "
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

        Assert.Equal(("E", "Estribo cerrado", 91, "#3"), (e.Id, e.Tipo, e.Cantidad, e.Diametro));
        Assert.Equal(1.468, e.Largo, 6);
        Assert.Equal("zonas 0.80 cada 0.10 · centro cada 0.20", e.Detalle);
        Assert.Equal(r.Kg, e.Kg, 6);
    }

    [Fact]
    public void ElTotalSumaBarrasBastonesYEstribos()
    {
        ResultadoVigas rv = MotorVigas.Calcular(EntradaDe(Portico1()));

        VistaPortico v = Armar();

        Assert.Equal(rv.Kg + v.ListaEstribos.Sum(e => e.Kg), v.Kg, 6);
        Assert.Equal((91, true), (v.Estribos, v.SePuedeGenerar));
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
        Assert.Equal(new ReglaVista("#3", "0.55", "0.166", "0.113", "0.560"), v.PorDiametro[0]);
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
        Assert.Null(c.Aviso);
    }

    [Fact]
    public void ElCorteTraeLosBastonesQuePasanPorAhi()
    {
        var o = OpcionesTablero.PorDefecto with { Bastones = BastonArriba };
        o = o with { Corte = new OpcionesCorte(X: DentroDelBastonDeB(o, Portico1())) };

        VistaPortico v = Armar(o);

        Assert.False(v.Corte!.Ajustado);
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
    public void UnCorteQueQuedaEnUnTraslapoAlRecalcularSeAjusta()
    {
        Rango z = Armar().ZonasTraslapo[0];
        int enMedio = (int)Math.Round((z.Desde + z.Hasta) / 2 * 1000);

        VistaPortico v = Armar(OpcionesTablero.PorDefecto with { Corte = new OpcionesCorte(X: enMedio) });

        Assert.True(v.Corte!.Ajustado);
        Assert.All(v.ZonasTraslapo, r => Assert.True(v.Corte.X < r.Desde || v.Corte.X > r.Hasta));
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
