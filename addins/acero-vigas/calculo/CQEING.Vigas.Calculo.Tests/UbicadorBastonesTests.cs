using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Seccion;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Puesto de los bastones en la sección de la maqueta (#89): 30 × 50 cm, recubrimiento 4 cm, estribo #3 y barras #5.
/// Caben 5 por fila; los puestos de la fila llena van en 57.45, 103.725, 150, 196.275 y 242.55. Un #5 entre dos #5
/// necesita 7.95 + 25 + 15.9 + 25 + 7.95 = 81.8 mm entre centros.
/// </summary>
public class UbicadorBastonesTests
{
    private const double Precision = 1e-6;

    private static EntradaSeccion Maqueta(IReadOnlyList<GrupoBarras> inferior, double separacion = 0, int maximoFilas = 3) =>
        new(Ancho: 300, Alto: 500, Recubrimientos.Iguales(40), DiametroEstribo: 3,
            Superior: [new GrupoBarras(3, 5)], Inferior: inferior, Reglas.Generales, separacion, maximoFilas);

    private static UbicacionBastones Abajo(EntradaSeccion e, params GrupoBarras[] bastones) =>
        UbicadorBastones.Ubicar(e, Posicion.Inferior, bastones);

    [Fact]
    public void ConTresBarras_ElBastonVaEnLaFila1EnElHuecoDeLaIzquierda()
    {
        UbicacionBastones u = Abajo(Maqueta([new GrupoBarras(3, 5)]), new GrupoBarras(1, 5));

        BastonEnSeccion b = Assert.Single(u.Barras);
        Assert.Equal(ErrorCara.Ninguno, u.Error);
        Assert.Equal(1, b.Fila);
        Assert.Equal(103.725, b.Barra.X, Precision);
        Assert.Equal(57.45, b.Barra.Y, Precision);
    }

    [Fact]
    public void DosBastones_UnoEnCadaExtremo()
    {
        UbicacionBastones u = Abajo(Maqueta([new GrupoBarras(3, 5)]), new GrupoBarras(2, 5));

        Assert.Equal([103.725, 196.275], u.Barras.Select(b => Math.Round(b.Barra.X, 6)));
        Assert.All(u.Barras, b => Assert.Equal(1, b.Fila));
    }

    [Fact]
    public void ConLaFila1Llena_VaALaFila2EnElPuestoDelExtremo()
    {
        BastonEnSeccion b = Assert.Single(Abajo(Maqueta([new GrupoBarras(5, 5)]), new GrupoBarras(1, 5)).Barras);

        Assert.Equal(2, b.Fila);
        Assert.Equal(57.45, b.Barra.X, Precision);
        Assert.Equal(73.35, b.Barra.Y, Precision);
    }

    [Fact]
    public void ConSieteBarras_VaAlPrimerPuestoLibreDeLaFila2()
    {
        BastonEnSeccion b = Assert.Single(Abajo(Maqueta([new GrupoBarras(7, 5)]), new GrupoBarras(1, 5)).Barras);

        Assert.Equal(2, b.Fila);
        Assert.Equal(103.725, b.Barra.X, Precision);
    }

    [Fact]
    public void ConCuatroBarras_ElHuecoNoAlcanzaYVaALaFila2()
    {
        BastonEnSeccion b = Assert.Single(Abajo(Maqueta([new GrupoBarras(4, 5)]), new GrupoBarras(1, 5)).Barras);

        Assert.Equal(2, b.Fila);
        Assert.Equal(57.45, b.Barra.X, Precision);
    }

    [Fact]
    public void ConFilasSeparadas_SeSumaLaSeparacion()
    {
        BastonEnSeccion b = Assert.Single(Abajo(Maqueta([new GrupoBarras(5, 5)], separacion: 10), new GrupoBarras(1, 5)).Barras);

        Assert.Equal(83.35, b.Barra.Y, Precision);
    }

    [Fact]
    public void SinLugarEnElMaximoDeFilas_ErrorNoCaben()
    {
        UbicacionBastones u = Abajo(Maqueta([new GrupoBarras(5, 5)], maximoFilas: 1), new GrupoBarras(1, 5));

        Assert.Equal(ErrorCara.NoCaben, u.Error);
        Assert.Empty(u.Barras);
    }

    [Fact]
    public void ArribaSeMideDesdeLaCaraDeArriba()
    {
        UbicacionBastones u = UbicadorBastones.Ubicar(Maqueta([new GrupoBarras(3, 5)]), Posicion.Superior, [new GrupoBarras(1, 5)]);

        Assert.Equal(500 - 57.45, Assert.Single(u.Barras).Barra.Y, Precision);
    }

    [Fact]
    public void UnBastonMasGruesoMideElHuecoConSuDiametro()
    {
        BastonEnSeccion b = Assert.Single(Abajo(Maqueta([new GrupoBarras(3, 5)]), new GrupoBarras(1, 8)).Barras);

        Assert.Equal((1, 8), (b.Fila, b.Barra.Diametro));
        Assert.Equal(103.725, b.Barra.X, Precision);
    }

    [Fact]
    public void UnBastonDel8EntreDosDel8NoCabe()
    {
        BastonEnSeccion b = Assert.Single(Abajo(Maqueta([new GrupoBarras(3, 8)]), new GrupoBarras(1, 8)).Barras);

        Assert.Equal(2, b.Fila);
        Assert.Equal(62.2, b.Barra.X, Precision);
    }
}
