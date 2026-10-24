using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Las barras iguales de una fila van en un solo conjunto de Revit (Santiago, 24-oct: que tres barras se etiqueten
/// "3 #7" y no "1 #7" tres veces). Cada barra del conjunto conserva su punto del corte.
/// </summary>
public class AgrupadorBarrasTests
{
    private static PiezaLongitudinal Pieza(double x, double y = 61, string pieza = "S1", int fila = 1, int diametro = 5,
        int desde = 0, int hasta = 3000, TipoGancho inicio = TipoGancho.De90, TipoGancho fin = TipoGancho.Sin) =>
        new(pieza, Posicion.Superior, diametro, x, y, desde, hasta, inicio, fin, "101", hasta - desde + 278, fila);

    [Fact]
    public void TresBarrasIgualesDeUnaFila_UnSoloConjunto()
    {
        // arriba la del medio queda más alta: las de esquina se apoyan en la curva del estribo
        List<ConjuntoBarras> c = AgrupadorBarras.Agrupar([Pieza(61, 439.3), Pieza(125, 442.6), Pieza(189, 439.3)]);

        ConjuntoBarras unico = Assert.Single(c);
        Assert.Equal([61.0, 125.0, 189.0], unico.Piezas.Select(p => p.XCorte));
        Assert.Equal([439.3, 442.6, 439.3], unico.Piezas.Select(p => p.YCorte));
    }

    [Fact]
    public void LasOrdenaDeIzquierdaADerecha()
    {
        ConjuntoBarras c = Assert.Single(AgrupadorBarras.Agrupar([Pieza(189), Pieza(61), Pieza(125)]));

        Assert.Equal([61.0, 125.0, 189.0], c.Piezas.Select(p => p.XCorte));
        Assert.Same(c.Piezas[0], c.Primera);
    }

    [Theory]
    [InlineData("pieza")]
    [InlineData("fila")]
    [InlineData("diametro")]
    [InlineData("desde")]
    [InlineData("hasta")]
    [InlineData("inicio")]
    [InlineData("fin")]
    public void LoQueNoEsIgual_VaEnOtroConjunto(string cambia)
    {
        PiezaLongitudinal otra = cambia switch
        {
            "pieza" => Pieza(189, pieza: "S2"),
            "fila" => Pieza(189, fila: 2),
            "diametro" => Pieza(189, diametro: 4),
            "desde" => Pieza(189, desde: 100),
            "hasta" => Pieza(189, hasta: 2900),
            "inicio" => Pieza(189, inicio: TipoGancho.Sin),
            _ => Pieza(189, fin: TipoGancho.De90),
        };

        List<ConjuntoBarras> c = AgrupadorBarras.Agrupar([Pieza(61), Pieza(125), otra]);

        Assert.Equal(2, c.Count);
        Assert.Equal([61.0, 125.0], c[0].Piezas.Select(p => p.XCorte));
        Assert.Same(otra, Assert.Single(c[1].Piezas));
    }

    [Fact]
    public void LaOtraCapa_VaEnOtroConjunto()
    {
        PiezaLongitudinal abajo = Pieza(189) with { Capa = Posicion.Inferior };

        Assert.Equal(2, AgrupadorBarras.Agrupar([Pieza(61), abajo]).Count);
    }

    [Fact]
    public void DosEnLaMismaX_NoVanEnElMismoConjunto()
    {
        // Revit reparte el conjunto a lo ancho: dos barras en la misma x no se pueden repartir.
        List<ConjuntoBarras> c = AgrupadorBarras.Agrupar([Pieza(61), Pieza(125, 57), Pieza(125.5, 41), Pieza(189)]);

        Assert.Equal(2, c.Count);
        Assert.Equal([61.0, 125.0, 189.0], c[0].Piezas.Select(p => p.XCorte));
        Assert.Equal(125.5, Assert.Single(c[1].Piezas).XCorte);
    }

    [Fact]
    public void LosConjuntosSalenEnElOrdenDeSuPrimeraPieza()
    {
        List<ConjuntoBarras> c = AgrupadorBarras.Agrupar([Pieza(61, pieza: "S2"), Pieza(61), Pieza(125, pieza: "S2")]);

        Assert.Equal(["S2", "S1"], c.Select(x => x.Primera.Pieza));
    }

    [Fact]
    public void ElPorticoDeLaMaqueta_CadaBarraEsUnConjuntoDeTres()
    {
        PlanAcero plan = PlanificadorAcero.Planificar("Pórtico 1", PlanificadorAceroTests.Portico(), OpcionesTablero.PorDefecto, Reglas.Generales)!;

        List<ConjuntoBarras> c = AgrupadorBarras.Agrupar(plan.Barras);

        Assert.All(c, x => Assert.Equal(3, x.Piezas.Count));
        Assert.Equal(plan.Barras.Count, c.Sum(x => x.Piezas.Count));
    }

    [Fact]
    public void FilaDosConGanchosAnidados_LasDeEsquinaVanAparte()
    {
        // 4 #5 en la fila 1 y 3 #5 en la fila 2: las de esquina de la fila 2 van debajo de un gancho de la fila 1 y
        // terminan antes (AnidarGanchos); la del medio no tiene gancho encima y llega hasta la punta.
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(4, 5), new GrupoBarras(3, 5, 2)] };
        PlanAcero plan = PlanificadorAcero.Planificar("Pórtico 1", PlanificadorAceroTests.Portico(), o, Reglas.Generales)!;

        List<ConjuntoBarras> fila2 = AgrupadorBarras.Agrupar(plan.Barras).Where(x => x.Primera.Pieza == "S1" && x.Primera.Fila == 2).ToList();

        Assert.Equal([1, 2], fila2.Select(x => x.Piezas.Count).Order());
        Assert.NotEqual(fila2[0].Primera.Desde, fila2[1].Primera.Desde);
    }
}
