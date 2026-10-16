using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Nucleo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>Pórtico de la maqueta con la tabla real (varilla de 6 m), 3 #5 arriba y 3 #5 abajo.</summary>
public class MotorVigasTests
{
    private static EntradaVigas Aerea() => new(Datos.PorticoMaqueta(), TipoViga.Aerea,
        Superior: [new GrupoBarras(3, 5)], Inferior: [new GrupoBarras(3, 5)], Reglas.Generales);

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
        Assert.Equal(r.Barras.Sum(b => b.Kg), r.Kg, precision: 6);
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
        Assert.Equal(278, ultima.GanchoFin);
        Assert.Equal(ultima.Largo, ultima.LargoRecto + ultima.GanchoInicio + ultima.GanchoFin);
        Assert.All(r.Traslapos, t => Assert.True(t.Hasta <= 12100 - 50));
    }

    [Fact]
    public void ConDosDiametros_LaCapaUsaElMinimoYElGanchoDelMayor()
    {
        ResultadoVigas r = MotorVigas.Calcular(Aerea() with { Inferior = [new GrupoBarras(3, 5), new GrupoBarras(2, 4)] });

        Assert.All(r.Inferior!.Traslapos, t => Assert.Equal((900, 5), (t.Minimo, t.Diametro)));
        Assert.Equal(278, r.Inferior.Barras[0].GanchoInicio);
        Assert.All(r.Inferior.Barras, b => Assert.Equal(b.Largo / 1000.0 * (3 * 1.552 + 2 * 0.994), b.Kg, precision: 6));
    }

    [Fact]
    public void UnDiametroSinTraslapoMinimo_NoSeCalculaYSeAvisa()
    {
        ResultadoVigas r = MotorVigas.Calcular(Aerea() with { Superior = [new GrupoBarras(2, 2)] });

        Assert.Null(r.Superior);
        Assert.Contains(new Aviso(TipoAviso.SinTraslapoMinimo, "#2"), r.Avisos);
    }
}
