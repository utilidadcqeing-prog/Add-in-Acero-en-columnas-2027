namespace CQEING.Despiece.Calculo.Tests;

public class TipoElementoTests
{
    [Theory]
    [InlineData("VT_10X35", 100, true)]
    [InlineData("VB_10X35", 100, true)]
    [InlineData("Vigueta 30x35", 300, true)]
    [InlineData("vigueta", 300, true)]
    [InlineData("VIG_25X35", 250, false)]
    [InlineData("VIG_20X35", 200, false)]
    [InlineData("Tipo raro", 150, true)]
    public void ReconoceLasViguetas(string tipo, int anchoMm, bool esVigueta) => Assert.Equal(esVigueta, TipoElemento.EsVigueta(tipo, anchoMm));

    [Fact]
    public void LaViguetaSeNombraDistintoQueLaViga()
    {
        var grupos = AgrupadorIguales.Agrupar(
        [
            new ElementoLeido("1", "A", 100, 350, 7000, [new BarraLeida("#4", "00", 1, 4000, 2000)], EsVigueta: true),
            new ElementoLeido("2", "A", 250, 350, 7000, [new BarraLeida("#5", "00", 3, 6000, 3000)]),
        ]);

        Assert.Contains(grupos, g => g.Nombre == "Vigueta A - (0.10x0.35)");
        Assert.Contains(grupos, g => g.Nombre == "V A - (0.25x0.35)");
    }

    [Fact]
    public void SinEjeTambienDiceVigueta()
    {
        var grupos = AgrupadorIguales.Agrupar([new ElementoLeido("1", "", 100, 350, 7000, [], EsVigueta: true)]);

        Assert.Equal("Vigueta - (0.10x0.35)", Assert.Single(grupos).Nombre);
    }
}
