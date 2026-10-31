using CQEING.Vigas.Calculo.Estribos;
using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Las posiciones de los estribos de un tramo partidas en corridas de separación constante: cada una es un conjunto de
/// Revit (decisión 10 del plan docs/planes/2026-10-19-generar-acero-vigas-85.md).
/// </summary>
public class CorridasEstribosTests
{
    [Fact]
    public void ParteDondeCambiaLaSeparacion()
    {
        IReadOnlyList<Corrida> c = CorridasEstribos.Partir([100, 200, 300, 500, 700, 900, 1000, 1100]);

        Assert.Equal([new Corrida(100, 3, 100), new Corrida(500, 3, 200), new Corrida(1000, 2, 100)], c);
    }

    [Fact]
    public void ToleraElRuidoDeDecimales()
    {
        IReadOnlyList<Corrida> c = CorridasEstribos.Partir([0, 100.4, 199.8, 300]);

        Assert.Equal(new Corrida(0, 4, 100), Assert.Single(c));
    }

    [Fact]
    public void UnoONinguno()
    {
        Assert.Equal([new Corrida(350, 1, 0)], CorridasEstribos.Partir([350]));
        Assert.Empty(CorridasEstribos.Partir([]));
    }

    [Fact]
    public void PorZonas_ElEstriboCompartidoEsDeLaZona()
    {
        // Hallazgo E6 (Revit, 2026-10-19): tramo 3-4 marcado con zona 0.20 @0.10 y centro @0.20, caras en 7630 y 11310.
        // Partiendo por separación salían 2 + 17 + 1; por zonas tienen que salir 2 + 16 + 2, como el rótulo del tablero.
        var tramo = new Tramo("3-4", 7630, 11310, TipoTramo.Luz);
        var c = new ConfigEstribos(LargoZona: 200, SeparacionZona: 100, SeparacionCentro: 200);
        IReadOnlyList<double> posiciones = DistribuidorEstribos.Posiciones(tramo, c, 9.5);

        IReadOnlyList<Corrida> corridas = CorridasEstribos.PorZonas(posiciones, DistribuidorEstribos.Zonas(tramo, c, 9.5));

        Assert.Equal([2, 16, 2], corridas.Select(x => x.Cantidad));
        Assert.Equal((7684.75, 100), (corridas[0].Primero, corridas[0].Separacion));
        Assert.Equal(7784.75 + 200, corridas[1].Primero, 6);
        Assert.Equal(200, corridas[1].Separacion, 6);
        Assert.Equal((11155.25, 100), (corridas[2].Primero, Math.Round(corridas[2].Separacion, 6)));
    }

    [Fact]
    public void PorZonas_DaLasMismasPosicionesEnLaMaqueta()
    {
        PorticoCalculado c = CalculoPortico.Calcular(ArmadorVistaTests.Portico1(), OpcionesTablero.PorDefecto, Reglas.Generales)!;

        foreach (Tramo t in DistribuidorEstribos.Tramos(ArmadorVistaTests.Portico1().Portico))
        {
            EstriboEnPortico e = c.Estribos.Estribos.Single(x => x.Id == "E" && x.Tramo == t.Nombre);
            IReadOnlyList<Corrida> corridas = CorridasEstribos.PorZonas(e.Posiciones, DistribuidorEstribos.Zonas(t, OpcionesTablero.PorDefecto.Estribos, 9.5));

            List<double> rehechas = corridas.SelectMany(x => Enumerable.Range(0, x.Cantidad).Select(k => x.Primero + k * x.Separacion)).ToList();
            Assert.Equal(e.Posiciones.Count, rehechas.Count);
            Assert.All(e.Posiciones.Zip(rehechas), z => Assert.Equal(z.First, z.Second, 1));
            if (t.Tipo == TipoTramo.Luz && corridas.Count == 3)
                Assert.Equal(corridas[0].Cantidad, corridas[2].Cantidad);
        }
    }

    [Fact]
    public void LasCorridasDeUnTramoRealDanSusMismasPosiciones()
    {
        PorticoCalculado c = CalculoPortico.Calcular(ArmadorVistaTests.Portico1(), OpcionesTablero.PorDefecto, Reglas.Generales)!;

        foreach (EstriboEnPortico e in c.Estribos.Estribos)
        {
            IReadOnlyList<Corrida> corridas = CorridasEstribos.Partir(e.Posiciones);

            List<double> rehechas = corridas.SelectMany(x => Enumerable.Range(0, x.Cantidad).Select(k => x.Primero + k * x.Separacion)).ToList();
            Assert.Equal(e.Posiciones.Count, rehechas.Count);
            Assert.All(e.Posiciones.Zip(rehechas), z => Assert.Equal(z.First, z.Second, 1));
            Assert.True(corridas.Count <= 3, $"{e.Tramo}: una luz tiene a lo más zona, centro y zona; salieron {corridas.Count}");
        }
    }
}
