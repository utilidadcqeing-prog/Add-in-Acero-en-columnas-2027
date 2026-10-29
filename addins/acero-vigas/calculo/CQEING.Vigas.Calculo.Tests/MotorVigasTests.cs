using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Nucleo;
using CQEING.Vigas.Calculo.Vigas;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>Pórtico de la maqueta con la tabla real (varilla de 6 m), 3 #5 arriba y 3 #5 abajo.</summary>
public class MotorVigasTests
{
    private static EntradaVigas Aerea() => new(Datos.PorticoMaqueta(), TipoViga.Aerea,
        Superior: [new GrupoBarras(3, 5)], Inferior: [new GrupoBarras(3, 5)], Datos.ReglasSinRedondeo);

    [Fact]
    public void LosIdsSiguenLaPosicionYVanDeIzquierdaADerecha()
    {
        ResultadoVigas r = MotorVigas.Calcular(Aerea());

        Assert.NotNull(r.Superior);
        Assert.Equal(Enumerable.Range(1, r.Superior.Barras.Count).Select(i => $"S{i}"), r.Superior.Barras.Select(b => b.Id));
        Assert.Equal("S1–S2", r.Superior.Traslapos[0].Id);
        Assert.Equal(("S1", "S2"), (r.Superior.Traslapos[0].BarraIzquierda, r.Superior.Traslapos[0].BarraDerecha));
        Assert.All(r.Inferior!.Barras, b => Assert.StartsWith("I", b.Id));
    }

    [Fact]
    public void EnCimentacion_LaCapaInferiorVaAlCentroYLosIdsNoCambian()
    {
        ResultadoVigas r = MotorVigas.Calcular(Aerea() with { Tipo = TipoViga.Cimentacion });

        Assert.Equal(Referencia.Centro, r.Inferior!.Referencia);
        Assert.Equal(Posicion.Superior, r.CapaDeLaCara);
        Assert.StartsWith("I", r.Inferior.Barras[0].Id);
    }

    [Fact]
    public void LosKilosSonLargoPorCantidadPorPesoPorMetro()
    {
        ResultadoVigas r = MotorVigas.Calcular(Aerea());

        Assert.All(r.Barras, b => Assert.Equal(b.Largo / 1000.0 * 3 * 1.552, b.Kg, precision: 6));
        Assert.Equal(r.Barras.Sum(b => b.Kg) + r.Bastones.Sum(b => b.Kg), r.Kg, precision: 6);
    }

    [Fact]
    public void LaCapaDeLaCara_TraeLasDosOpcionesYUsaLaMasEconomica()
    {
        ResultadoVigas r = MotorVigas.Calcular(Aerea());

        Assert.NotNull(r.CaraInicio);
        Assert.NotNull(r.CaraFinal);
        Assert.NotEqual(r.CaraInicio.Kg, r.CaraFinal.Kg, precision: 3);
        Cara esperada = r.CaraInicio.Kg <= r.CaraFinal.Kg ? Cara.Inicio : Cara.Final;
        Assert.Equal(esperada, r.CaraMasEconomica);
        Assert.Equal(esperada, r.CaraUsada);
    }

    [Fact]
    public void SiElUsuarioEligeLaCara_SeRespeta()
    {
        ResultadoVigas r = MotorVigas.Calcular(Aerea() with { CaraElegida = Cara.Final });

        Assert.Equal(Cara.Final, r.CaraUsada);
        Assert.Equal(Referencia.Fin, r.Inferior!.Referencia);
    }

    [Fact]
    public void LaUltimaBarraEntraAlVoladizoConGancho()
    {
        ResultadoVigas r = MotorVigas.Calcular(Aerea());

        Barra ultima = r.Superior!.Barras[^1];
        Assert.True(ultima.LlegaAlVoladizo);
        Assert.Equal(215, ultima.GanchoFin);
        Assert.Equal(ultima.Largo, ultima.LargoRecto + ultima.GanchoInicio + ultima.GanchoFin);
        Assert.All(r.Traslapos, t => Assert.True(t.Hasta <= 12100 - 50));
    }

