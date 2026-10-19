using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Los saltos del arrastre (#84, decisiones 6 a 8) sobre el pórtico de la maqueta (ver <see cref="CambiosManualesTests"/>:
/// S1 3.00 · S2 5.00 · S3 5.00 · S4 4.50, con S1–S2 de 1672 a 2572 y S2–S3 de 5628 a 6672; las barras van de −150 a 13950).
/// </summary>
public class EditorArrastreTests
{
    private static EntradaVigas Maqueta(CambiosManuales? cambios = null, ArmadoBastones? bastones = null) =>
        new(Datos.PorticoMaqueta(), TipoViga.Aerea, [new GrupoBarras(3, 5)], [new GrupoBarras(3, 5)], Reglas.Generales,
            Cambios: cambios, Bastones: bastones);

    private static IReadOnlyList<Salto> Saltos(EntradaVigas e, string elemento, PuntoArrastre punto) =>
        EditorArrastre.Saltos(e, MotorVigas.Calcular(e), elemento, punto);

    /// <summary>Lo que el motor hace al soltar en <paramref name="s"/>, como lo guarda la página.</summary>
    private static ResultadoVigas Soltar(EntradaVigas e, Salto s) => MotorVigas.Calcular(e with
    {
        Cambios = new CambiosManuales(Movidos: s.Cambios.Where(c => c.Tipo == "movido")
            .ToDictionary(c => c.Id, c => new TraslapoMovido(c.Desde, c.EnVoladizo))),
        Bastones = new ArmadoBastones(Puntas: s.Cambios.Where(c => c.Tipo == "puntas")
            .ToDictionary(c => c.Id, c => new PuntasBaston(c.Desde, c.Hasta))),
    });

    [Fact]
    public void LaPuntaIzquierdaDeUnaBarraMueveSuTraslapoDeModuloEnModulo()
    {
        // S2 por la izquierda corre S1–S2 dentro de A-B: de 2 módulos a la izquierda a 3 a la derecha
        IReadOnlyList<Salto> saltos = Saltos(Maqueta(), "S2", PuntoArrastre.Izquierda);

        Assert.Equal([0.672, 1.172, 1.672, 2.172, 2.672, 3.172], saltos.Select(s => s.X));
        Assert.All(saltos.Where(s => s.X != 1.672), s => Assert.Equal("movido", Assert.Single(s.Cambios).Tipo));
        Assert.Empty(saltos.Single(s => s.X == 1.672).Cambios);
        Salto uno = saltos.Single(s => s.X == 2.172);
        Assert.Equal(new CambioArrastre("movido", "S1–S2", 2172), uno.Cambios[0]);
        Assert.Equal((4.5, "3#5L= 4.50"), (uno.Total, uno.Rotulo));
    }

    [Fact]
    public void ElCentroDeUnaBarraMueveSusDosTraslapos()
    {
        // S2–S3 (5628 a 6672) solo cabe un módulo a cada lado en B-C
        IReadOnlyList<Salto> saltos = Saltos(Maqueta(), "S2", PuntoArrastre.Centro);

        Assert.Equal(3, saltos.Count);
        Salto derecha = saltos[^1];
        Assert.Equal([("S1–S2", 2172), ("S2–S3", 6128)], derecha.Cambios.Select(c => (c.Id, c.Desde)));
        Assert.Equal(5.0, derecha.Total);
    }

    [Fact]
    public void LaPuntaDelBordeYElCentroDeUnaBarraDeBordeNoSeArrastran()
    {
        Assert.Empty(Saltos(Maqueta(), "S1", PuntoArrastre.Izquierda));
        Assert.Empty(Saltos(Maqueta(), "S1", PuntoArrastre.Centro));
        Assert.NotEmpty(Saltos(Maqueta(), "S1", PuntoArrastre.Derecha));
    }

    [Fact]
    public void SinTraslaposLaBarraNoTienePuntos()
    {
        var corto = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 4400)]);
        EntradaVigas e = Maqueta() with { Portico = corto };

        Assert.All(Enum.GetValues<PuntoArrastre>(), p => Assert.Empty(Saltos(e, "S1", p)));
    }

    [Fact]
    public void LosSaltosNoEntranAlNudoNiPasanDeLaVarilla()
    {
        EntradaVigas e = Maqueta();
        foreach (string id in new[] { "S1", "S2", "S3", "S4", "I1", "I2", "I3" })
        foreach (PuntoArrastre punto in Enum.GetValues<PuntoArrastre>())
        foreach (Salto s in Saltos(e, id, punto))
        {
            ResultadoVigas r = Soltar(e, s);
            Assert.DoesNotContain(r.Avisos, a => a.Tipo == TipoAviso.CambioNoAplicado);
            Assert.All(r.Barras, b => Assert.True(b.Largo <= 6000));
            Assert.All(s.Cambios, c => Assert.Equal(c.Desde, r.Traslapos.Single(t => t.Id == c.Id).Desde));
        }
    }

    [Fact]
    public void LosSaltosDelVoladizoVanMarcados()
    {
        // el pórtico de MoverAlVoladizoSinConfirmarSeRechazaYConfirmadoSeAplica: I2–I3 puede pasar el apoyo C
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 4400), Datos.Columna("C", 6800)], VoladizoFinal: 2500);
        EntradaVigas e = Maqueta() with { Portico = portico, CaraElegida = Cara.Final };

        IReadOnlyList<Salto> saltos = Saltos(e, "I3", PuntoArrastre.Izquierda);

        Salto alVoladizo = saltos.Single(s => s.X == 7.212);
        Assert.True(alVoladizo.EnVoladizo);
        Assert.True(alVoladizo.Cambios[0].EnVoladizo);
        Assert.DoesNotContain(saltos, s => s.X is > 5.5 and < 7.0);
        Assert.DoesNotContain(Soltar(e, alVoladizo).Avisos, a => a.Tipo == TipoAviso.CambioNoAplicado);
    }

    [Fact]
    public void DentroDelVoladizoNoVuelveAPreguntar()
    {
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 4400), Datos.Columna("C", 6800)], VoladizoFinal: 2500);
        EntradaVigas e = Maqueta(new CambiosManuales(Movidos: new Dictionary<string, TraslapoMovido> { ["I2–I3"] = new(7212, true) }))
            with { Portico = portico, CaraElegida = Cara.Final };

        IReadOnlyList<Salto> saltos = Saltos(e, "I3", PuntoArrastre.Izquierda);

        Salto otro = saltos.Single(s => s.X == 7.712);
        Assert.False(otro.EnVoladizo);
        Assert.True(otro.Cambios[0].EnVoladizo);
    }

    [Fact]
    public void LaPuntaDelBastonSaltaDeModuloEnModuloYSePegaAlBorde()
    {
        // BS2 va de 3400 a 5400; su punta izquierda llega hasta el borde (−150), donde se pega
        EntradaVigas e = Maqueta();
        IReadOnlyList<Salto> saltos = Saltos(e, "BS2", PuntoArrastre.Izquierda);

        Assert.All(saltos, s => Assert.Equal(0, Math.Round(s.Total * 1000) % 500));
        Salto borde = saltos[0];
        Assert.Equal(-150, borde.Cambios[0].Desde);
        // 5.55 de recto sube a 6.00 y el sobrante va a la otra punta
        Assert.Equal((5850, 6.0), (borde.Cambios[0].Hasta, borde.Total));
        Assert.Contains(saltos, s => s.X == 4.9);
        Assert.Equal(-150, Soltar(e, borde).Bastones.Single(b => b.Id == "BS2").Desde);
    }

    [Fact]
    public void ElCentroDelBastonSaltaDe5cm()
    {
        IReadOnlyList<Salto> saltos = Saltos(Maqueta(), "BS2", PuntoArrastre.Centro);

        Assert.All(saltos.Zip(saltos.Skip(1)), par => Assert.Equal(0.05, par.Second.X - par.First.X, 3));
        Assert.All(saltos, s => Assert.Equal(2.0, s.Recto));
        Assert.Contains(saltos, s => s.Cambios.Count == 1 && s.Cambios[0] == new CambioArrastre("puntas", "BS2", 3450, 5450));
    }

    [Fact]
    public void RotuloConYSinGancho()
    {
        Assert.Equal("3#5L= 4.00/4.28", Rotulo.De([new GrupoBarras(3, 5)], 4000, 4278));
        Assert.Equal("1#4L= 2.30", Rotulo.De([new GrupoBarras(1, 4)], 2300, 2300));
        Assert.Equal("2#5+1#4L= 3.00", Rotulo.De([new GrupoBarras(2, 5), new GrupoBarras(1, 4)], 3000, 3000));
    }
}
