using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Seccion;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// La sección de la maqueta: 30 × 50 cm, recubrimiento libre de 4 cm, estribo #3 (9.5 mm) y barras #5 (15.9 mm).
/// Libre para barras: 300 − 2·40 − 2·9.5 = 201 mm → caben piso((201 + 25) / (15.9 + 25)) = 5 por fila.
/// La barra de esquina queda a 40 + 9.5 + 7.95 = 57.45 mm de cada cara.
/// </summary>
public class RepartidorFilasTests
{
    private static EntradaSeccion Maqueta(IReadOnlyList<GrupoBarras>? inferior = null, IReadOnlyList<GrupoBarras>? superior = null) =>
        new(Ancho: 300, Alto: 500, Recubrimientos.Iguales(40), DiametroEstribo: 3,
            Superior: superior ?? [new GrupoBarras(3, 5)], Inferior: inferior ?? [new GrupoBarras(3, 5)], Reglas.Generales);

    private static CaraSeccion Abajo(EntradaSeccion e) => RepartidorFilas.Repartir(e).Inferior;

    [Fact]
    public void EnLaSeccionDeLaMaqueta_Caben5PorFila()
    {
        CaraSeccion c = Abajo(Maqueta());

        Assert.Equal(5, c.CabenPorFila);
        Assert.Single(c.Filas);
        Assert.Equal(ErrorCara.Ninguno, c.Error);
    }

    [Fact]
    public void SieteBarras_Van5EnLaFila1Y2EnLaFila2()
    {
        CaraSeccion c = Abajo(Maqueta([new GrupoBarras(7, 5)]));

        Assert.Equal([5, 2], c.Filas.Select(f => f.Count));
    }

    [Fact]
    public void MasDeLasQueCabenEnLasTresFilas_ErrorConElMaximo()
    {
        CaraSeccion c = Abajo(Maqueta([new GrupoBarras(16, 5)]));

        Assert.Equal((ErrorCara.NoCaben, 15), (c.Error, c.Maximo));
        Assert.Equal(4, c.Filas.Count);
    }

    [Fact]
    public void ElMaximoDeFilasSeConfigura()
    {
        CaraSeccion c = Abajo(Maqueta([new GrupoBarras(16, 5)]) with { MaximoFilas = 4 });

        Assert.Equal((ErrorCara.Ninguno, 20), (c.Error, c.Maximo));
        Assert.Equal([5, 5, 5, 1], c.Filas.Select(f => f.Count));
    }

    [Fact]
    public void UnaSolaBarra_ElEstriboCerradoNoSePuedeArmar()
    {
        Assert.Equal(ErrorCara.MenosDeDosBarras, Abajo(Maqueta([new GrupoBarras(1, 5)])).Error);
    }

    [Fact]
    public void UnaVigaDondeNoCabeNinguna_ErrorConMaximoCero()
    {
        CaraSeccion c = Abajo(Maqueta() with { Ancho = 100 });

        Assert.Equal((0, ErrorCara.NoCaben, 0), (c.CabenPorFila, c.Error, c.Maximo));
    }

    [Fact]
    public void LaFila1VaDeEsquinaAEsquinaALaMismaDistancia()
    {
        CaraSeccion c = Abajo(Maqueta([new GrupoBarras(5, 5)]));

        IReadOnlyList<BarraEnSeccion> fila = c.Filas[0];
        Assert.Equal([57.45, 103.725, 150, 196.275, 242.55], fila.Select(b => Math.Round(b.X, 3)));
        Assert.All(fila, b => Assert.Equal(57.45, b.Y, precision: 6));
        Assert.All(fila.Zip(fila.Skip(1)), par => Assert.True(par.Second.X - par.First.X - 15.9 >= 25));
    }

    [Fact]
    public void LaFila2VaDebajoDeLasEsquinas_PegadaALaFila1()
    {
        CaraSeccion c = Abajo(Maqueta([new GrupoBarras(7, 5)]));

        Assert.Equal([57.45, 242.55], c.Filas[1].Select(b => Math.Round(b.X, 3)));
        Assert.All(c.Filas[1], b => Assert.Equal(57.45 + 15.9, b.Y, precision: 6));
    }

    [Fact]
    public void ConFilasSeparadas_SeSumaLaSeparacion()
    {
        CaraSeccion c = Abajo(Maqueta([new GrupoBarras(7, 5)]) with { SeparacionFilas = 10 });

        Assert.All(c.Filas[1], b => Assert.Equal(57.45 + 15.9 + 10, b.Y, precision: 6));
    }

