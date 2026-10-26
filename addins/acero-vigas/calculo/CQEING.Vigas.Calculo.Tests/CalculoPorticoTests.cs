using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
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

    /// <summary>Una vigueta de 10 × 35 con 1 #4 arriba y abajo y recubrimiento 2.5 (#941).</summary>
    private static PorticoLeido Vigueta(Elemento elemento = Elemento.Vigueta)
    {
        PorticoLeido p = ArmadorVistaTests.Portico1();
        return p with { Elemento = elemento, Tramos = p.Tramos.Select(t => t with { Ancho = 100, Alto = 350 }).ToList() };
    }

    private static readonly OpcionesTablero UnaBarra = OpcionesTablero.PorDefecto with
    {
        Superior = [new GrupoBarras(1, 4)], Inferior = [new GrupoBarras(1, 4)], RecubrimientoCm = 2.5,
    };

    [Fact]
    public void UnaViguetaConUnaBarraPorCara_SePuedeGenerar()
    {
        PorticoCalculado c = CalculoPortico.Calcular(Vigueta(), UnaBarra, Reglas.Generales)!;

        Assert.True(c.SePuedeGenerar);
        Assert.Equal(ErrorCara.Ninguno, c.Estribos.Seccion.Superior.Error);
    }

    [Fact]
    public void UnaVigaConUnaBarraPorCara_SigueSinPoderse()
    {
        Assert.False(CalculoPortico.Calcular(Vigueta(Elemento.Viga), UnaBarra, Reglas.Generales)!.SePuedeGenerar);
    }

    [Fact]
    public void LoQueCorrigeElUsuario_DecideSiPermiteUnaBarra()
    {
        // #940: el chip "Vigueta" manda sobre lo detectado.
        Assert.True(CalculoPortico.Calcular(Vigueta(Elemento.Viga), UnaBarra with { Elemento = Elemento.Vigueta }, Reglas.Generales)!.SePuedeGenerar);
        Assert.False(CalculoPortico.Calcular(Vigueta(), UnaBarra with { Elemento = Elemento.Viga }, Reglas.Generales)!.SePuedeGenerar);
    }

    [Fact]
    public void DiametroSinReglas()
    {
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(3, 9)] };

        Assert.Null(CalculoPortico.Calcular(ArmadorVistaTests.Portico1(), o, Reglas.Generales));
    }
}
