using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Nucleo;
using CQEING.Vigas.Calculo.Vigas;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>Con la tabla real de Carlos: #5 traslapa 900 y su gancho de 90° mide 278; #8, 1700 y 444.</summary>
public class ResolvedorCapaTests
{
    private static readonly ParametrosCapa CentroNumero5 = new(Referencia.Centro, TraslapoMinimo: 900, GanchoInicio: 278, GanchoFin: 278,
        Modulo: 500, Varilla: 6000, VarillaLarga: 12000);

    private static readonly GeometriaPortico Maqueta = new(Datos.PorticoMaqueta(), recubrimientoPuntas: 50);

    [Fact]
    public void PorticoDeLaMaqueta_ConVarillaDe6_UnTraslapoPorLuzYNingunoEnElVoladizo()
    {
        // Con el traslapo a no más de 0.50 de la mitad de la luz (decisión 15), dos traslapos no alcanzan: la
        // primera barra de 6 m no llega a la mitad de B-C. Quedan tres, como dibuja la maqueta.
        CapaResuelta? capa = ResolvedorCapa.Resolver(Maqueta, CentroNumero5);

        Assert.NotNull(capa);
        Assert.Equal(["A-B", "B-C", "C-D"], capa.Luces.Select(l => l.Nombre));
        Assert.All(capa.Solucion.Traslapos, t => Assert.InRange(t.Desvio, 0, 500));
        Assert.All(capa.Solucion.Barras, b => Assert.True(b.Largo <= 6000 && b.Largo % 500 == 0));
        Assert.Equal(13950, capa.Solucion.Barras[^1].Hasta);
        Assert.Equal(0, capa.Solucion.BarrasDe12);
    }

    [Fact]
    public void ConVarillaDe12_BastaUnTraslapo()
    {
        CapaResuelta? capa = ResolvedorCapa.Resolver(Maqueta, CentroNumero5 with { Varilla = 12000 });

        Assert.NotNull(capa);
        Assert.Single(capa.Solucion.Traslapos);
    }

    [Fact]
    public void LaCapaDeLaCara_TraslapaPegadaALaCaraIzquierda()
    {
        CapaResuelta? capa = ResolvedorCapa.Resolver(Maqueta, CentroNumero5 with { Referencia = Referencia.Inicio });

        Assert.NotNull(capa);
        Assert.All(capa.Solucion.Traslapos.Zip(capa.Luces),
            x => Assert.InRange(x.First.Desde - x.Second.CaraIzquierda, 50, 50 + 499));
    }

    [Fact]
    public void TramoCortoDondeNoCabeElTraslapo_UsaVarillaDe12()
    {
        // Luz libre de 1.20 m (no le cabe un traslapo #8 de 1.70) con voladizos de 3 m: la barra pasa de 6 m.
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 1600)], VoladizoInicio: 3000, VoladizoFinal: 3000);
        var g = new GeometriaPortico(portico, recubrimientoPuntas: 50);
        var numero8 = CentroNumero5 with { TraslapoMinimo = 1700, GanchoInicio = 444, GanchoFin = 444 };

        CapaResuelta? capa = ResolvedorCapa.Resolver(g, numero8);

        Assert.NotNull(capa);
        Assert.Empty(capa.Solucion.Traslapos);
        Assert.Equal(1, capa.Solucion.BarrasDe12);
    }

    [Fact]
    public void ConLucesFijas_UsaSoloEsas()
    {
        Luz[] soloCd = [Maqueta.Luces[2]];

        CapaResuelta? capa = ResolvedorCapa.Resolver(Maqueta, CentroNumero5 with { Varilla = 12000 }, lucesFijas: soloCd);

        Assert.NotNull(capa);
        Assert.Equal(soloCd, capa.Luces);
    }
}