    [Fact]
    public void LaCaraDeArribaSeMideDesdeArriba()
    {
        CaraSeccion arriba = RepartidorFilas.Repartir(Maqueta()).Superior;

        Assert.All(arriba.Filas[0], b => Assert.Equal(500 - 57.45, b.Y, precision: 6));
    }

    [Fact]
    public void ElRecubrimientoPorCaraCambiaSoloEsaCara()
    {
        ResultadoSeccion r = RepartidorFilas.Repartir(Maqueta() with { Recubrimiento = new Recubrimientos(Arriba: 40, Abajo: 60, Lados: 40) });

        Assert.Equal(77.45, r.Inferior.RecubrimientoAlEje, precision: 6);
        Assert.Equal(57.45, r.Superior.RecubrimientoAlEje, precision: 6);
        Assert.All(r.Inferior.Filas[0], b => Assert.Equal(77.45, b.Y, precision: 6));
    }

    [Fact]
    public void ConDosDiametros_LasEsquinasSonDelPrimeroYSeReparteSimetrico()
    {
        CaraSeccion c = Abajo(Maqueta([new GrupoBarras(3, 5), new GrupoBarras(2, 4)]));

        Assert.Equal(5, c.CabenPorFila);
        Assert.Equal([5, 4, 5, 4, 5], c.Filas[0].OrderBy(b => b.X).Select(b => b.Diametro));
    }

    [Fact]
    public void ConDosDiametros_CadaBarraApoyaEnElEstribo()
    {
        CaraSeccion c = Abajo(Maqueta([new GrupoBarras(3, 5), new GrupoBarras(2, 4)]));

        BarraEnSeccion numero4 = c.Filas[0].First(b => b.Diametro == 4);
        Assert.Equal(40 + 9.5 + 12.7 / 2, numero4.Y, precision: 6);
    }

    [Fact]
    public void FilasDeArribaYDeAbajoQueSePisan_ErrorDeAlto()
    {
        ResultadoSeccion r = RepartidorFilas.Repartir(
            Maqueta([new GrupoBarras(15, 5)], superior: [new GrupoBarras(15, 5)]) with { Alto = 150 });

        Assert.Equal(ErrorCara.NoCabenEnElAlto, r.Inferior.Error);
        Assert.Equal(ErrorCara.NoCabenEnElAlto, r.Superior.Error);
    }

    // Filas a mano (#84, decisión 11): primero los grupos con fila fija, en su fila; los Auto llenan lo libre desde la 1.

    [Fact]
    public void TresDe5EnLaFila1YTresDe4EnLa2()
    {
        CaraSeccion c = Abajo(Maqueta([new GrupoBarras(3, 5, 1), new GrupoBarras(3, 4, 2)]));

        Assert.Equal(ErrorCara.Ninguno, c.Error);
        Assert.Equal([[5, 5, 5], [4, 4, 4]], c.Filas.Select(f => f.Select(b => b.Diametro).ToArray()));
    }

    [Fact]
    public void LosAutoLlenanLosPuestosLibresDesdeLaFila1()
    {
        CaraSeccion c = Abajo(Maqueta([new GrupoBarras(2, 5, 2), new GrupoBarras(4, 4)]));

        Assert.Equal([[4, 4, 4, 4], [5, 5]], c.Filas.Select(f => f.Select(b => b.Diametro).ToArray()));
        // la esquina es del primer grupo de la fila 1: el #4
        Assert.Equal(40 + 9.5 + 12.7 / 2, c.Filas[0][0].X, 3);
    }

    [Fact]
    public void UnaFilaFijaConMasDeLasQueCabenEsNoCaben()
    {
        Assert.Equal(ErrorCara.NoCaben, Abajo(Maqueta([new GrupoBarras(6, 5, 1)])).Error);
    }

    [Fact]
    public void UnaFilaMayorQueElMaximoEsNoCaben()
    {
        Assert.Equal(ErrorCara.NoCaben, Abajo(Maqueta([new GrupoBarras(3, 5), new GrupoBarras(2, 5, 4)])).Error);
    }

    [Fact]
    public void LaFila1ConMenosDeDosEsMenosDeDosBarras()
    {
        Assert.Equal(ErrorCara.MenosDeDosBarras, Abajo(Maqueta([new GrupoBarras(1, 5, 1), new GrupoBarras(3, 5, 2)])).Error);
    }
}
