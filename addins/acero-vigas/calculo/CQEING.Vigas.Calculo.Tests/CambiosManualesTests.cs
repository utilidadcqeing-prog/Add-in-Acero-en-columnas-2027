using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Sobre el pórtico de la maqueta (tabla real, varilla de 6 m), cuya solución automática es:
/// arriba S1 3.00 · S2 5.00 · S3 5.00 · S4 4.50, con S1–S2 de 1672 a 2572 (0.90);
/// abajo I1 6.00 · I2 4.50 · I3 6.00, con I1–I2 de 4650 a 5572 (pegado a la cara de B) e I2–I3 de 8228 a 9150.
/// </summary>
public class CambiosManualesTests
{
    private static EntradaVigas Maqueta(CambiosManuales? cambios = null) => new(Datos.PorticoMaqueta(), TipoViga.Aerea,
        [new GrupoBarras(3, 5)], [new GrupoBarras(3, 5)], Reglas.Generales, Cambios: cambios);

    [Fact]
    public void SubirUnaBarraQueYaMide6_SeBloqueaPorLaVarilla()
    {
        Previsualizacion p = MotorVigas.PrevisualizarBarra(Maqueta(), "I1", 6500);

        Assert.Equal((false, MotivoBloqueo.NoSaleDeLaVarilla), (p.SePuede, p.Motivo));
    }

    [Fact]
    public void BajarUnaBarraQueDejariaElTraslapoLejosDeSuLugar_NoTieneSolucion()
    {
        // S1 en 2.50 termina en 2072: el traslapo S1–S2 tendría que centrarse a más de 0.50 de la mitad de A-B.
        Previsualizacion p = MotorVigas.PrevisualizarBarra(Maqueta(), "S1", 2500);

        Assert.Equal((false, MotivoBloqueo.SinSolucion), (p.SePuede, p.Motivo));
    }

    [Fact]
    public void SubirUnaBarra_RecalculaLasVecinasYQuedaManual()
    {
        Previsualizacion p = MotorVigas.PrevisualizarBarra(Maqueta(), "S1", 3500);

        Assert.True(p.SePuede);
        Barra s1 = p.Resultado!.Superior!.Barras[0];
        Assert.Equal((3500, true), (s1.Largo, s1.Manual));
        Assert.All(p.Resultado.Traslapos, t => Assert.True(t.Largo >= t.Minimo));
        Assert.All(p.Resultado.Barras, b => Assert.Equal(0, b.Largo % 500));
    }

    [Fact]
    public void TraslapoMasCortoQueElMinimo_SeBloquea()
    {
        Previsualizacion p = MotorVigas.PrevisualizarTraslapo(Maqueta(), "S1–S2", 850);

        Assert.Equal((false, MotivoBloqueo.TraslapoBajoElMinimo), (p.SePuede, p.Motivo));
    }

    [Fact]
    public void TraslapoAlargado_CreceLaBarraDeLaDerechaAlMultiplo()
    {
        // 1.20 pedido sobre 0.90: crece un módulo, así que S2 pasa a 5.50 y el traslapo queda en 1.40.
        Previsualizacion p = MotorVigas.PrevisualizarTraslapo(Maqueta(), "S1–S2", 1200);

        Assert.True(p.SePuede);
        SolucionCapa arriba = p.Resultado!.Superior!;
        Assert.Equal((1172, 2572, 1400, true), (arriba.Traslapos[0].Desde, arriba.Traslapos[0].Hasta, arriba.Traslapos[0].Largo, arriba.Traslapos[0].Manual));
        Assert.Equal((1172, 5500), (arriba.Barras[1].Desde, arriba.Barras[1].Largo));
        Assert.Equal(3000, arriba.Barras[0].Largo);
    }

    [Fact]
    public void TraslapoQueHariaPasarLaVecinaDeLaVarilla_SeBloquea()
    {
        // I3 ya mide 6.00: alargar I2–I3 la llevaría a 6.50.
        Previsualizacion p = MotorVigas.PrevisualizarTraslapo(Maqueta(), "I2–I3", 1000);

        Assert.Equal((false, MotivoBloqueo.NoSaleDeLaVarilla), (p.SePuede, p.Motivo));
    }

    [Fact]
    public void TraslapoPegadoALaCaraQueTendriaQueEntrarAlNudo_SeBloquea()
    {
        // I1–I2 empieza a 5 cm de la cara de B: crecer hacia la izquierda lo mete en el nudo.
        Previsualizacion p = MotorVigas.PrevisualizarTraslapo(Maqueta(), "I1–I2", 1000);

        Assert.Equal((false, MotivoBloqueo.EntraAlNudo), (p.SePuede, p.Motivo));
    }

    [Fact]
    public void LargoMenorOIgualAlActual_NoCambiaNada()
    {
        Previsualizacion p = MotorVigas.PrevisualizarTraslapo(Maqueta(), "S1–S2", 900);

        Assert.Equal((false, MotivoBloqueo.SinCambio), (p.SePuede, p.Motivo));
    }

    [Fact]
    public void ElementoQueNoExiste_SeAvisa()
    {
        Assert.Equal(MotivoBloqueo.NoExiste, MotorVigas.PrevisualizarBarra(Maqueta(), "S9", 3000).Motivo);
        Assert.Equal(MotivoBloqueo.NoExiste, MotorVigas.PrevisualizarTraslapo(Maqueta(), "S8–S9", 1000).Motivo);
    }

    [Fact]
    public void LosCambiosManualesSeConservanAlRecalcular()
    {
        var cambios = new CambiosManuales(
            Barras: new Dictionary<string, int> { ["S1"] = 3500 },
            Traslapos: new Dictionary<string, int> { ["I2–I3"] = 900 });
        EntradaVigas entrada = Maqueta(cambios);

        ResultadoVigas primero = MotorVigas.Calcular(entrada);
        ResultadoVigas segundo = MotorVigas.Calcular(entrada);

        Assert.Equal(3500, primero.Superior!.Barras[0].Largo);
        Assert.Equal(primero.Barras.Select(b => (b.Id, b.Desde, b.Hasta, b.Largo)), segundo.Barras.Select(b => (b.Id, b.Desde, b.Hasta, b.Largo)));
    }

    [Fact]
    public void UnCambioQueNoSePuedeAplicar_SeAvisaYSeIgnora()
    {
        ResultadoVigas r = MotorVigas.Calcular(Maqueta(new CambiosManuales(Barras: new Dictionary<string, int> { ["S1"] = 2500 })));

        Assert.Contains(r.Avisos, a => a is { Tipo: TipoAviso.CambioNoAplicado, Elemento: "S1", Motivo: MotivoBloqueo.SinSolucion });
        Assert.Equal((3000, false), (r.Superior!.Barras[0].Largo, r.Superior.Barras[0].Manual));
    }
}
