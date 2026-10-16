using CQEING.Vigas.Calculo.Nucleo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Ejemplos de la reunión del 08-oct-2026 (columna, en mm): traslapo 1000, gancho 200, módulo 500.
/// Base en 0 y corona en 11 385; la barra de abajo lleva gancho abajo y la de arriba, gancho arriba.
/// </summary>
public class ResolvedorLineaTests
{
    private static ProblemaLinea Columna(IReadOnlyList<Ranura> ranuras, IReadOnlyDictionary<int, int>? fijos = null) =>
        new(Inicio: 0, Fin: 11385, GanchoInicio: 200, GanchoFin: 200, Ranuras: ranuras,
            TraslapoMinimo: 1000, Modulo: 500, VarillaBase: 12000, VarillaMaxima: 12000, LargosFijos: fijos);

    private static readonly Ranura PisoUno = new(ZonaDesde: 0, ZonaHasta: 3500, PuntoIdeal: 2500, Referencia.Centro);
    private static readonly Ranura PisoTres = new(ZonaDesde: 7000, ZonaHasta: 10500, PuntoIdeal: 9500, Referencia.Centro);
    private static readonly Ranura PisoDos = new(ZonaDesde: 3500, ZonaHasta: 7000, PuntoIdeal: 6000, Referencia.Centro);

    [Fact]
    public void Ejemplo1_TresBarras_DaLasDeCarlos()
    {
        var s = ResolvedorLinea.Resolver(Columna([PisoUno, PisoTres]));

        Assert.NotNull(s);
        Assert.Equal([3500, 8000, 2500], s.Barras.Select(b => b.Largo));
        Assert.Equal([(2085, 3300), (9085, 10085)], s.Traslapos.Select(t => (t.Desde, t.Hasta)));
        Assert.Equal([192.5, 85], s.Traslapos.Select(t => t.Desvio));
    }

    [Fact]
    public void Ejemplo2_DosBarras_MandaLaDeArribaYElTraslapoCrece()
    {
        var s = ResolvedorLinea.Resolver(Columna([PisoDos]));

        Assert.NotNull(s);
        Assert.Equal([7000, 6000], s.Barras.Select(b => b.Largo));
        Assert.Equal(1215, s.Traslapos[0].Largo);
    }

    [Fact]
    public void BarraIntermediaQueManda_SeReparteEntreSusDosTraslapos()
    {
        // Sin corregir: abajo 3.00 y arriba 3.00 (múltiplos) y la del medio 6.10. Corregida, la del medio sube a
        // 6.50 y se corre: moverla hacia una punta o repartir da el mismo desvío total (200), y el empate se
        // resuelve repartiendo, 100 y 100.
        var abajo = new Ranura(ZonaDesde: 0, ZonaHasta: 4000, PuntoIdeal: 2300, Referencia.Centro);
        var arriba = new Ranura(ZonaDesde: 5000, ZonaHasta: 9500, PuntoIdeal: 7400, Referencia.Centro);

        var s = ResolvedorLinea.Resolver(Columna([abajo, arriba]) with { Fin = 9700 });

        Assert.NotNull(s);
        Assert.Equal([3000, 6500, 3000], s.Barras.Select(b => b.Largo));
        Assert.Equal([(1600, 2800), (6900, 8100)], s.Traslapos.Select(t => (t.Desde, t.Hasta)));
        Assert.Equal([100, 100], s.Traslapos.Select(t => t.Desvio));
    }

    [Fact]
    public void ElTraslapoNoSeAlejaDeSuPuntoIdealMasDeLoPermitido()
    {
        // Sin límite gana 7000 + 6000 con desvío 192.5. Con 100 como máximo hay que gastar un módulo más:
        // 7000 + 6500, traslapo 1715 y desvío 57.5.
        var s = ResolvedorLinea.Resolver(Columna([PisoDos with { DesvioMaximo = 100 }]));

        Assert.NotNull(s);
        Assert.Equal([7000, 6500], s.Barras.Select(b => b.Largo));
        Assert.Equal(1715, s.Traslapos[0].Largo);
        Assert.Equal(57.5, s.Traslapos[0].Desvio);
    }

    [Fact]
    public void SinRanuras_LaBarraVaDerechaAunqueNoSeaMultiplo()
    {
        var p = Columna([]) with { Fin = 5000 };

        var s = ResolvedorLinea.Resolver(p);

        Assert.NotNull(s);
        Assert.Equal([5400], s.Barras.Select(b => b.Largo));
        Assert.Empty(s.Traslapos);
    }

    [Fact]
    public void RanuraDondeNoCabeElTraslapo_NoHaySolucion()
    {
        var angosta = new Ranura(ZonaDesde: 0, ZonaHasta: 800, PuntoIdeal: 400, Referencia.Centro);

        Assert.Null(ResolvedorLinea.Resolver(Columna([angosta])));
    }

    [Fact]
    public void LargoFijo_SeRespetaYElRestoSeRecalcula()
    {
        var s = ResolvedorLinea.Resolver(Columna([PisoDos], new Dictionary<int, int> { [0] = 6500 }));

        Assert.NotNull(s);
        Assert.Equal([6500, 6500], s.Barras.Select(b => b.Largo));
        Assert.Equal(1215, s.Traslapos[0].Largo);
    }

    [Fact]
    public void VarillaBaseDeSeis_PrefiereNoUsarDoce()
    {
        // Con 6 m de base, 7000 + 6000 usa una de 12 m; 6000 + 6500 tampoco sirve (6500 > 6000).
        // Lo único sin 12 m es partir en tres barras, pero aquí solo hay una ranura: se usa 12 m y se cuenta.
        var s = ResolvedorLinea.Resolver(Columna([PisoDos]) with { VarillaBase = 6000 });

        Assert.NotNull(s);
        Assert.Equal(1, s.BarrasDe12);
    }
}
