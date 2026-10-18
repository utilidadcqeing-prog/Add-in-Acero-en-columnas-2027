using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

public class AvisosTests
{
    /// <summary>
    /// La maqueta con el eje D en 11.55 m. Si la capa de abajo traslapa al final de la luz, I3–I4 queda en 1.272
    /// (0.372 sobre el mínimo) porque I4 entra al voladizo y solo puede crecer; al inicio de la luz ningún traslapo
    /// pasa de 0.30 y se gasta un módulo menos: 0.50 × 3 × 1.552 = 4.656 kg.
    /// </summary>
    private static EntradaVigas DEnOnceCincuenta(Cara? cara) => new(
        new Portico([Datos.Columna("A", 0), Datos.Columna("B", 4400), Datos.Columna("C", 7900), Datos.Columna("D", 11550)],
            VoladizoFinal: 1500),
        TipoViga.Aerea, [new GrupoBarras(3, 5)], [new GrupoBarras(3, 5)], Reglas.Generales, CaraElegida: cara);

    [Fact]
    public void TraslapoQuePasaElMinimoEnMasDe30cm_AvisaConCausaYAhorro()
    {
        ResultadoVigas r = MotorVigas.Calcular(DEnOnceCincuenta(Cara.Final));

        Aviso aviso = Assert.Single(r.Avisos, EsTraslapoLargoDeAbajo);
        Assert.Equal(("I3–I4", 1272, 372, CausaAviso.VecinaDeFrontera), (aviso.Elemento, aviso.Largo, aviso.Exceso, aviso.Causa));
        Assert.NotNull(aviso.Alternativa);
        Assert.Equal(Cara.Inicio, aviso.Alternativa.Cara);
        Assert.True(aviso.Alternativa.LoEvita);
        Assert.Equal(4.656, aviso.Alternativa.KgAhorro, precision: 6);
    }

    [Fact]
    public void ConLaCaraMasEconomica_NoHayAviso()
    {
        ResultadoVigas r = MotorVigas.Calcular(DEnOnceCincuenta(cara: null));

        Assert.Equal(Cara.Inicio, r.CaraUsada);
        Assert.DoesNotContain(r.Avisos, EsTraslapoLargoDeAbajo);
    }

    [Fact]
    public void EnLaCapaDelCentro_ElAvisoNoTraeAlternativa()
    {
        ResultadoVigas r = MotorVigas.Calcular(DEnOnceCincuenta(cara: null));

        Aviso aviso = Assert.Single(r.Avisos, a => a.Tipo == TipoAviso.TraslapoLargo && a.Elemento.StartsWith('S'));
        Assert.Null(aviso.Alternativa);
    }

    /// <summary>En la viga aérea la capa de abajo es la de la cara.</summary>
    private static bool EsTraslapoLargoDeAbajo(Aviso a) => a.Tipo == TipoAviso.TraslapoLargo && a.Elemento.StartsWith('I');

    [Fact]
    public void BarraQueNoSaleDeLaVarillaDe6_AvisaQueUsaLaDe12()
    {
        // Luz libre de 1.20 m (no le cabe un traslapo #8) con voladizos de 3 m: cada capa es una barra de 8.79 m.
        var entrada = new EntradaVigas(
            new Portico([Datos.Columna("A", 0), Datos.Columna("B", 1600)], VoladizoInicio: 3000, VoladizoFinal: 3000),
            TipoViga.Aerea, [new GrupoBarras(2, 8)], [new GrupoBarras(2, 8)], Reglas.Generales);

        ResultadoVigas r = MotorVigas.Calcular(entrada);

        Assert.Contains(new Aviso(TipoAviso.VarillaLarga, "S1", Largo: 8788), r.Avisos);
        Assert.Equal(12000, r.Superior!.Barras[0].Varilla);
    }

    [Fact]
    public void LasZonasDeTraslapoSonLasDeLasDosCapasEnUso()
    {
        ResultadoVigas r = MotorVigas.Calcular(DEnOnceCincuenta(cara: null));

        Assert.Equal(r.Traslapos.Select(t => (t.Desde, t.Hasta)), r.ZonasTraslapo);
        Assert.Equal(r.Superior!.Traslapos.Count + r.Inferior!.Traslapos.Count, r.ZonasTraslapo.Count);
    }
}
