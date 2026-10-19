using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// El cálculo de un pórtico que comparten la vista del tablero y la generación en Revit (#85): lo que se genera tiene
/// que ser lo mismo que se ve.
/// </summary>
public class CalculoPorticoTests
{
    [Fact]
    public void MismoResultadoQueLaVista()
    {
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(3, 5), new GrupoBarras(2, 4)] };

        PorticoCalculado c = CalculoPortico.Calcular(ArmadorVistaTests.Portico1(), o, Reglas.Generales)!;
        VistaPortico v = ArmadorVista.Armar(ArmadorVistaTests.Portico1(), o, Reglas.Generales);

        Assert.Equal(v.Kg, c.Vigas.Kg + c.Estribos.Kg, 6);
        Assert.Equal(v.SePuedeGenerar, c.SePuedeGenerar);
        Assert.True(c.SePuedeGenerar);
        Assert.Equal(v.Barras.Select(b => b.Id), c.Vigas.Barras.Select(b => b.Id));
        Assert.Equal(v.Estribos, c.Estribos.Cantidad);
    }

    [Fact]
    public void NoSePuedeGenerarSiLasBarrasNoCaben()
    {
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(30, 8)] };

        PorticoCalculado c = CalculoPortico.Calcular(ArmadorVistaTests.Portico1(), o, Reglas.Generales)!;

        Assert.False(c.SePuedeGenerar);
        Assert.Equal(ArmadorVista.Armar(ArmadorVistaTests.Portico1(), o, Reglas.Generales).SePuedeGenerar, c.SePuedeGenerar);
    }

    [Fact]
    public void DiametroSinReglas()
    {
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(3, 9)] };

        Assert.Null(CalculoPortico.Calcular(ArmadorVistaTests.Portico1(), o, Reglas.Generales));
    }
}
