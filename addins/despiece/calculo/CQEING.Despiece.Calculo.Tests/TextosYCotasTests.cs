namespace CQEING.Despiece.Calculo.Tests;

public class TextosDespieceTests
{
    [Fact]
    public void LaBarraVaEnUnaLineaConBarra() => Assert.Equal("3 #5 / L= 6.00", TextosDespiece.Barra("#5 CQEING", 3, 6.0));

    [Fact]
    public void ElEstriboDiceCantidadLargoYGancho() => Assert.Equal("46 #3 / L= 0.99 / G= 0.08", TextosDespiece.Estribo("#3 CQEING", 46, 0.9898, 0.0845));

    [Fact]
    public void SinNumeroEnElTipoUsaElNombreDelTipo() => Assert.Equal("1 16 B 400 S / L= 2.00", TextosDespiece.Barra("16 B 400 S", 1, 2.0));

    [Theory]
    [InlineData("#5 CQEING", 5)]
    [InlineData("Barra #12", 12)]
    [InlineData("16 B 400 S", 0)]
    public void ElNumeroSaleDelNombre(string tipo, int numero) => Assert.Equal(numero, TextosDespiece.Numero(tipo));
}

public class TramosVigaTests
{
    [Fact]
    public void AlternaColumnasYLuces()
    {
        var tramos = TramosViga.Calcular(0, 10, [(-0.1, 0.3), (4.7, 5.0), (9.8, 10.1)]);

        Assert.Equal(
        [
            new TramoViga(-0.1, 0.3, true), new TramoViga(0.3, 4.7, false), new TramoViga(4.7, 5.0, true),
            new TramoViga(5.0, 9.8, false), new TramoViga(9.8, 10.1, true),
        ], tramos);
    }

    [Fact]
    public void SinColumnasEsUnaSolaLuz() => Assert.Equal([new TramoViga(0, 6, false)], TramosViga.Calcular(0, 6, []));

    [Fact]
    public void ColumnasEnElMismoSitioSeUnen() =>
        Assert.Equal([new TramoViga(0, 1, false), new TramoViga(1, 2, true), new TramoViga(2, 3, false)], TramosViga.Calcular(0, 3, [(1.5, 2), (1, 1.6)]));

    [Fact]
    public void UnaColumnaFueraDeLaVigaNoCuenta() => Assert.Equal([new TramoViga(0, 6, false)], TramosViga.Calcular(0, 6, [(7, 8)]));

    [Fact]
    public void LosTramosCubrenTodoSinHuecos()
    {
        var tramos = TramosViga.Calcular(0, 12, [(3, 3.5), (0, 0.25), (8, 8.4), (11.8, 12.2)]);

        for (int i = 1; i < tramos.Count; i++)
            Assert.Equal(tramos[i - 1].Hasta, tramos[i].Desde);
        Assert.Equal(0, tramos[0].Desde);
        Assert.Equal(12.2, tramos[^1].Hasta);
    }
}

public class ClasificadorBastonesTests
{
    // Medidas reales de la viga 388561 (hallazgos del plan), en metros.
    private static readonly double[] Alturas = [3.64, 3.64, 3.625, 3.41, 3.41, 3.425];

    [Theory]
    [InlineData(3.625, true)]
    [InlineData(3.425, true)]
    [InlineData(3.64, false)]
    [InlineData(3.41, false)]
    public void LoQueEstaMasAdentroDeLaCapaExteriorEsBaston(double altura, bool esBaston) =>
        Assert.Equal(esBaston, ClasificadorBastones.EsBaston(altura, Alturas, tolerancia: 0.005));

    [Fact]
    public void UnaSolaCapaNoTieneBastones() => Assert.False(ClasificadorBastones.EsBaston(3.41, [3.41, 3.41, 3.64, 3.64], 0.005));
}

public class AcomodadorFilasTests
{
    private const double HuecoEnFila = 0.2, HuecoFilas = 0.15;

    private static void SinEncimarse(IReadOnlyList<PiezaFila> piezas, Acomodo a)
    {
        Assert.Equal(piezas.Count, a.Piezas.Count);
        var cajas = a.Piezas.Select(u => (u.Pieza.Izquierda, u.Pieza.Derecha, Abajo: u.Piso, Arriba: u.Piso + u.Pieza.Alto)).ToList();
        for (int i = 0; i < cajas.Count; i++)
            for (int j = i + 1; j < cajas.Count; j++)
            {
                var (p, q) = (cajas[i], cajas[j]);
                bool enX = p.Izquierda < q.Derecha && q.Izquierda < p.Derecha;
                bool enZ = p.Abajo < q.Arriba && q.Abajo < p.Arriba;
                Assert.False(enX && enZ, $"se enciman {p} y {q}");
            }
    }

    [Fact]
    public void ElTextoAnchoDeUnaBarraCortaEnsanchaLaPieza()
    {
        PiezaFila p = PiezaFila.ConTexto(0, 4, 5, 0.1, anchoTexto: 3, altoTexto: 0.12, separacion: 0.03);

        Assert.Equal(3, p.Izquierda);
        Assert.Equal(6, p.Derecha);
        Assert.Equal(0.25, p.Alto, 6);
    }

