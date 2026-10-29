using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Gancho de 90°, 135° o 180° en cada punta del borde (#84, decisiones 5 y 9 del plan), en el pórtico de la maqueta:
/// las barras van de −150 a 13950 y BS1, centrado en A, se corta en −150.
/// </summary>
public class GanchosTests
{
    private static EntradaVigas Entrada(Dictionary<string, TipoGancho>? ganchos = null, ArmadoBastones? bastones = null, Reglas? reglas = null) =>
        new(Datos.PorticoMaqueta(), TipoViga.Aerea, [new GrupoBarras(3, 5)], [new GrupoBarras(3, 5)], reglas ?? Datos.ReglasSinRedondeo,
            Cambios: new CambiosManuales(Ganchos: ganchos), Bastones: bastones);

    private static Barra Barra(ResultadoVigas r, string id) => r.Barras.Single(b => b.Id == id);

    private static Baston Baston(ResultadoVigas r, string id) => r.Bastones.Single(b => b.Id == id);

    private static bool Avisa(ResultadoVigas r, string elemento, MotivoBloqueo motivo) =>
        r.Avisos.Any(a => a is { Tipo: TipoAviso.CambioNoAplicado } && a.Elemento == elemento && a.Motivo == motivo);

    [Fact]
    public void PorDefectoLasPuntasDelBordeLlevan90()
    {
        ResultadoVigas r = MotorVigas.Calcular(Entrada());

        Assert.Equal((TipoGancho.De90, 215), (Barra(r, "S1").TipoInicio, Barra(r, "S1").GanchoInicio));
        Assert.Equal((TipoGancho.De90, 215), (Barra(r, "S4").TipoFin, Barra(r, "S4").GanchoFin));
        Assert.Equal((TipoGancho.Sin, TipoGancho.Sin), (Barra(r, "S2").TipoInicio, Barra(r, "S2").TipoFin));
    }

    [Fact]
    public void ConLasReglasDeBase_ElGanchoSeRedondeaYLaBarraSigueEnMultiplo()
    {
        // #5 a 90°: B 278 → 300 con el redondeo de 5 cm, así que el gancho suma 22 más (215 → 237).
        ResultadoVigas r = MotorVigas.Calcular(Entrada(reglas: Reglas.Generales));

        Assert.Equal((TipoGancho.De90, 237), (Barra(r, "S1").TipoInicio, Barra(r, "S1").GanchoInicio));
        Assert.All(r.Barras, b => Assert.Equal(0, b.Largo % 500));
    }

    [Fact]
    public void LosGanchosDeAbajoQuedanAnidadosDentroDeLosDeArriba()
    {
        // #85, novena prueba en Revit: con las dos capas hasta el mismo borde, la pata del gancho de arriba y la de abajo
        // quedaban en el mismo punto. La de abajo termina un diámetro antes (centro a centro: la mitad de cada uno).
        ResultadoVigas r = MotorVigas.Calcular(Entrada());
        (int Desde, int Hasta) Puntas(Posicion capa) =>
            (r.Barras.Where(b => b.Capa == capa).Min(b => b.Desde), r.Barras.Where(b => b.Capa == capa).Max(b => b.Hasta));

        Assert.Equal((-150, 13950), Puntas(Posicion.Superior));
        Assert.Equal((-150 + 16, 13950 - 16), Puntas(Posicion.Inferior));
        Assert.All(r.Barras, b => Assert.Equal(0, b.Largo % 500));
    }

    [Fact]
    public void Gancho180EnElInicioCambiaElLargoYSigueEnMultiplo()
    {
        Barra antes = Barra(MotorVigas.Calcular(Entrada()), "S1");

        Barra s1 = Barra(MotorVigas.Calcular(Entrada(new() { ["S.inicio"] = TipoGancho.De180 })), "S1");

        Assert.Equal((TipoGancho.De180, 176), (s1.TipoInicio, s1.GanchoInicio));
        Assert.Equal(0, s1.Largo % 500);
        Assert.Equal(antes.Desde, s1.Desde);
    }

    [Fact]
    public void Gancho135UsaElDelCuadroDeEstribos()
    {
        Barra i3 = Barra(MotorVigas.Calcular(Entrada(new() { ["I.fin"] = TipoGancho.De135 })), "I3");

        Assert.Equal((TipoGancho.De135, 162), (i3.TipoFin, i3.GanchoFin));
    }

    [Fact]
    public void SinGanchoNoSePermiteEnLasCorridas()
    {
        ResultadoVigas r = MotorVigas.Calcular(Entrada(new() { ["S.inicio"] = TipoGancho.Sin }));

        Assert.True(Avisa(r, "S.inicio", MotivoBloqueo.GanchoNoValido));
        Assert.Equal((TipoGancho.De90, 215), (Barra(r, "S1").TipoInicio, Barra(r, "S1").GanchoInicio));
    }

    [Fact]
    public void GanchoEnUnaPuntaQueNoEstaEnElBordeSeIgnoraConAviso()
    {
        ResultadoVigas r = MotorVigas.Calcular(Entrada(new() { ["BS2.inicio"] = TipoGancho.De90 }));

        Assert.True(Avisa(r, "BS2.inicio", MotivoBloqueo.NoLlegaAlBorde));
        Assert.Equal((0, TipoGancho.Sin, 2000), (Baston(r, "BS2").GanchoInicio, Baston(r, "BS2").TipoInicio, Baston(r, "BS2").Largo));
    }

    [Fact]
    public void SinGanchoEnUnaPuntaQueNoEstaEnElBordeNoAvisa()
    {
        // el "Sin" quedó guardado de cuando el bastón llegaba al borde (Juan, 18-oct, en Revit)
        ResultadoVigas r = MotorVigas.Calcular(Entrada(new() { ["BS2.inicio"] = TipoGancho.Sin }));

        Assert.DoesNotContain(r.Avisos, a => a.Elemento == "BS2.inicio");
    }

    [Fact]
    public void GanchoDeUnBastonQueNoExisteAvisa()
    {
        ResultadoVigas r = MotorVigas.Calcular(Entrada(new() { ["BS9.inicio"] = TipoGancho.De90 }));

        Assert.True(Avisa(r, "BS9.inicio", MotivoBloqueo.NoExiste));
    }

    [Fact]
    public void BastonConGanchoElegidoRedondeaHaciaArribaYCreceLaOtraPunta()
    {
        // BS1 va de −150 a 1000 (1.15); con el gancho de 90° (0.215, con la cola del cuadro) da 1.365 y sube a 1.50: la punta de adentro crece 135 mm
        Baston antes = Baston(MotorVigas.Calcular(Entrada()), "BS1");

        Baston bs1 = Baston(MotorVigas.Calcular(Entrada(new() { ["BS1.inicio"] = TipoGancho.De90 })), "BS1");

        Assert.Equal((-150, 1000), (antes.Desde, antes.Hasta));
        Assert.Equal((TipoGancho.De90, 215, 1500), (bs1.TipoInicio, bs1.GanchoInicio, bs1.Largo));
        Assert.Equal((-150, 1135), (bs1.Desde, bs1.Hasta));
        Assert.True(bs1.PuntaInicioEnBorde);
        Assert.False(bs1.PuntaFinEnBorde);
    }

    [Fact]
    public void BastonConElGanchoGeneralTambienSeRedondea()
    {
        // "Con gancho" del paso 2 (#89) redondea igual que el de los botones (Juan, 29-oct: en Revit quedaban de 1.31):
        // 1.15 + 0.215 = 1.365 sube a 1.50 y la punta de adentro crece 135 mm
        Baston bs1 = Baston(MotorVigas.Calcular(Entrada(bastones: new ArmadoBastones(ConGancho: true))), "BS1");

        Assert.Equal((TipoGancho.De90, 1500), (bs1.TipoInicio, bs1.Largo));
        Assert.Equal((-150, 1135), (bs1.Desde, bs1.Hasta));
    }

    [Fact]
    public void BastonSinGanchoEnElBordeQuedaSinGancho()
    {
        Baston bs1 = Baston(MotorVigas.Calcular(Entrada(new() { ["BS1.inicio"] = TipoGancho.Sin }, new ArmadoBastones(ConGancho: true))), "BS1");

        Assert.Equal((TipoGancho.Sin, 0, 1150), (bs1.TipoInicio, bs1.GanchoInicio, bs1.Largo));
    }

    [Fact]
    public void BastonSinGanchoEscritoNoSeRedondea()
    {
        Baston bs2 = Baston(MotorVigas.Calcular(Entrada(bastones: new ArmadoBastones(Largos: new Dictionary<string, int> { ["BS2"] = 2300 }))), "BS2");

        Assert.Equal(2300, bs2.LargoRecto);
    }

    [Fact]
    public void UnGanchoMasLargoQueNoSaleDeLaVarillaUsaLaLargaOAvisa()
    {
        // en los cuadros de Carlos el de 90° es el más largo; aquí el de 180° del #5 mide 0.80 para forzar el caso
        ReglaDiametro r5 = Reglas.Generales.PorDiametro[5];
        var reglas = Datos.ReglasSinRedondeo with { PorDiametro = new Dictionary<int, ReglaDiametro>(Reglas.Generales.PorDiametro) { [5] = r5 with { Gancho180 = new Gancho(800) } } };

        ResultadoVigas r = MotorVigas.Calcular(Entrada(new() { ["I.inicio"] = TipoGancho.De180 }, reglas: reglas));

        Assert.True(r.Inferior is null ? r.Avisos.Any(a => a.Tipo == TipoAviso.SinSolucion) : Barra(r, "I1").Largo % 500 == 0);
        if (r.Inferior is not null)
            Assert.Equal((TipoGancho.De180, 800), (Barra(r, "I1").TipoInicio, Barra(r, "I1").GanchoInicio));
    }
}
