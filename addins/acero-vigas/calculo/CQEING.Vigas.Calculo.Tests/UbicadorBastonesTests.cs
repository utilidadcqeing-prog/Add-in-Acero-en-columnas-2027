using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Seccion;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Puesto de los bastones en la sección de la maqueta (#89): 30 × 50 cm, recubrimiento 4 cm, estribo #3 y barras #5.
/// Caben 5 por fila; los puestos de la fila llena van en 57.45, 103.725, 150, 196.275 y 242.55. Un #5 entre dos #5
/// necesita 7.95 + 25 + 15.9 + 25 + 7.95 = 81.8 mm entre centros. La #5 de esquina de la fila 1 se apoya en la curva del
/// estribo y entra EnCurva5 de lado y de alto (21-oct): queda en 60.686 y la fila 2 baja lo mismo.
/// </summary>
public class UbicadorBastonesTests
{
    private const double Precision = 1e-6;
    private static readonly double EnCurva5 = (19 - 7.95) * (1 - 1 / Math.Sqrt(2));

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
        Assert.Equal((57.45 + EnCurva5 + 150) / 2, b.Barra.X, Precision);
        Assert.Equal(57.45, b.Barra.Y, Precision);
    }

    [Fact]
    public void DosBastones_UnoEnCadaExtremo()
    {
        UbicacionBastones u = Abajo(Maqueta([new GrupoBarras(3, 5)]), new GrupoBarras(2, 5));

        Assert.Equal([Math.Round((57.45 + EnCurva5 + 150) / 2, 6), Math.Round((150 + 242.55 - EnCurva5) / 2, 6)], u.Barras.Select(b => Math.Round(b.Barra.X, 6)));
        Assert.All(u.Barras, b => Assert.Equal(1, b.Fila));
    }

    [Fact]
    public void EnUnaVigueta_ElBastonVaPorDentroDeLaCorrida()
    {
        // Carlos (24-oct, min 08:11): en la vigueta de 10 cm no cabe al lado; va en la fila 2, encima de la de abajo.
        var vigueta = new EntradaSeccion(Ancho: 100, Alto: 350, Recubrimientos.Iguales(25), DiametroEstribo: 3,
            Superior: [new GrupoBarras(1, 4)], Inferior: [new GrupoBarras(1, 4)], Reglas.Generales, PermiteUnaBarra: true);

        UbicacionBastones u = Abajo(vigueta, new GrupoBarras(1, 4));

        BastonEnSeccion b = Assert.Single(u.Barras);
        Assert.Equal((ErrorCara.Ninguno, 2), (u.Error, b.Fila));
        Assert.Equal(50, b.Barra.X, Precision);
    }

    [Fact]
    public void EnUnaVigueta_SiLaFila2LaOcupaUnTraslapo_ElBastonVaEncimaEnLaFila3()
    {
        // Encontrado al planificar una vigueta (#941): con un solo puesto por fila, la barra que traslapa por dentro
        // ocupa la fila 2; la fila quedaba vacía y su altura reventaba.
        var vigueta = new EntradaSeccion(Ancho: 100, Alto: 350, Recubrimientos.Iguales(25), DiametroEstribo: 3,
            Superior: [new GrupoBarras(1, 4)], Inferior: [new GrupoBarras(1, 4)], Reglas.Generales, PermiteUnaBarra: true);
        // la barra que traslapa queda donde iría una segunda fila de la corrida
        BarraEnSeccion traslapo = RepartidorFilas.Repartir(vigueta with { Inferior = [new GrupoBarras(1, 4, 1), new GrupoBarras(1, 4, 2)] })
            .Inferior.Filas[1][0];

        UbicacionBastones u = UbicadorBastones.Ubicar(vigueta, Posicion.Inferior, [new GrupoBarras(1, 4)], [traslapo]);

        BastonEnSeccion b = Assert.Single(u.Barras);
        Assert.Equal((ErrorCara.Ninguno, 3), (u.Error, b.Fila));
        Assert.Equal(traslapo.Y + 12.7, b.Barra.Y, Precision);
    }

    [Fact]
    public void ConLaFila1Llena_VaALaFila2EnElPuestoDelExtremo()
    {
        BastonEnSeccion b = Assert.Single(Abajo(Maqueta([new GrupoBarras(5, 5)]), new GrupoBarras(1, 5)).Barras);

        Assert.Equal(2, b.Fila);
        Assert.Equal(57.45, b.Barra.X, Precision);
        Assert.Equal(73.35 + EnCurva5, b.Barra.Y, Precision);
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

        Assert.Equal(83.35 + EnCurva5, b.Barra.Y, Precision);
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
        // el hueco entre la esquina (60.686) y el centro es de 89.31 entre centros: un #6 pide 7.95 + 25 + 19.1 + 25 + 7.95
        // = 85 y cabe; un #8 pide 91.3 y ya no (antes de apoyar la esquina en la curva, el hueco era de 92.55)
        BastonEnSeccion seis = Assert.Single(Abajo(Maqueta([new GrupoBarras(3, 5)]), new GrupoBarras(1, 6)).Barras);
        BastonEnSeccion ocho = Assert.Single(Abajo(Maqueta([new GrupoBarras(3, 5)]), new GrupoBarras(1, 8)).Barras);

        Assert.Equal((1, 6), (seis.Fila, seis.Barra.Diametro));
        Assert.Equal((57.45 + EnCurva5 + 150) / 2, seis.Barra.X, Precision);
        Assert.Equal((2, 8), (ocho.Fila, ocho.Barra.Diametro));
    }

    /// <summary>
    /// Alto 170: las corridas dejan 170 − 65.4 − 65.4 = 39.2 mm libres entre caras, pero el bastón abre la fila 2
    /// abajo y deja 170 − 65.4 − 81.3 = 23.3 &lt; 25 mm.
    /// </summary>
    [Fact]
    public void SiLaFilaNuevaChocaConLaOtraCara_ErrorNoCabenEnElAlto()
    {
        EntradaSeccion baja = Maqueta([new GrupoBarras(5, 5)]) with { Alto = 170 };

        UbicacionBastones u = Abajo(baja, new GrupoBarras(1, 5));

        Assert.Equal(ErrorCara.NoCabenEnElAlto, u.Error);
        Assert.Empty(u.Barras);
    }

    [Fact]
    public void UnBastonDel8EntreDosDel8NoCabe()
    {
        BastonEnSeccion b = Assert.Single(Abajo(Maqueta([new GrupoBarras(3, 8)]), new GrupoBarras(1, 8)).Barras);

        Assert.Equal(2, b.Fila);
        Assert.Equal(62.2, b.Barra.X, Precision);
    }
}
