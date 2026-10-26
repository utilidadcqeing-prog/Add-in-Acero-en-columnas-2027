using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Del marco del pórtico (x desde el eje A; X e Y del corte) a las coordenadas del modelo, en mm (#85). La cara
/// izquierda del corte es la de la izquierda mirando hacia +x.
/// </summary>
public class MarcoPorticoTests
{
    private static UbicacionPortico EnX(params VigaEnPortico[] vigas) =>
        new(1, 0, 1000, 2000, 3000, 3500, 300, 500, vigas);

    [Fact]
    public void EnX_LaCaraIzquierdaEsLaDeMasY()
    {
        UbicacionPortico u = EnX();

        Assert.Equal(new Punto3(1500, 2150, 3000), MarcoPortico.AlModelo(u, 500, 0, 0));
        Assert.Equal(new Punto3(1500, 1850, 3450), MarcoPortico.AlModelo(u, 500, 300, 450));
        Assert.Equal(new Punto3(1000, 2000, 3250), MarcoPortico.AlModelo(u, 0, 150, 250));
    }

    [Fact]
    public void EnDiagonal_ElCentroDelCorteQuedaSobreLaLinea()
    {
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var u = new UbicacionPortico(c, s, 1000, 2000, 3000, 3500, 300, 500, []);

        Punto3 p = MarcoPortico.AlModelo(u, 1000, 150, 0);

        Assert.Equal(1000 + 1000 * c, p.X, 2);
        Assert.Equal(2000 + 1000 * s, p.Y, 2);
        // la cara izquierda queda a 150 mm a la izquierda de la dirección (−s, c)
        Punto3 izquierda = MarcoPortico.AlModelo(u, 1000, 0, 0);
        Assert.Equal(p.X - 150 * s, izquierda.X, 2);
        Assert.Equal(p.Y + 150 * c, izquierda.Y, 2);
    }

    [Fact]
    public void DelModeloAlCorte_EsLaVueltaDeAlModelo()
    {
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var u = new UbicacionPortico(c, s, 1000, 2000, 3000, 3500, 300, 500, []);

        (double x, double y) = MarcoPortico.EnCorte(u, MarcoPortico.AlModelo(u, 700, 42.5, 310));

        Assert.Equal(42.5, x, 6);
        Assert.Equal(310, y, 6);
    }

    [Fact]
    public void LaVigaDeUnPunto_EsLaQueLoContieneOLaMasCercana()
    {
        UbicacionPortico u = EnX(new VigaEnPortico("v1", 0, 4000), new VigaEnPortico("v2", 4400, 8000));

        Assert.Equal("v1", MarcoPortico.VigaEn(u, 2000));
        Assert.Equal("v2", MarcoPortico.VigaEn(u, 4300));
        Assert.Equal("v1", MarcoPortico.VigaEn(u, 4100));
        Assert.Equal("v2", MarcoPortico.VigaEn(u, 9000));
        Assert.Null(MarcoPortico.VigaEn(EnX(), 100));
    }
}
