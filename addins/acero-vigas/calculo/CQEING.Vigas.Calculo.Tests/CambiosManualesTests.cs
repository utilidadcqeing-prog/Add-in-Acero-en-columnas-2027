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

    [Fact]
    public void UnCambioDeUnIdQueYaNoExisteAvisaYSigue()
    {
        // de 4 barras arriba se pasó a menos y S9 ya no existe: se avisa, y el cambio de S1 se aplica igual
        ResultadoVigas r = MotorVigas.Calcular(Maqueta(new CambiosManuales(Barras: new Dictionary<string, int> { ["S9"] = 3000, ["S1"] = 3500 })));

        Assert.Contains(r.Avisos, a => a is { Tipo: TipoAviso.CambioNoAplicado, Elemento: "S9", Motivo: MotivoBloqueo.NoExiste });
        Assert.Equal((3500, true), (r.Superior!.Barras[0].Largo, r.Superior.Barras[0].Manual));
    }

    private static CambiosManuales Movido(string id, int desde, bool enVoladizo = false) =>
        new(Movidos: new Dictionary<string, TraslapoMovido> { [id] = new(desde, enVoladizo) });

    [Fact]
    public void MoverUnTraslapoUnModuloCreceLaIzquierdaYAcortaLaDerecha()
    {
        // con S1–S2 (1672 a 2572): I1 ya mide 6.00 y no puede crecer
        SolucionCapa arriba = MotorVigas.Calcular(Maqueta(Movido("S1–S2", 1672 + 500))).Superior!;

        Assert.Equal((2172, 3072, 900, true), (arriba.Traslapos[0].Desde, arriba.Traslapos[0].Hasta, arriba.Traslapos[0].Largo, arriba.Traslapos[0].Manual));
        Assert.Equal((3072, 3500, true), (arriba.Barras[0].Hasta, arriba.Barras[0].Largo, arriba.Barras[0].Manual));
        Assert.Equal((2172, 4500, true), (arriba.Barras[1].Desde, arriba.Barras[1].Largo, arriba.Barras[1].Manual));
    }

    [Fact]
    public void MoverUnTraslapoQueNoEsMultiploSeLlevaAlModuloMasCercano()
    {
        SolucionCapa arriba = MotorVigas.Calcular(Maqueta(Movido("S1–S2", 1672 + 260))).Superior!;

        Assert.Equal(2172, arriba.Traslapos[0].Desde);
    }

    [Fact]
    public void MoverUnTraslapoAlNudoSeRechaza()
    {
        // corrido 2.00 terminaría en 4572, dentro de la columna B (4200 a 4600)
        ResultadoVigas r = MotorVigas.Calcular(Maqueta(Movido("S1–S2", 1672 + 2000)));

        Assert.Contains(r.Avisos, a => a is { Tipo: TipoAviso.CambioNoAplicado, Elemento: "S1–S2", Motivo: MotivoBloqueo.EntraAlNudo });
        Assert.Equal((1672, false), (r.Superior!.Traslapos[0].Desde, r.Superior.Traslapos[0].Manual));
    }

    [Fact]
    public void MoverUnTraslapoALaLuzSiguienteSeRechaza()
    {
        // corrido 3.00 queda entero en B-C (4672 a 5572): un traslapo arrastrado no cambia de luz
        ResultadoVigas r = MotorVigas.Calcular(Maqueta(Movido("S1–S2", 1672 + 3000)));

        Assert.Contains(r.Avisos, a => a is { Tipo: TipoAviso.CambioNoAplicado, Elemento: "S1–S2", Motivo: MotivoBloqueo.EntraAlNudo });
        Assert.Equal(1672, r.Superior!.Traslapos[0].Desde);
    }

    [Fact]
    public void MoverAlVoladizoSinConfirmarSeRechazaYConfirmadoSeAplica()
    {
        // ejes A, B y C en 0, 4.40 y 6.80, voladizo de 2.50 (C termina en 7000 y las barras en 9450); abajo, junto a
        // la cara final: I2 3.00 · I3 4.50, con I2–I3 de 5228 a 6172 en B-C
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 4400), Datos.Columna("C", 6800)], VoladizoFinal: 2500);
        EntradaVigas Entrada(bool confirmado) => new(portico, TipoViga.Aerea, [new GrupoBarras(3, 5)], [new GrupoBarras(3, 5)],
            Reglas.Generales, Cara.Final, Cambios: Movido("I2–I3", 5228 + 2000, confirmado));

        ResultadoVigas sinConfirmar = MotorVigas.Calcular(Entrada(false));
        ResultadoVigas confirmado = MotorVigas.Calcular(Entrada(true));

        Assert.Contains(sinConfirmar.Avisos, a => a is { Tipo: TipoAviso.CambioNoAplicado, Elemento: "I2–I3", Motivo: MotivoBloqueo.TraslapoEnVoladizo });
        Assert.Equal(5228, sinConfirmar.Inferior!.Traslapos[^1].Desde);
        SolucionCapa abajo = confirmado.Inferior!;
        Assert.Equal((7228, 8172), (abajo.Traslapos[^1].Desde, abajo.Traslapos[^1].Hasta));
        Assert.Equal((5000, true), (abajo.Barras[^2].Largo, abajo.Barras[^2].LlegaAlVoladizo));
        Assert.Equal(2500, abajo.Barras[^1].Largo);
    }

    [Fact]
    public void ElMovidoSeAplicaDespuesDeSubirOBajar()
    {
        var subir = new Dictionary<string, int> { ["S1"] = 3500 };
        Traslapo subido = MotorVigas.Calcular(Maqueta(new CambiosManuales(Barras: subir))).Superior!.Traslapos[0];

        SolucionCapa arriba = MotorVigas.Calcular(Maqueta(new CambiosManuales(Barras: subir,
            Movidos: new Dictionary<string, TraslapoMovido> { ["S1–S2"] = new(subido.Desde + 500) }))).Superior!;

        Assert.Equal((subido.Desde + 500, subido.Largo), (arriba.Traslapos[0].Desde, arriba.Traslapos[0].Largo));
        Assert.Equal(4000, arriba.Barras[0].Largo);
    }

    [Fact]
    public void UnMovidoDeUnIdQueYaNoExisteAvisaYSigue()
    {
        ResultadoVigas r = MotorVigas.Calcular(Maqueta(new CambiosManuales(Movidos: new Dictionary<string, TraslapoMovido>
        {
            ["S8–S9"] = new(1000),
            ["S1–S2"] = new(2172),
        })));

        Assert.Contains(r.Avisos, a => a is { Tipo: TipoAviso.CambioNoAplicado, Elemento: "S8–S9", Motivo: MotivoBloqueo.NoExiste });
        Assert.Equal(2172, r.Superior!.Traslapos[0].Desde);
    }
}
