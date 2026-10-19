using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Modelos armados como el modelo real de prueba ("Prueba 080720sfsd26.rvt", ver Hallazgos del plan #81): vigas
/// partidas en cada eje con las puntas en el centro de las columnas, ejes A / 5 / 6 en x = 0 / 3500 / 7000.
/// </summary>
public class AgrupadorPorticosTests
{
    private static readonly NivelLeido Piso1 = new("N+0.00 Piso 1", 0);
    private static readonly NivelLeido Piso2 = new("N+3.50 Piso 2", 3500);

    private static readonly EjeLeido[] Ejes =
    [
        new("A", new(0, -3000), new(0, 14000)),
        new("5", new(3500, -3000), new(3500, 14000)),
        new("6", new(7000, -3000), new(7000, 14000)),
        new("1", new(-2000, 0), new(8000, 0)),
    ];

    private static ApoyoLeido Columna(string id, double x, double y, double lado, double z0, double z1) =>
        new(id, TipoApoyo.Columna, [new(x - lado / 2, y - lado / 2), new(x + lado / 2, y - lado / 2), new(x + lado / 2, y + lado / 2), new(x - lado / 2, y + lado / 2)], z0, z1);

    private static VigaLeida Viga(string id, double x0, double x1, double zArriba, NivelLeido? nivel, double y = 0, double ancho = 250, double alto = 350) =>
        new(id, new(x0, y), new(x1, y), zArriba - alto, zArriba, ancho, nivel);

    /// <summary>Eje 1 del piso 2: vigas A-5 y 5-6, columnas de 30 × 30 debajo (0–3500) y encima (3500–7000).</summary>
    private static ModeloLeido EjeUnoPiso2() => new(Ejes,
        [Viga("v1", 0, 3500, 3500, Piso2), Viga("v2", 3500, 7000, 3500, Piso2)],
        [
            Columna("c1", 0, 0, 300, 0, 3500), Columna("c2", 3500, 0, 300, 0, 3500), Columna("c3", 7000, 0, 300, 0, 3500),
            Columna("c4", 0, 0, 300, 3500, 7000), Columna("c5", 3500, 0, 300, 3500, 7000), Columna("c6", 7000, 0, 300, 3500, 7000),
        ]);

    [Fact]
    public void VigasSeguidasSobreUnEje_FormanUnPorticoConSusApoyosYCaras()
    {
        ResultadoLectura r = AgrupadorPorticos.Agrupar(EjeUnoPiso2());

        PorticoLeido p = Assert.Single(r.Porticos);
        Assert.Equal(("Pórtico 1", "1", "N+3.50 Piso 2", TipoViga.Aerea), (p.Nombre, p.Eje, p.Nivel, p.Tipo));
        Assert.Equal(
            [new Apoyo("A", 0, -150, 150), new Apoyo("5", 3500, 3350, 3650), new Apoyo("6", 7000, 6850, 7150)],
            p.Portico.Apoyos);
        Assert.Equal((0, 0), (p.Portico.VoladizoInicio, p.Portico.VoladizoFinal));
        Assert.Equal(["Viga A-5", "Viga 5-6"], p.Tramos.Select(t => t.Nombre));
        Assert.All(p.Tramos, t => Assert.Equal((250, 350), (t.Ancho, t.Alto)));
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public void LaColumnaQueSoloTocaLaVigaEnLaCaraDeArriba_NoEsApoyo()
    {
        // Piso 1 del modelo real: viga en z −350..0 sobre pedestales de 40 × 40 (−650..0); la columna de
        // 30 × 30 de arriba (0..3500) la toca solo en z = 0.
        var modelo = new ModeloLeido(Ejes,
            [Viga("v1", 0, 3500, 0, Piso1, ancho: 300), Viga("v2", 3500, 7000, 0, Piso1, ancho: 300)],
            [
                Columna("p1", 0, 0, 400, -650, 0), Columna("p2", 3500, 0, 400, -650, 0), Columna("p3", 7000, 0, 400, -650, 0),
                Columna("c1", 0, 0, 300, 0, 3500), Columna("c2", 3500, 0, 300, 0, 3500), Columna("c3", 7000, 0, 300, 0, 3500),
            ]);

        PorticoLeido p = Assert.Single(AgrupadorPorticos.Agrupar(modelo).Porticos);

        Assert.Equal([(-200, 200), (3300, 3700), (6800, 7200)], p.Portico.Apoyos.Select(a => (a.CaraIzquierda, a.CaraDerecha)));
    }

    [Theory]
    [InlineData("N+0.00 Piso 1", 0, TipoViga.Cimentacion)]
    [InlineData("N-0.65", -650, TipoViga.Cimentacion)]
    [InlineData("Cimentación", 2000, TipoViga.Cimentacion)]
    [InlineData("FUNDACION", 2000, TipoViga.Cimentacion)]
    [InlineData("N+3.50 Piso 2", 3500, TipoViga.Aerea)]
    public void ElTipoSaleDelNivel_CotaMenorOIgualACeroOElNombre(string nombre, double cota, TipoViga esperado)
    {
        var nivel = new NivelLeido(nombre, cota);
        ModeloLeido m = EjeUnoPiso2() with { Vigas = [Viga("v1", 0, 3500, 3500, nivel), Viga("v2", 3500, 7000, 3500, nivel)] };

        Assert.Equal(esperado, Assert.Single(AgrupadorPorticos.Agrupar(m).Porticos).Tipo);
    }

    [Fact]
    public void SinNivel_EsAereaYSeAvisa()
    {
        ModeloLeido m = EjeUnoPiso2() with { Vigas = [Viga("v1", 0, 3500, 3500, null), Viga("v2", 3500, 7000, 3500, null)] };

        ResultadoLectura r = AgrupadorPorticos.Agrupar(m);

        Assert.Equal(TipoViga.Aerea, Assert.Single(r.Porticos).Tipo);
        Assert.Contains(r.Avisos, a => a.Tipo == TipoAvisoLectura.SinNivel);
    }

    [Fact]
    public void ElPorticoDeLaMaqueta_SaleIgualAlQueUsaElMotor()
    {
        // Ejes A, B, C y D en 0, 4.40, 7.90 y 12.30 m; columnas de 40 × 40; la viga sigue 1.50 m después de la
        // cara de D (termina en 14.00).
        EjeLeido[] ejes = [new("A", new(0, -1000), new(0, 1000)), new("B", new(4400, -1000), new(4400, 1000)),
            new("C", new(7900, -1000), new(7900, 1000)), new("D", new(12300, -1000), new(12300, 1000)), new("1", new(-1000, 0), new(15000, 0))];
        var modelo = new ModeloLeido(ejes,
            [Viga("v1", 0, 4400, 3500, Piso2), Viga("v2", 4400, 7900, 3500, Piso2), Viga("v3", 7900, 14000, 3500, Piso2)],
            [Columna("a", 0, 0, 400, 0, 3500), Columna("b", 4400, 0, 400, 0, 3500), Columna("c", 7900, 0, 400, 0, 3500), Columna("d", 12300, 0, 400, 0, 3500)]);

        PorticoLeido p = Assert.Single(AgrupadorPorticos.Agrupar(modelo).Porticos);

        Portico esperado = Datos.PorticoMaqueta();
        Assert.Equal(esperado.Apoyos, p.Portico.Apoyos);
        Assert.Equal((esperado.VoladizoInicio, esperado.VoladizoFinal), (p.Portico.VoladizoInicio, p.Portico.VoladizoFinal));
        Assert.Equal(["Viga A-B", "Viga B-C", "Viga C-D", "Voladizo D"], p.Tramos.Select(t => t.Nombre));
        Assert.Equal([false, false, false, true], p.Tramos.Select(t => t.EsVoladizo));

        ResultadoVigas calculo = MotorVigas.Calcular(new EntradaVigas(p.Portico, p.Tipo, [new GrupoBarras(3, 5)], [new GrupoBarras(3, 5)], Reglas.Generales));
        Assert.NotNull(calculo.Superior);
        Assert.NotNull(calculo.Inferior);
    }

    [Fact]
    public void UnHuecoSinApoyo_PartePorticoEnAYB()
    {
        // Eje 1: vigas de A a 6 (0–7000) y otra de 9000 a 12000, con columnas en cada punta. Entre 7000 y 9000 no
        // hay viga ni un apoyo que tape el hueco: son dos pórticos.
        var modelo = new ModeloLeido(Ejes,
            [Viga("v1", 0, 3500, 3500, Piso2), Viga("v2", 3500, 7000, 3500, Piso2), Viga("v3", 9000, 12000, 3500, Piso2)],
            [Columna("c1", 0, 0, 300, 0, 3500), Columna("c2", 3500, 0, 300, 0, 3500), Columna("c3", 7000, 0, 300, 0, 3500),
             Columna("c4", 9000, 0, 300, 0, 3500), Columna("c5", 12000, 0, 300, 0, 3500)]);

        ResultadoLectura r = AgrupadorPorticos.Agrupar(modelo);

        Assert.Equal(["Pórtico 1a", "Pórtico 1b"], r.Porticos.Select(p => p.Nombre));
        Assert.Equal([2, 1], r.Porticos.Select(p => p.Tramos.Count));
    }

    [Fact]
    public void UnaVigaQueTerminaSobreOtraViga_LaTomaComoApoyo()
    {
        // La viga del eje 1 va de A a 5; en 5 no hay columna sino una viga que cruza (eje 5, de y −2000 a 2000).
        var modelo = new ModeloLeido(Ejes,
            [Viga("v1", 0, 3500, 3500, Piso2), new VigaLeida("cargadora", new(3500, -2000), new(3500, 2000), 3100, 3500, 300, Piso2)],
            [Columna("c1", 0, 0, 300, 0, 3500), Columna("c2", 3500, -2000, 300, 0, 3500), Columna("c3", 3500, 2000, 300, 0, 3500)]);

        PorticoLeido p = AgrupadorPorticos.Agrupar(modelo).Porticos.Single(x => x.Eje == "1");

        Assert.Equal([TipoApoyo.Columna, TipoApoyo.Viga], p.TiposDeApoyo);
        Assert.Equal((3350, 3650), (p.Portico.Apoyos[1].CaraIzquierda, p.Portico.Apoyos[1].CaraDerecha));
    }

    [Fact]
    public void LaVigaDeBordeDeUnVoladizoNoEsApoyo()
    {
        // Como el eje A del modelo de Carlos: pasada la última columna (5), la viga sigue hasta 5000 y en la punta
        // cruza la viga de borde, que no descansa en ninguna columna: es armazón y queda voladizo.
        var modelo = new ModeloLeido(Ejes,
            [Viga("v1", 0, 3500, 3500, Piso2), Viga("v2", 3500, 5000, 3500, Piso2),
             new VigaLeida("borde", new(5000, -2000), new(5000, 2000), 3150, 3500, 100, Piso2)],
            [Columna("c1", 0, 0, 300, 0, 3500), Columna("c2", 3500, 0, 300, 0, 3500)]);

        PorticoLeido p = AgrupadorPorticos.Agrupar(modelo).Porticos.Single(x => x.Eje == "1");

        Assert.Equal([TipoApoyo.Columna, TipoApoyo.Columna], p.TiposDeApoyo);
        Assert.Equal(1350, p.Portico.VoladizoFinal);
        Assert.Equal(["Viga A-5", "Voladizo 5"], p.Tramos.Select(t => t.Nombre));
    }

    [Fact]
    public void UnaVigaQueCruzaEntreDosColumnasSigueSiendoApoyo()
    {
        // Entre las columnas de A y 6, la viga del eje 1 termina en 5 sobre una viga que cruza y no tiene columnas.
        var modelo = new ModeloLeido(Ejes,
            [Viga("v1", 0, 3500, 3500, Piso2), Viga("v2", 3500, 7000, 3500, Piso2),
             new VigaLeida("cruza", new(3500, -2000), new(3500, 2000), 3100, 3500, 300, Piso2)],
            [Columna("c1", 0, 0, 300, 0, 3500), Columna("c3", 7000, 0, 300, 0, 3500)]);

        PorticoLeido p = AgrupadorPorticos.Agrupar(modelo).Porticos.Single(x => x.Eje == "1");

        Assert.Equal([TipoApoyo.Columna, TipoApoyo.Viga, TipoApoyo.Columna], p.TiposDeApoyo);
    }

    [Fact]
    public void UnMuroDeCargaQueCruzaLaViga_EsApoyo_YUnoALoLargoNo()
    {
        ApoyoLeido muroQueCruza = new("m1", TipoApoyo.Muro, [new(3440, -2000), new(3560, -2000), new(3560, 2000), new(3440, 2000)], 0, 3500, Direccion: new(0, 1));
        ApoyoLeido muroALoLargo = new("m2", TipoApoyo.Muro, [new(-100, -60), new(7100, -60), new(7100, 60), new(-100, 60)], 0, 3500, Direccion: new(1, 0));
        var modelo = new ModeloLeido(Ejes, [Viga("v1", 0, 3500, 3500, Piso2), Viga("v2", 3500, 7000, 3500, Piso2)],
            [Columna("c1", 0, 0, 300, 0, 3500), muroQueCruza, Columna("c3", 7000, 0, 300, 0, 3500), muroALoLargo]);

        PorticoLeido p = Assert.Single(AgrupadorPorticos.Agrupar(modelo).Porticos);

        Assert.Equal([TipoApoyo.Columna, TipoApoyo.Muro, TipoApoyo.Columna], p.TiposDeApoyo);
        Assert.Equal((3440, 3560), (p.Portico.Apoyos[1].CaraIzquierda, p.Portico.Apoyos[1].CaraDerecha));
    }

    [Fact]
    public void UnaVigaSinApoyos_NoArmaPorticoYSeAvisa()
    {
        // Como la viga 809244 del modelo real: nada la cruza en planta y en alto a la vez.
        var modelo = new ModeloLeido(Ejes, [Viga("809244", 0, 7000, 0, Piso1)], [Columna("c1", 0, 0, 300, 0, 2100)]);

        ResultadoLectura r = AgrupadorPorticos.Agrupar(modelo);

        Assert.Empty(r.Porticos);
        Assert.Contains(new AvisoLectura(TipoAvisoLectura.SinApoyos, "809244"), r.Avisos);
    }

    [Fact]
    public void UnCambioDeSeccionEntreTramos_SeAvisa()
    {
        ModeloLeido m = EjeUnoPiso2() with { Vigas = [Viga("v1", 0, 3500, 3500, Piso2), Viga("v2", 3500, 7000, 3500, Piso2, alto: 450)] };

        ResultadoLectura r = AgrupadorPorticos.Agrupar(m);

        Assert.Equal([350, 450], Assert.Single(r.Porticos).Tramos.Select(t => t.Alto));
        Assert.Contains(new AvisoLectura(TipoAvisoLectura.CambioDeSeccion, "Pórtico 1 · N+3.50 Piso 2"), r.Avisos);
    }

    [Fact]
    public void DiferenciasDeDecimalesEnLaSeccion_NoSonCambioDeSeccion()
    {
        // En el modelo real, el ancho medido en la geometría trae ruido de decimales: el informe del 16-oct dio un
        // aviso falso de cambio de sección en "Pórtico A".
        ModeloLeido m = EjeUnoPiso2() with { Vigas = [Viga("v1", 0, 3500, 3500, Piso2), Viga("v2", 3500, 7000, 3500, Piso2, ancho: 250.0000004)] };

        ResultadoLectura r = AgrupadorPorticos.Agrupar(m);

        Assert.DoesNotContain(r.Avisos, a => a.Tipo == TipoAvisoLectura.CambioDeSeccion);
        Assert.All(Assert.Single(r.Porticos).Tramos, t => Assert.Equal(250, t.Ancho));
    }

    [Fact]
    public void UnPorticoFueraDeLosEjes_SeNombraSinEjeYSeAvisa()
    {
        ModeloLeido m = EjeUnoPiso2() with
        {
            Vigas = [Viga("v1", 0, 3500, 3500, Piso2, y: 1000), Viga("v2", 3500, 7000, 3500, Piso2, y: 1000)],
            Apoyos = [Columna("c1", 0, 1000, 300, 0, 3500), Columna("c2", 3500, 1000, 300, 0, 3500), Columna("c3", 7000, 1000, 300, 0, 3500)],
        };

        ResultadoLectura r = AgrupadorPorticos.Agrupar(m);

        PorticoLeido p = Assert.Single(r.Porticos);
        Assert.Equal(("Pórtico sin eje 1", null), (p.Nombre, p.Eje));
        Assert.Contains(new AvisoLectura(TipoAvisoLectura.SinEje, "Pórtico sin eje 1 · N+3.50 Piso 2"), r.Avisos);
    }

    [Fact]
    public void UnaVigaCorridaDelEjeTomaElNombreDeLaRejilla()
    {
        // Como los ejes A y C del modelo de Carlos: viga de 25 justificada a un lado, con su línea en la cara de las
        // columnas de 25 y a 125 mm del eje 1, que sí pasa por el centro de las columnas.
        ModeloLeido m = EjeUnoPiso2() with
        {
            Vigas = [Viga("v1", 0, 3500, 3500, Piso2, y: -125), Viga("v2", 3500, 7000, 3500, Piso2, y: -125)],
            Apoyos = [Columna("c1", 0, 0, 250, 0, 3500), Columna("c2", 3500, 0, 250, 0, 3500), Columna("c3", 7000, 0, 250, 0, 3500)],
        };

        ResultadoLectura r = AgrupadorPorticos.Agrupar(m);

        PorticoLeido p = Assert.Single(r.Porticos);
        Assert.Equal(("Pórtico 1", "1"), (p.Nombre, p.Eje));
        Assert.DoesNotContain(r.Avisos, a => a.Tipo == TipoAvisoLectura.SinEje);
    }

    [Fact]
    public void UnaRejillaParalelaQueNoPasaPorLosApoyosNoSeToma()
    {
        // El eje B va a 200 mm de la viga pero por fuera de las columnas (850–1150).
        ModeloLeido m = EjeUnoPiso2() with
        {
            Ejes = [.. Ejes, new("B", new(-2000, 800), new(8000, 800))],
            Vigas = [Viga("v1", 0, 3500, 3500, Piso2, y: 1000), Viga("v2", 3500, 7000, 3500, Piso2, y: 1000)],
            Apoyos = [Columna("c1", 0, 1000, 300, 0, 3500), Columna("c2", 3500, 1000, 300, 0, 3500), Columna("c3", 7000, 1000, 300, 0, 3500)],
        };

        ResultadoLectura r = AgrupadorPorticos.Agrupar(m);

        PorticoLeido p = Assert.Single(r.Porticos);
        Assert.Equal(("Pórtico sin eje 1", null), (p.Nombre, p.Eje));
    }

    [Fact]
    public void ElMismoEjeEnDosNiveles_SonDosPorticos()
    {
        ModeloLeido m = EjeUnoPiso2() with
        {
            Vigas = [Viga("v1", 0, 3500, 3500, Piso2), Viga("v2", 3500, 7000, 3500, Piso2), Viga("v3", 0, 3500, 7000, new("Cubierta", 7000), ancho: 200)],
        };

        ResultadoLectura r = AgrupadorPorticos.Agrupar(m);

        Assert.Equal([("Pórtico 1", "N+3.50 Piso 2"), ("Pórtico 1", "Cubierta")], r.Porticos.Select(p => (p.Nombre, p.Nivel)));
    }
}
