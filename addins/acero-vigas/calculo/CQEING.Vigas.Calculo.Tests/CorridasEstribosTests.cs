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
