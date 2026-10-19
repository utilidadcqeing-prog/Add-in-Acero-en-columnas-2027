using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// La vista previa de la edición (#84): lo que dicen y si dejan aplicar Subir / Bajar, el traslapo y el bastón. Sobre
/// el pórtico de la maqueta con las opciones de base (ver <see cref="CambiosManualesTests"/>: S1 3.00, I1 6.00…).
/// </summary>
public class EditorTableroTests
{
    private static PreviaVista Previa(string elemento, AccionEdicion accion, double? valor = null, OpcionesTablero? o = null) =>
        EditorTablero.Previsualizar(ArmadorVistaTests.Portico1(), o ?? OpcionesTablero.PorDefecto, Reglas.Generales,
            new PedidoEdicion(1, elemento, accion, valor));

    private static OpcionEdicion Opcion(PreviaVista p, string accion) => Assert.Single(p.Opciones, o => o.Accion == accion);

    private static EntradaVigas Entrada() =>
        CalculoPortico.EntradaDe(ArmadorVistaTests.Portico1(), OpcionesTablero.PorDefecto, Reglas.Generales);

    [Fact]
    public void SubirYBajarDevuelvenLasDosOpciones()
    {
        PreviaVista p = Previa("S1", AccionEdicion.Barra);

        Assert.Equal(("S1", 2), (p.Elemento, p.Opciones.Count));
        Assert.Equal(3.50, Opcion(p, "subir").Valor);
        Assert.Equal(2.50, Opcion(p, "bajar").Valor);
        Assert.Empty(p.Saltos);
    }

    [Fact]
    public void SubirQueNoSaleDeLaVarillaSeBloqueaConSuMotivo()
    {
        OpcionEdicion subir = Opcion(Previa("I1", AccionEdicion.Barra), "subir");

        Assert.Equal((false, "No sale de una varilla de 6 m."), (subir.SePuede, subir.Texto));
    }

    [Fact]
    public void BajarQueDejaUnTraslapoCortoSeBloquea()
    {
        OpcionEdicion bajar = Opcion(Previa("S1", AccionEdicion.Barra), "bajar");

        Assert.Equal((false, "Bajarla dejaría un traslapo por debajo del mínimo."), (bajar.SePuede, bajar.Texto));
    }

    [Fact]
    public void SubirDiceComoQuedanLasVecinas()
    {
        ResultadoVigas antes = MotorVigas.Calcular(Entrada());
        ResultadoVigas despues = MotorVigas.PrevisualizarBarra(Entrada(), "S1", 3500).Resultado!;
        List<string> cambian = despues.Barras.Where(b => b.Id != "S1" && antes.Barras.Single(a => a.Id == b.Id).Largo != b.Largo)
            .Select(b => b.Id).ToList();

        OpcionEdicion subir = Opcion(Previa("S1", AccionEdicion.Barra), "subir");

        Assert.True(subir.SePuede);
        Assert.StartsWith("S1 pasa a 3.50 m.", subir.Texto);
        Assert.All(cambian, id => Assert.Contains(id + " pasa a ", subir.Texto));
    }

    [Fact]
    public void TraslapoBajoElMinimo()
    {
        OpcionEdicion aplicar = Opcion(Previa("S1–S2", AccionEdicion.Traslapo, 0.80), "aplicar");

        Assert.Equal((false, "No puede ser menor que el mínimo (0.90 m)."), (aplicar.SePuede, aplicar.Texto));
    }

    [Fact]
    public void TraslapoQueCabeDiceElEfecto()
    {
        // 1.20 sobre 0.90 crece un módulo: S2 pasa a 5.50 y el traslapo a 1.40 (CambiosManualesTests).
        OpcionEdicion aplicar = Opcion(Previa("S1–S2", AccionEdicion.Traslapo, 1.20), "aplicar");

        Assert.Equal((true, "S2 pasa a 5.50 m (múltiplo de 0.50) y el traslapo queda en 1.40 m."), (aplicar.SePuede, aplicar.Texto));
    }

    [Fact]
    public void TraslapoCuyaVecinaNoSaleDeLaVarilla()
    {
        OpcionEdicion aplicar = Opcion(Previa("I2–I3", AccionEdicion.Traslapo, 1.00), "aplicar");

        Assert.Equal((false, "I3 pasaría a 6.50 m y no sale de una varilla de 6 m."), (aplicar.SePuede, aplicar.Texto));
    }

    [Fact]
    public void TraslapoQueNoCrecePideUnLargoMayor()
    {
        OpcionEdicion aplicar = Opcion(Previa("S1–S2", AccionEdicion.Traslapo, 0.90), "aplicar");

        Assert.Equal((false, "Escribe un largo mayor que el actual (0.90 m)."), (aplicar.SePuede, aplicar.Texto));
    }

    [Fact]
    public void ArrastreDevuelveLosSaltos()
    {
        PreviaVista p = EditorTablero.Previsualizar(ArmadorVistaTests.Portico1(), OpcionesTablero.PorDefecto, Reglas.Generales,
            new PedidoEdicion(1, "S2", AccionEdicion.Arrastre, Punto: PuntoArrastre.Izquierda));

        Assert.NotEmpty(p.Saltos);
        Assert.Empty(p.Opciones);
    }

    [Fact]
    public void BastonEscritoNoSeRedondea()
    {
        OpcionEdicion aplicar = Opcion(Previa("BS2", AccionEdicion.Baston, 2.30), "aplicar");

        Assert.Equal((true, 2.30, "Queda 1.15 m a cada lado de su eje. No se redondea."), (aplicar.SePuede, aplicar.Valor, aplicar.Texto));
    }

    [Fact]
    public void BastonSinLargoValido()
    {
        OpcionEdicion aplicar = Opcion(Previa("BS2", AccionEdicion.Baston, 0), "aplicar");

        Assert.Equal((false, "El largo debe ser mayor que 0."), (aplicar.SePuede, aplicar.Texto));
    }

    [Fact]
    public void SinValorPideUnLargo()
    {
        OpcionEdicion aplicar = Opcion(Previa("S1–S2", AccionEdicion.Traslapo), "aplicar");

        Assert.Equal((false, "Escribe un largo en metros."), (aplicar.SePuede, aplicar.Texto));
    }

    [Fact]
    public void UnElementoQueYaNoEstaNoDejaAplicar()
    {
        PreviaVista p = Previa("S9", AccionEdicion.Barra);

        Assert.All(p.Opciones, o => Assert.Equal((false, "Ese elemento ya no está en el pórtico."), (o.SePuede, o.Texto)));
    }

    [Fact]
    public void LaPreviaTieneEnCuentaLosCambiosQueYaHay()
    {
        // con S1 ya en 3.50, Subir la llevaría a 4.00 y Bajar la devolvería a 3.00
        var o = OpcionesTablero.PorDefecto with { Cambios = new CambiosManuales(Barras: new Dictionary<string, int> { ["S1"] = 3500 }) };

        PreviaVista p = Previa("S1", AccionEdicion.Barra, o: o);

        Assert.Equal((4.00, 3.00), (Opcion(p, "subir").Valor, Opcion(p, "bajar").Valor));
    }
}