    [Fact]
    public void ElTextoQuedaPegadoALaBarra()
    {
        PiezaFila p = PiezaFila.ConTexto(0, 0, 5, 0.1, 1, 0.12, 0.03);
        var u = AcomodadorFilas.Acomodar([p], 10, true, HuecoEnFila, HuecoFilas).Piezas[0];

        Assert.Equal(10, u.Piso);
        Assert.Equal(10 + 0.1 + 0.03, u.BaseTexto, 6);
        Assert.Equal(10 + 0.1 + 0.03 + 0.12, u.TopeTexto, 6);
    }

    [Fact]
    public void DosBarrasQueNoSeTocanVanEnLaMismaFila()
    {
        PiezaFila a = new(0, 0, 2, 0.3), b = new(1, 3, 5, 0.3);

        var r = AcomodadorFilas.Acomodar([a, b], 0, true, HuecoEnFila, HuecoFilas);

        Assert.All(r.Piezas, u => Assert.Equal(0, u.Piso));
    }

    [Fact]
    public void DosTextosQueSeTocanVanEnFilasDistintas()
    {
        PiezaFila[] p = [PiezaFila.ConTexto(0, 0, 1, 0.1, 2, 0.12, 0.03), PiezaFila.ConTexto(1, 1.1, 2.1, 0.1, 2, 0.12, 0.03)];

        SinEncimarse(p, AcomodadorFilas.Acomodar(p, 0, true, HuecoEnFila, HuecoFilas));
    }

    [Fact]
    public void HaciaAbajoLasFilasBajanDesdeElPunto()
    {
        PiezaFila[] p = [new(0, 0, 5, 0.3), new(1, 1, 4, 0.3)];

        var r = AcomodadorFilas.Acomodar(p, 0, false, HuecoEnFila, HuecoFilas);

        SinEncimarse(p, r);
        Assert.All(r.Piezas, u => Assert.True(u.Piso + u.Pieza.Alto <= 0));
        Assert.True(r.Hasta <= r.Piezas.Min(u => u.Piso));
    }

    [Fact]
    public void AlCentrarQuedaEnElMedioYDiceHastaDondeLlega()
    {
        PiezaFila[] p = [new(0, 0, 5, 0.3), new(1, 1, 4, 0.3)];

        var r = AcomodadorFilas.Centrar(p, 2, HuecoEnFila, HuecoFilas);

        SinEncimarse(p, r);
        Assert.Equal(2 - r.Desde, r.Hasta - 2, 6);
        Assert.Equal(r.Desde, r.Piezas.Min(u => u.Piso), 6);
        Assert.Equal(r.Hasta, r.Piezas.Max(u => u.Piso + u.Pieza.Alto), 6);
    }

    [Fact]
    public void SinPiezasNoOcupaNada()
    {
        Assert.Equal(7, AcomodadorFilas.Acomodar([], 7, true, HuecoEnFila, HuecoFilas).Hasta);
        var c = AcomodadorFilas.Centrar([], 7, HuecoEnFila, HuecoFilas);
        Assert.Equal(7, c.Desde);
        Assert.Equal(7, c.Hasta);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AlAzarNuncaSeEncimanNiDejanElHuecoEnFila(int semilla)
    {
        var azar = new Random(semilla);
        List<PiezaFila> p = Enumerable.Range(0, 60).Select(i =>
        {
            double izq = azar.NextDouble() * 20, largo = 0.5 + azar.NextDouble() * 6;
            return PiezaFila.ConTexto(i, izq, izq + largo, 0.05 + azar.NextDouble() * 0.3, 0.8 + azar.NextDouble(), 0.12, 0.03);
        }).ToList();

        foreach (Acomodo r in new[] { AcomodadorFilas.Acomodar(p, 0, true, HuecoEnFila, HuecoFilas), AcomodadorFilas.Acomodar(p, 0, false, HuecoEnFila, HuecoFilas), AcomodadorFilas.Centrar(p, 0, HuecoEnFila, HuecoFilas) })
        {
            SinEncimarse(p, r);
            foreach (var fila in r.Piezas.GroupBy(u => u.Fila))
            {
                var orden = fila.OrderBy(u => u.Pieza.Izquierda).ToList();
                for (int i = 1; i < orden.Count; i++)
                    Assert.True(orden[i].Pieza.Izquierda >= orden[i - 1].Pieza.Derecha + HuecoEnFila);
            }
        }
    }

    [Fact]
    public void LosDetallesDeEstribosSeApilanSinEncimarse()
    {
        var y = AcomodadorFilas.Apilar([0.4, 0.2, 0.3], 1.0, 0.1);

        Assert.Equal(0.6, y[0], 6);
        Assert.Equal(0.3, y[1], 6);
        Assert.Equal(-0.1, y[2], 6);
    }
}