    [Fact]
    public void ConDosDiametros_LaCapaUsaElMinimoYElGanchoDelMayor()
    {
        ResultadoVigas r = MotorVigas.Calcular(Aerea() with { Inferior = [new GrupoBarras(3, 5), new GrupoBarras(2, 4)] });

        Assert.All(r.Inferior!.Traslapos, t => Assert.Equal((900, 5), (t.Minimo, t.Diametro)));
        Assert.Equal(215, r.Inferior.Barras[0].GanchoInicio);
        Assert.All(r.Inferior.Barras, b => Assert.Equal(b.Largo / 1000.0 * 3 * 1.552 + b.Medida(4).Largo / 1000.0 * 2 * 0.994, b.Kg, precision: 6));
    }

    /// <summary>
    /// Santiago (29-oct): con dos números en la capa, la barra menor no puede llevar el traslapo ni el gancho de la mayor:
    /// en Revit lleva el suyo y su largo ya no daba la varilla (3.10 en vez de 3.00). Traslapa en la misma luz, con su
    /// traslapo mínimo y su gancho, y su largo queda en múltiplo del módulo.
    /// </summary>
    [Theory]
    [InlineData(5, 4)]
    [InlineData(7, 5)]
    [InlineData(8, 5)]
    public void ConDosDiametros_LaBarraMenorLlevaSuTraslapoYSuGanchoEnLaMismaLuz(int mayor, int menor)
    {
        Reglas reglas = Reglas.Generales;
        ResultadoVigas r = MotorVigas.Calcular(Aerea() with
        {
            Reglas = reglas, Superior = [new GrupoBarras(2, mayor), new GrupoBarras(2, menor)],
        });

        SolucionCapa capa = r.Superior!;
        List<MedidaBarra> menores = capa.Barras.Select(b => b.Medida(menor)).ToList();
        ReglaDiametro regla = reglas.PorDiametro[menor];
        Assert.Equal(reglas.LargoGanchoRedondeado(regla, TipoGancho.De90), menores[0].GanchoInicio);
        Assert.Equal(reglas.LargoGanchoRedondeado(regla, TipoGancho.De90), menores[^1].GanchoFin);
        Assert.All(menores, m => Assert.Equal((menor, 0), (m.Diametro, m.Largo % reglas.Modulo)));
        Assert.All(menores, m => Assert.Equal(m.LargoRecto + m.GanchoInicio + m.GanchoFin, m.Largo));
        for (int i = 0; i < capa.Traslapos.Count; i++)
        {
            int desde = menores[i + 1].Desde, hasta = menores[i].Hasta;
            // al menos su mínimo; puede pasarlo, para que el largo cierre en el módulo
            Assert.True(hasta - desde >= regla.Traslapo!.Value);
            Luz luz = new GeometriaPortico(Datos.PorticoMaqueta(), 50).Luces.Single(l => l.Nombre == capa.Traslapos[i].Luz);
            Assert.InRange(desde, luz.CaraIzquierda, luz.CaraDerecha);
        }
    }

    [Fact]
    public void ConUnSoloDiametro_LaBarraNoTraeMenores()
    {
        ResultadoVigas r = MotorVigas.Calcular(Aerea());

        Assert.All(r.Barras, b => Assert.Null(b.Menores));
        Assert.All(r.Barras, b => Assert.Equal((b.Desde, b.Hasta, b.Largo), (b.Medida(5).Desde, b.Medida(5).Hasta, b.Medida(5).Largo)));
    }

    [Fact]
    public void LasZonasDeTraslapoIncluyenLasDeLaBarraMenor()
    {
        ResultadoVigas r = MotorVigas.Calcular(Aerea() with { Superior = [new GrupoBarras(2, 8), new GrupoBarras(2, 5)] });

        List<Barra> barras = r.Superior!.Barras.ToList();
        for (int i = 0; i + 1 < barras.Count; i++)
            Assert.Contains((barras[i + 1].Medida(5).Desde, barras[i].Medida(5).Hasta), r.ZonasTraslapo);
    }

    [Fact]
    public void UnDiametroSinTraslapoMinimo_NoSeCalculaYSeAvisa()
    {
        ResultadoVigas r = MotorVigas.Calcular(Aerea() with { Superior = [new GrupoBarras(2, 2)] });

        Assert.Null(r.Superior);
        Assert.Contains(new Aviso(TipoAviso.SinTraslapoMinimo, "#2"), r.Avisos);
    }
}
